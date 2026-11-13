using Microsoft.Extensions.Logging;
using Optica.Application.Comun;
using Optica.Application.Facturacion;
using Optica.Domain.Configuracion;
using Optica.Domain.Facturacion;
using Optica.Domain.Presupuestos;

namespace Optica.Application.Tests.Facturacion;

/// <summary>
/// Spec FEAT-007, Block 7: <see cref="EmitirFactura"/> con dobles en memoria y reloj controlado. Datos del cliente
/// ficticios (D-50).
/// </summary>
public sealed class EmitirFacturaTests
{
    /// <summary>31/10/2026 02:00 UTC: 30/10/2026 23:00 en Argentina (UTC−3).</summary>
    private static readonly DateTimeOffset Ahora = new(2026, 10, 31, 2, 0, 0, TimeSpan.Zero);

    private readonly AlmacenFacturacion _almacen = new();
    private readonly CandadoDoble _candado = new();
    private readonly RelojControlado _reloj = new(Ahora);
    private readonly RegistroEmision _registro = new();
    private readonly AutorizadorDoble _autorizador;

    public EmitirFacturaTests()
    {
        _autorizador = new AutorizadorDoble(_almacen);
    }

    private EmitirFactura Crear() =>
        new(_almacen, _almacen, _almacen, _almacen, _almacen, _autorizador, _candado, _reloj, _registro);

    private Task<ResultadoEmision> EmitirAsync(
        long numero = 1, string? concepto = EmitirFactura.ConceptoProductos, CancellationToken cancelacion = default) =>
        Crear().EjecutarAsync(numero, concepto, cancelacion);

    private static void AfirmarConflicto(ResultadoEmision resultado, MotivoConflictoEmision motivo)
    {
        Assert.Equal(TipoResultadoEmision.Conflicto, resultado.Tipo);
        Assert.Equal(motivo, resultado.Motivo);
    }

    /// <summary>AC-01: Final con 3 líneas y total $3.000: la factura copia las 3 líneas y ARCA recibe 3.000.</summary>
    [Fact]
    public async Task Emitir_Final3Lineas_FacturaConLineasYTotal3000()
    {
        var presupuesto = _almacen.Sembrar(1, EstadoPresupuesto.Final, Presupuestos_.Dni, 500m, 1000m, 1500m);

        var resultado = await EmitirAsync();

        Assert.True(resultado.EsEmitida);
        Assert.Equal(3000m, resultado.Factura.Total);
        Assert.Equal(3000m, Assert.Single(_autorizador.Solicitudes).Importes.Total);
        var factura = Assert.Single(_almacen.Facturas);
        Assert.Equal(1, factura.PresupuestoNumero);
        Assert.Equal(3000m, factura.Importes.Total);
        Assert.Equal(3, factura.Lineas.Count);
        for (var indice = 0; indice < 3; indice++)
        {
            Assert.Equal(indice + 1, factura.Lineas[indice].Orden);
            Assert.Equal(presupuesto.Lineas[indice].Descripcion, factura.Lineas[indice].Descripcion);
            Assert.Equal(presupuesto.Lineas[indice].PrecioFinal, factura.Lineas[indice].PrecioFinal);
        }
    }

    /// <summary>AC-02 (sad path): un Borrador no se factura y no se llama a ARCA ni se graba una pendiente.</summary>
    [Fact]
    public async Task Emitir_Borrador_Conflicto409SinLlamarArca()
    {
        _almacen.Sembrar(1, EstadoPresupuesto.Borrador);

        var resultado = await EmitirAsync();

        AfirmarConflicto(resultado, MotivoConflictoEmision.Borrador);
        Assert.Equal(0, _autorizador.Llamadas);
        Assert.Empty(_almacen.Pendientes);
        Assert.Empty(_almacen.Facturas);
        Assert.Equal(0, _candado.Tomados);
    }

    /// <summary>AC-03 (sad path): un presupuesto que ya tiene factura no se factura otra vez.</summary>
    [Fact]
    public async Task Emitir_YaFacturado_Conflicto409SinLlamarArca()
    {
        var presupuesto = _almacen.Sembrar(1);
        var anterior = _almacen.SembrarFactura(presupuesto);

        var resultado = await EmitirAsync();

        AfirmarConflicto(resultado, MotivoConflictoEmision.Facturado);
        Assert.Equal(0, _autorizador.Llamadas);
        Assert.Same(anterior, Assert.Single(_almacen.Facturas));
        Assert.Empty(_almacen.Pendientes);
    }

