using Microsoft.Extensions.Logging;
using Optica.Application.Backup;
using Optica.Application.Comun;
using Optica.Application.Tests.Catalogo;
using Optica.Domain.Backup;

namespace Optica.Application.Tests.Backup;

/// <summary>
/// Spec FEAT-010, Block 4 (FR-05 a FR-07, FR-10 a FR-12; AC-04, AC-13; D-11, D-12, D-20, D-23): el caso de uso del backup
/// con un copiador, un almacén, un historial y un candado falsos, sin disco ni base. Las pruebas con SQLite real y
/// archivos están en Optica.Infrastructure.Tests (<c>CopiadorBackupSqliteTests</c>, <c>AlmacenCopiasArchivoTests</c>).
/// Rutas ficticias armadas sobre la carpeta temporal, con valores testigo que no deben llegar al log.
/// </summary>
public class HacerBackupTests
{
    /// <summary>Un valor testigo dentro de la ruta: si aparece en el log, la ruta se filtró (D-12, R-05).</summary>
    private const string Testigo = "Juana Testigo Quimera";

    /// <summary>Una zona fija UTC-3, sin horario de verano, como la de la óptica.</summary>
    private static readonly TimeZoneInfo ZonaLocal = TimeZoneInfo.CreateCustomTimeZone(
        "Prueba-03", TimeSpan.FromHours(-3), "Prueba UTC-3", "Prueba UTC-3");

    private static readonly string Carpeta =
        Path.Combine(Path.GetTempPath(), "optica-pruebas-backup", "Pendrive " + Testigo, "Backups");

    private static readonly DateOnly Hoy = new(2026, 10, 30);

    [Fact]
    public async Task Backup_CopiaConFechaDelDia()
    {
        // 22:30 del 30/10 en la óptica (UTC-3) es 01:30 del 31/10 en UTC: la fecha de la copia es la local (FR-06).
        var momentoUtc = new DateTimeOffset(2026, 10, 31, 1, 30, 0, TimeSpan.Zero);
        var escenario = new Escenario(momentoUtc);

        var resultado = await escenario.Crear().EjecutarAsync(MotivoBackup.Programado);

        // AC-04: la copia del 30/10/2026, en la carpeta configurada, con el nombre del dominio.
        Assert.Equal(EstadoBackup.Hecho, resultado.Estado);
        Assert.Equal(Hoy, resultado.Fecha);
        Assert.Null(resultado.Falla);
        Assert.Equal([Hoy], escenario.Almacen.Copias);
        Assert.Empty(escenario.Almacen.Temporales);
        Assert.Equal(Path.Combine(Carpeta, CopiaBackup.NombreParcial(Hoy)), Assert.Single(escenario.Copiador.Destinos));

        // El historial anota la fecha del nombre y el momento UTC, confirmado con la unidad de trabajo.
        Assert.Equal([(Hoy, momentoUtc)], escenario.Historial.Confirmadas);
        Assert.Empty(escenario.Historial.Pendientes);

        // El orden: huérfanos, copia, verificación, renombre, historial y recién entonces la retención (FR-12).
        Assert.Equal(
            ["borrar-temporales", "copiar", "verificar", "renombrar", "confirmar-historial", "listar", "borrar-copias"],
            escenario.Bitacora);
    }

