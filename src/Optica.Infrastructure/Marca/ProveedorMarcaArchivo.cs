using System.Text.Json;
using Microsoft.Extensions.Logging;
using Optica.Application.Marca;
using Optica.Domain.Marca;

namespace Optica.Infrastructure.Marca;

/// <summary>
/// Lee la marca de <c>branding.json</c> y su logo en la carpeta <c>Optica:Marca:Directorio</c> (fuera del repositorio,
/// FR-07). Si la carpeta no está configurada, no es absoluta, no existe, no tiene <c>branding.json</c>, la marca es
/// inválida o leerla falla por cualquier otro motivo, usa la marca de ejemplo completa de <c>marca-ejemplo/</c> y registra una advertencia. Si tampoco la de
/// ejemplo se puede usar, lanza <see cref="MarcaEjemploIlegibleException"/> (instalación dañada).
/// <para>
/// Del campo <c>logo</c> se toma solo el nombre del archivo y se busca dentro de la carpeta; un nombre que se
/// resolviera afuera, un enlace simbólico o un archivo que no es un archivo común se rechazan (R-09).
/// </para>
/// <para>
/// La marca se lee la primera vez que se pide y queda en memoria hasta reiniciar la Api; una falla no se guarda y se
/// reintenta en el pedido siguiente. Ni las advertencias ni los mensajes de error llevan rutas del sistema de archivos
/// (la carpeta puede estar bajo el perfil del usuario de Windows), valores leídos del archivo ni la excepción original.
/// </para>
/// </summary>
public sealed partial class ProveedorMarcaArchivo : IProveedorMarca
{
    public const string ClaveDirectorio = "Optica:Marca:Directorio";

    /// <summary>Carpeta de la marca de ejemplo, junto a los binarios de la Api.</summary>
    public const string CarpetaEjemplo = "marca-ejemplo";

    public const string ArchivoBranding = "branding.json";

    /// <summary>Tamaño máximo de <c>branding.json</c>: 64 KB. El de la marca real ocupa menos de 1 KB.</summary>
    public const int TamanoMaximoBranding = 64 * 1024;

    private const string DescripcionLogo = "el logo indicado en branding.json";

    private static readonly char[] SeparadoresDeRuta = ['/', '\\'];

    /// <summary>Caracteres que no pueden ir en el nombre del logo en Windows (además de los de control).</summary>
    private static readonly char[] CaracteresProhibidos = ['<', '>', ':', '"', '|', '?', '*'];