    /// <summary>AC-06: emitida el 30/10/2026 en Argentina (31/10 en UTC), la fecha del comprobante es 30/10/2026.</summary>
    [Fact]
    public async Task Emitir_FechaDelDia30_10_2026()
    {
        _almacen.Sembrar(1);

        var resultado = await EmitirAsync();

        var dia = new DateOnly(2026, 10, 30);
        Assert.True(resultado.EsEmitida);
        Assert.Equal(dia, resultado.Factura.Fecha);
        Assert.Equal(dia, Assert.Single(_autorizador.Solicitudes).Fecha);
        Assert.Equal(dia, Assert.Single(_almacen.Facturas).Fecha);
    }

    /// <summary>AC-12: con el tope cambiado de $100.000 a $40.000, un total de $50.000 identifica al receptor.</summary>
    [Fact]
    public async Task Emitir_TopeCambiadoA40000_UsaElVigente()
    {
        _almacen.Sembrar(1, EstadoPresupuesto.Final, Presupuestos_.Dni, 50_000m);
        _almacen.Sembrar(2, EstadoPresupuesto.Final, Presupuestos_.Dni, 50_000m);

        _almacen.Configuracion = Configuraciones.Vigente(tope: 100_000m);
        Assert.True((await EmitirAsync(1)).EsEmitida);
        _almacen.Configuracion = Configuraciones.Vigente(tope: 40_000m);
        Assert.True((await EmitirAsync(2)).EsEmitida);

        Assert.Equal(2, _autorizador.Llamadas);
        Assert.False(_autorizador.Solicitudes[0].Receptor.EstaIdentificado);
        Assert.Equal(Presupuestos_.Dni, _autorizador.Solicitudes[1].Receptor.Dni);
    }

    /// <summary>AC-13 (sad path): sin tope no se llama a ARCA.</summary>
    [Fact]
    public async Task Emitir_TopeFaltante_Conflicto409SinLlamarArca()
    {
        _almacen.Sembrar(1);
        _almacen.Configuracion = Configuraciones.Vigente(tope: null);

        var resultado = await EmitirAsync();

        AfirmarConflicto(resultado, MotivoConflictoEmision.SinTope);
        Assert.Equal(0, _autorizador.Llamadas);
        Assert.Empty(_almacen.Pendientes);
    }

    /// <summary>AC-16: con la alícuota cambiada a 10,5, un total de $1.105 da neto $1.000 e IVA $105.</summary>
    [Fact]
    public async Task Emitir_AlicuotaCambiadaA10_5_UsaLaVigente()
    {
        _almacen.Sembrar(1, EstadoPresupuesto.Final, Presupuestos_.Dni, 1105m);
        _almacen.Configuracion = Configuraciones.Vigente(alicuota: 10.5m);

        var resultado = await EmitirAsync();

        Assert.True(resultado.EsEmitida);
        Assert.Equal(1000m, resultado.Factura.Neto);
        Assert.Equal(105m, resultado.Factura.Iva);
        var importes = Assert.Single(_autorizador.Solicitudes).Importes;
        Assert.Equal(10.5m, importes.Alicuota);
        Assert.Equal(1000m, importes.Neto);
        Assert.Equal(105m, importes.Iva);
    }

    /// <summary>AC-05: con Monotributo se emite una Factura C, sin desglose.</summary>
    [Fact]
    public async Task Emitir_CondicionCambiadaAMonotributo_FacturaC()
    {
        _almacen.Sembrar(1, EstadoPresupuesto.Final, Presupuestos_.Dni, 1815m);
        _almacen.Configuracion = Configuraciones.Vigente(condicion: CondicionFiscal.Monotributo);

        var resultado = await EmitirAsync();

        Assert.True(resultado.EsEmitida);
        Assert.Equal(FacturaEmitida.TipoC, resultado.Factura.Tipo);
        Assert.Equal("Factura C 0003-00000125", resultado.Factura.Comprobante);
        Assert.Equal(1815m, resultado.Factura.Neto);
        Assert.Equal(0m, resultado.Factura.Iva);
        Assert.Equal(TipoComprobante.FacturaC, Assert.Single(_autorizador.Solicitudes).Tipo);
        Assert.Equal(TipoComprobante.FacturaC, Assert.Single(_almacen.Facturas).Tipo);
    }

