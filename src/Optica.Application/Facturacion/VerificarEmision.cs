using Microsoft.Extensions.Logging;
using Optica.Application.Comun;
using Optica.Domain.Configuracion;
using Optica.Domain.Facturacion;
using Optica.Domain.Presupuestos;

namespace Optica.Application.Facturacion;

/// <summary>
/// Verifica en ARCA la emisión pendiente de un presupuesto, a pedido de la operadora (FR-04 a FR-08, FR-12 a FR-16,
/// ADR-025, ADR-026):
/// <list type="number">
/// <item>Lee el presupuesto (no encontrado) y su pendiente (<c>sin_pendiente</c>).</item>
/// <item>Si no pasó <see cref="OpcionesVerificacion.EsperaVerificacion"/> (60 s) desde el momento de la pendiente,
/// <c>espera</c> con los segundos que faltan, redondeados hacia arriba (FR-16, AC-17), sin llamar a ARCA.</item>
/// <item>Sin número (FR-14) o de un entorno distinto del configurado (FR-15): graba la observación, sin llamar a ARCA ni
/// tomar el candado.</item>
/// <item>Si no, toma el turno del candado con un plazo de <see cref="IAutorizadorComprobantes.TiempoMaximo"/> (30 s) y lo
/// conserva hasta el final (D-09, D-10). Con el turno, relee la pendiente: si otra operación la reemplazó mientras
/// esperaba (otro número o momento), <c>no_enviada</c> sin consultar ni grabar (D-30); si ya no está,
/// <c>sin_pendiente</c>. Si sigue siendo la misma, repite los controles y consulta el número con el token del plazo:
/// <list type="bullet">
/// <item>Autorizado con el mismo total en centavos: una transacción graba <see cref="Factura.Recuperada"/> y quita la
/// observación y la pendiente (FR-05), sin pedir número ni enviar solicitud. Si la factura no valida o no se puede grabar,
/// la pendiente queda y el resultado es <c>no_enviada</c> (D-18).</item>
/// <item>Autorizado con otro total: reemplaza la observación por <c>importe_distinto</c> con los dos totales (FR-07,
/// FR-08, D-12).</item>
/// <item>No autorizado: arma la solicitud con el presupuesto, el concepto guardado y la configuración vigente, y la envía
/// con <see cref="ProcesoEmision"/> en modo reemplazo, con el mismo turno y un plazo nuevo de 30 s (FR-06, FR-13, D-10,
/// D-11).</item>
/// <item>Sin respuesta o no consultada: la pendiente queda intacta, <c>sin_respuesta</c> (FR-12, AC-11).</item>
/// </list></item>
/// </list>
/// Nunca reintenta. Volver a verificar con una observación vigente vuelve a evaluar y la reemplaza en una transacción
/// (D-12). Las transacciones que graban no usan el token del pedido ni el del plazo (D-10).
/// </summary>
/// <remarks>
/// El log nunca lleva el DNI, los datos del cliente, la descripción de un rechazo de ARCA ni el motivo de "no consultada"
/// del autorizador, ni adjunta excepciones (solo su tipo).
/// </remarks>
public sealed class VerificarEmision(
    IPresupuestoRepository presupuestos,
    IConfiguracionNegocioRepository configuracion,
    IFacturaRepository facturas,
    IEmisionPendienteRepository pendientes,
    IObservacionEmisionRepository observaciones,
    IUnidadDeTrabajo unidadDeTrabajo,
    IAutorizadorComprobantes autorizador,
    ICandadoEmision candado,
    ProcesoEmision proceso,
    OpcionesVerificacion opciones,
    TimeProvider reloj,
    ILogger<VerificarEmision> registro)
{
    private const string MotivoOtraOperacion =
        "Otra emisión o verificación ocupó el plazo de espera: no se consultó a ARCA.";

    private const string MotivoPendienteCambio =
        "La emisión pendiente cambió durante la verificación: no se grabó nada.";

    public async Task<ResultadoVerificacion> EjecutarAsync(long numero, CancellationToken cancelacion = default)
    {
        if (numero < 1)
        {
            return ResultadoVerificacion.NoEncontrado;
        }

        if (await presupuestos.ObtenerAsync(numero, cancelacion) is not { } presupuesto)
        {
            return ResultadoVerificacion.NoEncontrado;
        }

        var leida = await pendientes.ObtenerPendienteAsync(numero, cancelacion);
        if (await EvaluarAsync(numero, leida) is { } sinConsultar)
        {
            return sinConsultar;
        }

        // D-10: el plazo cuenta desde que se pide el candado y cubre la consulta; no se enlaza con el token del pedido.
        using var plazo = PlazoEmision.Iniciar(autorizador.TiempoMaximo, reloj);
        var turno = await candado.TomarAsync(plazo.Token);
        if (turno is null)
        {
            registro.LogWarning(
                "La verificación del presupuesto {Presupuesto} no se hizo: {Motivo}", numero, MotivoOtraOperacion);
            return ResultadoVerificacion.Conflicto(MotivoConflictoVerificacion.NoEnviada);
        }

        try
        {
            // D-30: la operadora pidió verificar la pendiente que leyó. Si mientras se esperaba el turno otra operación la
            // reemplazó (FR-06 de otra verificación), no se consulta ni se graba nada sobre la nueva.
            var actual = await pendientes.ObtenerPendienteAsync(numero, CancellationToken.None);
            if (actual is not null && !EsLaMisma(actual, leida!))
            {
                return CambioLaPendiente(numero, actual);
            }

            if (await EvaluarAsync(numero, actual) is { } resuelta)
            {
                return resuelta;
            }

            return await ConsultarAsync(presupuesto, actual!, turno, plazo);
        }
        finally
        {
            Liberar(turno);
        }
    }

    /// <summary>
    /// Los controles que no llaman a ARCA: sin pendiente, espera (FR-16), sin número (FR-14) y otro entorno (FR-15). Null
    /// si la pendiente se puede consultar.
    /// </summary>
    private async Task<ResultadoVerificacion?> EvaluarAsync(long numero, EmisionPendiente? pendiente)
    {
        if (pendiente is null)
        {
            return ResultadoVerificacion.Conflicto(MotivoConflictoVerificacion.SinPendiente);
        }

        var falta = opciones.EsperaVerificacion - (reloj.GetUtcNow() - pendiente.Momento);
        if (falta > TimeSpan.Zero)
        {
            return ResultadoVerificacion.Espera((int)Math.Min(Math.Ceiling(falta.TotalSeconds), int.MaxValue));
        }

        if (!pendiente.TieneNumero)
        {
            return await ObservarAsync(numero, pendiente, MotivoObservacionEmision.SinNumero, null);
        }

        if (pendiente.Entorno != autorizador.Entorno)
        {
            return await ObservarAsync(numero, pendiente, MotivoObservacionEmision.OtroEntorno, null);
        }

        return null;
    }

    private async Task<ResultadoVerificacion> ConsultarAsync(
        Presupuesto presupuesto, EmisionPendiente pendiente, IDisposable turno, PlazoEmision plazo)
    {
        var numero = presupuesto.Numero;
        var consulta = await autorizador.ConsultarAsync(
            pendiente.Tipo, pendiente.PuntoVenta, pendiente.Numero!.Value, plazo.Token);

        switch (consulta)
        {
            case ResultadoConsulta.Autorizado autorizado when Centavos(autorizado.Total) == Centavos(pendiente.Total):
                return await RecuperarAsync(presupuesto, pendiente, autorizado);

            case ResultadoConsulta.Autorizado autorizado:
                // Solo números del comprobante y totales: no son datos del cliente.
                registro.LogWarning(
                    "ARCA tiene autorizada la {Tipo} punto de venta {PuntoVenta} número {Numero} con otro total; la " +
                    "emisión del presupuesto {Presupuesto} queda observada para revisar en ARCA.",
                    pendiente.Tipo,
                    pendiente.PuntoVenta,
                    pendiente.Numero,
                    numero);
                return await ObservarAsync(
                    numero, pendiente, MotivoObservacionEmision.ImporteDistinto,
                    Math.Round(autorizado.Total, 2, MidpointRounding.AwayFromZero));

            case ResultadoConsulta.NoAutorizado:
                return await EmitirAsync(presupuesto, pendiente, turno);

            case ResultadoConsulta.SinRespuesta:
                registro.LogWarning(
                    "ARCA no respondió a la consulta del comprobante del presupuesto {Presupuesto}; queda pendiente de " +
                    "verificación.",
                    numero);
                return ResultadoVerificacion.Conflicto(MotivoConflictoVerificacion.SinRespuesta);

            case ResultadoConsulta.NoConsultada:
                // Texto propio: el motivo del autorizador no se repite (INFO del Block 2).
                registro.LogWarning(
                    "La consulta a ARCA del comprobante del presupuesto {Presupuesto} no se envió; queda pendiente de " +
                    "verificación.",
                    numero);
                return ResultadoVerificacion.Conflicto(MotivoConflictoVerificacion.SinRespuesta);

            default:
                throw new InvalidOperationException("Resultado de la consulta de ARCA no previsto.");
        }
    }

    /// <summary>
    /// FR-05: la transacción que graba la factura recuperada y quita la observación y la pendiente, si la pendiente sigue
    /// siendo la consultada. Si la factura no valida (D-18) o no se puede grabar, la pendiente queda y el resultado es
    /// <c>no_enviada</c>; al log van el campo que no validó y el tipo de la falla, sin DNI.
    /// </summary>
    private async Task<ResultadoVerificacion> RecuperarAsync(
        Presupuesto presupuesto, EmisionPendiente pendiente, ResultadoConsulta.Autorizado autorizado)
    {
        var numero = presupuesto.Numero;
        Factura? factura = null;
        try
        {
            var rechazo = await unidadDeTrabajo.EjecutarEnTransaccionAsync<ResultadoVerificacion?>(
                async operacion =>
                {
                    var actual = await pendientes.ObtenerPendienteAsync(numero, operacion);
                    if (actual is null || !EsLaMisma(actual, pendiente))
                    {
                        return CambioLaPendiente(numero, actual);
                    }

                    if (await facturas.ExisteParaPresupuestoAsync(numero, operacion))
                    {
                        return ResultadoVerificacion.Conflicto(MotivoConflictoVerificacion.Facturado);
                    }

                    factura = Factura.Recuperada(
                        presupuesto, actual, autorizado.Cae, autorizado.Vencimiento, autorizador.Entorno);
                    facturas.Agregar(factura);
                    observaciones.QuitarObservacion(numero);
                    pendientes.Quitar(numero);
                    await unidadDeTrabajo.ConfirmarAsync(operacion);
                    return null;
                },
                CancellationToken.None);
            if (rechazo is not null)
            {
                return rechazo;
            }
        }
        catch (Exception error)
        {
            // Solo el campo (sin valor) y el tipo de la falla: el texto o las Entries pueden llevar el DNI.
            registro.LogError(
                "ARCA tiene autorizada la {Tipo} punto de venta {PuntoVenta} número {Numero} con CAE {Cae} y la factura " +
                "recuperada no se pudo grabar ({TipoFalla}, campo {Campo}); la emisión del presupuesto {Presupuesto} " +
                "queda pendiente de verificación.",
                pendiente.Tipo,
                pendiente.PuntoVenta,
                pendiente.Numero,
                autorizado.Cae,
                error.GetType().Name,
                (error as ArgumentException)?.ParamName,
                numero);
            return ResultadoVerificacion.Conflicto(MotivoConflictoVerificacion.NoEnviada);
        }

        registro.LogInformation(
            "Se recuperó de ARCA la {Tipo} punto de venta {PuntoVenta} número {Numero} del presupuesto {Presupuesto}.",
            pendiente.Tipo,
            pendiente.PuntoVenta,
            pendiente.Numero,
            numero);
        return ResultadoVerificacion.Recuperada(FacturaEmitidaDesde(factura!));
    }

    /// <summary>
    /// FR-06: ARCA no tiene el número. Arma la solicitud con el concepto guardado y la configuración vigente, y la envía
    /// con <see cref="ProcesoEmision"/> en modo reemplazo, con el mismo turno y un plazo nuevo (D-10, D-11).
    /// </summary>
    private async Task<ResultadoVerificacion> EmitirAsync(
        Presupuesto presupuesto, EmisionPendiente pendiente, IDisposable turno)
    {
        var vigente = await configuracion.ObtenerAsync(CancellationToken.None);
        var fecha = DateOnly.FromDateTime(reloj.GetLocalNow().DateTime);
        var armado = ReglasEmision.Armar(presupuesto, vigente, pendiente.Concepto!.Value, fecha);
        if (!armado.EsArmada)
        {
            registro.LogWarning(
                "La emisión del presupuesto {Presupuesto} no se envió a ARCA: la configuración vigente no la admite " +
                "({Motivo}); la emisión pendiente queda.",
                presupuesto.Numero,
                armado.Motivo.Value);
            return ResultadoVerificacion.Conflicto(MotivoConflictoVerificacion.NoEnviada);
        }

        using var plazoEmision = PlazoEmision.Iniciar(autorizador.TiempoMaximo, reloj);
        var resultado = await proceso.EjecutarAsync(presupuesto, armado.Solicitud, turno, plazoEmision, pendiente);
        return Traducir(resultado);
    }

    /// <summary>El resultado de la emisión de FR-06, en los motivos de la verificación.</summary>
    private static ResultadoVerificacion Traducir(ResultadoEmision resultado)
    {
        if (resultado.EsEmitida)
        {
            return ResultadoVerificacion.Emitida(resultado.Factura);
        }

        if (resultado.Tipo == TipoResultadoEmision.NoEncontrado)
        {
            return ResultadoVerificacion.NoEncontrado;
        }

        if (resultado.Tipo != TipoResultadoEmision.Conflicto)
        {
            throw new InvalidOperationException("Resultado de la emisión no previsto.");
        }

        return resultado.Motivo switch
        {
            MotivoConflictoEmision.Rechazada =>
                ResultadoVerificacion.Rechazada(resultado.CodigoArca!.Value, resultado.DescripcionArca!),
            MotivoConflictoEmision.Facturado => ResultadoVerificacion.Conflicto(MotivoConflictoVerificacion.Facturado),

            // Las dos dejan la pendiente con el número nuevo: hay que volver a verificar (FR-13, D-31 de FEAT-007).
            MotivoConflictoEmision.SinRespuesta or MotivoConflictoEmision.PendienteVerificacion =>
                ResultadoVerificacion.Conflicto(MotivoConflictoVerificacion.SinRespuestaEmision),

            // No se envió nada: la pendiente vieja quedó, o la nueva se quitó (D-29).
            MotivoConflictoEmision.NoEnviada or MotivoConflictoEmision.Pendiente or MotivoConflictoEmision.Borrador
                or MotivoConflictoEmision.SinTope or MotivoConflictoEmision.SinPuntoVenta
                or MotivoConflictoEmision.AlicuotaNoAdmitida =>
                ResultadoVerificacion.Conflicto(MotivoConflictoVerificacion.NoEnviada),
            _ => throw new InvalidOperationException("Motivo de la emisión no previsto."),
        };
    }

    /// <summary>
    /// Graba (o reemplaza, D-12) la observación de la pendiente en una transacción: quita la vigente y confirma, agrega la
    /// nueva y confirma. Si la pendiente ya no es la evaluada, no graba nada.
    /// </summary>
    private async Task<ResultadoVerificacion> ObservarAsync(
        long numero, EmisionPendiente pendiente, MotivoObservacionEmision motivo, decimal? totalArca)
    {
        var observacion = new ObservacionEmision(
            numero, motivo, pendiente.Numero, pendiente.Total, totalArca, pendiente.Entorno, reloj.GetUtcNow());
        var rechazo = await unidadDeTrabajo.EjecutarEnTransaccionAsync<ResultadoVerificacion?>(
            async operacion =>
            {
                var actual = await pendientes.ObtenerPendienteAsync(numero, operacion);
                if (actual is null || !EsLaMisma(actual, pendiente))
                {
                    return CambioLaPendiente(numero, actual);
                }

                observaciones.QuitarObservacion(numero);
                await unidadDeTrabajo.ConfirmarAsync(operacion);
                observaciones.Agregar(observacion);
                await unidadDeTrabajo.ConfirmarAsync(operacion);
                return null;
            },
            CancellationToken.None);

        return rechazo ?? ResultadoVerificacion.Observada(DetalleObservacion.Desde(observacion, pendiente));
    }

    private ResultadoVerificacion CambioLaPendiente(long numero, EmisionPendiente? actual)
    {
        if (actual is null)
        {
            return ResultadoVerificacion.Conflicto(MotivoConflictoVerificacion.SinPendiente);
        }

        registro.LogWarning(
            "La verificación del presupuesto {Presupuesto} no se completó: {Motivo}", numero, MotivoPendienteCambio);
        return ResultadoVerificacion.Conflicto(MotivoConflictoVerificacion.NoEnviada);
    }

    /// <summary>La misma pendiente: mismo número registrado y mismo momento (como el modo reemplazo, D-28).</summary>
    private static bool EsLaMisma(EmisionPendiente actual, EmisionPendiente evaluada) =>
        actual.Numero == evaluada.Numero && actual.Momento == evaluada.Momento;

    /// <summary>El importe en centavos, para comparar el total de ARCA con el registrado (FR-05, FR-07).</summary>
    private static decimal Centavos(decimal importe) => Math.Round(importe * 100m, 0, MidpointRounding.AwayFromZero);

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

    /// <summary>
    /// Libera el turno. Si la Api se está apagando y el candado ya se descartó, lo tolera (WARN-1 de la auditoría de
    /// FEAT-007).
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
}
