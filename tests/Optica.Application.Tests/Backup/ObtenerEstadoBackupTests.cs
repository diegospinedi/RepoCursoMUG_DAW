using Optica.Application.Backup;
using Optica.Domain.Backup;

namespace Optica.Application.Tests.Backup;

/// <summary>
/// Spec FEAT-010, Block 6 (FR-09; AC-09, AC-10, AC-18; D-11, D-12, D-20, D-21, D-26): el estado del backup para el aviso
/// al ingresar, con un historial, una ubicación y un almacén falsos, sin disco ni base. La carpeta es ficticia, sobre la
/// carpeta temporal.
/// </summary>
public class ObtenerEstadoBackupTests
{
    /// <summary>Una zona fija UTC-3, sin horario de verano, como la de la óptica.</summary>
    private static readonly TimeZoneInfo ZonaLocal = TimeZoneInfo.CreateCustomTimeZone(
        "Prueba-03", TimeSpan.FromHours(-3), "Prueba UTC-3", "Prueba UTC-3");

    private static readonly string Carpeta = Path.Combine(Path.GetTempPath(), "optica-pruebas-estado", "Pendrive", "Backups");

    /// <summary>9:00 del 31/10/2026 en la óptica.</summary>
    private static readonly DateTimeOffset Ahora = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly Hoy = new(2026, 10, 31);

    [Fact]
    public async Task Estado_FaltaAyerConUltimo29()
    {
        // AC-09: el del 30/10 no se hizo (tampoco al iniciar) y el último es del 29/10.
        var historial = new HistorialFijo(new DateOnly(2026, 10, 29));
        var almacen = new AlmacenFalso();

        var estado = await Crear(historial, Carpeta, almacen).EjecutarAsync();

        Assert.Equal(new EstadoBackupVigente(true, new DateOnly(2026, 10, 29), EstadoBackupVigente.UbicacionElegida, true), estado);
        Assert.Equal("elegida", EstadoBackupVigente.UbicacionElegida);
        // El último se pide hasta hoy (D-21), con la fecha local y no la UTC.
        Assert.Equal([Hoy], historial.Consultadas);
        Assert.Equal([Carpeta], almacen.Consultadas);
    }

    [Fact]
    public async Task Estado_SinNingunBackup()
    {
        // AC-10: base nueva, sin backups y sin ubicación elegida.
        var almacen = new AlmacenFalso();

        var estado = await Crear(new HistorialFijo(null), ruta: null, almacen).EjecutarAsync();

        Assert.Equal(new EstadoBackupVigente(true, null, EstadoBackupVigente.UbicacionSinElegir, false), estado);
        Assert.Equal("sin_elegir", EstadoBackupVigente.UbicacionSinElegir);
        // Sin ubicación no hay carpeta que mirar.
        Assert.Empty(almacen.Consultadas);
    }

    [Fact]
    public async Task Estado_PendriveDesconectado_UltimoDelHistorial()
    {
        // AC-18: el pendrive está desconectado (o la carpeta no se puede leer); la fecha sale del historial, no de la
        // carpeta.
        var almacen = new AlmacenFalso { Legible = false };

        var estado = await Crear(new HistorialFijo(new DateOnly(2026, 10, 29)), Carpeta, almacen).EjecutarAsync();

        Assert.Equal(new EstadoBackupVigente(true, new DateOnly(2026, 10, 29), EstadoBackupVigente.UbicacionElegida, false), estado);
        Assert.Equal([Carpeta], almacen.Consultadas);
    }

    [Theory]
    [InlineData(2026, 10, 30)]
    [InlineData(2026, 10, 31)]
    public async Task Estado_ConLaDeAyerOLaDeHoy_NoFalta(int anio, int mes, int dia)
    {
        // D-11: la copia hecha hoy al arrancar cubre la de ayer; la regla es la misma del backup al iniciar (D-20).
        var ultimo = new DateOnly(anio, mes, dia);

        var estado = await Crear(new HistorialFijo(ultimo), Carpeta, new AlmacenFalso()).EjecutarAsync();

        Assert.False(estado.FaltaAyer);
        Assert.Equal(ultimo, estado.Ultimo);
        Assert.Equal(ReglaBackupDiario.FaltaLaDeAyer(ultimo, Hoy), estado.FaltaAyer);
    }