    /// <summary>
    /// AC-18: autorizada, la factura se guarda con número, CAE, vencimiento y entorno, y la pendiente se quita. La
    /// pendiente estaba grabada al llamar a ARCA y la llamada corrió fuera de toda transacción (ADR-021).
    /// </summary>
    [Fact]
    public async Task Emitir_Autorizada_GuardaFacturaYQuitaPendiente()
    {
        _almacen.Sembrar(1);
        _autorizador.Responder = (solicitud, _) => Task.FromResult<ResultadoAutorizacion>(
            new ResultadoAutorizacion.Autorizada(125, "75123456789012", new DateOnly(2026, 11, 9)));

        var resultado = await EmitirAsync();

        Assert.True(resultado.EsEmitida);
        Assert.Equal([false], _autorizador.DentroDeTransaccion);
        Assert.Equal([1], _autorizador.PendientesAlLlamar);
        var factura = Assert.Single(_almacen.Facturas);
        Assert.Equal(125, factura.Numero);
        Assert.Equal(3, factura.PuntoVenta);
        Assert.Equal(TipoComprobante.FacturaB, factura.Tipo);
        Assert.Equal("75123456789012", factura.Cae);
        Assert.Equal(new DateOnly(2026, 11, 9), factura.VencimientoCae);
        Assert.Equal(EntornoArca.Simulado, factura.Entorno);
        Assert.Empty(_almacen.Pendientes);
        Assert.Equal("75123456789012", resultado.Factura.Cae);
        Assert.Equal(new DateOnly(2026, 11, 9), resultado.Factura.VencimientoCae);
        Assert.Equal(125, resultado.Factura.Numero);
        Assert.Equal(FacturaEmitida.TipoB, resultado.Factura.Tipo);
        // Dos transacciones: la de la pendiente y la de la factura (D-09).
        Assert.Equal(2, _almacen.TokensDeTransaccion.Count);
    }

    /// <summary>
    /// AC-20 y FR-22 (sad path, rechazo): sin factura y sin pendiente, con el código y la descripción de ARCA; el mismo
    /// presupuesto se puede volver a emitir.
    /// </summary>
    [Fact]
    public async Task Emitir_Rechazada_SinFacturaYSinPendiente()
    {
        _almacen.Sembrar(1);
        _autorizador.Responder = (_, _) => Task.FromResult<ResultadoAutorizacion>(
            new ResultadoAutorizacion.Rechazada(10016, "El número no es el próximo."));

        var resultado = await EmitirAsync();

        AfirmarConflicto(resultado, MotivoConflictoEmision.Rechazada);
        Assert.Equal(10016, resultado.CodigoArca);
        Assert.Equal("El número no es el próximo.", resultado.DescripcionArca);
        Assert.Empty(_almacen.Facturas);
        Assert.Empty(_almacen.Pendientes);

        _autorizador.Responder = null;
        Assert.True((await EmitirAsync()).EsEmitida);
        Assert.Equal(2, _autorizador.Llamadas);
    }

    /// <summary>
    /// AC-21 (timeout, sad path): ARCA no responde; a los 30 s la emisión se abandona, la pendiente queda y no se
    /// reintenta. A los 29,9 s todavía espera.
    /// </summary>
    [Fact]
    public async Task Emitir_SinRespuesta30s_TimeoutDejaPendienteSinReintentar()
    {
        _almacen.Sembrar(1);
        _autorizador.Responder = (_, token) => AutorizadorDoble.NoResponderAsync(token);

        var emision = EmitirAsync();
        await _autorizador.Llamado.Task.WaitAsync(TimeSpan.FromSeconds(10));
        _reloj.Avanzar(TimeSpan.FromSeconds(29.9));
        Assert.False(_autorizador.Tokens[0].IsCancellationRequested, "El plazo venció antes de los 30 s.");

        _reloj.Avanzar(TimeSpan.FromSeconds(0.1));
        Assert.True(_autorizador.Tokens[0].IsCancellationRequested, "El plazo no venció a los 30 s.");
        var resultado = await emision.WaitAsync(TimeSpan.FromSeconds(10));

        AfirmarConflicto(resultado, MotivoConflictoEmision.SinRespuesta);
        Assert.Equal(1, _autorizador.Llamadas);
        Assert.Single(_almacen.Pendientes);
        Assert.Empty(_almacen.Facturas);
        Assert.True(_autorizador.Tokens[0].IsCancellationRequested);
    }

    /// <summary>AC-22 (sad path): con una emisión pendiente no se envía otra solicitud.</summary>
    [Fact]
    public async Task Emitir_ConPendiente_Conflicto409SinLlamarArca()
    {
        var presupuesto = _almacen.Sembrar(1);
        var pendiente = _almacen.SembrarPendiente(presupuesto);

        var resultado = await EmitirAsync();

        AfirmarConflicto(resultado, MotivoConflictoEmision.Pendiente);
        Assert.Equal(0, _autorizador.Llamadas);
        Assert.Same(pendiente, Assert.Single(_almacen.Pendientes));
    }

    /// <summary>AC-29 (sad path): alícuota guardada 19 con Responsable Inscripto: no se llama a ARCA.</summary>
    [Fact]
    public async Task Emitir_Alicuota19_Conflicto409SinLlamarArca()
    {
        _almacen.Sembrar(1);
        _almacen.Configuracion = Configuraciones.Vigente(alicuota: 19m);

        var resultado = await EmitirAsync();

        AfirmarConflicto(resultado, MotivoConflictoEmision.AlicuotaNoAdmitida);
        Assert.Equal(0, _autorizador.Llamadas);
        Assert.Empty(_almacen.Pendientes);
    }

