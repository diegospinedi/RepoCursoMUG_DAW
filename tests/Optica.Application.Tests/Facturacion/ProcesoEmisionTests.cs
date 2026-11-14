using Optica.Application.Comun;
using Optica.Application.Facturacion;
using Optica.Domain.Facturacion;
using Optica.Domain.Presupuestos;

namespace Optica.Application.Tests.Facturacion;

/// <summary>
/// Spec FEAT-008, Block 4: <see cref="ProcesoEmision"/> con el turno del candado ya tomado y el plazo recibido (D-09,
/// D-10), con dobles en memoria y reloj controlado. Datos del cliente ficticios (D-50 de FEAT-007).
/// </summary>
public sealed class ProcesoEmisionTests
{
    /// <summary>31/10/2026 02:00 UTC: 30/10/2026 23:00 en Argentina (UTC−3).</summary>
    private static readonly DateTimeOffset Ahora = new(2026, 10, 31, 2, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly Hoy = new(2026, 10, 30);

    private readonly AlmacenFacturacion _almacen = new();
    private readonly RelojControlado _reloj = new(Ahora);
    private readonly RegistroEmision _registro = new();
    private readonly AutorizadorDoble _autorizador;
    private readonly ProcesoEmision _proceso;

    public ProcesoEmisionTests()
    {
        _autorizador = new AutorizadorDoble(_almacen);
        _proceso = new ProcesoEmision(
            _almacen, _almacen, _almacen, _almacen, _almacen, _autorizador, _reloj, _registro);
    }

    /// <summary>Arma la solicitud del presupuesto con la configuración vigente y emite con un plazo de 30 s.</summary>
    private async Task<ResultadoEmision> EmitirAsync(Presupuesto presupuesto, EmisionPendiente? reemplazada = null)
    {
        var solicitud = ReglasEmision.Armar(presupuesto, _almacen.Configuracion, ConceptoFactura.Productos, Hoy)
            .Solicitud!;
        using var plazo = PlazoEmision.Iniciar(_autorizador.TiempoMaximo, _reloj);
        using var turno = new TurnoDePrueba();
        return await _proceso.EjecutarAsync(presupuesto, solicitud, turno, plazo, reemplazada);
    }

    private static void AfirmarConflicto(ResultadoEmision resultado, MotivoConflictoEmision motivo)
    {
        Assert.Equal(TipoResultadoEmision.Conflicto, resultado.Tipo);
        Assert.Equal(motivo, resultado.Motivo);
    }

    /// <summary>
    /// AC-01: el último autorizado de la Factura B es el 124 y el total es $1.815: antes de enviar la solicitud queda
    /// registrada la pendiente con el número 125, Factura B, $1.815 y el presupuesto. El próximo número se pide antes de
    /// grabar nada y fuera de toda transacción; la autorización recibe el 125.
    /// </summary>
    [Fact]
    public async Task Emitir_RegistraNumero125AntesDeEnviar()
    {
        var presupuesto = _almacen.Sembrar(1, EstadoPresupuesto.Final, Presupuestos_.Dni, 1815m);
        var alPedirNumero = (Pendientes: -1, EnTransaccion: true);
        _autorizador.ResponderProximo = (_, _, _) =>
        {
            alPedirNumero = (_almacen.Pendientes.Count, _almacen.EnTransaccion);
            return Task.FromResult<ResultadoProximoNumero>(new ResultadoProximoNumero.Numero(125));
        };
        List<EmisionPendiente> alEnviar = [];
        _autorizador.Responder = (solicitud, _) =>
        {
            alEnviar = [.. _almacen.Pendientes];
            return Task.FromResult<ResultadoAutorizacion>(
                new ResultadoAutorizacion.Autorizada(125, "75123456789012", solicitud.Fecha.AddDays(10)));
        };

        var resultado = await EmitirAsync(presupuesto);

        Assert.Equal((0, false), alPedirNumero);
        var registrada = Assert.Single(alEnviar);
        Assert.Equal(1, registrada.PresupuestoNumero);
        Assert.Equal(125, registrada.Numero);
        Assert.Equal(TipoComprobante.FacturaB, registrada.Tipo);
        Assert.Equal(3, registrada.PuntoVenta);
        Assert.Equal(1815m, registrada.Total);
        Assert.Equal(ConceptoFactura.Productos, registrada.Concepto);
        Assert.Equal(1815m, registrada.Importes!.Total);
        Assert.False(registrada.ReceptorIdentificado);
        Assert.Equal(EntornoArca.Simulado, registrada.Entorno);
        Assert.Equal(Ahora, registrada.Momento);
        Assert.Equal([125L], _autorizador.Numeros);
        Assert.Equal([false], _autorizador.DentroDeTransaccion);
        Assert.True(resultado.EsEmitida);
        Assert.Equal("Factura B 0003-00000125", resultado.Factura.Comprobante);
        Assert.Equal(125, Assert.Single(_almacen.Facturas).Numero);
        Assert.Empty(_almacen.Pendientes);
    }

    /// <summary>
    /// Sad path: el próximo número no se obtiene (WSAA, configuración, red): "no enviada", sin pendiente, sin abrir
    /// ninguna transacción y sin pedir la autorización.
    /// </summary>
    [Fact]
    public async Task Emitir_ProximoNumeroFalla_NoEnviadaSinPendiente()
    {
        var presupuesto = _almacen.Sembrar(1);
        _autorizador.ResponderProximo = (_, _, _) => Task.FromResult<ResultadoProximoNumero>(
            new ResultadoProximoNumero.NoEnviada("No se pudo obtener el último número de ARCA."));

        var resultado = await EmitirAsync(presupuesto);

        AfirmarConflicto(resultado, MotivoConflictoEmision.NoEnviada);
        Assert.Equal(1, _autorizador.PedidosDeNumero);
        Assert.Equal(0, _autorizador.Llamadas);
        Assert.Empty(_almacen.Pendientes);
        Assert.Empty(_almacen.Facturas);
        Assert.Empty(_almacen.TokensDeTransaccion);
    }

    /// <summary>
    /// D-11 (sad path): en el modo reemplazo de la verificación, si el próximo número falla, la pendiente vieja (con su
    /// observación) queda tal cual y el resultado es "no enviada".
    /// </summary>
    [Fact]
    public async Task Emitir_ReemplazoProximoNumeroFalla_PendienteViejaIntacta()
    {
        var presupuesto = _almacen.Sembrar(1);
        var vieja = _almacen.SembrarPendiente(presupuesto);
        var observacion = _almacen.SembrarObservacion(vieja);
        _autorizador.ResponderProximo = (_, _, _) => Task.FromResult<ResultadoProximoNumero>(
            new ResultadoProximoNumero.NoEnviada("No se pudo obtener el último número de ARCA."));

        var resultado = await EmitirAsync(presupuesto, vieja);

        AfirmarConflicto(resultado, MotivoConflictoEmision.NoEnviada);
        Assert.Same(vieja, Assert.Single(_almacen.Pendientes));
        Assert.Same(observacion, Assert.Single(_almacen.Observaciones));
        Assert.Equal(0, _autorizador.Llamadas);
    }

    /// <summary>
    /// D-10 (timeout, sad path): el pedido del próximo número consumió el plazo hasta dejar menos de 10 s. La pendiente
    /// con número se grabó, el margen no alcanza justo antes de FECAESolicitar: "no enviada", sin pedir la autorización,
    /// y la pendiente recién grabada se quita.
    /// </summary>
    [Fact]
    public async Task Emitir_MargenInsuficiente_NoEnviadaYQuitaPendiente()
    {
        var presupuesto = _almacen.Sembrar(1);
        _autorizador.ResponderProximo = (_, _, _) =>
        {
            _reloj.Avanzar(TimeSpan.FromSeconds(21));
            return Task.FromResult<ResultadoProximoNumero>(new ResultadoProximoNumero.Numero(125));
        };

        var resultado = await EmitirAsync(presupuesto);

        AfirmarConflicto(resultado, MotivoConflictoEmision.NoEnviada);
        Assert.Equal(0, _autorizador.Llamadas);
        Assert.Empty(_almacen.Pendientes);
        var accesos = _almacen.Accesos.ToList();
        Assert.Contains("AgregarPendiente", accesos);
        Assert.True(
            accesos.IndexOf("AgregarPendiente") < accesos.IndexOf("QuitarPendiente"),
            "La pendiente no se grabó antes de quitarla.");
    }

    /// <summary>
    /// AC-12 (timeout, sad path): la emisión de la verificación (modo reemplazo, la vieja con el 125 y una observación)
    /// registra el 126, lo envía y ARCA no responde en 30 s: la pendiente queda con el número nuevo, la observación se
    /// quitó, y no se reintenta.
    /// </summary>
    [Fact]
    public async Task Emitir_SinRespuesta_PendienteConNumero()
    {
        var presupuesto = _almacen.Sembrar(1, EstadoPresupuesto.Final, Presupuestos_.Dni, 1815m);
        var solicitud = ReglasEmision.Armar(presupuesto, _almacen.Configuracion, ConceptoFactura.Productos, Hoy)
            .Solicitud!;
        var vieja = new EmisionPendiente(1, Ahora.AddMinutes(-5), solicitud, 125, EntornoArca.Simulado);
        _almacen.Pendientes.Add(vieja);
        _almacen.SembrarObservacion(vieja);
        _autorizador.ResponderProximo = (_, _, _) =>
            Task.FromResult<ResultadoProximoNumero>(new ResultadoProximoNumero.Numero(126));
        _autorizador.Responder = (_, token) => AutorizadorDoble.NoResponderAsync(token);

        var emision = EmitirAsync(presupuesto, vieja);
        await _autorizador.Llamado.Task.WaitAsync(TimeSpan.FromSeconds(10));
        _reloj.Avanzar(TimeSpan.FromSeconds(30));
        var resultado = await emision.WaitAsync(TimeSpan.FromSeconds(10));

        AfirmarConflicto(resultado, MotivoConflictoEmision.SinRespuesta);
        var pendiente = Assert.Single(_almacen.Pendientes);
        Assert.Equal(126, pendiente.Numero);
        Assert.Equal(1815m, pendiente.Total);
        Assert.Empty(_almacen.Observaciones);
        Assert.Equal([126L], _autorizador.Numeros);
        Assert.Equal(1, _autorizador.PedidosDeNumero);
        Assert.Empty(_almacen.Facturas);
    }

    /// <summary>
    /// D-11 y D-24: en el modo reemplazo, autorizada, se quitan la observación y la pendiente vieja, se graba la factura
    /// con el número nuevo y no queda ninguna pendiente.
    /// </summary>
    [Fact]
    public async Task Emitir_ReemplazoAutorizada_FacturaConNumeroNuevoSinPendienteNiObservacion()
    {
        var presupuesto = _almacen.Sembrar(1);
        var vieja = _almacen.SembrarPendiente(presupuesto);
        _almacen.SembrarObservacion(vieja);
        List<long?> alEnviar = [];
        _autorizador.Responder = (solicitud, _) =>
        {
            alEnviar = [.. _almacen.Pendientes.Select(pendiente => pendiente.Numero)];
            return Task.FromResult<ResultadoAutorizacion>(
                new ResultadoAutorizacion.Autorizada(125, "75123456789012", solicitud.Fecha.AddDays(10)));
        };

        var resultado = await EmitirAsync(presupuesto, vieja);

        Assert.True(resultado.EsEmitida);
        Assert.Equal([125L], alEnviar);
        Assert.Equal(125, Assert.Single(_almacen.Facturas).Numero);
        Assert.Empty(_almacen.Pendientes);
        Assert.Empty(_almacen.Observaciones);
    }

    /// <summary>
    /// Sad path: en el modo reemplazo, si la pendiente guardada ya no es la que se verificó (otra con otro número), no se
    /// toca nada y no se envía: conflicto <c>pendiente</c>.
    /// </summary>
    [Fact]
    public async Task Emitir_ReemplazoDeOtraPendiente_ConflictoSinEnviar()
    {
        var presupuesto = _almacen.Sembrar(1);
        var guardada = _almacen.SembrarPendiente(presupuesto);
        var solicitud = ReglasEmision.Armar(presupuesto, _almacen.Configuracion, ConceptoFactura.Productos, Hoy)
            .Solicitud!;
        var verificada = new EmisionPendiente(1, Ahora.AddMinutes(-5), solicitud, 99, EntornoArca.Simulado);

        var resultado = await EmitirAsync(presupuesto, verificada);

        AfirmarConflicto(resultado, MotivoConflictoEmision.Pendiente);
        Assert.Same(guardada, Assert.Single(_almacen.Pendientes));
        Assert.Equal(0, _autorizador.Llamadas);
    }

    /// <summary>
    /// D-28 (sad path): en el modo reemplazo, si ya no hay pendiente guardada (se descartó mientras tanto), "no enviada"
    /// sin grabar ninguna pendiente y sin pedir la autorización.
    /// </summary>
    [Fact]
    public async Task Emitir_ReemplazoSinPendienteGuardada_NoEnviadaSinGrabar()
    {
        var presupuesto = _almacen.Sembrar(1);
        var solicitud = ReglasEmision.Armar(presupuesto, _almacen.Configuracion, ConceptoFactura.Productos, Hoy)
            .Solicitud!;
        var verificada = new EmisionPendiente(1, Ahora.AddMinutes(-5), solicitud, 99, EntornoArca.Simulado);

        var resultado = await EmitirAsync(presupuesto, verificada);

        AfirmarConflicto(resultado, MotivoConflictoEmision.NoEnviada);
        Assert.Empty(_almacen.Pendientes);
        Assert.DoesNotContain("AgregarPendiente", _almacen.Accesos);
        Assert.Equal(0, _autorizador.Llamadas);
    }

    /// <summary>
    /// D-11 y D-28 (sad path): en el modo reemplazo, la baja de la vieja (con su observación) se confirma y el alta de la
    /// nueva choca por unicidad: se revierte la transacción entera, la vieja y su observación quedan, y no se envía.
    /// </summary>
    [Fact]
    public async Task Emitir_ReemplazoConflictoAlGrabarLaNueva_RevierteYLaViejaQueda()
    {
        var presupuesto = _almacen.Sembrar(1);
        var vieja = _almacen.SembrarPendiente(presupuesto);
        var observacion = _almacen.SembrarObservacion(vieja);
        _almacen.FallaAlConfirmarAlta =
            () => new ConflictoAlConfirmarException("Conflicto.", MotivoConflicto.Unicidad);

        var resultado = await EmitirAsync(presupuesto, vieja);

        AfirmarConflicto(resultado, MotivoConflictoEmision.Pendiente);
        Assert.Same(vieja, Assert.Single(_almacen.Pendientes));
        Assert.Same(observacion, Assert.Single(_almacen.Observaciones));
        Assert.Equal(0, _autorizador.Llamadas);
    }

    /// <summary>
    /// D-29 (timeout, sad path): en el modo reemplazo el próximo número consumió el plazo; la nueva se grabó en lugar de
    /// la vieja, y sin margen se quita como en la emisión normal: "no enviada", sin pendiente ni observación.
    /// </summary>
    [Fact]
    public async Task Emitir_ReemplazoMargenInsuficiente_NoEnviadaSinPendiente()
    {
        var presupuesto = _almacen.Sembrar(1);
        var vieja = _almacen.SembrarPendiente(presupuesto);
        _almacen.SembrarObservacion(vieja);
        _autorizador.ResponderProximo = (_, _, _) =>
        {
            _reloj.Avanzar(TimeSpan.FromSeconds(21));
            return Task.FromResult<ResultadoProximoNumero>(new ResultadoProximoNumero.Numero(126));
        };

        var resultado = await EmitirAsync(presupuesto, vieja);

        AfirmarConflicto(resultado, MotivoConflictoEmision.NoEnviada);
        Assert.Equal(0, _autorizador.Llamadas);
        Assert.Empty(_almacen.Pendientes);
        Assert.Empty(_almacen.Observaciones);
        Assert.Contains("AgregarPendiente", _almacen.Accesos);
    }

    /// <summary>
    /// D-24 (sad path): rechazada en el modo normal con una observación que apareció sobre la pendiente nueva, la baja la
    /// quita antes: sin pendiente, sin observación y con el código del rechazo.
    /// </summary>
    [Fact]
    public async Task Emitir_RechazadaConObservacion_QuitaObservacionYPendiente()
    {
        var presupuesto = _almacen.Sembrar(1);
        _autorizador.Responder = (_, _) =>
        {
            _almacen.SembrarObservacion(_almacen.Pendientes.Single());
            return Task.FromResult<ResultadoAutorizacion>(
                new ResultadoAutorizacion.Rechazada(10016, "El número no es el próximo."));
        };

        var resultado = await EmitirAsync(presupuesto);

        AfirmarConflicto(resultado, MotivoConflictoEmision.Rechazada);
        Assert.Empty(_almacen.Pendientes);
        Assert.Empty(_almacen.Observaciones);
    }

    /// <summary>Sad path: sin turno o sin plazo, el proceso no arranca (el candado lo toma quien llama, D-09).</summary>
    [Fact]
    public async Task Emitir_SinTurno_Lanza()
    {
        var presupuesto = _almacen.Sembrar(1);
        var solicitud = ReglasEmision.Armar(presupuesto, _almacen.Configuracion, ConceptoFactura.Productos, Hoy)
            .Solicitud!;
        using var plazo = PlazoEmision.Iniciar(_autorizador.TiempoMaximo, _reloj);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _proceso.EjecutarAsync(presupuesto, solicitud, null!, plazo));
        Assert.Equal(0, _autorizador.PedidosDeNumero);
    }

    /// <summary>Un turno del candado que la prueba ya tiene tomado.</summary>
    private sealed class TurnoDePrueba : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
