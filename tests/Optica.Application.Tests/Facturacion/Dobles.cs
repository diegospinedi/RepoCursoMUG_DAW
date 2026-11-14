using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Optica.Application.Comun;
using Optica.Application.Facturacion;
using Optica.Domain.Configuracion;
using Optica.Domain.Facturacion;
using Optica.Domain.Presupuestos;

namespace Optica.Application.Tests.Facturacion;

/// <summary>
/// Presupuestos, configuración, facturas y emisiones pendientes en una sola unidad de trabajo en memoria, como el
/// contexto compartido real (D-09, ADR-021): <see cref="Agregar(Factura)"/>, <see cref="Agregar(EmisionPendiente)"/> y
/// <see cref="Quitar"/> registran y <see cref="ConfirmarAsync"/> guarda todo o nada. Una segunda factura o una segunda
/// pendiente para el mismo presupuesto fallan con <see cref="MotivoConflicto.Unicidad"/>, y un conflicto descarta lo
/// registrado, como <c>UnidadDeTrabajoEf</c>. Quitar y volver a agregar la misma pendiente sin confirmar lanza (D-49).
/// </summary>
/// <remarks>
/// Anota cada acceso en <see cref="Accesos"/> y si hay una transacción abierta (<see cref="EnTransaccion"/>), para fijar
/// que la llamada a ARCA corre fuera de toda transacción. Guarda el token que recibió cada transacción en
/// <see cref="TokensDeTransaccion"/>.
/// </remarks>
internal sealed class AlmacenFacturacion :
    IPresupuestoRepository, IConfiguracionNegocioRepository, IFacturaRepository, IEmisionPendienteRepository,
    IUnidadDeTrabajo
{
    private readonly List<Factura> _facturasRegistradas = [];
    private readonly List<EmisionPendiente> _pendientesRegistradas = [];
    private readonly List<long> _pendientesQuitadas = [];

    public List<Presupuesto> Presupuestos { get; } = [];

    public ConfiguracionNegocio Configuracion { get; set; } = Configuraciones.Vigente();

    public List<Factura> Facturas { get; } = [];

    public List<EmisionPendiente> Pendientes { get; } = [];

    public ConcurrentQueue<string> Accesos { get; } = new();

    public List<CancellationToken> TokensDeTransaccion { get; } = [];

    public bool EnTransaccion { get; private set; }

    /// <summary>Si no es null, confirmar una factura lanza esta excepción (D-31).</summary>
    public Func<Exception>? FallaAlConfirmarFactura { get; set; }

    /// <summary>Si no es null, confirmar solo la baja de una pendiente (sin factura) lanza esta excepción.</summary>
    public Func<Exception>? FallaAlConfirmarBaja { get; set; }

    // Presupuestos: solo la lectura por número.

    public Task<Presupuesto?> ObtenerAsync(long numero, CancellationToken cancelacion = default)
    {
        cancelacion.ThrowIfCancellationRequested();
        Accesos.Enqueue("ObtenerPresupuesto");
        return Task.FromResult(Presupuestos.SingleOrDefault(presupuesto => presupuesto.Numero == numero));
    }

    public Task<long?> ObtenerUltimoNumeroAsync(CancellationToken cancelacion = default) =>
        throw new NotSupportedException();

    public void Agregar(Presupuesto presupuesto) => throw new NotSupportedException();

    public void Actualizar(Presupuesto original, Presupuesto modificado, long versionOriginal) =>
        throw new NotSupportedException();

    public void CambiarEstado(Presupuesto presupuesto, long versionOriginal) => throw new NotSupportedException();

    public Task<PaginaPresupuestos> BuscarAsync(
        FiltroPresupuestos filtro, int pagina, int tamanoPagina, CancellationToken cancelacion = default) =>
        throw new NotSupportedException();

    // Configuración: la vigente, leída en cada llamada.

    Task<ConfiguracionNegocio> IConfiguracionNegocioRepository.ObtenerAsync(CancellationToken cancelacion)
    {
        cancelacion.ThrowIfCancellationRequested();
        Accesos.Enqueue("ObtenerConfiguracion");
        return Task.FromResult(Configuracion);
    }

    public void Reemplazar(ConfiguracionNegocio nueva) => throw new NotSupportedException();

    // Facturas.

    public void Agregar(Factura factura)
    {
        ArgumentNullException.ThrowIfNull(factura);
        Accesos.Enqueue("AgregarFactura");
        _facturasRegistradas.Add(factura);
    }

    public Task<Factura?> ObtenerPorPresupuestoAsync(long presupuestoNumero, CancellationToken cancelacion = default)
    {
        Accesos.Enqueue("ObtenerFactura");
        return Task.FromResult(Facturas.SingleOrDefault(factura => factura.PresupuestoNumero == presupuestoNumero));
    }

    public Task<NumeroComprobante?> ObtenerComprobantePorPresupuestoAsync(
        long presupuestoNumero, CancellationToken cancelacion = default)
    {
        Accesos.Enqueue("ObtenerComprobante");
        return Task.FromResult(
            Facturas.SingleOrDefault(factura => factura.PresupuestoNumero == presupuestoNumero)?.Comprobante);
    }

    public Task<bool> ExisteParaPresupuestoAsync(long presupuestoNumero, CancellationToken cancelacion = default)
    {
        cancelacion.ThrowIfCancellationRequested();
        Accesos.Enqueue("ExisteFactura");
        return Task.FromResult(Facturas.Any(factura => factura.PresupuestoNumero == presupuestoNumero));
    }

    // Emisiones pendientes.

    public void Agregar(EmisionPendiente pendiente)
    {
        ArgumentNullException.ThrowIfNull(pendiente);
        if (_pendientesQuitadas.Contains(pendiente.PresupuestoNumero))
        {
            throw new InvalidOperationException("La pendiente se quitó y la baja no se confirmó.");
        }

        Accesos.Enqueue("AgregarPendiente");
        _pendientesRegistradas.Add(pendiente);
    }

    public void Quitar(long presupuestoNumero)
    {
        Accesos.Enqueue("QuitarPendiente");
        _pendientesQuitadas.Add(presupuestoNumero);
    }

    public Task<bool> ExisteAsync(long presupuestoNumero, CancellationToken cancelacion = default)
    {
        cancelacion.ThrowIfCancellationRequested();
        Accesos.Enqueue("ExistePendiente");
        return Task.FromResult(Pendientes.Any(pendiente => pendiente.PresupuestoNumero == presupuestoNumero));
    }

    // Unidad de trabajo.

    public async Task<T> EjecutarEnTransaccionAsync<T>(
        Func<CancellationToken, Task<T>> operacion, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(operacion);
        if (EnTransaccion)
        {
            throw new InvalidOperationException("La operación en transacción no se anida.");
        }

        TokensDeTransaccion.Add(cancelacion);
        var facturasAlEntrar = new List<Factura>(Facturas);
        var pendientesAlEntrar = new List<EmisionPendiente>(Pendientes);
        EnTransaccion = true;
        Accesos.Enqueue("Transaccion");
        try
        {
            var resultado = await operacion(cancelacion);
            if (HayRegistrados)
            {
                throw new InvalidOperationException("La operación terminó con cambios sin confirmar.");
            }

            return resultado;
        }
        catch
        {
            Facturas.Clear();
            Facturas.AddRange(facturasAlEntrar);
            Pendientes.Clear();
            Pendientes.AddRange(pendientesAlEntrar);
            Descartar();
            throw;
        }
        finally
        {
            EnTransaccion = false;
            Accesos.Enqueue("FinTransaccion");
        }
    }

    public Task ConfirmarAsync(CancellationToken cancelacion = default)
    {
        Accesos.Enqueue("Confirmar");
        try
        {
            if (_facturasRegistradas.Count > 0 && FallaAlConfirmarFactura is { } falla)
            {
                throw falla();
            }

            if (_facturasRegistradas.Count == 0 && _pendientesQuitadas.Count > 0 && FallaAlConfirmarBaja is { } fallaBaja)
            {
                throw fallaBaja();
            }

            var pendientes = Pendientes.Where(p => !_pendientesQuitadas.Contains(p.PresupuestoNumero)).ToList();
            if (_facturasRegistradas.Any(nueva => Facturas.Any(f => f.PresupuestoNumero == nueva.PresupuestoNumero))
                || _pendientesRegistradas.Any(nueva => pendientes.Any(p => p.PresupuestoNumero == nueva.PresupuestoNumero)))
            {
                throw new ConflictoAlConfirmarException("Conflicto al confirmar.", MotivoConflicto.Unicidad);
            }

            Facturas.AddRange(_facturasRegistradas);
            pendientes.AddRange(_pendientesRegistradas);
            Pendientes.Clear();
            Pendientes.AddRange(pendientes);
        }
        finally
        {
            Descartar();
        }

        return Task.CompletedTask;
    }

    private bool HayRegistrados =>
        _facturasRegistradas.Count > 0 || _pendientesRegistradas.Count > 0 || _pendientesQuitadas.Count > 0;

    private void Descartar()
    {
        _facturasRegistradas.Clear();
        _pendientesRegistradas.Clear();
        _pendientesQuitadas.Clear();
    }

    /// <summary>Guarda un presupuesto ya confirmado con <paramref name="totales"/> como Precio Final de sus líneas.</summary>
    public Presupuesto Sembrar(
        long numero, EstadoPresupuesto estado = EstadoPresupuesto.Final, string dni = Presupuestos_.Dni,
        params decimal[] totales)
    {
        var presupuesto = Presupuestos_.Crear(numero, estado, dni, totales.Length == 0 ? [1000m, 1000m, 1000m] : totales);
        Presupuestos.Add(presupuesto);
        return presupuesto;
    }

    /// <summary>Guarda una factura ya autorizada para el presupuesto, como si existiera de antes.</summary>
    public Factura SembrarFactura(Presupuesto presupuesto, long numero = 7)
    {
        var solicitud = ReglasEmision.Armar(presupuesto, Configuraciones.Vigente(), ConceptoFactura.Productos, Fecha)
            .Solicitud!;
        var factura = Factura.Emitida(
            presupuesto, solicitud, numero, "71234567890123", Fecha.AddDays(10), EntornoArca.Simulado);
        Facturas.Add(factura);
        return factura;
    }

    /// <summary>Guarda una emisión pendiente para el presupuesto, como si existiera de antes.</summary>
    public EmisionPendiente SembrarPendiente(Presupuesto presupuesto)
    {
        var solicitud = ReglasEmision.Armar(presupuesto, Configuraciones.Vigente(), ConceptoFactura.Productos, Fecha)
            .Solicitud!;
        var pendiente = new EmisionPendiente(
            presupuesto.Numero, new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero), solicitud, EntornoArca.Simulado);
        Pendientes.Add(pendiente);
        return pendiente;
    }

    private static readonly DateOnly Fecha = new(2026, 10, 1);
}

