using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Optica.Domain.Backup;
using Optica.Infrastructure.Persistencia;

namespace Optica.Infrastructure.Tests.Persistencia;

/// <summary>
/// Spec FEAT-010, Block 1 (FR-09, AC-18, D-08, D-11, D-21): el historial de backups verificados en la tabla
/// <c>BackupRealizado</c>, de solo agregar. La zona horaria local llega por el <see cref="TimeProvider"/>, así la regla
/// de las 20:00 no depende de la zona de la máquina que corre la prueba.
/// </summary>
public class HistorialBackupRepositoryTests
{
    private const int SqliteRestriccionTrigger = 1811; // SQLITE_CONSTRAINT_TRIGGER

    /// <summary>Una zona fija UTC-3, sin horario de verano, como la de la óptica.</summary>
    private static readonly TimeZoneInfo ZonaLocal = TimeZoneInfo.CreateCustomTimeZone(
        "Prueba-03", TimeSpan.FromHours(-3), "Prueba UTC-3", "Prueba UTC-3");

    [Fact]
    public async Task Historial_UpdateOBorrado_RechazadoPorTrigger()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = FacturasPrueba.CadenaDe(baseTemporal);
        var contexto = baseTemporal.CrearContexto();
        var historial = new HistorialBackupRepository(contexto, new RelojConZona(ZonaLocal));

        // El alta solo se registra en el contexto compartido (como DescarteEmision): se graba al confirmar la unidad de
        // trabajo, con el momento en UTC ISO 8601.
        historial.Agregar(new DateOnly(2026, 10, 29), new DateTimeOffset(2026, 10, 29, 20, 3, 0, TimeSpan.FromHours(-3)));
        Assert.Empty(FacturasPrueba.Filas(cadena, "SELECT Id FROM BackupRealizado"));
        await new UnidadDeTrabajoEf(contexto).ConfirmarAsync();
        Assert.Equal(
            ["1", "2026-10-29", "2026-10-29T23:03:00.0000000Z"],
            Assert.Single(FacturasPrueba.Filas(cadena, "SELECT Id, Fecha, MomentoUtc FROM BackupRealizado")));

        // Solo agregar: la base rechaza modificar o borrar una fila, aunque se esquive el repositorio.
        foreach (var sentencia in new[]
                 {
                     "UPDATE BackupRealizado SET Fecha = '2026-10-30'",
                     "UPDATE BackupRealizado SET MomentoUtc = '2026-10-30T23:00:00.0000000Z'",
                     "DELETE FROM BackupRealizado",
                 })
        {
            var error = Assert.Throws<SqliteException>(() => FacturasPrueba.Ejecutar(cadena, sentencia));
            Assert.Equal(SqliteRestriccionTrigger, error.SqliteExtendedErrorCode);
            Assert.Contains("registro_cerrado", error.Message, StringComparison.Ordinal);
        }

        Assert.Single(FacturasPrueba.Filas(cadena, "SELECT Id FROM BackupRealizado"));