    /// <summary>AC-30: con el punto de venta 3 y el número 125 de ARCA, "Factura B 0003-00000125".</summary>
    [Fact]
    public async Task Emitir_PuntoVenta3_Numero125_Comprobante()
    {
        _almacen.Sembrar(1);

        var resultado = await EmitirAsync();

        Assert.True(resultado.EsEmitida);
        Assert.Equal(3, Assert.Single(_autorizador.Solicitudes).PuntoVenta);
        Assert.Equal("Factura B 0003-00000125", resultado.Factura.Comprobante);
        Assert.Equal(125, resultado.Factura.Numero);
    }

    /// <summary>AC-32 (sad path): sin punto de venta no se llama a ARCA.</summary>
    [Fact]
    public async Task Emitir_PuntoVentaFaltante_Conflicto409SinLlamarArca()
    {
        _almacen.Sembrar(1);
        _almacen.Configuracion = Configuraciones.Vigente(puntoVenta: null);

        var resultado = await EmitirAsync();

        AfirmarConflicto(resultado, MotivoConflictoEmision.SinPuntoVenta);
        Assert.Equal(0, _autorizador.Llamadas);
        Assert.Empty(_almacen.Pendientes);
    }

    /// <summary>Sad path: concepto faltante o que no es uno de los dos textos exactos: inválido sin leer la base.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Productos")]
    [InlineData("productos ")]
    [InlineData("servicios")]
    [InlineData("productos y servicios")]
    public async Task Emitir_ConceptoInvalido_400(string? concepto)
    {
        _almacen.Sembrar(1);

        var resultado = await EmitirAsync(concepto: concepto);

        Assert.Equal(TipoResultadoEmision.Invalido, resultado.Tipo);
        var error = Assert.Single(resultado.Errores);
        Assert.Equal(EmitirFactura.CampoConcepto, error.Campo);
        Assert.Equal(EmitirFactura.AccionConceptoInvalido, error.Accion);
        Assert.Empty(_almacen.Accesos);
        Assert.Equal(0, _autorizador.Llamadas);
    }

    /// <summary>Los dos conceptos válidos llegan a la solicitud (FR-08).</summary>
    [Theory]
    [InlineData(EmitirFactura.ConceptoProductos, ConceptoFactura.Productos)]
    [InlineData(EmitirFactura.ConceptoProductosYServicios, ConceptoFactura.ProductosYServicios)]
    public async Task Emitir_ConceptoValido_LlegaALaSolicitud(string concepto, ConceptoFactura esperado)
    {
        _almacen.Sembrar(1);

        Assert.True((await EmitirAsync(concepto: concepto)).EsEmitida);

        Assert.Equal(esperado, Assert.Single(_autorizador.Solicitudes).Concepto);
        Assert.Equal(esperado, Assert.Single(_almacen.Facturas).Concepto);
    }

    /// <summary>Sad path: un número sin presupuesto, no encontrado sin llamar a ARCA.</summary>
    [Fact]
    public async Task Emitir_PresupuestoInexistente_404Error()
    {
        _almacen.Sembrar(1);

        var resultado = await EmitirAsync(99);

        Assert.Equal(TipoResultadoEmision.NoEncontrado, resultado.Tipo);
        // Se buscó el presupuesto dentro de la primera transacción y no se leyó nada más.
        Assert.Equal(["Transaccion", "ObtenerPresupuesto", "FinTransaccion"], _almacen.Accesos);
        Assert.Equal(0, _autorizador.Llamadas);
        Assert.Empty(_almacen.Pendientes);
    }

    /// <summary>Sad path: la solicitud no se envió (D-16): sin pendiente y sin factura.</summary>
    [Fact]
    public async Task Emitir_NoEnviada_SinPendienteError()
    {
        _almacen.Sembrar(1);
        _autorizador.Responder = (_, _) => Task.FromResult<ResultadoAutorizacion>(
            new ResultadoAutorizacion.NoEnviada("La facturación con ARCA no está configurada."));

        var resultado = await EmitirAsync();

        AfirmarConflicto(resultado, MotivoConflictoEmision.NoEnviada);
        Assert.Empty(_almacen.Pendientes);
        Assert.Empty(_almacen.Facturas);
        Assert.Equal(1, _autorizador.Llamadas);
    }