/// <summary>Presupuestos de prueba con datos ficticios (D-50: el DNI 00718294 no es de una persona).</summary>
internal static class Presupuestos_
{
    public const string Dni = "00718294";
    public const string Apellido = "Zq7Ficticiez";
    public const string Nombre = "Zq7Prueboncia";
    public const string Email = "zq7@example.com";
    public const string Telefono = "011-5555-0199";

    public static Presupuesto Crear(long numero, EstadoPresupuesto estado, string dni, IReadOnlyList<decimal> totales) =>
        new(
            numero,
            new DateOnly(2026, 10, 1),
            estado,
            new ClientePresupuesto(Apellido, Nombre, dni, "Calle Ficticia 123", Email, Telefono),
            [.. totales.Select((total, indice) =>
                new LineaPresupuesto(indice + 1L, $"Artículo {indice + 1}", total, 1, 0m, total, total))],
            version: 1);
}

/// <summary>Configuraciones de negocio de prueba.</summary>
internal static class Configuraciones
{
    /// <summary>Responsable Inscripto, alícuota 21, tope $100.000 y punto de venta 3.</summary>
    public static ConfiguracionNegocio Vigente(
        decimal alicuota = 21m,
        CondicionFiscal condicion = CondicionFiscal.ResponsableInscripto,
        decimal? tope = 100_000m,
        int? puntoVenta = 3) =>
        new(alicuota, condicion, 1m, tope, puntoVenta);
}