    [Theory]
    [InlineData(PuntoDeFalla.BorrarHuerfanos, FallaBackup.CarpetaInaccesible)]
    [InlineData(PuntoDeFalla.Copia, FallaBackup.Copia)]
    [InlineData(PuntoDeFalla.VerificacionNoIntegra, FallaBackup.Integridad)]
    [InlineData(PuntoDeFalla.VerificacionLanza, FallaBackup.Integridad)]
    [InlineData(PuntoDeFalla.Renombre, FallaBackup.Renombre)]
    [InlineData(PuntoDeFalla.Historial, FallaBackup.Historial)]
    public async Task Backup_Falla_NoBorraCopias(PuntoDeFalla punto, FallaBackup falla)
    {
        var escenario = new Escenario(new DateTimeOffset(2026, 10, 30, 23, 0, 0, TimeSpan.Zero));
        // Cinco meses de copias: la retención borraría la mayoría si se aplicara.
        var existentes = Enumerable.Range(0, 150).Select(d => Hoy.AddDays(-1 - d)).ToList();
        escenario.Almacen.Copias.AddRange(existentes);
        escenario.Fallar(punto);

        var resultado = await escenario.Crear().EjecutarAsync(MotivoBackup.Programado);

        // AC-13: falla del día, y ninguna copia existente se borra.
        Assert.Equal(EstadoBackup.Fallido, resultado.Estado);
        Assert.Equal(falla, resultado.Falla);
        Assert.DoesNotContain("borrar-copias", escenario.Bitacora);
        Assert.Superset(existentes.ToHashSet(), escenario.Almacen.Copias.ToHashSet());
        Assert.Empty(escenario.Almacen.Temporales);

        // Una copia que no pasó la verificación no cuenta (FR-07): no se renombra ni se anota.
        // La falla queda en el log con su tipo, como error.
        var error = Assert.Single(escenario.Registros.Entradas, e => e.Nivel == LogLevel.Error);
        Assert.Equal(falla.ToString(), error.Valores["Falla"]?.ToString());
        Assert.Null(error.Error);
        Assert.Empty(escenario.Historial.Confirmadas);

        if (punto != PuntoDeFalla.Historial)
        {
            Assert.DoesNotContain(Hoy, escenario.Almacen.Copias);
            Assert.Contains("no cuenta como backup", error.Mensaje, StringComparison.Ordinal);
        }
        else
        {
            // La copia verificada ya estaba renombrada: queda en la carpeta, sin anotar y sin retención; el log lo dice así.
            Assert.Contains(Hoy, escenario.Almacen.Copias);
            Assert.Contains("la copia quedó en la carpeta", error.Mensaje, StringComparison.Ordinal);
            Assert.Contains("no se anotó en el historial", error.Mensaje, StringComparison.Ordinal);
            Assert.Contains("no se aplicó la retención", error.Mensaje, StringComparison.Ordinal);
        }

        // El candado se libera aunque falle.
        Assert.Equal(0, escenario.Candado.Tomados);
    }

    [Fact]
    public async Task Backup_AlIniciarConLaDeAyer_NoRepite()
    {
        var nueve = new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero); // 09:00 en la óptica

        // Con la de ayer en el historial, al arrancar no copia ni toca la carpeta (FR-08, D-20).
        var conAyer = new Escenario(nueve);
        conAyer.Historial.Confirmadas.Add((Hoy.AddDays(-1), nueve.AddHours(-13)));
        var resultado = await conAyer.Crear().EjecutarAsync(MotivoBackup.AlIniciar);
        Assert.Equal(EstadoBackup.NoHaciaFalta, resultado.Estado);
        Assert.Empty(conAyer.Bitacora);
        Assert.Single(conAyer.Historial.Confirmadas);

        // Con la de hoy (una copia al arrancar ya hecha, D-11), tampoco.
        var conHoy = new Escenario(nueve);
        conHoy.Historial.Confirmadas.Add((Hoy, nueve.AddHours(-1)));
        Assert.Equal(EstadoBackup.NoHaciaFalta, (await conHoy.Crear().EjecutarAsync(MotivoBackup.AlIniciar)).Estado);
        Assert.Empty(conHoy.Bitacora);

        // Con la última de anteayer falta la de ayer: copia con la fecha de hoy (D-11).
        var conAnteayer = new Escenario(nueve);
        conAnteayer.Historial.Confirmadas.Add((Hoy.AddDays(-2), nueve.AddDays(-2)));
        var hecho = await conAnteayer.Crear().EjecutarAsync(MotivoBackup.AlIniciar);
        Assert.Equal(EstadoBackup.Hecho, hecho.Estado);
        Assert.Equal(Hoy, hecho.Fecha);
        Assert.Equal([Hoy], conAnteayer.Almacen.Copias);