    /// <summary>
    /// D-32 (sad path): otra emisión tiene el candado y no lo libera; a los 30 s esta responde "no enviada" sin llamar a
    /// ARCA y sin dejar la pendiente.
    /// </summary>
    [Fact]
    public async Task Emitir_OtraEmisionEnCurso_NoEnviadaTimeout()
    {
        _almacen.Sembrar(1);
        _candado.Ocupado = true;

        var emision = EmitirAsync();
        await _candado.Pedido.Task.WaitAsync(TimeSpan.FromSeconds(10));
        _reloj.Avanzar(TimeSpan.FromSeconds(29.9));
        Assert.False(_candado.Tokens[0].IsCancellationRequested, "La espera del candado venció antes de los 30 s.");
        Assert.Single(_almacen.Pendientes);

        _reloj.Avanzar(TimeSpan.FromSeconds(0.1));
        Assert.True(_candado.Tokens[0].IsCancellationRequested, "La espera del candado no venció a los 30 s.");
        var resultado = await emision.WaitAsync(TimeSpan.FromSeconds(10));

        AfirmarConflicto(resultado, MotivoConflictoEmision.NoEnviada);
        Assert.Equal(0, _autorizador.Llamadas);
        Assert.Empty(_almacen.Pendientes);
    }

    /// <summary>
    /// D-31 (sad path): ARCA autorizó pero la factura no se pudo grabar (conflicto al confirmar u otra falla): la
    /// pendiente queda, el log registra tipo, punto de venta, número y CAE sin datos del cliente, y el motivo es
    /// <c>pendiente_verificacion</c>.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Emitir_AutorizadaConConflictoAlGrabar_409PendienteVerificacionError(bool conflicto)
    {
        _almacen.Sembrar(1, EstadoPresupuesto.Final, Presupuestos_.Dni, 150_000m);
        _almacen.FallaAlConfirmarFactura = conflicto
            ? () => new ConflictoAlConfirmarException("Conflicto.", MotivoConflicto.Unicidad)
            : () => new InvalidOperationException("Falla forzada " + Presupuestos_.Dni);
        _autorizador.Responder = (_, _) => Task.FromResult<ResultadoAutorizacion>(
            new ResultadoAutorizacion.Autorizada(125, "75123456789012", new DateOnly(2026, 11, 9)));

        var resultado = await EmitirAsync();

        AfirmarConflicto(resultado, MotivoConflictoEmision.PendienteVerificacion);
        Assert.Single(_almacen.Pendientes);
        Assert.Empty(_almacen.Facturas);
        var error = Assert.Single(_registro.Lineas, linea => linea.Nivel >= LogLevel.Error);
        Assert.Contains("FacturaB", error.Texto, StringComparison.Ordinal);
        Assert.Contains("125", error.Texto, StringComparison.Ordinal);
        Assert.Contains("75123456789012", error.Texto, StringComparison.Ordinal);
        Assert.Contains("PuntoVenta=3", error.Texto, StringComparison.Ordinal);
        AfirmarSinDatosDelCliente(_registro.Todo);
    }

    /// <summary>
    /// WARN-2 de la auditoría: si el pedido HTTP se cancela durante la llamada a ARCA, la emisión sigue: el plazo de ARCA
    /// no depende del token del pedido y la segunda transacción corre sin cancelación, así la factura autorizada se graba.
    /// </summary>
    [Fact]
    public async Task Emitir_PedidoHttpCancelado_LaFacturaAutorizadaSeGraba()
    {
        _almacen.Sembrar(1);
        using var pedido = new CancellationTokenSource();
        _autorizador.Responder = (solicitud, _) =>
        {
            pedido.Cancel();
            return Task.FromResult<ResultadoAutorizacion>(
                new ResultadoAutorizacion.Autorizada(125, "75123456789012", solicitud.Fecha.AddDays(10)));
        };

        var resultado = await EmitirAsync(cancelacion: pedido.Token);

        Assert.True(resultado.EsEmitida);
        Assert.Single(_almacen.Facturas);
        Assert.Empty(_almacen.Pendientes);
        Assert.False(_autorizador.Tokens[0].IsCancellationRequested);
        Assert.True(_autorizador.Tokens[0].CanBeCanceled);
        Assert.Equal(2, _almacen.TokensDeTransaccion.Count);
        Assert.False(_almacen.TokensDeTransaccion[1].CanBeCanceled);
    }

