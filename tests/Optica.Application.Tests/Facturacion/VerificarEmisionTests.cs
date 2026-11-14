using Optica.Application.Facturacion;
using Optica.Domain.Facturacion;
using Optica.Domain.Presupuestos;

namespace Optica.Application.Tests.Facturacion;

/// <summary>
/// Spec FEAT-008, Block 5: <see cref="VerificarEmision"/> con dobles en memoria y reloj controlado (la espera de 60 s y
/// los plazos de 30 s sin esperas reales). Datos del cliente ficticios (D-50 de FEAT-007).
/// </summary>
public sealed class VerificarEmisionTests
{
    /// <summary>31/10/2026 02:00 UTC: 30/10/2026 23:00 en Argentina (UTC−3).</summary>
    private static readonly DateTimeOffset Ahora = new(2026, 10, 31, 2, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly Hoy = new(2026, 10, 30);

    private const string CaeArca = "75123456789012";

    private readonly AlmacenFacturacion _almacen = new();
    private readonly RelojControlado _reloj = new(Ahora);
    private readonly RegistroEmision _registro = new();
    private readonly CandadoDoble _candado = new();
    private readonly AutorizadorDoble _autorizador;
    private readonly VerificarEmision _caso;

    public VerificarEmisionTests()
    {
        _autorizador = new AutorizadorDoble(_almacen);
        _caso = Crear(_autorizador);
    }

    private VerificarEmision Crear(IAutorizadorComprobantes autorizador)
    {
        var proceso = new ProcesoEmision(_almacen, _almacen, _almacen, _almacen, _almacen, autorizador, _reloj, _registro);
        return new VerificarEmision(
            _almacen, _almacen, _almacen, _almacen, _almacen, _almacen, autorizador, _candado, proceso,
            new OpcionesVerificacion(TimeSpan.FromSeconds(60)), _reloj, _registro);
    }

    /// <summary>Guarda una pendiente con número del presupuesto, registrada hace <paramref name="hace"/> (5 min por defecto).</summary>
    private EmisionPendiente Pendiente(
        Presupuesto presupuesto, long numero = 125, TimeSpan? hace = null, EntornoArca entorno = EntornoArca.Simulado,
        ConceptoFactura concepto = ConceptoFactura.Productos)
    {
        var solicitud = ReglasEmision.Armar(presupuesto, _almacen.Configuracion, concepto, Hoy).Solicitud!;
        var pendiente = new EmisionPendiente(
            presupuesto.Numero, Ahora - (hace ?? TimeSpan.FromMinutes(5)), solicitud, numero, entorno);
        _almacen.Pendientes.Add(pendiente);
        return pendiente;
    }

    private void ResponderConsulta(ResultadoConsulta resultado) =>
        _autorizador.ResponderConsulta = (_, _, _, _) => Task.FromResult(resultado);

    private static void AfirmarConflicto(ResultadoVerificacion resultado, MotivoConflictoVerificacion motivo)
    {
        Assert.Equal(TipoResultadoVerificacion.Conflicto, resultado.Tipo);
        Assert.Equal(motivo, resultado.Motivo);
    }

    /// <summary>
    /// AC-05: la pendiente tiene el 125 por $1.815 y ARCA tiene autorizado el 125 por $1.815: se guarda la factura con el
    /// CAE de ARCA, se quitan la observación vieja y la pendiente, y no se pide número ni se envía ninguna solicitud. La
    /// consulta corre con el plazo de 30 s, con el turno del candado, fuera de toda transacción.
    /// </summary>
    [Fact]
    public async Task Verificar_AutorizadoMismoTotal_RecuperaSinEnviarSolicitud()
    {
        var presupuesto = _almacen.Sembrar(1, EstadoPresupuesto.Final, Presupuestos_.Dni, 1815m);
        var pendiente = Pendiente(presupuesto, concepto: ConceptoFactura.ProductosYServicios);
        _almacen.SembrarObservacion(pendiente, MotivoObservacionEmision.ImporteDistinto, 2000m);
        var alConsultar = (EnTransaccion: true, Turnos: -1);
        var tokenConsulta = CancellationToken.None;
        _autorizador.ResponderConsulta = (_, _, _, token) =>
        {
            alConsultar = (_almacen.EnTransaccion, _candado.Tomados - _candado.Liberados);
            tokenConsulta = token;
            return Task.FromResult<ResultadoConsulta>(
                new ResultadoConsulta.Autorizado(1815.00m, CaeArca, Hoy.AddDays(10), Hoy));
        };

        var resultado = await _caso.EjecutarAsync(1);

        Assert.Equal(TipoResultadoVerificacion.Recuperada, resultado.Tipo);
        Assert.NotNull(resultado.Factura);
        Assert.Equal("Factura B 0003-00000125", resultado.Factura.Comprobante);
        Assert.Equal(CaeArca, resultado.Factura.Cae);
        Assert.Equal(1815m, resultado.Factura.Total);
        Assert.Equal([(TipoComprobante.FacturaB, 3, 125L)], _autorizador.Consultas);
        Assert.Equal((false, 1), alConsultar);
        Assert.True(tokenConsulta.CanBeCanceled);
        Assert.Equal(0, _autorizador.Llamadas);
        Assert.Equal(0, _autorizador.PedidosDeNumero);
        var factura = Assert.Single(_almacen.Facturas);
        Assert.Equal((1L, 125L, CaeArca, Hoy.AddDays(10), EntornoArca.Simulado), (
            factura.PresupuestoNumero, factura.Numero, factura.Cae, factura.VencimientoCae, factura.Entorno));
        Assert.Equal(ConceptoFactura.ProductosYServicios, factura.Concepto);
        Assert.False(factura.Receptor.EstaIdentificado);
        Assert.Equal(pendiente.Fecha, factura.Fecha);
        Assert.Empty(_almacen.Pendientes);
        Assert.Empty(_almacen.Observaciones);
        Assert.Equal((1, 1), (_candado.Tomados, _candado.Liberados));
    }

    /// <summary>
    /// AC-06: ARCA no tiene autorizado el 125. Primero se consulta y recién después se pide el número y se envía la
    /// solicitud, con el concepto guardado, el mismo turno (una sola toma del candado) y un plazo nuevo de 30 s: la consulta
    /// consumió 25 s, y con el plazo de la consulta no habría margen para enviar.
    /// </summary>
    [Fact]
    public async Task Verificar_NoAutorizado_ConsultaPrimeroYEmiteConNumeroNuevo()
    {
        var presupuesto = _almacen.Sembrar(1, EstadoPresupuesto.Final, Presupuestos_.Dni, 1815m);
        Pendiente(presupuesto, concepto: ConceptoFactura.ProductosYServicios);
        List<string> orden = [];
        _autorizador.ResponderConsulta = (_, _, _, _) =>
        {
            orden.Add("consulta");
            _reloj.Avanzar(TimeSpan.FromSeconds(25));
            return Task.FromResult<ResultadoConsulta>(new ResultadoConsulta.NoAutorizado());
        };
        _autorizador.ResponderProximo = (_, _, _) =>
        {
            orden.Add("proximo");
            return Task.FromResult<ResultadoProximoNumero>(new ResultadoProximoNumero.Numero(126));
        };
        List<long?> pendientesAlEnviar = [];
        _autorizador.Responder = (solicitud, _) =>
        {
            orden.Add("autorizar");
            pendientesAlEnviar = [.. _almacen.Pendientes.Select(pendiente => pendiente.Numero)];
            return Task.FromResult<ResultadoAutorizacion>(
                new ResultadoAutorizacion.Autorizada(126, CaeArca, solicitud.Fecha.AddDays(10)));
        };

        var resultado = await _caso.EjecutarAsync(1);

        Assert.Equal(TipoResultadoVerificacion.Emitida, resultado.Tipo);
        Assert.Equal("Factura B 0003-00000126", resultado.Factura!.Comprobante);
        Assert.Equal(["consulta", "proximo", "autorizar"], orden);
        Assert.Equal([126L], pendientesAlEnviar);
        Assert.Equal([126L], _autorizador.Numeros);
        var enviada = Assert.Single(_autorizador.Solicitudes);
        Assert.Equal(ConceptoFactura.ProductosYServicios, enviada.Concepto);
        Assert.Equal(1815m, enviada.Importes.Total);
        Assert.Equal(126, Assert.Single(_almacen.Facturas).Numero);
        Assert.Empty(_almacen.Pendientes);
        Assert.Equal((1, 1), (_candado.Tomados, _candado.Liberados));
    }

    /// <summary>
    /// AC-07 (sad path): ARCA tiene el 125 por $2.000 y la pendiente dice $1.815: no se emite ni se recupera; la
    /// observación vieja se reemplaza por importe distinto con el comprobante y los dos totales (D-12), y la pendiente
    /// queda.
    /// </summary>
    [Fact]
    public async Task Verificar_ImporteDistinto_ObservacionSinEmitir()
    {
        var presupuesto = _almacen.Sembrar(1, EstadoPresupuesto.Final, Presupuestos_.Dni, 1815m);
        var pendiente = Pendiente(presupuesto);
        _almacen.SembrarObservacion(pendiente);
        ResponderConsulta(new ResultadoConsulta.Autorizado(2000m, CaeArca, Hoy.AddDays(10), Hoy));

        var resultado = await _caso.EjecutarAsync(1);

        AfirmarConflicto(resultado, MotivoConflictoVerificacion.ImporteDistinto);
        Assert.Equal(
            new DetalleObservacion(
                MotivoObservacionEmision.ImporteDistinto, "Factura B 0003-00000125", 1815m, 2000m, EntornoArca.Simulado),
            resultado.Observacion);
        var observacion = Assert.Single(_almacen.Observaciones);
        Assert.Equal(
            (MotivoObservacionEmision.ImporteDistinto, (long?)125, 1815m, (decimal?)2000m, Ahora),
            (observacion.Motivo, observacion.Numero, observacion.TotalRegistrado, observacion.TotalArca,
                observacion.Momento));
        Assert.Same(pendiente, Assert.Single(_almacen.Pendientes));
        Assert.Empty(_almacen.Facturas);
        Assert.Equal(0, _autorizador.Llamadas);
        Assert.Equal(0, _autorizador.PedidosDeNumero);
        Assert.Equal((1, 1), (_candado.Tomados, _candado.Liberados));
    }

    /// <summary>
    /// AC-11 (timeout, sad path): ARCA no responde a la consulta. A los 30 s se abandona la espera, la pendiente queda
    /// intacta, se informa "sin respuesta" y no se reintenta (una sola consulta, ningún envío).
    /// </summary>
    [Fact]
    public async Task Verificar_ConsultaSinRespuesta30s_PendienteIntactaSinReintento()
    {
        var presupuesto = _almacen.Sembrar(1);
        var pendiente = Pendiente(presupuesto);
        var consultada = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _autorizador.ResponderConsulta = async (_, _, _, token) =>
        {
            consultada.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            catch (OperationCanceledException)
            {
            }

            return new ResultadoConsulta.SinRespuesta();
        };

        var verificacion = _caso.EjecutarAsync(1);
        await consultada.Task.WaitAsync(TimeSpan.FromSeconds(10));
        _reloj.Avanzar(TimeSpan.FromSeconds(29));
        Assert.False(verificacion.IsCompleted, "La consulta se abandonó antes de los 30 s.");
        _reloj.Avanzar(TimeSpan.FromSeconds(1));
        var resultado = await verificacion.WaitAsync(TimeSpan.FromSeconds(10));

        AfirmarConflicto(resultado, MotivoConflictoVerificacion.SinRespuesta);
        Assert.Same(pendiente, Assert.Single(_almacen.Pendientes));
        Assert.Empty(_almacen.Observaciones);
        Assert.Single(_autorizador.Consultas);
        Assert.Equal(0, _autorizador.PedidosDeNumero);
        Assert.Equal(0, _autorizador.Llamadas);
        Assert.Equal((1, 1), (_candado.Tomados, _candado.Liberados));
    }

    /// <summary>
    /// FR-12 (sad path): la consulta no llegó a enviarse. La pendiente queda y se informa "sin respuesta"; el log usa un
    /// texto propio de la consulta (no habla de "emisión") y no repite el motivo del autorizador.
    /// </summary>
    [Fact]
    public async Task Verificar_NoConsultada_PendienteIntactaSinRespuesta()
    {
        var presupuesto = _almacen.Sembrar(1);
        var pendiente = Pendiente(presupuesto);
        ResponderConsulta(new ResultadoConsulta.NoConsultada("Texto del autorizador zq7."));

        var resultado = await _caso.EjecutarAsync(1);

        AfirmarConflicto(resultado, MotivoConflictoVerificacion.SinRespuesta);
        Assert.Same(pendiente, Assert.Single(_almacen.Pendientes));
        Assert.Equal(0, _autorizador.Llamadas);
        var linea = Assert.Single(_registro.Lineas, l => l.Texto.Contains("consulta", StringComparison.Ordinal));
        Assert.DoesNotContain("zq7", linea.Texto, StringComparison.Ordinal);
        Assert.DoesNotContain("emisión", linea.Texto, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// AC-12 (timeout, sad path): ARCA no tiene el 125, se registra el 126 y la emisión queda sin respuesta: la pendiente
    /// queda con el número nuevo, sin reintentar.
    /// </summary>
    [Fact]
    public async Task Verificar_EmisionSinRespuesta_PendienteConNumeroNuevo()
    {
        var presupuesto = _almacen.Sembrar(1, EstadoPresupuesto.Final, Presupuestos_.Dni, 1815m);
        Pendiente(presupuesto);
        _autorizador.ResponderProximo = (_, _, _) =>
            Task.FromResult<ResultadoProximoNumero>(new ResultadoProximoNumero.Numero(126));
        _autorizador.Responder = (_, token) => AutorizadorDoble.NoResponderAsync(token);

        var verificacion = _caso.EjecutarAsync(1);
        await _autorizador.Llamado.Task.WaitAsync(TimeSpan.FromSeconds(10));
        _reloj.Avanzar(TimeSpan.FromSeconds(30));
        var resultado = await verificacion.WaitAsync(TimeSpan.FromSeconds(10));

        AfirmarConflicto(resultado, MotivoConflictoVerificacion.SinRespuestaEmision);
        var nueva = Assert.Single(_almacen.Pendientes);
        Assert.Equal(126, nueva.Numero);
        Assert.Equal(1815m, nueva.Total);
        Assert.Equal(1, _autorizador.Llamadas);
        Assert.Single(_autorizador.Consultas);
        Assert.Empty(_almacen.Facturas);
        Assert.Equal((1, 1), (_candado.Tomados, _candado.Liberados));
    }

    /// <summary>
    /// AC-15 (sad path): la pendiente del presupuesto 150 la grabó FEAT-007, sin número: no se consulta, no se emite ni
    /// se recupera; queda la observación "sin número" y no se toma el candado.
    /// </summary>
    [Fact]
    public async Task Verificar_SinNumero_ObservacionSinConsultar()
    {
        _almacen.Sembrar(150, EstadoPresupuesto.Final, Presupuestos_.Dni, 1815m);
        var pendiente = EmisionPendiente.SinNumero(
            150, Ahora.AddMinutes(-5), TipoComprobante.FacturaB, 3, Hoy, 1815m, EntornoArca.Simulado);
        _almacen.Pendientes.Add(pendiente);

        var resultado = await _caso.EjecutarAsync(150);

        AfirmarConflicto(resultado, MotivoConflictoVerificacion.SinNumero);
        Assert.Equal(
            new DetalleObservacion(MotivoObservacionEmision.SinNumero, null, 1815m, null, EntornoArca.Simulado),
            resultado.Observacion);
        var observacion = Assert.Single(_almacen.Observaciones);
        Assert.Equal((MotivoObservacionEmision.SinNumero, (long?)null), (observacion.Motivo, observacion.Numero));
        Assert.Same(pendiente, Assert.Single(_almacen.Pendientes));
        Assert.Empty(_autorizador.Consultas);
        Assert.Equal(0, _autorizador.PedidosDeNumero);
        Assert.Equal(0, _autorizador.Llamadas);
        Assert.Equal(0, _candado.Tomados);
    }

    /// <summary>
    /// AC-16 (sad path): el entorno configurado es Homologación y la pendiente se envió al Simulado: no se consulta ni se
    /// emite; la observación "otro entorno" dice Simulado. Volver a verificar reemplaza la observación (D-12).
    /// </summary>
    [Fact]
    public async Task Verificar_OtroEntorno_ObservacionSinConsultar()
    {
        var homologacion = new AutorizadorDoble(_almacen) { Entorno = EntornoArca.Homologacion };
        var caso = Crear(homologacion);
        var presupuesto = _almacen.Sembrar(1, EstadoPresupuesto.Final, Presupuestos_.Dni, 1815m);
        var pendiente = Pendiente(presupuesto, entorno: EntornoArca.Simulado);

        var resultado = await caso.EjecutarAsync(1);
        _reloj.Avanzar(TimeSpan.FromMinutes(1));
        var otraVez = await caso.EjecutarAsync(1);

        AfirmarConflicto(resultado, MotivoConflictoVerificacion.OtroEntorno);
        AfirmarConflicto(otraVez, MotivoConflictoVerificacion.OtroEntorno);
        Assert.Equal(
            new DetalleObservacion(
                MotivoObservacionEmision.OtroEntorno, "Factura B 0003-00000125", 1815m, null, EntornoArca.Simulado),
            resultado.Observacion);
        var observacion = Assert.Single(_almacen.Observaciones);
        Assert.Equal(
            (MotivoObservacionEmision.OtroEntorno, EntornoArca.Simulado, Ahora.AddMinutes(1)),
            (observacion.Motivo, observacion.Entorno, observacion.Momento));
        Assert.Same(pendiente, Assert.Single(_almacen.Pendientes));
        Assert.Empty(homologacion.Consultas);
        Assert.Equal(0, homologacion.Llamadas);
        Assert.Equal(0, _candado.Tomados);
    }

    /// <summary>
    /// AC-17 (sad path): la pendiente del presupuesto 150 quedó hace 20 s: no se consulta ni se emite, la pendiente queda y
    /// faltan 40 s. Los segundos se redondean hacia arriba (a los 59,5 s falta 1); a los 60 s ya se consulta.
    /// </summary>
    [Fact]
    public async Task Verificar_A20Segundos_EsperaFaltan40SinConsultar()
    {
        var presupuesto = _almacen.Sembrar(150);
        var pendiente = Pendiente(presupuesto, hace: TimeSpan.FromSeconds(20));

        var resultado = await _caso.EjecutarAsync(150);

        AfirmarConflicto(resultado, MotivoConflictoVerificacion.Espera);
        Assert.Equal(40, resultado.SegundosRestantes);
        Assert.Same(pendiente, Assert.Single(_almacen.Pendientes));
        Assert.Empty(_almacen.Observaciones);
        Assert.Empty(_autorizador.Consultas);
        Assert.Equal(0, _autorizador.Llamadas);
        Assert.Equal(0, _candado.Tomados);

        _reloj.Avanzar(TimeSpan.FromSeconds(39.5));
        var casi = await _caso.EjecutarAsync(150);
        AfirmarConflicto(casi, MotivoConflictoVerificacion.Espera);
        Assert.Equal(1, casi.SegundosRestantes);
        Assert.Empty(_autorizador.Consultas);

        _reloj.Avanzar(TimeSpan.FromSeconds(0.5));
        ResponderConsulta(new ResultadoConsulta.SinRespuesta());
        AfirmarConflicto(await _caso.EjecutarAsync(150), MotivoConflictoVerificacion.SinRespuesta);
        Assert.Single(_autorizador.Consultas);
    }

    /// <summary>
    /// D-30 (sad path): mientras la verificación esperaba el turno, otra operación reemplazó la pendiente (otro número y
    /// otro momento, ya pasada la espera). Con el turno, la pendiente releída no es la evaluada: "no enviada", sin
    /// consultar a ARCA ni grabar nada.
    /// </summary>
    [Fact]
    public async Task Verificar_PendienteReemplazadaMientrasEsperabaElTurno_NoEnviadaSinConsultar()
    {
        var presupuesto = _almacen.Sembrar(1, EstadoPresupuesto.Final, Presupuestos_.Dni, 1815m);
        var vieja = Pendiente(presupuesto);
        var solicitud = ReglasEmision.Armar(presupuesto, _almacen.Configuracion, ConceptoFactura.Productos, Hoy)
            .Solicitud!;
        var nueva = new EmisionPendiente(1, Ahora.AddMinutes(-2), solicitud, 126, EntornoArca.Simulado);
        _candado.AlTomar = () =>
        {
            _almacen.Pendientes.Remove(vieja);
            _almacen.Pendientes.Add(nueva);
        };
        _autorizador.ResponderConsulta = (_, _, _, _) =>
            Task.FromResult<ResultadoConsulta>(new ResultadoConsulta.NoAutorizado());

        var accesosAntes = _almacen.Accesos.Count(a => a.StartsWith("Agregar", StringComparison.Ordinal)
            || a.StartsWith("Quitar", StringComparison.Ordinal));
        var resultado = await _caso.EjecutarAsync(1);

        AfirmarConflicto(resultado, MotivoConflictoVerificacion.NoEnviada);
        Assert.Empty(_autorizador.Consultas);
        Assert.Equal(0, _autorizador.PedidosDeNumero);
        Assert.Equal(0, _autorizador.Llamadas);
        Assert.Same(nueva, Assert.Single(_almacen.Pendientes));
        Assert.Empty(_almacen.Observaciones);
        Assert.Empty(_almacen.Facturas);
        Assert.Equal(accesosAntes, _almacen.Accesos.Count(a => a.StartsWith("Agregar", StringComparison.Ordinal)
            || a.StartsWith("Quitar", StringComparison.Ordinal)));
        Assert.Equal((1, 1), (_candado.Tomados, _candado.Liberados));
    }

    /// <summary>
    /// Concurrencia (D-30, FR-06): dos verificaciones en paralelo con el candado real de un turno a la vez. La primera
    /// consulta (ARCA no tiene el 125) y emite el 126, que queda sin respuesta; la segunda leyó la pendiente vieja antes de
    /// esperar el turno: con él, la pendiente ya es otra y termina "no enviada" sin consultar. Una sola consulta y un solo
    /// envío.
    /// </summary>
    [Fact]
    public async Task Verificar_DosEnParalelo_LaPrimeraReemplaza_LaSegundaNoEnviada()
    {
        var serial = new CandadoSerial();
        var proceso = new ProcesoEmision(_almacen, _almacen, _almacen, _almacen, _almacen, _autorizador, _reloj, _registro);
        var caso = new VerificarEmision(
            _almacen, _almacen, _almacen, _almacen, _almacen, _almacen, _autorizador, serial, proceso,
            new OpcionesVerificacion(TimeSpan.FromSeconds(60)), _reloj, _registro);
        var presupuesto = _almacen.Sembrar(1, EstadoPresupuesto.Final, Presupuestos_.Dni, 1815m);
        Pendiente(presupuesto);
        var consultando = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var seguir = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _autorizador.ResponderConsulta = async (_, _, _, _) =>
        {
            consultando.TrySetResult();
            await seguir.Task;
            return new ResultadoConsulta.NoAutorizado();
        };
        _autorizador.ResponderProximo = (_, _, _) =>
            Task.FromResult<ResultadoProximoNumero>(new ResultadoProximoNumero.Numero(126));
        _autorizador.Responder = (_, _) =>
            Task.FromResult<ResultadoAutorizacion>(new ResultadoAutorizacion.SinRespuesta());

        var primera = caso.EjecutarAsync(1);
        await consultando.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var segunda = caso.EjecutarAsync(1);
        await serial.SegundoPedido.Task.WaitAsync(TimeSpan.FromSeconds(10));
        seguir.SetResult();

        AfirmarConflicto(await primera.WaitAsync(TimeSpan.FromSeconds(10)), MotivoConflictoVerificacion.SinRespuestaEmision);
        AfirmarConflicto(await segunda.WaitAsync(TimeSpan.FromSeconds(10)), MotivoConflictoVerificacion.NoEnviada);
        Assert.Single(_autorizador.Consultas);
        Assert.Equal(1, _autorizador.Llamadas);
        Assert.Equal(126, Assert.Single(_almacen.Pendientes).Numero);
        Assert.Empty(_almacen.Facturas);
        Assert.Equal(2, serial.Liberados);
    }

    /// <summary>
    /// Sad path: un presupuesto sin pendiente responde "sin pendiente"; uno que no existe (o un número menor que 1), no
    /// encontrado. En ningún caso se llama a ARCA ni se toma el candado.
    /// </summary>
    [Fact]
    public async Task Verificar_SinPendiente_Conflicto()
    {
        var facturado = _almacen.Sembrar(1);
        _almacen.SembrarFactura(facturado);
        _almacen.Sembrar(2);

        AfirmarConflicto(await _caso.EjecutarAsync(1), MotivoConflictoVerificacion.SinPendiente);
        AfirmarConflicto(await _caso.EjecutarAsync(2), MotivoConflictoVerificacion.SinPendiente);
        Assert.Equal(TipoResultadoVerificacion.NoEncontrado, (await _caso.EjecutarAsync(99)).Tipo);
        Assert.Equal(TipoResultadoVerificacion.NoEncontrado, (await _caso.EjecutarAsync(0)).Tipo);
        Assert.Empty(_autorizador.Consultas);
        Assert.Equal(0, _candado.Tomados);
        Assert.Empty(_almacen.Observaciones);
    }

    /// <summary>
    /// Sad path (D-18): ARCA tiene el número con el mismo total, pero la factura recuperada no se puede grabar (falla la
    /// base) o no valida (el vencimiento del CAE es anterior a la fecha). La pendiente queda intacta, el resultado es "no
    /// enviada", no se envía nada y el log no lleva el DNI del receptor identificado.
    /// </summary>
    [Fact]
    public async Task Verificar_RecuperadaNoGrabable_PendienteIntacta()
    {
        // Total sobre el tope de $100.000: la pendiente dice receptor identificado (con el DNI del presupuesto).
        var presupuesto = _almacen.Sembrar(1, EstadoPresupuesto.Final, Presupuestos_.Dni, 150_000m);
        var pendiente = Pendiente(presupuesto);
        Assert.True(pendiente.ReceptorIdentificado);
        ResponderConsulta(new ResultadoConsulta.Autorizado(150_000m, CaeArca, Hoy.AddDays(10), Hoy));
        _almacen.FallaAlConfirmarFactura = () => new InvalidOperationException("Falla con el DNI " + Presupuestos_.Dni);

        var noGrabable = await _caso.EjecutarAsync(1);

        AfirmarConflicto(noGrabable, MotivoConflictoVerificacion.NoEnviada);
        Assert.Same(pendiente, Assert.Single(_almacen.Pendientes));
        Assert.Empty(_almacen.Facturas);

        _almacen.FallaAlConfirmarFactura = null;
        ResponderConsulta(new ResultadoConsulta.Autorizado(150_000m, CaeArca, Hoy.AddDays(-1), Hoy));

        var invalida = await _caso.EjecutarAsync(1);

        AfirmarConflicto(invalida, MotivoConflictoVerificacion.NoEnviada);
        Assert.Same(pendiente, Assert.Single(_almacen.Pendientes));
        Assert.Empty(_almacen.Facturas);
        Assert.Empty(_almacen.Observaciones);
        Assert.Equal(0, _autorizador.Llamadas);
        Assert.Equal(0, _autorizador.PedidosDeNumero);
        Assert.Equal((2, 2), (_candado.Tomados, _candado.Liberados));
        Assert.NotEmpty(_registro.Lineas);
        Assert.DoesNotContain(Presupuestos_.Dni, _registro.Todo, StringComparison.Ordinal);
    }

    /// <summary>
    /// D-18: una pendiente identificada que se recupera lleva el DNI del cliente del presupuesto como receptor.
    /// </summary>
    [Fact]
    public async Task Verificar_RecuperadaIdentificada_ReceptorConElDniDelPresupuesto()
    {
        var presupuesto = _almacen.Sembrar(1, EstadoPresupuesto.Final, Presupuestos_.Dni, 150_000m);
        Pendiente(presupuesto);
        ResponderConsulta(new ResultadoConsulta.Autorizado(150_000m, CaeArca, Hoy.AddDays(10), Hoy));

        var resultado = await _caso.EjecutarAsync(1);

        Assert.Equal(TipoResultadoVerificacion.Recuperada, resultado.Tipo);
        Assert.Equal(Presupuestos_.Dni, Assert.Single(_almacen.Facturas).Receptor.Dni);
        Assert.DoesNotContain(Presupuestos_.Dni, _registro.Todo, StringComparison.Ordinal);
    }

    /// <summary>
    /// AC-13 contra un ARCA en memoria (el simulador de Infrastructure no está al alcance de esta capa) y NFR-02: los
    /// escenarios de AC-05, AC-06, AC-07, AC-11 y AC-12 (con una segunda verificación que recupera el CAE del envío sin
    /// respuesta) dejan cada presupuesto con 1 comprobante autorizado en ARCA como máximo, y las facturas guardadas son
    /// las de ARCA.
    /// </summary>
    [Fact]
    public async Task Verificar_EscenariosAC05aAC12_UnComprobantePorPresupuesto()
    {
        var arca = new ArcaEnMemoria(_reloj);
        var caso = Crear(arca);

        // AC-05: el 125 ya está autorizado por $1.815, del presupuesto 1.
        var p1 = _almacen.Sembrar(1, EstadoPresupuesto.Final, Presupuestos_.Dni, 1815m);
        arca.Precargar(125, 1815m);
        Pendiente(p1, numero: 125);

        // AC-07: el 126 está autorizado por $2.000 (otra venta) y la pendiente del 3 dice $1.700.
        var p3 = _almacen.Sembrar(3, EstadoPresupuesto.Final, Presupuestos_.Dni, 1700m);
        arca.Precargar(126, 2000m);
        Pendiente(p3, numero: 126);

        // AC-06: el 127 nunca llegó a ARCA.
        var p2 = _almacen.Sembrar(2, EstadoPresupuesto.Final, Presupuestos_.Dni, 1900m);
        Pendiente(p2, numero: 127);

        // AC-11: el 4 tiene una pendiente cuya consulta no tiene respuesta.
        var p4 = _almacen.Sembrar(4, EstadoPresupuesto.Final, Presupuestos_.Dni, 1600m);
        Pendiente(p4, numero: 200);

        // AC-12: el 5 tiene una pendiente sin enviar; la emisión de la verificación se autoriza sin respuesta.
        var p5 = _almacen.Sembrar(5, EstadoPresupuesto.Final, Presupuestos_.Dni, 1500m);
        Pendiente(p5, numero: 129);

        Assert.Equal(TipoResultadoVerificacion.Recuperada, (await caso.EjecutarAsync(1)).Tipo);
        AfirmarConflicto(await caso.EjecutarAsync(3), MotivoConflictoVerificacion.ImporteDistinto);
        Assert.Equal(TipoResultadoVerificacion.Emitida, (await caso.EjecutarAsync(2)).Tipo);

        arca.ConsultaSinRespuesta = true;
        var sinRespuesta = caso.EjecutarAsync(4);
        await arca.Esperando.Task.WaitAsync(TimeSpan.FromSeconds(10));
        _reloj.Avanzar(TimeSpan.FromSeconds(30));
        AfirmarConflicto(await sinRespuesta.WaitAsync(TimeSpan.FromSeconds(10)), MotivoConflictoVerificacion.SinRespuesta);
        arca.ConsultaSinRespuesta = false;

        arca.AutorizarSinResponder = true;
        AfirmarConflicto(await caso.EjecutarAsync(5), MotivoConflictoVerificacion.SinRespuestaEmision);
        arca.AutorizarSinResponder = false;
        _reloj.Avanzar(TimeSpan.FromSeconds(60));
        Assert.Equal(TipoResultadoVerificacion.Recuperada, (await caso.EjecutarAsync(5)).Tipo);

        foreach (var presupuesto in new[] { p1, p2, p3, p4, p5 })
        {
            Assert.True(
                arca.AutorizadosCon(presupuesto.Total) <= 1,
                $"El presupuesto {presupuesto.Numero} tiene más de un comprobante autorizado.");
        }

        Assert.Equal((1, 1, 0, 0, 1), (
            arca.AutorizadosCon(1815m), arca.AutorizadosCon(1900m), arca.AutorizadosCon(1700m),
            arca.AutorizadosCon(1600m), arca.AutorizadosCon(1500m)));
        Assert.Equal([1L, 2L, 5L], _almacen.Facturas.Select(f => f.PresupuestoNumero).Order());
        foreach (var factura in _almacen.Facturas)
        {
            Assert.Equal(factura.Importes.Total, arca.TotalDe(factura.Numero));
        }

        Assert.Equal([3L, 4L], _almacen.Pendientes.Select(p => p.PresupuestoNumero).Order());
    }

    /// <summary>
    /// Un ARCA en memoria para un tipo y un punto de venta: numera con el último autorizado + 1, autoriza el número que
    /// recibe solo si es el siguiente (como WSFEv1), y consulta por número. Puede no responder a la consulta o autorizar
    /// sin responder (como <c>autorizar_sin_responder</c> del simulador, D-19).
    /// </summary>
    private sealed class ArcaEnMemoria(TimeProvider reloj) : IAutorizadorComprobantes
    {
        private readonly Dictionary<long, decimal> _autorizados = [];
        private long _ultimo;

        public EntornoArca Entorno => EntornoArca.Simulado;

        public TimeSpan TiempoMaximo => TimeSpan.FromSeconds(30);

        public bool ConsultaSinRespuesta { get; set; }

        public bool AutorizarSinResponder { get; set; }

        public TaskCompletionSource Esperando { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Precargar(long numero, decimal total)
        {
            _autorizados[numero] = total;
            _ultimo = Math.Max(_ultimo, numero);
        }

        public int AutorizadosCon(decimal total) => _autorizados.Values.Count(valor => valor == total);

        public decimal TotalDe(long numero) => _autorizados[numero];

        public Task<ResultadoProximoNumero> ProximoNumeroAsync(
            TipoComprobante tipo, int puntoVenta, CancellationToken cancelacion) =>
            Task.FromResult<ResultadoProximoNumero>(new ResultadoProximoNumero.Numero(_ultimo + 1));

        public Task<ResultadoAutorizacion> AutorizarAsync(
            SolicitudComprobante solicitud, long numero, CancellationToken cancelacion)
        {
            if (numero != _ultimo + 1)
            {
                return Task.FromResult<ResultadoAutorizacion>(
                    new ResultadoAutorizacion.Rechazada(10016, "El número no es el próximo."));
            }

            _autorizados[numero] = solicitud.Importes.Total;
            _ultimo = numero;
            var vencimiento = DateOnly.FromDateTime(reloj.GetLocalNow().DateTime).AddDays(10);
            return Task.FromResult<ResultadoAutorizacion>(AutorizarSinResponder
                ? new ResultadoAutorizacion.SinRespuesta()
                : new ResultadoAutorizacion.Autorizada(numero, CaeArca, vencimiento));
        }

        public async Task<ResultadoConsulta> ConsultarAsync(
            TipoComprobante tipo, int puntoVenta, long numero, CancellationToken cancelacion)
        {
            if (ConsultaSinRespuesta)
            {
                Esperando.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancelacion);
                }
                catch (OperationCanceledException)
                {
                }

                return new ResultadoConsulta.SinRespuesta();
            }

            var hoy = DateOnly.FromDateTime(reloj.GetLocalNow().DateTime);
            return _autorizados.TryGetValue(numero, out var total)
                ? new ResultadoConsulta.Autorizado(total, CaeArca, hoy.AddDays(10), hoy)
                : new ResultadoConsulta.NoAutorizado();
        }
    }
}