/// <summary>
/// Autorizador de ARCA de prueba: por defecto autoriza con números 125, 126… Anota cada solicitud, el token que
/// recibió y si había una transacción abierta en el almacén al llamarlo. <see cref="Responder"/> permite otra respuesta.
/// </summary>
internal sealed class AutorizadorDoble(AlmacenFacturacion? almacen = null) : IAutorizadorComprobantes
{
    private long _proximo = 125;

    public EntornoArca Entorno { get; init; } = EntornoArca.Simulado;

    public TimeSpan TiempoMaximo { get; init; } = TimeSpan.FromSeconds(30);

    public List<SolicitudComprobante> Solicitudes { get; } = [];

    public List<CancellationToken> Tokens { get; } = [];

    public List<bool> DentroDeTransaccion { get; } = [];

    /// <summary>Las pendientes guardadas en el almacén en el momento de cada llamada.</summary>
    public List<int> PendientesAlLlamar { get; } = [];

    /// <summary>Se completa en la primera llamada.</summary>
    public TaskCompletionSource Llamado { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Func<SolicitudComprobante, CancellationToken, Task<ResultadoAutorizacion>>? Responder { get; set; }

    public int Llamadas => Solicitudes.Count;

    public Task<ResultadoAutorizacion> AutorizarAsync(SolicitudComprobante solicitud, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        Solicitudes.Add(solicitud);
        Tokens.Add(cancelacion);
        DentroDeTransaccion.Add(almacen?.EnTransaccion ?? false);
        PendientesAlLlamar.Add(almacen?.Pendientes.Count ?? 0);
        Llamado.TrySetResult();
        if (Responder is { } responder)
        {
            return responder(solicitud, cancelacion);
        }

        return Task.FromResult<ResultadoAutorizacion>(
            new ResultadoAutorizacion.Autorizada(_proximo++, "71234567890123", solicitud.Fecha.AddDays(10)));
    }

    /// <summary>Espera la cancelación del token y responde "sin respuesta", como ARCA que no contesta.</summary>
    public static async Task<ResultadoAutorizacion> NoResponderAsync(CancellationToken cancelacion)
    {
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancelacion);
        }
        catch (OperationCanceledException)
        {
        }