    /// <summary>
    /// D-32: un único plazo de 30 s desde que se pide el candado. Si la espera del candado consumió 15 s, ARCA tiene los
    /// 15 restantes, no 30 más.
    /// </summary>
    [Fact]
    public async Task Emitir_CandadoYArca_UnSoloPlazoDe30s()
    {
        _almacen.Sembrar(1);
        _candado.AlTomar = () => _reloj.Avanzar(TimeSpan.FromSeconds(15));
        _autorizador.Responder = (_, token) => AutorizadorDoble.NoResponderAsync(token);

        var emision = EmitirAsync();
        await _autorizador.Llamado.Task.WaitAsync(TimeSpan.FromSeconds(10));
        // El mismo token (el mismo plazo) para la espera del candado y para la llamada.
        Assert.Equal(Assert.Single(_candado.Tokens), _autorizador.Tokens[0]);
        _reloj.Avanzar(TimeSpan.FromSeconds(14.9));
        Assert.False(_autorizador.Tokens[0].IsCancellationRequested, "El plazo venció antes de los 30 s.");

        _reloj.Avanzar(TimeSpan.FromSeconds(0.1));
        Assert.True(_autorizador.Tokens[0].IsCancellationRequested, "El plazo no venció a los 30 s en total.");
        var resultado = await emision.WaitAsync(TimeSpan.FromSeconds(10));

        AfirmarConflicto(resultado, MotivoConflictoEmision.SinRespuesta);
        Assert.Equal(1, _candado.Liberados);
    }

    /// <summary>
    /// D-32 (sad path): si después de tomar el candado quedan menos de 10 s del plazo, responde "no enviada" sin llamar a
    /// ARCA y quita la pendiente. Con 10 s justos todavía llama.
    /// </summary>
    [Theory]
    [InlineData(21, false)]
    [InlineData(20, true)]
    public async Task Emitir_CandadoConsumioElPlazo_NoEnviadaSinLlamarArca(int segundosDeEspera, bool llamaAArca)
    {
        _almacen.Sembrar(1);
        _candado.AlTomar = () => _reloj.Avanzar(TimeSpan.FromSeconds(segundosDeEspera));

        var resultado = await EmitirAsync();

        if (llamaAArca)
        {
            Assert.True(resultado.EsEmitida);
            Assert.Equal(1, _autorizador.Llamadas);
        }
        else
        {
            AfirmarConflicto(resultado, MotivoConflictoEmision.NoEnviada);
            Assert.Equal(0, _autorizador.Llamadas);
            Assert.Empty(_almacen.Pendientes);
        }

        Assert.Equal(1, _candado.Liberados);
    }

    /// <summary>
    /// R-07: con el receptor identificado por DNI, ni el log de una emisión autorizada, ni el de una rechazada (con su
    /// descripción), ni el de una sin respuesta llevan el DNI, el nombre, el email ni la descripción de ARCA.
    /// </summary>
    [Fact]
    public async Task Emitir_Log_SinDniNiDatosDelCliente()
    {
        _almacen.Sembrar(1, EstadoPresupuesto.Final, Presupuestos_.Dni, 150_000m);
        _almacen.Sembrar(2, EstadoPresupuesto.Final, Presupuestos_.Dni, 150_000m);
        _almacen.Sembrar(3, EstadoPresupuesto.Final, Presupuestos_.Dni, 150_000m);

        Assert.True((await EmitirAsync(1)).EsEmitida);
        Assert.Equal(Presupuestos_.Dni, _autorizador.Solicitudes[0].Receptor.Dni);
        _autorizador.Responder = (_, _) => Task.FromResult<ResultadoAutorizacion>(
            new ResultadoAutorizacion.Rechazada(10016, "Zq7DescripcionDeArca"));
        AfirmarConflicto(await EmitirAsync(2), MotivoConflictoEmision.Rechazada);
        _autorizador.Responder = (_, _) => Task.FromResult<ResultadoAutorizacion>(new ResultadoAutorizacion.SinRespuesta());
        AfirmarConflicto(await EmitirAsync(3), MotivoConflictoEmision.SinRespuesta);

        Assert.NotEmpty(_registro.Lineas);
        AfirmarSinDatosDelCliente(_registro.Todo);
        Assert.DoesNotContain("Zq7DescripcionDeArca", _registro.Todo, StringComparison.Ordinal);
    }

    /// <summary>
    /// R-09: un conflicto de unicidad al grabar la pendiente (otro pedido la grabó primero) es "pendiente"; si ya hay
    /// factura, "facturado". Ninguno llama a ARCA.
    /// </summary>
    [Fact]
    public async Task Emitir_ConflictoAlGrabarLaPendiente_PendienteSinLlamarArca()
    {
        var presupuesto = _almacen.Sembrar(1);
        var otra = new AlmacenFacturacionConCarrera(_almacen, presupuesto);

        var resultado = await new EmitirFactura(
            otra, _almacen, otra, otra, otra, _autorizador, _candado, _reloj, _registro)
            .EjecutarAsync(1, EmitirFactura.ConceptoProductos);

        AfirmarConflicto(resultado, MotivoConflictoEmision.Pendiente);
        Assert.Equal(0, _autorizador.Llamadas);
        Assert.Single(_almacen.Pendientes);
    }

