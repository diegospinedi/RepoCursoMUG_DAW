using System.Text.Json;
using Optica.Application.Backup;

namespace Optica.Infrastructure.Backup;

/// <summary>
/// Las carpetas locales de nube de la cuenta que corre la Api (D-18), leídas en cada llamada:
/// <list type="bullet">
/// <item>OneDrive: las variables de entorno <c>OneDrive</c>, <c>OneDriveConsumer</c> y <c>OneDriveCommercial</c>.</item>
/// <item>Dropbox: la ruta de cada cuenta en <c>Dropbox\info.json</c> de <c>%LOCALAPPDATA%</c> y de <c>%APPDATA%</c>.</item>
/// <item>Google Drive para escritorio, en modo espejo: <c>Mi unidad</c>, <c>My Drive</c> y <c>Google Drive</c> dentro de
/// <c>%USERPROFILE%</c>, solo si existe su indicio de instalación <c>%LOCALAPPDATA%\Google\DriveFS</c>. En modo
/// transmisión Google Drive monta otra letra de unidad, que ya es otra unidad.</item>
/// </list>
/// </summary>
/// <remarks>
/// Solo se devuelven rutas absolutas. El <c>info.json</c> se lee de forma tolerante: si falta, pesa más de
/// <see cref="TamanoMaximoInfoDropbox"/> o no se puede interpretar, se omite sin lanzar, y nunca se propaga ni se registra
/// su contenido (lleva rutas con el nombre de la persona, D-12). Las variables se leen con la función recibida, para
/// probar con valores falsos; la configuración de la Api usa las del proceso.
/// </remarks>
public sealed class FuenteCarpetasNube : IFuenteCarpetasNube
{
    /// <summary>Tamaño máximo del <c>info.json</c> de Dropbox que se lee; uno real pesa unos cientos de bytes.</summary>
    public const int TamanoMaximoInfoDropbox = 64 * 1024;

    private static readonly string[] VariablesOneDrive = ["OneDrive", "OneDriveConsumer", "OneDriveCommercial"];
    private static readonly string[] VariablesDropbox = ["LOCALAPPDATA", "APPDATA"];
    private static readonly string[] CarpetasGoogleDrive = ["Mi unidad", "My Drive", "Google Drive"];

    private readonly Func<string, string?> _variable;

    /// <summary>Con las variables de entorno del proceso.</summary>
    public FuenteCarpetasNube()
        : this(Environment.GetEnvironmentVariable)
    {
    }

    /// <param name="variable">Devuelve el valor de una variable de entorno, o null si no está definida.</param>
    public FuenteCarpetasNube(Func<string, string?> variable) =>
        _variable = variable ?? throw new ArgumentNullException(nameof(variable));

    public IReadOnlyList<string> Carpetas()
    {
        var carpetas = new List<string>();
        foreach (var nombre in VariablesOneDrive)
        {
            Agregar(carpetas, Leer(nombre));
        }

        foreach (var nombre in VariablesDropbox)
        {
            if (Leer(nombre) is { } datosDeAplicacion)
            {
                foreach (var ruta in RutasDropbox(Path.Combine(datosDeAplicacion, "Dropbox", "info.json")))
                {
                    Agregar(carpetas, ruta);
                }
            }
        }

        if (Leer("USERPROFILE") is { } perfil && GoogleDriveInstalado())
        {
            foreach (var carpeta in CarpetasGoogleDrive)
            {
                Agregar(carpetas, Path.Combine(perfil, carpeta));
            }
        }

        return carpetas;
    }

    /// <summary>
    /// Verdadero si existe la carpeta <c>%LOCALAPPDATA%\Google\DriveFS</c>, el indicio de que Google Drive para escritorio
    /// está instalado para esta cuenta: sin él, una carpeta "Mi unidad" creada a mano no es nube. Solo se mira que exista.
    /// </summary>
    private bool GoogleDriveInstalado()
    {
        if (Leer("LOCALAPPDATA") is not { } local)
        {
            return false;
        }

        try
        {
            return Directory.Exists(Path.Combine(local, "Google", "DriveFS"));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>El valor de la variable si es una ruta absoluta; null si falta, está en blanco o es relativa.</summary>
    private string? Leer(string nombre)
    {
        var valor = _variable(nombre)?.Trim();
        return !string.IsNullOrEmpty(valor) && EsAbsoluta(valor) ? valor : null;
    }

    private static void Agregar(List<string> carpetas, string? ruta)
    {
        if (ruta is not null && EsAbsoluta(ruta) && !carpetas.Contains(ruta, StringComparer.Ordinal))
        {
            carpetas.Add(ruta);
        }
    }

    private static bool EsAbsoluta(string ruta)
    {
        try
        {
            return Path.IsPathFullyQualified(ruta);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// La propiedad <c>path</c> de cada cuenta (<c>personal</c>, <c>business</c>) del <c>info.json</c> de Dropbox. Vacío si
    /// el archivo falta, es demasiado grande o no se puede interpretar.
    /// </summary>
    private static List<string> RutasDropbox(string archivo)
    {
        try
        {
            if (!File.Exists(archivo))
            {
                return [];
            }

            // Se leen a lo sumo TamanoMaximoInfoDropbox + 1 bytes: si se llega al tope, el archivo es demasiado grande y se
            // omite, sin confiar en el largo informado (puede crecer entre la consulta y la lectura).
            var contenido = new byte[TamanoMaximoInfoDropbox + 1];
            var leidos = 0;
            using (var lector = new FileStream(archivo, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                int parte;
                while (leidos < contenido.Length && (parte = lector.Read(contenido, leidos, contenido.Length - leidos)) > 0)
                {
                    leidos += parte;
                }
            }

            if (leidos > TamanoMaximoInfoDropbox)
            {
                return [];
            }

            using var documento = JsonDocument.Parse(contenido.AsMemory(0, leidos));
            if (documento.RootElement.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            var rutas = new List<string>();
            foreach (var cuenta in documento.RootElement.EnumerateObject())
            {
                if (cuenta.Value.ValueKind == JsonValueKind.Object
                    && cuenta.Value.TryGetProperty("path", out var ruta)
                    && ruta.ValueKind == JsonValueKind.String
                    && ruta.GetString() is { Length: > 0 } texto)
                {
                    rutas.Add(texto);
                }
            }

            return rutas;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException
            or System.Security.SecurityException or ArgumentException)
        {
            // Tolerante: sin Dropbox legible no hay carpeta de Dropbox; el texto del error no se propaga.
            return [];
        }
    }
}
