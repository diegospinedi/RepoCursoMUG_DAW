using Optica.Application.Backup;
using Optica.Domain.Backup;

namespace Optica.Infrastructure.Backup;

/// <summary>
/// Las copias de backup como archivos de la carpeta configurada (ADR-029, R-03). Enumera solo los archivos de primer
/// nivel (nunca subcarpetas) y decide por el nombre exacto con <see cref="CopiaBackup"/>, comparado sin distinguir nada
/// más que lo que distingue el dominio (ordinal): un archivo ajeno, una subcarpeta con nombre de copia o un nombre casi
/// igual no se listan ni se borran. Los nombres que escribe los arma el dominio a partir de la fecha. No crea la carpeta
/// ni registra nada.
/// </summary>
public sealed class AlmacenCopiasArchivo : IAlmacenCopias
{
    public string RutaTemporal(string carpeta, DateOnly fecha)
    {
        ArgumentException.ThrowIfNullOrEmpty(carpeta);
        return Path.Combine(carpeta, CopiaBackup.NombreParcial(fecha));
    }

    public IReadOnlyList<DateOnly> ListarCopias(string carpeta) =>
        [.. Copias(carpeta).Select(copia => copia.Fecha)];

    public IReadOnlyList<DateOnly> ListarTemporalesSinCopia(string carpeta)
    {
        var fechas = new List<DateOnly>();
        foreach (var nombre in Archivos(carpeta).Select(Path.GetFileName).OfType<string>())
        {
            // Solo el temporal mismo (no sus auxiliares) y con la copia de esa fecha ausente.
            if (!nombre.EndsWith(CopiaBackup.SufijoParcial, StringComparison.Ordinal) || !CopiaBackup.EsParcial(nombre))
            {
                continue;
            }

            // La ausencia la decide el sistema de archivos (en Windows, sin distinguir mayúsculas): nunca se renombra sobre
            // un archivo o una carpeta que ya tenga ese nombre.
            var copia = nombre[..^CopiaBackup.SufijoParcial.Length];
            var rutaCopia = Path.Combine(carpeta, copia);
            if (CopiaBackup.TryLeerFecha(copia, out var fecha) && !File.Exists(rutaCopia) && !Directory.Exists(rutaCopia))
            {
                fechas.Add(fecha);
            }
        }

        return fechas;
    }

    public void RenombrarTemporal(string carpeta, DateOnly fecha)
    {
        ArgumentException.ThrowIfNullOrEmpty(carpeta);
        File.Move(RutaTemporal(carpeta, fecha), Path.Combine(carpeta, CopiaBackup.Nombre(fecha)), overwrite: true);
    }

    public int BorrarCopias(string carpeta, IReadOnlyCollection<DateOnly> fechas)
    {
        ArgumentNullException.ThrowIfNull(fechas);
        var aBorrar = fechas.ToHashSet();
        var borradas = 0;
        foreach (var (ruta, _) in Copias(carpeta).Where(copia => aBorrar.Contains(copia.Fecha)))
        {
            File.Delete(ruta);
            borradas++;
        }

        return borradas;
    }

    public void BorrarTemporales(string carpeta)
    {
        foreach (var ruta in Archivos(carpeta).Where(ruta => CopiaBackup.EsParcial(Path.GetFileName(ruta))))
        {
            File.Delete(ruta);
        }
    }

    /// <summary>Las copias terminadas de primer nivel, con el nombre exacto y una fecha que existe.</summary>
    private static List<(string Ruta, DateOnly Fecha)> Copias(string carpeta)
    {
        var copias = new List<(string, DateOnly)>();
        foreach (var ruta in Archivos(carpeta))
        {
            if (CopiaBackup.TryLeerFecha(Path.GetFileName(ruta), out var fecha))
            {
                copias.Add((ruta, fecha));
            }
        }

        return copias;
    }

    /// <summary>
    /// Los archivos de primer nivel de la carpeta, materializados antes de borrar ninguno. Solo los que empiezan con el
    /// prefijo de las copias; el filtro del sistema no es la regla (en Windows no distingue mayúsculas): la regla es la
    /// del dominio, sobre el nombre.
    /// </summary>
    private static List<string> Archivos(string carpeta)
    {
        ArgumentException.ThrowIfNullOrEmpty(carpeta);
        return [.. Directory.EnumerateFiles(carpeta, CopiaBackup.Prefijo + "*", SearchOption.TopDirectoryOnly)];
    }
}