        // La fecha es yyyy-MM-dd (CHECK GLOB), la segunda barrera detrás del repositorio.
        var check = Assert.Throws<SqliteException>(() => FacturasPrueba.Ejecutar(cadena,
            "INSERT INTO BackupRealizado (Fecha, MomentoUtc) VALUES ('2026-1-5', '2026-01-05T23:00:00.0000000Z')"));
        Assert.Contains("CHECK constraint failed: CK_BackupRealizado_Fecha", check.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Historial_UltimaFecha()
    {
        using var baseTemporal = new BaseTemporal();
        var historial = Historial(baseTemporal);

        // Sin backups no hay última fecha (AC-10).
        Assert.Null(await historial.UltimaFechaHastaAsync(new DateOnly(2026, 10, 31)));

        // El 28/10 una copia al arrancar (09:30 local) y otra a las 19:59; el 29/10 la de las 20:00; con el reloj
        // adelantado, una del 20/11; y una segunda copia del 29/10 (D-20: la de las 20:00 reemplaza la del día).
        await AgregarYConfirmar(
            baseTemporal,
            (new DateOnly(2026, 10, 28), Local(2026, 10, 28, 9, 30)),
            (new DateOnly(2026, 10, 28), Local(2026, 10, 28, 19, 59)),
            (new DateOnly(2026, 10, 29), Local(2026, 10, 29, 20, 0)),
            (new DateOnly(2026, 11, 20), Local(2026, 11, 20, 20, 0)),
            (new DateOnly(2026, 10, 29), Local(2026, 10, 29, 21, 15)));

        // AC-18: el 31/10, sin el backup del 30, el último verificado es el del 29/10, leído de la base aunque la
        // ubicación no esté accesible. La copia futura no cuenta (D-21: la mayor fecha que no pasa de hoy).
        var otroAlcance = Historial(baseTemporal);
        Assert.Equal(new DateOnly(2026, 10, 29), await otroAlcance.UltimaFechaHastaAsync(new DateOnly(2026, 10, 31)));
        Assert.Equal(new DateOnly(2026, 10, 29), await otroAlcance.UltimaFechaHastaAsync(new DateOnly(2026, 10, 29)));
        Assert.Equal(new DateOnly(2026, 10, 28), await otroAlcance.UltimaFechaHastaAsync(new DateOnly(2026, 10, 28)));
        Assert.Null(await otroAlcance.UltimaFechaHastaAsync(new DateOnly(2026, 10, 27)));
        Assert.Equal(new DateOnly(2026, 11, 20), await otroAlcance.UltimaFechaHastaAsync(new DateOnly(2026, 11, 20)));

        // La copia programada de un día es una fila de esa fecha con momento local desde las 20:00 (D-11, D-20).
        Assert.True(await otroAlcance.HuboCopiaProgramadaAsync(new DateOnly(2026, 10, 29)));
        Assert.False(await otroAlcance.HuboCopiaProgramadaAsync(new DateOnly(2026, 10, 28)));
        Assert.False(await otroAlcance.HuboCopiaProgramadaAsync(new DateOnly(2026, 10, 30)));

        // Con otra zona, la misma fila es de otra hora local: la regla usa la zona inyectada, no la de la máquina. En
        // UTC+3, las copias de las 20:00 y las 21:15 del 29/10 (UTC-3) son de la madrugada del 30/10: no cuentan.
        var zonaMas3 = TimeZoneInfo.CreateCustomTimeZone("Prueba+03", TimeSpan.FromHours(3), "Prueba UTC+3", "Prueba UTC+3");
        var conOtraZona = new HistorialBackupRepository(baseTemporal.CrearContexto(), new RelojConZona(zonaMas3));
        Assert.False(await conOtraZona.HuboCopiaProgramadaAsync(new DateOnly(2026, 10, 29)));
        Assert.False(await conOtraZona.HuboCopiaProgramadaAsync(new DateOnly(2026, 10, 28)));

        // Registrado en la raíz de composición, por alcance como el resto de los repositorios.
        using var alcance = baseTemporal.Servicios.CreateScope();
        Assert.IsType<HistorialBackupRepository>(
            alcance.ServiceProvider.GetRequiredService<IHistorialBackupRepository>());
    }

    [Fact]
    public async Task Historial_HuboCopiaProgramada_SoloEseDiaDesdeLas20()
    {
        using var baseTemporal = new BaseTemporal();

        // La copia programada de un día es la de ese día con momento local en [fecha 20:00, fecha+1 00:00).
        await AgregarYConfirmar(
            baseTemporal,
            (new DateOnly(2026, 10, 27), Local(2026, 10, 27, 20, 0)),
            (new DateOnly(2026, 10, 28), Local(2026, 10, 28, 23, 59).AddSeconds(59)),
            (new DateOnly(2026, 10, 29), Local(2026, 10, 30, 0, 0)),
            (new DateOnly(2026, 10, 30), Local(2026, 10, 30, 19, 59).AddSeconds(59)),
            (new DateOnly(2026, 11, 2), Local(2026, 11, 1, 21, 0)));

        var lector = Historial(baseTemporal);

        // Límite inferior incluido y último segundo del día.
        Assert.True(await lector.HuboCopiaProgramadaAsync(new DateOnly(2026, 10, 27)));
        Assert.True(await lector.HuboCopiaProgramadaAsync(new DateOnly(2026, 10, 28)));

        // Límite superior excluido: una fila del 29/10 con momento de las 00:00 del 30/10 (reloj o zona corridos) no es
        // la de las 20:00 del 29/10.
        Assert.False(await lector.HuboCopiaProgramadaAsync(new DateOnly(2026, 10, 29)));

        // Un segundo antes de las 20:00 no cuenta, ni un momento del día anterior aunque sea después de las 20:00.
        Assert.False(await lector.HuboCopiaProgramadaAsync(new DateOnly(2026, 10, 30)));
        Assert.False(await lector.HuboCopiaProgramadaAsync(new DateOnly(2026, 11, 2)));

        // Un día sin filas.
        Assert.False(await lector.HuboCopiaProgramadaAsync(new DateOnly(2026, 10, 31)));
    }

    private static HistorialBackupRepository Historial(BaseTemporal baseTemporal) =>
        new(baseTemporal.CrearContexto(), new RelojConZona(ZonaLocal));

    /// <summary>Registra las filas en un contexto propio y las confirma con la unidad de trabajo, como Application.</summary>
    private static async Task AgregarYConfirmar(BaseTemporal baseTemporal, params (DateOnly Fecha, DateTimeOffset Momento)[] filas)
    {
        var contexto = baseTemporal.CrearContexto();
        var historial = new HistorialBackupRepository(contexto, new RelojConZona(ZonaLocal));
        foreach (var (fecha, momento) in filas)
        {
            historial.Agregar(fecha, momento);
        }

        await new UnidadDeTrabajoEf(contexto).ConfirmarAsync();
    }

    private static DateTimeOffset Local(int anio, int mes, int dia, int hora, int minuto) =>
        new(anio, mes, dia, hora, minuto, 0, TimeSpan.FromHours(-3));

    /// <summary>El reloj del sistema con la zona local fijada por la prueba.</summary>
    private sealed class RelojConZona(TimeZoneInfo zona) : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => zona;
    }
}
