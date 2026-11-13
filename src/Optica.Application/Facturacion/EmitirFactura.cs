using Microsoft.Extensions.Logging;
using Optica.Application.Comun;
using Optica.Domain.Configuracion;
using Optica.Domain.Facturacion;
using Optica.Domain.Presupuestos;

namespace Optica.Application.Facturacion;

/// <summary>
/// Emite la factura a consumidor final de un presupuesto en Final (FR-01 a FR-04, FR-07, FR-12, FR-13, FR-15, FR-17 a
/// FR-25, FR-27, FR-29) en dos transacciones con una emisión pendiente (D-09, ADR-021):
/// <list type="number">
/// <item>Valida el concepto antes de leer la base: faltante o distinto de <see cref="ConceptoProductos"/> y
/// <see cref="ConceptoProductosYServicios"/>, inválido en <see cref="CampoConcepto"/>.</item>
/// <item>Transacción 1 (con el token del pedido): lee el presupuesto (no encontrado), rechaza un Borrador, uno con
/// factura o con emisión pendiente; lee la configuración vigente; arma la solicitud con la fecha local del día
/// (<see cref="ReglasEmision.Armar"/>: sin tope, sin punto de venta, alícuota no admitida); graba la pendiente y
/// confirma. Un conflicto de unicidad al confirmar es <c>facturado</c> si ya hay factura y si no <c>pendiente</c>.</item>
/// <item>Un único plazo, <see cref="IAutorizadorComprobantes.TiempoMaximo"/>, medido con un
/// <see cref="CancellationTokenSource"/> sobre el <see cref="TimeProvider"/> desde que se pide el candado de emisión:
/// cubre la espera del candado y la llamada a ARCA (D-32). Sin el candado en el plazo, o con menos de
/// margen restante al tomarlo (el menor entre <see cref="MargenMaximo"/> y un tercio del plazo), "no enviada" sin llamar a ARCA. El plazo no se enlaza con el token del
/// pedido: si el navegador corta, la emisión sigue.</item>
/// <item>Transacción 2, sin cancelación: autorizada, graba la factura y quita la pendiente (FR-18); rechazada o no
/// enviada, quita la pendiente (FR-21, FR-22); sin respuesta, la deja (FR-23, FR-24). Si la factura autorizada no se
/// puede grabar, la pendiente queda y el resultado es <c>pendiente_verificacion</c> (D-31).</item>
/// </list>
/// Ninguna rama reintenta por su cuenta.
/// </summary>
/// <remarks>
/// El log (R-07) nunca lleva el DNI, la solicitud, los datos del cliente ni la descripción de un rechazo de ARCA, ni
/// adjunta excepciones (su texto o sus <c>Entries</c> pueden llevar el DNI). Solo D-31 registra tipo, punto de venta,
/// número y CAE, para que el PRD 07 recupere la factura.
/// </remarks>
public sealed class EmitirFactura(
    IPresupuestoRepository presupuestos,
    IConfiguracionNegocioRepository configuracion,
    IFacturaRepository facturas,
    IEmisionPendienteRepository pendientes,
    IUnidadDeTrabajo unidadDeTrabajo,
    IAutorizadorComprobantes autorizador,
    ICandadoEmision candado,
    TimeProvider reloj,
    ILogger<EmitirFactura> registro)
{
    /// <summary>El campo del concepto en los errores (FR-08).</summary>
    public const string CampoConcepto = "concepto";

    /// <summary>El texto del concepto Productos.</summary>
    public const string ConceptoProductos = "productos";

    /// <summary>El texto del concepto Productos y servicios.</summary>
    public const string ConceptoProductosYServicios = "productos_y_servicios";

    /// <summary>La acción de un concepto faltante o desconocido; no repite lo recibido.</summary>
    public const string AccionConceptoInvalido = "Elegí el concepto de la factura: Productos o Productos y servicios.";

    /// <summary>
    /// El tope del margen que tiene que quedar del plazo al tomar el candado para llamar a ARCA (D-32): 10 s, el que rige
    /// con el plazo de 30 s de producción. El margen es el menor entre este tope y un tercio del plazo: con un plazo más
    /// corto (el harness de e2e usa 2 s, D-15) es ~0,67 s, así una emisión sin espera no queda siempre "no enviada".
    /// </summary>
    public static readonly TimeSpan MargenMaximo = TimeSpan.FromSeconds(10);

    public async Task<ResultadoEmision> EjecutarAsync(long numero, string? concepto, CancellationToken cancelacion = default)
    {
        if (LeerConcepto(concepto) is not { } conceptoFactura)
        {
            return ResultadoEmision.Invalido([new ErrorValidacion(CampoConcepto, AccionConceptoInvalido)]);
        }

        var preparada = await unidadDeTrabajo.EjecutarEnTransaccionAsync(
            operacion => PrepararAsync(numero, conceptoFactura, operacion), cancelacion);
        if (preparada.Rechazo is { } rechazo)
        {
            return rechazo;
        }

        var presupuesto = preparada.Presupuesto!;
        var solicitud = preparada.Solicitud!;
        var resultado = await AutorizarEnPlazoAsync(solicitud);

        // Sin cancelación a propósito (D-32): un pedido HTTP cortado no pierde un CAE ya autorizado.
        return resultado switch
        {
            ResultadoAutorizacion.Autorizada autorizada => await GrabarFacturaAsync(presupuesto, solicitud, autorizada),
            ResultadoAutorizacion.Rechazada rechazada => await RechazadaAsync(numero, rechazada),
            ResultadoAutorizacion.NoEnviada noEnviada => await NoEnviadaAsync(numero, noEnviada.Motivo),
            ResultadoAutorizacion.SinRespuesta => SinRespuesta(numero),
            _ => throw new InvalidOperationException("Resultado de la autorización de ARCA no previsto."),
        };
    }

    private static ConceptoFactura? LeerConcepto(string? concepto) => concepto switch
    {
        ConceptoProductos => ConceptoFactura.Productos,
        ConceptoProductosYServicios => ConceptoFactura.ProductosYServicios,
        _ => null,
    };

    /// <summary>Transacción 1: valida contra lo guardado, arma la solicitud y graba la emisión pendiente.</summary>
    private async Task<Preparacion> PrepararAsync(long numero, ConceptoFactura concepto, CancellationToken operacion)
    {
        var presupuesto = await presupuestos.ObtenerAsync(numero, operacion);
        if (presupuesto is null)
        {
            return Preparacion.Rechazada(ResultadoEmision.NoEncontrado);
        }

        if (presupuesto.Estado != EstadoPresupuesto.Final)
        {
            return Preparacion.Rechazada(ResultadoEmision.Conflicto(MotivoConflictoEmision.Borrador));
        }

        if (await facturas.ExisteParaPresupuestoAsync(numero, operacion))
        {
            return Preparacion.Rechazada(ResultadoEmision.Conflicto(MotivoConflictoEmision.Facturado));
        }

        if (await pendientes.ExisteAsync(numero, operacion))
        {
            return Preparacion.Rechazada(ResultadoEmision.Conflicto(MotivoConflictoEmision.Pendiente));
        }

        var vigente = await configuracion.ObtenerAsync(operacion);
        var fecha = DateOnly.FromDateTime(reloj.GetLocalNow().DateTime);
        var armado = ReglasEmision.Armar(presupuesto, vigente, concepto, fecha);
        if (!armado.EsArmada)
        {
            return Preparacion.Rechazada(ResultadoEmision.Conflicto(MotivoDe(armado.Motivo.Value)));
        }

        pendientes.Agregar(new EmisionPendiente(numero, reloj.GetUtcNow(), armado.Solicitud, autorizador.Entorno));
        try
        {
            await unidadDeTrabajo.ConfirmarAsync(operacion);
        }
        catch (ConflictoAlConfirmarException conflicto) when (conflicto.Motivo == MotivoConflicto.Unicidad)
        {
            // R-09: otro pedido grabó la pendiente (o la factura) primero. BEGIN IMMEDIATE lo impide; queda como defensa.
            var facturado = await facturas.ExisteParaPresupuestoAsync(numero, operacion);
            return Preparacion.Rechazada(ResultadoEmision.Conflicto(
                facturado ? MotivoConflictoEmision.Facturado : MotivoConflictoEmision.Pendiente));
        }

        return new Preparacion(presupuesto, armado.Solicitud, null);
    }

    private static MotivoConflictoEmision MotivoDe(MotivoEmisionRechazada motivo) => motivo switch
    {
        MotivoEmisionRechazada.Borrador => MotivoConflictoEmision.Borrador,
        MotivoEmisionRechazada.SinTope => MotivoConflictoEmision.SinTope,
        MotivoEmisionRechazada.SinPuntoVenta => MotivoConflictoEmision.SinPuntoVenta,
        MotivoEmisionRechazada.AlicuotaNoAdmitida => MotivoConflictoEmision.AlicuotaNoAdmitida,
        _ => throw new InvalidOperationException("Motivo de rechazo de la emisión no previsto."),
    };

    /// <summary>
    /// El paso 3 (D-32): un solo plazo, desde que se pide el candado, para la espera de otra emisión y la llamada. Libera
    /// el candado apenas ARCA responde: la numeración de ARCA ya avanzó, y la grabación no necesita el turno.
    /// </summary>
    private async Task<ResultadoAutorizacion> AutorizarEnPlazoAsync(SolicitudComprobante solicitud)
    {
        var tiempoMaximo = autorizador.TiempoMaximo;
        using var plazo = new CancellationTokenSource(tiempoMaximo, reloj);
        var inicio = reloj.GetTimestamp();
        var turno = await candado.TomarAsync(plazo.Token);
        if (turno is null)
        {
            return new ResultadoAutorizacion.NoEnviada(MotivoOtraEmision);
        }

        try
        {
            var restante = tiempoMaximo - reloj.GetElapsedTime(inicio);
            if (restante < Margen(tiempoMaximo))
            {
                return new ResultadoAutorizacion.NoEnviada(MotivoOtraEmision);
            }

            return await autorizador.AutorizarAsync(solicitud, plazo.Token);
        }
        finally
        {
            Liberar(turno);
        }
    }

    /// <summary>
    /// Libera el turno. Si la Api se está apagando y el candado ya se descartó, lo tolera: una factura que ARCA autorizó
    /// no se pierde por no poder liberar un candado que ya no existe (WARN-1 de la auditoría).
    /// </summary>
    private static void Liberar(IDisposable turno)
    {
        try
        {
            turno.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // Intencional: el candado ya no existe; no hay turno que devolver.
        }
    }

    /// <summary>El menor entre <see cref="MargenMaximo"/> y un tercio del plazo.</summary>
    private static TimeSpan Margen(TimeSpan tiempoMaximo)
    {
        var tercio = tiempoMaximo / 3;
        return tercio < MargenMaximo ? tercio : MargenMaximo;
    }

    private const string MotivoOtraEmision =
        "Otra emisión ocupó el plazo de espera: la solicitud no se envió a ARCA.";

    /// <summary>Transacción 2 de una autorizada: la factura con sus líneas y la baja de la pendiente (FR-18).</summary>
    private async Task<ResultadoEmision> GrabarFacturaAsync(
        Presupuesto presupuesto, SolicitudComprobante solicitud, ResultadoAutorizacion.Autorizada autorizada)
    {
        Factura? factura = null;
        try
        {
            await unidadDeTrabajo.EjecutarEnTransaccionAsync(
                async operacion =>
                {
                    factura = Factura.Emitida(
                        presupuesto, solicitud, autorizada.Numero, autorizada.Cae, autorizada.Vencimiento,
                        autorizador.Entorno);
                    facturas.Agregar(factura);
                    pendientes.Quitar(presupuesto.Numero);
                    await unidadDeTrabajo.ConfirmarAsync(operacion);
                    return true;
                },
                CancellationToken.None);
        }
        catch (Exception error)
        {
            // D-31: ARCA autorizó y la factura no quedó grabada. La pendiente sigue (la revirtió la transacción) para el
            // PRD 07. Solo el tipo de la falla: su texto o sus Entries pueden llevar el DNI del receptor (R-07).
            registro.LogError(
                "ARCA autorizó la {Tipo} punto de venta {PuntoVenta} número {Numero} con CAE {Cae} y la factura no se " +
                "pudo grabar ({TipoFalla}); la emisión del presupuesto {Presupuesto} queda pendiente de verificación.",
                solicitud.Tipo,
                solicitud.PuntoVenta,
                autorizada.Numero,
                autorizada.Cae,
                error.GetType().Name,
                presupuesto.Numero);
            return ResultadoEmision.Conflicto(MotivoConflictoEmision.PendienteVerificacion);
        }

        return ResultadoEmision.Emitida(FacturaEmitidaDesde(factura!));
    }

    private static FacturaEmitida FacturaEmitidaDesde(Factura factura) => new(
        factura.Comprobante.ToString(),
        factura.Tipo == TipoComprobante.FacturaB ? FacturaEmitida.TipoB : FacturaEmitida.TipoC,
        factura.Numero,
        factura.Cae,
        factura.VencimientoCae,
        factura.Fecha,
        factura.Importes.Total,
        factura.Importes.Neto,
        factura.Importes.Iva);

    private async Task<ResultadoEmision> RechazadaAsync(long numero, ResultadoAutorizacion.Rechazada rechazada)
    {
        await IntentarQuitarPendienteAsync(numero);
        // Solo el código: la descripción de ARCA puede repetir datos de la solicitud (R-07).
        registro.LogInformation(
            "ARCA rechazó la factura del presupuesto {Presupuesto} con el código {Codigo}.", numero, rechazada.Codigo);
        return ResultadoEmision.Conflicto(MotivoConflictoEmision.Rechazada, rechazada.Codigo, rechazada.Descripcion);
    }

    private async Task<ResultadoEmision> NoEnviadaAsync(long numero, string motivo)
    {
        await IntentarQuitarPendienteAsync(numero);
        // El motivo de "no enviada" es un texto fijo, sin datos del cliente ni secretos (ResultadoAutorizacion).
        registro.LogWarning(
            "La factura del presupuesto {Presupuesto} no se envió a ARCA: {Motivo}", numero, motivo);
        return ResultadoEmision.Conflicto(MotivoConflictoEmision.NoEnviada);
    }

    private ResultadoEmision SinRespuesta(long numero)
    {
        // FR-23 y FR-24: no se sabe si ARCA autorizó; la pendiente queda y no se reintenta.
        registro.LogWarning(
            "ARCA no respondió a la emisión del presupuesto {Presupuesto}: queda pendiente de verificación.", numero);
        return ResultadoEmision.Conflicto(MotivoConflictoEmision.SinRespuesta);
    }

    /// <summary>
    /// Transacción 2 de una rechazada o no enviada: solo la baja de la pendiente (FR-21, FR-22). Si la baja falla, la
    /// pendiente queda grabada (la resuelve el PRD 07) y la operadora recibe igual el motivo real (WARN-2 de la
    /// auditoría). Al log va solo el tipo de la falla: su texto o sus <c>Entries</c> pueden llevar datos (R-07).
    /// </summary>
    private async Task IntentarQuitarPendienteAsync(long numero)
    {
        try
        {
            await unidadDeTrabajo.EjecutarEnTransaccionAsync(
                async operacion =>
                {
                    pendientes.Quitar(numero);
                    await unidadDeTrabajo.ConfirmarAsync(operacion);
                    return true;
                },
                CancellationToken.None);
        }
        catch (Exception error)
        {
            registro.LogError(
                "No se pudo quitar la emisión pendiente del presupuesto {Presupuesto} ({TipoFalla}); queda pendiente de " +
                "verificación aunque ARCA no emitió la factura.",
                numero,
                error.GetType().Name);
        }
    }

    /// <summary>Lo que deja la transacción 1: el presupuesto y la solicitud ya con su pendiente grabada, o el rechazo.</summary>
    private sealed record Preparacion(
        Presupuesto? Presupuesto, SolicitudComprobante? Solicitud, ResultadoEmision? Rechazo)
    {
        public static Preparacion Rechazada(ResultadoEmision rechazo) => new(null, null, rechazo);

        // Lleva el DNI del receptor dentro de la solicitud: sin valores en ToString (Ley 25.326).
        private bool PrintMembers(System.Text.StringBuilder constructor) => false;
    }
}
