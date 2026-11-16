using System.Globalization;

namespace Optica.Domain.Backup;

/// <summary>
/// Los nombres de los archivos de backup (FR-06 de FEAT-010, ADR-029): cada copia verificada se llama
/// <c>optica-AAAA-MM-DD.db</c>, con la fecha del día en que se hizo, y mientras se escribe y verifica lleva además el
/// sufijo <see cref="SufijoParcial"/>. Los archivos de la carpeta son la verdad sobre qué copias hay, así que solo cuenta
/// como copia un nombre con el patrón exacto y una fecha que existe.
/// </summary>
public static class CopiaBackup
{
    /// <summary>El principio del nombre de toda copia.</summary>
    public const string Prefijo = "optica-";

    /// <summary>La extensión de una copia terminada.</summary>
    public const string Extension = ".db";

    /// <summary>El sufijo del temporal: un archivo con él nunca cuenta como copia (ADR-029).</summary>
    public const string SufijoParcial = ".parcial";

    /// <summary>El formato de la fecha dentro del nombre.</summary>
    public const string FormatoFecha = "yyyy-MM-dd";

    /// <summary>
    /// La hora local desde la que corre el backup programado (FR-04): una copia del día hecha desde esta hora es la de
    /// las 20:00.
    /// </summary>
    public static readonly TimeOnly HoraProgramada = new(20, 0);

    /// <summary>
    /// Los sufijos que SQLite agrega al temporal mientras lo escribe (diario, WAL y memoria compartida): un temporal
    /// huérfano puede dejarlos.
    /// </summary>
    public static readonly IReadOnlyList<string> SufijosAuxiliares = ["-journal", "-wal", "-shm"];

    private static readonly int LargoNombre = Prefijo.Length + FormatoFecha.Length + Extension.Length;

    /// <summary>El nombre de la copia de <paramref name="fecha"/>, por ejemplo <c>optica-2026-10-30.db</c>.</summary>
    public static string Nombre(DateOnly fecha) =>
        Prefijo + fecha.ToString(FormatoFecha, CultureInfo.InvariantCulture) + Extension;

    /// <summary>El nombre del temporal de la copia de <paramref name="fecha"/>: <c>optica-2026-10-30.db.parcial</c>.</summary>
    public static string NombreParcial(DateOnly fecha) => Nombre(fecha) + SufijoParcial;

    /// <summary>
    /// La fecha de una copia a partir del nombre del archivo (sin carpeta). Falso, con la fecha por defecto, si el nombre
    /// no es exactamente <c>optica-AAAA-MM-DD.db</c> (mayúsculas, espacios, temporales y auxiliares incluidos) o la fecha
    /// no existe en el calendario, como <c>2026-02-30</c>.
    /// </summary>
    public static bool TryLeerFecha(string? nombre, out DateOnly fecha)
    {
        fecha = default;
        if (nombre is null
            || nombre.Length != LargoNombre
            || !nombre.StartsWith(Prefijo, StringComparison.Ordinal)
            || !nombre.EndsWith(Extension, StringComparison.Ordinal))
        {
            return false;
        }

        var texto = nombre.Substring(Prefijo.Length, FormatoFecha.Length);
        for (var i = 0; i < texto.Length; i++)
        {
            var esperado = i is 4 or 7 ? '-' : (char?)null;
            if (esperado is { } guion ? texto[i] != guion : !char.IsAsciiDigit(texto[i]))
            {
                return false;
            }
        }

        if (!DateOnly.TryParseExact(texto, FormatoFecha, CultureInfo.InvariantCulture, DateTimeStyles.None, out var leida))
        {
            return false;
        }

        fecha = leida;
        return true;
    }

    /// <summary>
    /// Verdadero si el nombre es el temporal de una copia (<c>optica-AAAA-MM-DD.db.parcial</c>, con fecha válida) o uno de
    /// sus auxiliares (<c>.parcial-journal</c>, <c>.parcial-wal</c>, <c>.parcial-shm</c>): los únicos archivos que se
    /// borran como huérfanos antes de cada backup (D-23).
    /// </summary>
    public static bool EsParcial(string? nombre)
    {
        if (nombre is null)
        {
            return false;
        }

        var largoTemporal = LargoNombre + SufijoParcial.Length;
        if (nombre.Length < largoTemporal
            || !string.Equals(nombre.Substring(LargoNombre, SufijoParcial.Length), SufijoParcial, StringComparison.Ordinal)
            || !TryLeerFecha(nombre[..LargoNombre], out _))
        {
            return false;
        }

        var resto = nombre[largoTemporal..];
        return resto.Length == 0 || SufijosAuxiliares.Contains(resto, StringComparer.Ordinal);
    }
}
