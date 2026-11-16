namespace Optica.Domain.Backup;

/// <summary>
/// Qué copias de backup se conservan (FR-10, FR-11 de FEAT-010, D-21). Función pura: no sabe de archivos ni de relojes;
/// recibe las fechas de las copias que hay y la fecha de hoy.
/// </summary>
public static class PoliticaRetencion
{
    /// <summary>Cuántas copias diarias se conservan (FR-10).</summary>
    public const int Diarias = 7;

    /// <summary>Cuántos meses calendario terminados se miran para las copias mensuales (FR-11).</summary>
    public const int Mensuales = 4;

    /// <summary>
    /// Las fechas de las copias que se conservan, sin repetir y de la más vieja a la más nueva:
    /// <list type="bullet">
    /// <item>las <see cref="Diarias"/> más recientes que no pasan de <paramref name="hoy"/>;</item>
    /// <item>la última copia de cada uno de los <see cref="Mensuales"/> meses calendario terminados antes del mes de
    /// <paramref name="hoy"/>; un mes de esos sin copias no se reemplaza por uno anterior (quedan menos mensuales);</item>
    /// <item>todas las de fecha posterior a <paramref name="hoy"/> (reloj adelantado), que no cuentan entre las diarias.</item>
    /// </list>
    /// Una copia puede ser diaria y mensual a la vez y aparece una sola vez. Las que no figuran se pueden borrar.
    /// </summary>
    public static IReadOnlyList<DateOnly> Conservar(IEnumerable<DateOnly> copias, DateOnly hoy)
    {
        ArgumentNullException.ThrowIfNull(copias);

        var distintas = copias.Distinct().ToList();
        var conservadas = new SortedSet<DateOnly>(distintas.Where(c => c > hoy));

        var pasadas = distintas.Where(c => c <= hoy).OrderDescending().ToList();
        conservadas.UnionWith(pasadas.Take(Diarias));

        var mesActual = new DateOnly(hoy.Year, hoy.Month, 1);
        var primerMesMirado = mesActual.AddMonths(-Mensuales);
        var ultimasDeCadaMes = pasadas
            .Where(c => c >= primerMesMirado && c < mesActual)
            .GroupBy(c => (c.Year, c.Month))
            .Select(mes => mes.Max());
        conservadas.UnionWith(ultimasDeCadaMes);

        return [.. conservadas];
    }
}