        return new ResultadoAutorizacion.SinRespuesta();
    }
}

/// <summary>
/// Candado de emisión de prueba. Libre por defecto; <see cref="AlTomar"/> corre antes de entregar el turno (por
/// ejemplo, para adelantar el reloj como si se hubiera esperado otra emisión). Con <see cref="Ocupado"/>, espera la
/// cancelación del token y devuelve null, como si otra emisión no lo liberara.
/// </summary>
internal sealed class CandadoDoble : ICandadoEmision
{
    public Action? AlTomar { get; set; }

    public bool Ocupado { get; set; }

    public List<CancellationToken> Tokens { get; } = [];

    public int Tomados { get; private set; }

    public int Liberados { get; private set; }

    /// <summary>Si es verdadero, liberar el turno lanza <see cref="ObjectDisposedException"/>, como al apagar la Api.</summary>
    public bool LiberarLanza { get; set; }

    public TaskCompletionSource Pedido { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<IDisposable?> TomarAsync(CancellationToken cancelacion)
    {
        Tokens.Add(cancelacion);
        Pedido.TrySetResult();
        if (Ocupado)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancelacion);
            }
            catch (OperationCanceledException)
            {
            }

            return null;
        }

        AlTomar?.Invoke();
        Tomados++;
        return new Turno(this);
    }

    private sealed class Turno(CandadoDoble candado) : IDisposable
    {
        private bool _liberado;

        public void Dispose()
        {
            if (!_liberado)
            {
                _liberado = true;
                candado.Liberados++;
                if (candado.LiberarLanza)
                {
                    throw new ObjectDisposedException("SemaphoreSlim");
                }
            }
        }
    }
}

/// <summary>
/// Reloj controlado con zona horaria de Argentina (UTC−3): <see cref="Avanzar"/> mueve la hora y dispara los
/// temporizadores vencidos, incluidos los de un <see cref="CancellationTokenSource"/> creado con este reloj.
/// </summary>
internal sealed class RelojControlado(DateTimeOffset utc) : TimeProvider
{
    public static readonly TimeZoneInfo Argentina =
        TimeZoneInfo.CreateCustomTimeZone("Prueba/Argentina", TimeSpan.FromHours(-3), "Argentina", "Argentina");

    private readonly object _bloqueo = new();
    private readonly List<Temporizador> _temporizadores = [];
    private DateTimeOffset _utc = utc;

    public override TimeZoneInfo LocalTimeZone => Argentina;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_bloqueo)
        {
            return _utc;
        }
    }

    public override long GetTimestamp() => GetUtcNow().UtcTicks;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var temporizador = new Temporizador(this, callback, state);
        temporizador.Change(dueTime, period);
        return temporizador;
    }

    public void Avanzar(TimeSpan tiempo)
    {
        List<Temporizador> vencidos;
        lock (_bloqueo)
        {
            _utc += tiempo;
            vencidos = [.. _temporizadores.Where(t => t.Vence is { } vence && vence <= _utc)];
            foreach (var vencido in vencidos)
            {
                vencido.Vence = null;
            }
        }

        foreach (var vencido in vencidos)
        {
            vencido.Disparar();
        }
    }

    private sealed class Temporizador(RelojControlado reloj, TimerCallback callback, object? state) : ITimer
    {
        public DateTimeOffset? Vence { get; set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (reloj._bloqueo)
            {
                if (!reloj._temporizadores.Contains(this))
                {
                    reloj._temporizadores.Add(this);
                }

                Vence = dueTime == Timeout.InfiniteTimeSpan ? null : reloj._utc + dueTime;
            }

            return true;
        }

        public void Disparar() => callback(state);

        public void Dispose()
        {
            lock (reloj._bloqueo)
            {
                reloj._temporizadores.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>Registro en memoria del caso de uso: el mensaje formateado y las propiedades estructuradas.</summary>
internal sealed class RegistroEmision : ILogger<EmitirFactura>
{
    public ConcurrentQueue<(LogLevel Nivel, string Texto)> Lineas { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var propiedades = state is IEnumerable<KeyValuePair<string, object?>> pares
            ? string.Join("; ", pares.Select(par => $"{par.Key}={par.Value}"))
            : string.Empty;
        Lineas.Enqueue((logLevel, formatter(state, exception) + " | " + propiedades + " | " + exception));
    }

    public string Todo => string.Join(Environment.NewLine, Lineas.Select(linea => linea.Texto));
}