    /// <summary>R-09: si el conflicto de unicidad al grabar la pendiente se da con la factura ya grabada, "facturado".</summary>
    [Fact]
    public async Task Emitir_ConflictoAlGrabarLaPendienteConFactura_FacturadoSinLlamarArca()
    {
        var presupuesto = _almacen.Sembrar(1);
        var otra = new AlmacenFacturacionConCarrera(_almacen, presupuesto, conFactura: true);

        var resultado = await new EmitirFactura(
            otra, _almacen, otra, otra, otra, _autorizador, _candado, _reloj, _registro)
            .EjecutarAsync(1, EmitirFactura.ConceptoProductos);

        AfirmarConflicto(resultado, MotivoConflictoEmision.Facturado);
        Assert.Equal(0, _autorizador.Llamadas);
    }

    /// <summary>
    /// WARN-2 de la auditoría (sad path): si la baja de la pendiente falla después de un rechazo o de un "no enviada",
    /// la operadora recibe igual el motivo real; la pendiente queda grabada (la resuelve el PRD 07) y el log lleva solo el
    /// tipo de la falla, nunca su texto.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Emitir_BajaDeLaPendienteFalla_DevuelveElMotivoReal(bool rechazo)
    {
        _almacen.Sembrar(1);
        _almacen.FallaAlConfirmarBaja = () => new InvalidOperationException("Falla forzada " + Presupuestos_.Dni);
        _autorizador.Responder = (_, _) => Task.FromResult<ResultadoAutorizacion>(rechazo
            ? new ResultadoAutorizacion.Rechazada(10016, "Número no correlativo.")
            : new ResultadoAutorizacion.NoEnviada("La facturación con ARCA no está configurada."));

        var resultado = await SinExcepcionAsync(() => EmitirAsync());

        if (rechazo)
        {
            AfirmarConflicto(resultado, MotivoConflictoEmision.Rechazada);
            Assert.Equal(10016, resultado.CodigoArca);
            Assert.Equal("Número no correlativo.", resultado.DescripcionArca);
        }
        else
        {
            AfirmarConflicto(resultado, MotivoConflictoEmision.NoEnviada);
        }

        Assert.Single(_almacen.Pendientes);
        Assert.Empty(_almacen.Facturas);
        Assert.Contains(_registro.Lineas, linea => linea.Texto.Contains(nameof(InvalidOperationException), StringComparison.Ordinal));
        Assert.DoesNotContain("Falla forzada", _registro.Todo, StringComparison.Ordinal);
        AfirmarSinDatosDelCliente(_registro.Todo);
    }

    /// <summary>
    /// WARN-1 de la auditoría: si liberar el candado falla porque la Api se está apagando, una factura que ARCA autorizó
    /// se graba igual.
    /// </summary>
    [Fact]
    public async Task Emitir_LiberarElCandadoFalla_LaAutorizadaNoSePierde()
    {
        _almacen.Sembrar(1);
        _candado.LiberarLanza = true;

        var resultado = await SinExcepcionAsync(() => EmitirAsync());

        Assert.True(resultado.EsEmitida);
        Assert.Single(_almacen.Facturas);
        Assert.Empty(_almacen.Pendientes);
        Assert.Equal(1, _candado.Liberados);
    }

    /// <summary>
    /// D-32 con un plazo corto (el harness de e2e usa 2 s, D-15): el margen es un tercio del plazo (~0,67 s), no 10 s.
    /// Quedan 0,7 s, llama; quedan 0,6 s, "no enviada" sin llamar.
    /// </summary>
    [Theory]
    [InlineData(1300, true)]
    [InlineData(1400, false)]
    public async Task Emitir_PlazoDe2s_MargenDeUnTercio(int esperaMs, bool llamaAArca)
    {
        var autorizador = new AutorizadorDoble(_almacen) { TiempoMaximo = TimeSpan.FromSeconds(2) };
        _almacen.Sembrar(1);
        _candado.AlTomar = () => _reloj.Avanzar(TimeSpan.FromMilliseconds(esperaMs));

        var resultado = await new EmitirFactura(
            _almacen, _almacen, _almacen, _almacen, _almacen, autorizador, _candado, _reloj, _registro)
            .EjecutarAsync(1, EmitirFactura.ConceptoProductos);

        Assert.Equal(llamaAArca ? 1 : 0, autorizador.Llamadas);
        if (!llamaAArca)
        {
            AfirmarConflicto(resultado, MotivoConflictoEmision.NoEnviada);
        }
    }

    /// <summary>D-32 con el plazo de 30 s: el margen máximo de 10 s rige.</summary>
    [Fact]
    public void MargenMaximo_DiezSegundos()
    {
        Assert.Equal(TimeSpan.FromSeconds(10), EmitirFactura.MargenMaximo);
    }