        // Sin ningún backup también falta.
        var sinNinguno = new Escenario(nueve);
        Assert.Equal(EstadoBackup.Hecho, (await sinNinguno.Crear().EjecutarAsync(MotivoBackup.AlIniciar)).Estado);

        // "No repetir" vale solo al arrancar: el programado copia aunque esté la de ayer (D-20).
        var programado = new Escenario(nueve.AddHours(11));
        programado.Historial.Confirmadas.Add((Hoy.AddDays(-1), nueve.AddHours(-13)));
        Assert.Equal(EstadoBackup.Hecho, (await programado.Crear().EjecutarAsync(MotivoBackup.Programado)).Estado);

        // La regla es una sola, reutilizable por el estado (Block 6): falta si no hay ni ayer ni hoy.
        Assert.True(ReglaBackupDiario.FaltaLaDeAyer(null, Hoy));
        Assert.True(ReglaBackupDiario.FaltaLaDeAyer(Hoy.AddDays(-2), Hoy));
        Assert.False(ReglaBackupDiario.FaltaLaDeAyer(Hoy.AddDays(-1), Hoy));
        Assert.False(ReglaBackupDiario.FaltaLaDeAyer(Hoy, Hoy));
        Assert.False(await ReglaBackupDiario.FaltaLaDeAyerAsync(conAnteayer.Historial, Hoy.AddDays(1)));
        Assert.True(await ReglaBackupDiario.FaltaLaDeAyerAsync(new HistorialEnMemoria([]), Hoy));
    }

    [Fact]
    public async Task Backup_Registro_SinRutas()
    {
        var momentoUtc = new DateTimeOffset(2026, 10, 30, 23, 0, 0, TimeSpan.Zero);

        // Éxito: fecha UTC, motivo y fecha de la copia; nunca la ruta.
        var exito = new Escenario(momentoUtc);
        await exito.Crear().EjecutarAsync(MotivoBackup.Programado);
        Assert.Contains(HacerBackup.CategoriaRegistro, exito.Registros.Categorias);
        Assert.StartsWith("Optica.Application.Backup", HacerBackup.CategoriaRegistro, StringComparison.Ordinal);
        var hecho = Assert.Single(exito.Registros.Entradas, e => e.Nivel == LogLevel.Information);
        Assert.Equal(momentoUtc.UtcDateTime, hecho.Valores["FechaHoraUtc"]);
        Assert.Equal(nameof(MotivoBackup.Programado), hecho.Valores["Motivo"]?.ToString());
        // El inicio, en Debug, con el motivo y la fecha UTC, antes del resultado.
        var inicio = exito.Registros.Entradas[0];
        Assert.Equal(LogLevel.Debug, inicio.Nivel);
        Assert.Contains("iniciado", inicio.Mensaje, StringComparison.Ordinal);
        Assert.Equal(nameof(MotivoBackup.Programado), inicio.Valores["Motivo"]?.ToString());
        Assert.Equal(momentoUtc.UtcDateTime, inicio.Valores["FechaHoraUtc"]);
        AfirmarSinRutas(exito.Registros);

        // Un temporal que no se pudo borrar después de la falla es una copia completa con datos personales fuera de la
        // retención: advertencia, sin ruta.
        var temporalRetenido = new Escenario(momentoUtc);
        temporalRetenido.Fallar(PuntoDeFalla.Copia);
        temporalRetenido.Almacen.FallarAlLimpiar = true;
        await temporalRetenido.Crear().EjecutarAsync(MotivoBackup.Programado);
        Assert.Single(temporalRetenido.Registros.Entradas, e => e.Nivel == LogLevel.Error);
        var retenido = Assert.Single(temporalRetenido.Registros.Entradas, e => e.Nivel == LogLevel.Warning);
        Assert.Equal(nameof(DirectoryNotFoundException), retenido.Valores["TipoError"]?.ToString());
        AfirmarSinRutas(temporalRetenido.Registros);

        // Falla con una excepción cuyo texto lleva la ruta: solo el tipo de falla y el tipo de error llegan al log.
        var falla = new Escenario(momentoUtc);
        falla.Fallar(PuntoDeFalla.Copia);
        await falla.Crear().EjecutarAsync(MotivoBackup.AlIniciar);
        var error = Assert.Single(falla.Registros.Entradas, e => e.Nivel == LogLevel.Error);
        Assert.Equal(nameof(IOException), error.Valores["TipoError"]?.ToString());
        Assert.Equal(nameof(MotivoBackup.AlIniciar), error.Valores["Motivo"]?.ToString());
        Assert.Equal(momentoUtc.UtcDateTime, error.Valores["FechaHoraUtc"]);
        Assert.Null(error.Error);
        AfirmarSinRutas(falla.Registros);

        // La retención que no se puede aplicar no cambia el resultado: advertencia sin ruta.
        var retencion = new Escenario(momentoUtc);
        retencion.Almacen.FallarAlBorrarCopias = true;
        Assert.Equal(EstadoBackup.Hecho, (await retencion.Crear().EjecutarAsync(MotivoBackup.Programado)).Estado);
        Assert.Single(retencion.Registros.Entradas, e => e.Nivel == LogLevel.Warning);
        AfirmarSinRutas(retencion.Registros);

        // Sin ubicación no hace nada y lo deja en Debug (D-25).
        var sinUbicacion = new Escenario(momentoUtc, conUbicacion: false);
        Assert.Equal(
            EstadoBackup.SinUbicacion, (await sinUbicacion.Crear().EjecutarAsync(MotivoBackup.Programado)).Estado);
        Assert.Empty(sinUbicacion.Bitacora);
        Assert.Equal(LogLevel.Debug, Assert.Single(sinUbicacion.Registros.Entradas).Nivel);

        // Con el candado tomado, el pedido no hace nada (D-22).
        var enCurso = new Escenario(momentoUtc);
        using (enCurso.Candado.IntentarTomar())
        {
            Assert.Equal(EstadoBackup.EnCurso, (await enCurso.Crear().EjecutarAsync(MotivoBackup.Programado)).Estado);
        }

        Assert.Empty(enCurso.Bitacora);
        // En Debug: el programador reintenta cada 5 minutos y no hace falta llenar el Registro de eventos.
        Assert.Equal(LogLevel.Debug, Assert.Single(enCurso.Registros.Entradas).Nivel);
        AfirmarSinRutas(enCurso.Registros);
    }

    [Theory]
    [InlineData(MomentoCancelacion.AntesDeCopiar)]
    [InlineData(MomentoCancelacion.DespuesDeCopiar)]
    [InlineData(MomentoCancelacion.DespuesDelRenombre)]
    public async Task Backup_Cancelacion_BorraElTemporalOAnotaLaCopia(MomentoCancelacion momento)
    {
        var escenario = new Escenario(new DateTimeOffset(2026, 10, 30, 23, 0, 0, TimeSpan.Zero));
        var existentes = Enumerable.Range(0, 30).Select(d => Hoy.AddDays(-1 - d)).ToList();
        escenario.Almacen.Copias.AddRange(existentes);
        using var cancelacion = new CancellationTokenSource();
        switch (momento)
        {
            case MomentoCancelacion.AntesDeCopiar:
                escenario.Copiador.AntesDeCopiar = cancelacion.Cancel;
                break;
            case MomentoCancelacion.DespuesDeCopiar:
                escenario.Copiador.DespuesDeCopiar = cancelacion.Cancel;
                break;
            case MomentoCancelacion.DespuesDelRenombre:
                escenario.Almacen.DespuesDeRenombrar = cancelacion.Cancel;
                break;
        }

        var caso = escenario.Crear();

        if (momento != MomentoCancelacion.DespuesDelRenombre)
        {
            // Antes del renombre la cancelación se propaga, borra el temporal y no deja copia, historial ni retención.
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => caso.EjecutarAsync(MotivoBackup.Programado, cancelacion.Token));
            Assert.Empty(escenario.Almacen.Temporales);
            Assert.DoesNotContain(Hoy, escenario.Almacen.Copias);
            Assert.Empty(escenario.Historial.Confirmadas);
            Assert.Equal(existentes, escenario.Almacen.Copias);
        }
        else
        {
            // Con la copia verificada ya renombrada, anotarla no se cancela: queda en el historial.
            var resultado = await caso.EjecutarAsync(MotivoBackup.Programado, cancelacion.Token);
            Assert.Equal(EstadoBackup.Hecho, resultado.Estado);
            Assert.Contains(Hoy, escenario.Almacen.Copias);
            Assert.Equal([Hoy], escenario.Historial.Confirmadas.Select(c => c.Fecha));
        }

        Assert.Equal(0, escenario.Candado.Tomados);
    }

    public enum MomentoCancelacion
    {
        AntesDeCopiar,
        DespuesDeCopiar,
        DespuesDelRenombre,
    }

    private static void AfirmarSinRutas(FabricaRegistrosFalsa registros)
    {
        Assert.NotEmpty(registros.Entradas);
        foreach (var entrada in registros.Entradas)
        {
            var texto = entrada.Mensaje + " " + string.Join(" ", entrada.Valores.Values);
            Assert.DoesNotContain(Testigo, texto, StringComparison.Ordinal);
            Assert.DoesNotContain("optica-pruebas-backup", texto, StringComparison.Ordinal);
            Assert.DoesNotContain(Carpeta, texto, StringComparison.Ordinal);
            Assert.Null(entrada.Error);
        }
    }

    public enum PuntoDeFalla
    {
        BorrarHuerfanos,
        Copia,
        VerificacionNoIntegra,
        VerificacionLanza,
        Renombre,
        Historial,
    }

    /// <summary>Los dobles de un backup, con una bitácora común del orden de las llamadas.</summary>
    private sealed class Escenario
    {
        private readonly DateTimeOffset _ahoraUtc;
        private readonly bool _conUbicacion;

        public Escenario(DateTimeOffset ahoraUtc, bool conUbicacion = true)
        {
            _ahoraUtc = ahoraUtc;
            _conUbicacion = conUbicacion;
            Almacen = new AlmacenEnMemoria(Bitacora);
            Copiador = new CopiadorFalso(Bitacora, Almacen);
            Historial = new HistorialEnMemoria(Bitacora);
        }

        public List<string> Bitacora { get; } = [];

        public AlmacenEnMemoria Almacen { get; }

        public CopiadorFalso Copiador { get; }

        public HistorialEnMemoria Historial { get; }

        public CandadoEnMemoria Candado { get; } = new();

        public FabricaRegistrosFalsa Registros { get; } = new();

        public void Fallar(PuntoDeFalla punto)
        {
            switch (punto)
            {
                case PuntoDeFalla.BorrarHuerfanos:
                    Almacen.FallarAlBorrarTemporales = 1;
                    break;
                case PuntoDeFalla.Copia:
                    Copiador.FallarAlCopiar = true;
                    break;
                case PuntoDeFalla.VerificacionNoIntegra:
                    Copiador.Integra = false;
                    break;
                case PuntoDeFalla.VerificacionLanza:
                    Copiador.FallarAlVerificar = true;
                    break;
                case PuntoDeFalla.Renombre:
                    Almacen.FallarAlRenombrar = true;
                    break;
                case PuntoDeFalla.Historial:
                    Historial.FallarAlConfirmar = true;
                    break;
            }
        }

        public HacerBackup Crear() => new(
            new UbicacionFija(_conUbicacion ? Carpeta : null),
            Historial,
            Historial,
            Copiador,
            Almacen,
            Candado,
            new RelojLocal(_ahoraUtc),
            Registros);
    }

    private sealed class RelojLocal(DateTimeOffset ahoraUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ahoraUtc;

        public override TimeZoneInfo LocalTimeZone => ZonaLocal;
    }

    private sealed class UbicacionFija(string? ruta) : IUbicacionBackupRepository
    {
        public Task<UbicacionBackup?> ObtenerAsync(CancellationToken cancelacion = default) =>
            Task.FromResult(ruta is null ? null : UbicacionBackup.Leer(ruta));

        public void Reemplazar(UbicacionBackup ubicacion) =>
            throw new NotSupportedException("El backup no graba la ubicación.");
    }

    /// <summary>Las copias y los temporales de la carpeta, por fecha y nombre; anota cada llamada.</summary>
    private sealed class AlmacenEnMemoria(List<string> bitacora) : IAlmacenCopias
    {
        public List<DateOnly> Copias { get; } = [];

        public HashSet<string> Temporales { get; } = new(StringComparer.Ordinal);

        /// <summary>Cuántas veces más falla el borrado de temporales (la primera, al empezar).</summary>
        public int FallarAlBorrarTemporales { get; set; }

        /// <summary>Falla el borrado de temporales que viene después de la copia (la limpieza tras una falla).</summary>
        public bool FallarAlLimpiar { get; set; }

        public bool FallarAlRenombrar { get; set; }

        public bool FallarAlBorrarCopias { get; set; }

        public string RutaTemporal(string carpeta, DateOnly fecha) =>
            Path.Combine(carpeta, CopiaBackup.NombreParcial(fecha));

        public IReadOnlyList<DateOnly> ListarCopias(string carpeta)
        {
            Assert.Equal(Carpeta, carpeta);
            bitacora.Add("listar");
            return [.. Copias];
        }

        public void RenombrarTemporal(string carpeta, DateOnly fecha)
        {
            bitacora.Add("renombrar");
            if (FallarAlRenombrar)
            {
                throw new IOException("No se pudo renombrar en " + carpeta);
            }

            Assert.True(Temporales.Remove(RutaTemporal(carpeta, fecha)));
            if (!Copias.Contains(fecha))
            {
                Copias.Add(fecha);
            }

            DespuesDeRenombrar?.Invoke();
        }

        /// <summary>Corre con el temporal ya renombrado a la copia (por ejemplo, cancelar).</summary>
        public Action? DespuesDeRenombrar { get; set; }

        /// <summary>Sin temporales huérfanos que recuperar: la recuperación se prueba con archivos reales.</summary>
        public IReadOnlyList<DateOnly> ListarTemporalesSinCopia(string carpeta) => [];

        public int BorrarCopias(string carpeta, IReadOnlyCollection<DateOnly> fechas)
        {
            bitacora.Add("borrar-copias");
            if (FallarAlBorrarCopias)
            {
                throw new UnauthorizedAccessException("Sin permiso en " + carpeta);
            }

            return Copias.RemoveAll(fechas.Contains);
        }

        public void BorrarTemporales(string carpeta)
        {
            Assert.Equal(Carpeta, carpeta);
            bitacora.Add("borrar-temporales");

            if (FallarAlBorrarTemporales > 0 || (FallarAlLimpiar && bitacora.Contains("copiar")))
            {
                FallarAlBorrarTemporales = Math.Max(0, FallarAlBorrarTemporales - 1);
                throw new DirectoryNotFoundException("No existe " + carpeta);
            }

            Temporales.Clear();
        }
    }

    private sealed class CopiadorFalso(List<string> bitacora, AlmacenEnMemoria almacen) : ICopiadorBackup
    {
        public List<string> Destinos { get; } = [];

        public bool FallarAlCopiar { get; set; }

        public bool FallarAlVerificar { get; set; }

        public bool Integra { get; set; } = true;

        /// <summary>Corre al entrar a la copia, antes de escribir nada (por ejemplo, cancelar).</summary>
        public Action? AntesDeCopiar { get; set; }

        /// <summary>Corre con el temporal ya escrito.</summary>
        public Action? DespuesDeCopiar { get; set; }

        public Task CopiarAsync(string rutaTemporal, CancellationToken cancelacion = default)
        {
            bitacora.Add("copiar");
            AntesDeCopiar?.Invoke();
            cancelacion.ThrowIfCancellationRequested();
            Destinos.Add(rutaTemporal);
            almacen.Temporales.Add(rutaTemporal);
            if (FallarAlCopiar)
            {
                throw new IOException("El dispositivo no está listo: " + rutaTemporal);
            }

            DespuesDeCopiar?.Invoke();
            return Task.CompletedTask;
        }

        public Task<bool> VerificarAsync(string rutaTemporal, CancellationToken cancelacion = default)
        {
            bitacora.Add("verificar");
            cancelacion.ThrowIfCancellationRequested();
            Assert.Contains(rutaTemporal, almacen.Temporales);
            if (FallarAlVerificar)
            {
                throw new IOException("No se pudo leer " + rutaTemporal);
            }

            return Task.FromResult(Integra);
        }
    }

    /// <summary>Historial y unidad de trabajo: lo agregado queda pendiente hasta confirmar.</summary>
    private sealed class HistorialEnMemoria(List<string> bitacora) : IHistorialBackupRepository, IUnidadDeTrabajo
    {
        public List<(DateOnly Fecha, DateTimeOffset MomentoUtc)> Pendientes { get; } = [];

        public List<(DateOnly Fecha, DateTimeOffset MomentoUtc)> Confirmadas { get; } = [];

        public bool FallarAlConfirmar { get; set; }

        public void Agregar(DateOnly fecha, DateTimeOffset momentoUtc) => Pendientes.Add((fecha, momentoUtc));

        public Task<DateOnly?> UltimaFechaHastaAsync(DateOnly hoy, CancellationToken cancelacion = default) =>
            Task.FromResult(Confirmadas.Where(c => c.Fecha <= hoy).Select(c => (DateOnly?)c.Fecha).Max());

        public Task<bool> HuboCopiaProgramadaAsync(DateOnly fecha, CancellationToken cancelacion = default) =>
            throw new NotSupportedException("El backup no consulta la copia programada.");

        public Task<T> EjecutarEnTransaccionAsync<T>(
            Func<CancellationToken, Task<T>> operacion, CancellationToken cancelacion = default) =>
            operacion(cancelacion);

        public Task ConfirmarAsync(CancellationToken cancelacion = default)
        {
            bitacora.Add("confirmar-historial");
            // Como EF: un token cancelado no graba nada.
            cancelacion.ThrowIfCancellationRequested();
            if (FallarAlConfirmar)
            {
                Pendientes.Clear();
                throw new InvalidOperationException("La base no está disponible.");
            }

            Confirmadas.AddRange(Pendientes);
            Pendientes.Clear();
            return Task.CompletedTask;
        }
    }

    /// <summary>Un turno a la vez, como el singleton de Infrastructure; cuenta los turnos tomados sin liberar.</summary>
    private sealed class CandadoEnMemoria : ICandadoBackup
    {
        private int _tomado;

        public int Tomados => Volatile.Read(ref _tomado);

        public IDisposable? IntentarTomar() =>
            Interlocked.CompareExchange(ref _tomado, 1, 0) == 0 ? new Turno(this) : null;

        private sealed class Turno(CandadoEnMemoria candado) : IDisposable
        {
            public void Dispose() => Interlocked.Exchange(ref candado._tomado, 0);
        }
    }
}