    [Fact]
    public async Task Estado_FechaLocal_NoLaUtc()
    {
        // 22:30 del 30/10 en la óptica es 01:30 del 31/10 en UTC: hoy es el 30/10, y una copia del 29/10 es la de ayer.
        var historial = new HistorialFijo(new DateOnly(2026, 10, 29));
        var estado = await new ObtenerEstadoBackup(
            historial,
            new UbicacionFija(Carpeta),
            new AlmacenFalso(),
            new RelojLocal(new DateTimeOffset(2026, 10, 31, 1, 30, 0, TimeSpan.Zero))).EjecutarAsync();

        Assert.False(estado.FaltaAyer);
        Assert.Equal([new DateOnly(2026, 10, 30)], historial.Consultadas);
    }

    private static ObtenerEstadoBackup Crear(HistorialFijo historial, string? ruta, AlmacenFalso almacen) =>
        new(historial, new UbicacionFija(ruta), almacen, new RelojLocal(Ahora));

    private sealed class RelojLocal(DateTimeOffset ahoraUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ahoraUtc;

        public override TimeZoneInfo LocalTimeZone => ZonaLocal;
    }

    /// <summary>Un historial cuyo último backup hasta cualquier fecha es <paramref name="ultimo"/>; anota las consultas.</summary>
    private sealed class HistorialFijo(DateOnly? ultimo) : IHistorialBackupRepository
    {
        public List<DateOnly> Consultadas { get; } = [];

        public void Agregar(DateOnly fecha, DateTimeOffset momentoUtc) =>
            throw new NotSupportedException("El estado no agrega.");

        public Task<DateOnly?> UltimaFechaHastaAsync(DateOnly hoy, CancellationToken cancelacion = default)
        {
            Consultadas.Add(hoy);
            return Task.FromResult(ultimo);
        }

        public Task<bool> HuboCopiaProgramadaAsync(DateOnly fecha, CancellationToken cancelacion = default) =>
            throw new NotSupportedException("El estado no consulta la copia programada.");
    }

    private sealed class UbicacionFija(string? ruta) : IUbicacionBackupRepository
    {
        public Task<UbicacionBackup?> ObtenerAsync(CancellationToken cancelacion = default) =>
            Task.FromResult(ruta is null ? null : UbicacionBackup.Leer(ruta));

        public void Reemplazar(UbicacionBackup ubicacion) => throw new NotSupportedException("El estado no graba.");
    }

    /// <summary>
    /// Un almacén que solo responde si la carpeta se puede leer (<see cref="Legible"/>; falso, como una carpeta
    /// desconectada o sin permiso). Cualquier otro método lanza: el estado no lista ni toca archivos.
    /// </summary>
    private sealed class AlmacenFalso : IAlmacenCopias
    {
        public bool Legible { get; init; } = true;

        public List<string> Consultadas { get; } = [];

        public bool SePuedeLeer(string carpeta)
        {
            Consultadas.Add(carpeta);
            return Legible;
        }

        public IReadOnlyList<DateOnly> ListarCopias(string carpeta) => throw NoUsar();

        public string RutaTemporal(string carpeta, DateOnly fecha) => throw NoUsar();

        public IReadOnlyList<DateOnly> ListarTemporalesSinCopia(string carpeta) => throw NoUsar();

        public void RenombrarTemporal(string carpeta, DateOnly fecha) => throw NoUsar();

        public int BorrarCopias(string carpeta, IReadOnlyCollection<DateOnly> fechas) => throw NoUsar();

        public void BorrarTemporales(string carpeta) => throw NoUsar();

        private static NotSupportedException NoUsar() => new("El estado solo mira si la carpeta se puede leer.");
    }
}