    private static readonly JsonDocumentOptions OpcionesJson = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 16,
        AllowDuplicateProperties = false,
    };

    private readonly string? _directorioMarca;
    private readonly string _directorioEjemplo;
    private readonly ILogger<ProveedorMarcaArchivo> _registro;
    private readonly Lock _cerrojo = new();
    private IdentidadVisual? _vigente;

    /// <param name="directorioMarca">El valor de <c>Optica:Marca:Directorio</c>: ruta absoluta, vacía o nula.</param>
    /// <param name="directorioEjemplo">Ruta absoluta de la carpeta <c>marca-ejemplo</c>.</param>
    /// <param name="registro">Registro de advertencias.</param>
    public ProveedorMarcaArchivo(string? directorioMarca, string directorioEjemplo, ILogger<ProveedorMarcaArchivo> registro)
    {
        _directorioMarca = directorioMarca;
        _directorioEjemplo = directorioEjemplo;
        _registro = registro;
    }

    public IdentidadVisual Obtener()
    {
        var vigente = Volatile.Read(ref _vigente);
        if (vigente is not null)
        {
            return vigente;
        }

        lock (_cerrojo)
        {
            if (_vigente is null)
            {
                Volatile.Write(ref _vigente, Cargar());
            }

            return _vigente!;
        }
    }

    private IdentidadVisual Cargar()
    {
        if (string.IsNullOrWhiteSpace(_directorioMarca))
        {
            AdvertirSinCarpeta(ClaveDirectorio);
            return CargarEjemplo();
        }

        try
        {
            return Leer(_directorioMarca, esEjemplo: false);
        }
        catch (MarcaNoUsableException error)
        {
            AdvertirMarcaDeEjemplo(error.Message, ClaveDirectorio);
            return CargarEjemplo();
        }
        catch (Exception error) when (error is not (OutOfMemoryException or InsufficientExecutionStackException))
        {
            // Las fallas críticas del proceso no se tapan con la marca de ejemplo. Cualquier otra falla (por ejemplo, un
            // valor anómalo en la clave que hace fallar a GetFullPath) tampoco puede dejar la Api sin marca: solo el
            // tipo, porque el mensaje de la excepción puede traer la ruta.
            AdvertirFallaInesperada(error.GetType().Name, ClaveDirectorio);
            return CargarEjemplo();
        }
    }

    private IdentidadVisual CargarEjemplo()
    {
        try
        {
            return Leer(_directorioEjemplo, esEjemplo: true);
        }
        catch (MarcaNoUsableException error)
        {
            // Sin la causa adjunta ni rutas: el manejador de errores de la Api registra este mensaje.
            throw new MarcaEjemploIlegibleException(
                $"No se pudo usar la marca de ejemplo ({CarpetaEjemplo}): {error.Message}. La instalación está dañada: " +
                "volvé a instalar la aplicación.");
        }
    }

    private static IdentidadVisual Leer(string directorio, bool esEjemplo)
    {
        if (!Path.IsPathFullyQualified(directorio))
        {
            throw new MarcaNoUsableException("la carpeta de marca no es una ruta absoluta");
        }

        var carpeta = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directorio));
        if (!Directory.Exists(carpeta))
        {
            throw new MarcaNoUsableException("la carpeta de marca no existe");
        }

        var branding = LeerArchivo(Path.Combine(carpeta, ArchivoBranding), TamanoMaximoBranding, ArchivoBranding);
        var (campoLogo, primario, fondo) = Interpretar(branding);

        var nombreLogo = NombreDeArchivo(campoLogo)
            ?? throw new MarcaNoUsableException("el campo logo de branding.json no tiene un nombre de archivo válido");
        if (LogoMarca.FormatoDe(nombreLogo) is null)
        {
            throw new MarcaNoUsableException("el logo no tiene una extensión permitida (.png, .jpg o .svg)");
        }

        var rutaLogo = Path.GetFullPath(Path.Combine(carpeta, nombreLogo));
        if (!EstaEnLaCarpeta(carpeta, rutaLogo))
        {
            throw new MarcaNoUsableException("el logo indicado en branding.json queda fuera de la carpeta de marca");
        }

        var contenidoLogo = LeerArchivo(rutaLogo, LogoMarca.TamanoMaximo, DescripcionLogo);
        try
        {
            return IdentidadVisual.Crear(primario, fondo, LogoMarca.Crear(nombreLogo, contenidoLogo), esEjemplo);
        }
        catch (MarcaInvalidaException error)
        {
            throw new MarcaNoUsableException(QuitarPuntoFinal(error.Message));
        }
    }

    /// <summary>
    /// Lee el esquema <c>{ "logo": string, "colores": { "primario": string, "fondo": string } }</c>; los demás campos
    /// se ignoran. Los colores se validan después, en el dominio.
    /// </summary>
    private static (string Logo, string? Primario, string? Fondo) Interpretar(byte[] branding)
    {
        try
        {
            // El flujo (y no los bytes) para aceptar el BOM de UTF-8 que agregan algunos editores de Windows.
            using var documento = JsonDocument.Parse(new MemoryStream(branding, writable: false), OpcionesJson);
            var raiz = documento.RootElement;
            if (raiz.ValueKind != JsonValueKind.Object
                || !raiz.TryGetProperty("logo", out var logo)
                || logo.ValueKind != JsonValueKind.String
                || !raiz.TryGetProperty("colores", out var colores)
                || colores.ValueKind != JsonValueKind.Object)
            {
                throw new MarcaNoUsableException("branding.json no tiene los campos logo y colores");
            }

            return (logo.GetString()!, Texto(colores, "primario"), Texto(colores, "fondo"));
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException)
        {
            throw new MarcaNoUsableException("branding.json no es un JSON válido");
        }
    }

    private static string? Texto(JsonElement objeto, string propiedad) =>
        objeto.TryGetProperty(propiedad, out var valor) && valor.ValueKind == JsonValueKind.String ? valor.GetString() : null;

    /// <summary>
    /// Si <paramref name="ruta"/>, una vez resueltos los <c>..</c>, es un archivo directamente dentro de
    /// <paramref name="carpeta"/>: ni en una subcarpeta ni en otra carpeta, aunque su nombre empiece igual.
    /// <paramref name="carpeta"/> es absoluta y sin separador final.
    /// </summary>
    internal static bool EstaEnLaCarpeta(string carpeta, string ruta) =>
        string.Equals(Path.GetDirectoryName(Path.GetFullPath(ruta)), carpeta, StringComparison.Ordinal);

    /// <summary>
    /// Solo el nombre del archivo, sin carpetas (con <c>/</c> o <c>\</c>, en cualquier sistema): así
    /// <c>"Marca/logo.png"</c> se busca como <c>logo.png</c> y <c>"../secreto.png"</c> como <c>secreto.png</c>, dentro
    /// de la carpeta. Nulo si queda vacío, es <c>.</c> o <c>..</c>, o tiene caracteres de control o no permitidos en
    /// Windows (por ejemplo <c>:</c>, que abriría otra unidad o un flujo alternativo), o es un nombre de dispositivo
    /// reservado de Windows.
    /// </summary>
    internal static string? NombreDeArchivo(string campo)
    {
        var nombre = campo[(campo.LastIndexOfAny(SeparadoresDeRuta) + 1)..];
        if (nombre.Length == 0
            || nombre is "." or ".."
            || nombre.IndexOfAny(CaracteresProhibidos) >= 0
            || nombre.Any(char.IsControl)
            || EsNombreReservadoDeWindows(nombre))
        {
            return null;
        }

        return nombre;
    }

    /// <summary>
    /// Si el nombre es un dispositivo de Windows según "Naming Files, Paths, and Namespaces" de Microsoft (<c>CON</c>,
    /// <c>PRN</c>, <c>AUX</c>, <c>NUL</c>, <c>COM0</c>–<c>COM9</c>, <c>LPT0</c>–<c>LPT9</c>, <c>COM¹</c>–<c>COM³</c>,
    /// <c>LPT¹</c>–<c>LPT³</c>, <c>CONIN$</c> y <c>CONOUT$</c>), con cualquier extensión y sin distinguir mayúsculas:
    /// Windows abriría el dispositivo, no un archivo. Se toma lo anterior al primer punto, sin los espacios finales
    /// (<c>"NUL .png"</c> también lo es).
    /// </summary>
    internal static bool EsNombreReservadoDeWindows(string nombre)
    {
        var punto = nombre.IndexOf('.', StringComparison.Ordinal);
        var baseDelNombre = (punto < 0 ? nombre : nombre[..punto]).TrimEnd(' ').ToUpperInvariant();
        return baseDelNombre switch
        {
            "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" => true,
            { Length: 4 } when (baseDelNombre.StartsWith("COM", StringComparison.Ordinal)
                    || baseDelNombre.StartsWith("LPT", StringComparison.Ordinal))
                && baseDelNombre[3] is (>= '0' and <= '9') or '¹' or '²' or '³' => true,
            _ => false,
        };
    }

    /// <summary>
    /// Lee un archivo común (no un enlace simbólico ni una carpeta) de hasta <paramref name="maximo"/> bytes. Los
    /// errores de lectura se informan con su tipo, nunca con el texto de la excepción (trae la ruta).
    /// </summary>
    private static byte[] LeerArchivo(string ruta, int maximo, string descripcion)
    {
        try
        {
            var archivo = new FileInfo(ruta);
            if (!archivo.Exists)
            {
                throw new MarcaNoUsableException($"falta {descripcion}");
            }

            if (archivo.LinkTarget is not null || archivo.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new MarcaNoUsableException($"{descripcion} es un enlace; tiene que ser un archivo de la carpeta de marca");
            }

            if (archivo.Length > maximo)
            {
                throw new MarcaNoUsableException($"{descripcion} supera el tamaño máximo de {maximo / 1024} KB");
            }

            using var flujo = archivo.OpenRead();
            using var memoria = new MemoryStream();
            var bloque = new byte[16 * 1024];
            int leidos;
            while ((leidos = flujo.Read(bloque)) > 0)
            {
                memoria.Write(bloque, 0, leidos);
                // El archivo pudo crecer después de medirlo.
                if (memoria.Length > maximo)
                {
                    throw new MarcaNoUsableException($"{descripcion} supera el tamaño máximo de {maximo / 1024} KB");
                }
            }

            return memoria.ToArray();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            throw new MarcaNoUsableException($"no se pudo leer {descripcion} ({error.GetType().Name})");
        }
    }

    private static string QuitarPuntoFinal(string mensaje) => mensaje.TrimEnd('.');

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "No hay carpeta de marca configurada en la clave {Clave}: se muestra la marca de ejemplo.")]
    private partial void AdvertirSinCarpeta(string clave);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "No se usa la marca instalada: {Motivo}. Se muestra la marca de ejemplo. Revisá la carpeta configurada " +
            "en la clave {Clave} y reiniciá la Api.")]
    private partial void AdvertirMarcaDeEjemplo(string motivo, string clave);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "No se usa la marca instalada: falla inesperada al leerla ({Tipo}). Se muestra la marca de ejemplo. " +
            "Revisá la carpeta configurada en la clave {Clave} y reiniciá la Api.")]
    private partial void AdvertirFallaInesperada(string tipo, string clave);

    /// <summary>La marca leída no se puede usar; el mensaje describe el motivo sin rutas ni valores leídos.</summary>
    private sealed class MarcaNoUsableException(string motivo) : Exception(motivo);
}

/// <summary>
/// Tampoco la marca de ejemplo se puede usar: la instalación está dañada y la Api responde 500. El mensaje no lleva
/// rutas ni la excepción original.
/// </summary>
public sealed class MarcaEjemploIlegibleException(string mensaje) : Exception(mensaje);