    /// <summary>
    /// Sad path: una excepción imprevista del autorizador (el puerto dice que no lanza) se propaga sin atenderla: no se
    /// sabe si se envió, así que la pendiente queda; el candado se libera.
    /// </summary>
    [Fact]
    public async Task Emitir_AutorizadorLanza_SePropagaYLaPendienteQueda()
    {
        _almacen.Sembrar(1);
        _autorizador.Responder = (_, _) => throw new InvalidOperationException("Falla imprevista.");

        await Assert.ThrowsAsync<InvalidOperationException>(() => EmitirAsync());

        Assert.Single(_almacen.Pendientes);
        Assert.Empty(_almacen.Facturas);
        Assert.Equal(1, _candado.Liberados);
    }

    /// <summary>Ejecuta la emisión y afirma que no lanzó: la falla se informa como aserción, con el tipo de la excepción.</summary>
    private static async Task<ResultadoEmision> SinExcepcionAsync(Func<Task<ResultadoEmision>> emitir)
    {
        ResultadoEmision? resultado = null;
        var error = await Record.ExceptionAsync(async () => resultado = await emitir());
        Assert.Null(error);
        return resultado!;
    }

    private static void AfirmarSinDatosDelCliente(string texto)
    {
        foreach (var dato in new[]
        {
            Presupuestos_.Dni, "00.718.294", "718294", Presupuestos_.Apellido, Presupuestos_.Nombre, "Zq7",
            Presupuestos_.Email, "5555-0199", "Calle Ficticia",
        })
        {
            Assert.DoesNotContain(dato, texto, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// El almacén con una carrera: justo antes de confirmar la pendiente, otro pedido graba la suya (lo que en SQLite
    /// impide <c>BEGIN IMMEDIATE</c>, y queda solo como defensa).
    /// </summary>
    private sealed class AlmacenFacturacionConCarrera(
        AlmacenFacturacion real, Presupuesto presupuesto, bool conFactura = false) :
        IPresupuestoRepository, IFacturaRepository, IEmisionPendienteRepository, IUnidadDeTrabajo
    {
        public Task<long?> ObtenerUltimoNumeroAsync(CancellationToken cancelacion = default) =>
            real.ObtenerUltimoNumeroAsync(cancelacion);

        public void Agregar(Presupuesto nuevo) => real.Agregar(nuevo);

        public Task<Presupuesto?> ObtenerAsync(long numero, CancellationToken cancelacion = default) =>
            real.ObtenerAsync(numero, cancelacion);

        public void Actualizar(Presupuesto original, Presupuesto modificado, long versionOriginal) =>
            real.Actualizar(original, modificado, versionOriginal);

        public void CambiarEstado(Presupuesto cambiado, long versionOriginal) =>
            real.CambiarEstado(cambiado, versionOriginal);

        public Task<PaginaPresupuestos> BuscarAsync(
            FiltroPresupuestos filtro, int pagina, int tamanoPagina, CancellationToken cancelacion = default) =>
            real.BuscarAsync(filtro, pagina, tamanoPagina, cancelacion);

        public void Agregar(Factura factura) => real.Agregar(factura);

        public Task<Factura?> ObtenerPorPresupuestoAsync(long numero, CancellationToken cancelacion = default) =>
            real.ObtenerPorPresupuestoAsync(numero, cancelacion);

        public Task<NumeroComprobante?> ObtenerComprobantePorPresupuestoAsync(
            long numero, CancellationToken cancelacion = default) =>
            real.ObtenerComprobantePorPresupuestoAsync(numero, cancelacion);

        public Task<bool> ExisteParaPresupuestoAsync(long numero, CancellationToken cancelacion = default) =>
            real.ExisteParaPresupuestoAsync(numero, cancelacion);

        public void Agregar(EmisionPendiente pendiente) => real.Agregar(pendiente);

        public void Quitar(long numero) => real.Quitar(numero);

        public Task<bool> ExisteAsync(long numero, CancellationToken cancelacion = default) =>
            real.ExisteAsync(numero, cancelacion);

        public Task<T> EjecutarEnTransaccionAsync<T>(
            Func<CancellationToken, Task<T>> operacion, CancellationToken cancelacion = default) =>
            real.EjecutarEnTransaccionAsync(operacion, cancelacion);

        public Task ConfirmarAsync(CancellationToken cancelacion = default)
        {
            if (real.Pendientes.Count == 0)
            {
                // El otro pedido llegó a grabar la pendiente y, con conFactura, también su factura.
                real.SembrarPendiente(presupuesto);
                if (conFactura)
                {
                    real.SembrarFactura(presupuesto);
                }
            }

            return real.ConfirmarAsync(cancelacion);
        }
    }
}
