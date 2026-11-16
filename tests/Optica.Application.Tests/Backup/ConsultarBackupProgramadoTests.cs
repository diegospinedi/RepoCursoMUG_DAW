using Optica.Application.Backup;
using Optica.Domain.Backup;

namespace Optica.Application.Tests.Backup;

/// <summary>
/// Spec FEAT-010, Block 5 (FR-04, D-20, D-24): si toca el backup programado de hoy, con un reloj en la zona de la óptica
/// (UTC-3) y un historial en memoria que decide la copia programada como la implementación real.
/// </summary>
public class ConsultarBackupProgramadoTests
{
    private static readonly TimeZoneInfo ZonaLocal = TimeZoneInfo.CreateCustomTimeZone(
        "Prueba-03", TimeSpan.FromHours(-3), "Prueba UTC-3", "Prueba UTC-3");

    private static readonly DateOnly Hoy = new(2026, 11, 16);

    [Fact]
    public async Task Consultar_A_las_19_59_NoToca()
    {
        var historial = new HistorialProgramado();

        Assert.False(await Crear(historial, Local(Hoy, 19, 59)).TocaAsync());
        // Antes de la hora ni siquiera consulta el historial.
        Assert.Empty(historial.Consultadas);
    }

    [Fact]
    public async Task Consultar_A_las_20_00_SinCopiaProgramada_Toca()
    {
        var historial = new HistorialProgramado();
        // La copia de la mañana (al arrancar) no es la programada (D-20).
        historial.Filas.Add((Hoy, Local(Hoy, 9, 0)));

        Assert.True(await Crear(historial, Local(Hoy, 20, 0)).TocaAsync());
        Assert.Equal([Hoy], historial.Consultadas);
    }

    [Fact]
    public async Task Consultar_A_las_20_00_ConLaCopiaProgramadaAnotada_NoToca()
    {
        var historial = new HistorialProgramado();
        historial.Filas.Add((Hoy, Local(Hoy, 20, 0)));

        Assert.False(await Crear(historial, Local(Hoy, 20, 0)).TocaAsync());
        Assert.False(await Crear(historial, Local(Hoy, 23, 59)).TocaAsync());
    }

    [Fact]
    public async Task Consultar_CambioDeDia_ConsultaElDiaLocalNuevo()
    {
        var historial = new HistorialProgramado();
        historial.Filas.Add((Hoy, Local(Hoy, 20, 0)));

        // 00:00 del día siguiente en la óptica (03:00 UTC): todavía no es la hora del nuevo día.
        Assert.False(await Crear(historial, Local(Hoy.AddDays(1), 0, 0)).TocaAsync());

        // 20:00 del día siguiente: la copia programada de ayer no cuenta para hoy.
        Assert.True(await Crear(historial, Local(Hoy.AddDays(1), 20, 0)).TocaAsync());

        // 22:30 locales es 01:30 UTC del otro día: la fecha que consulta es la local, no la UTC.
        historial.Consultadas.Clear();
        Assert.True(await Crear(historial, Local(Hoy.AddDays(1), 22, 30)).TocaAsync());
        Assert.Equal([Hoy.AddDays(1)], historial.Consultadas);
    }

    private static ConsultarBackupProgramado Crear(HistorialProgramado historial, DateTimeOffset ahoraUtc) =>
        new(historial, new RelojLocal(ahoraUtc));

    private static DateTimeOffset Local(DateOnly fecha, int hora, int minuto) =>
        new DateTimeOffset(fecha.ToDateTime(new TimeOnly(hora, minuto)), ZonaLocal.BaseUtcOffset).ToUniversalTime();

    private sealed class RelojLocal(DateTimeOffset ahoraUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ahoraUtc;

        public override TimeZoneInfo LocalTimeZone => ZonaLocal;
    }

    /// <summary>Las filas del historial; la copia programada es una fila de esa fecha hecha desde las 20:00 locales.</summary>
    private sealed class HistorialProgramado : IHistorialBackupRepository
    {
        public List<(DateOnly Fecha, DateTimeOffset MomentoUtc)> Filas { get; } = [];

        public List<DateOnly> Consultadas { get; } = [];

        public void Agregar(DateOnly fecha, DateTimeOffset momentoUtc) =>
            throw new NotSupportedException("La consulta no agrega.");

        public Task<DateOnly?> UltimaFechaHastaAsync(DateOnly hoy, CancellationToken cancelacion = default) =>
            throw new NotSupportedException("La consulta no lee el último backup.");

        public Task<bool> HuboCopiaProgramadaAsync(DateOnly fecha, CancellationToken cancelacion = default)
        {
            Consultadas.Add(fecha);
            var desde = fecha.ToDateTime(CopiaBackup.HoraProgramada);
            var hasta = fecha.AddDays(1).ToDateTime(TimeOnly.MinValue);
            return Task.FromResult(Filas.Any(f => f.Fecha == fecha
                && TimeZoneInfo.ConvertTime(f.MomentoUtc, ZonaLocal).DateTime is var local
                && local >= desde && local < hasta));
        }
    }
}
