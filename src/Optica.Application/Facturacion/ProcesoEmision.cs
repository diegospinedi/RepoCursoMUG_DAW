using Microsoft.Extensions.Logging;
using Optica.Application.Comun;
using Optica.Domain.Facturacion;
using Optica.Domain.Presupuestos;

namespace Optica.Application.Facturacion;

/// <summary>
/// El envío de una solicitud a ARCA con el número registrado antes (FR-01, ADR-025). No toma el candado de emisión:
/// recibe el turno ya tomado y el plazo (D-09), y no lo libera; lo libera quien lo tomó, después de que este proceso
/// termina (así la factura o la baja de la pendiente quedan grabadas antes de que otro pedido tenga el turno).
/// <list type="number">
/// <item>Pide el próximo número del tipo en el punto de venta, con el token del plazo. Si falla, "no enviada" sin grabar
/// nada; en el modo reemplazo, la pendiente vieja queda intacta (D-11).</item>
/// <item>Transacción (<c>BEGIN IMMEDIATE</c>, sin cancelación) que repite las comprobaciones (D-08): el presupuesto
/// existe y está en Final, no tiene factura y no tiene pendiente (conflicto <c>facturado</c> o <c>pendiente</c>, sin
/// enviar). En el modo reemplazo, la pendiente guardada tiene que ser la verificada (mismo número y momento; si no hay
/// ninguna, "no enviada"; si es otra, <c>pendiente</c>): quita su observación (D-24) y la pendiente, confirma, y sigue.
/// Graba la pendiente con el número, el concepto, los importes y si el receptor se identificó, y confirma. Si algo falla
/// en el medio, se revierte toda la transacción: la garantía de D-11 ("siempre existe una pendiente") cubre esta
/// transacción de registro del modo reemplazo. Después, si falta margen, ARCA rechaza o la solicitud no se envía, se
/// quita la pendiente nueva como en la emisión normal (D-29): no abre un duplicado, porque ARCA ya informó que el número
/// viejo no estaba autorizado y el nuevo no se envió o fue rechazado.</item>
/// <item>Justo antes de FECAESolicitar, si al plazo le queda menos que el margen (el menor entre
/// <see cref="MargenMaximo"/> y un tercio del plazo), "no enviada" sin enviar, y se quita la pendiente recién grabada
/// (D-10).</item>
/// <item>Pide la autorización con ese número y el token del plazo.</item>
/// <item>Transacción 2, sin cancelación, como en FEAT-007: autorizada, la factura y la baja de la pendiente (FR-18);
/// rechazada o no enviada, la baja de la pendiente (FR-21, FR-22); sin respuesta, la pendiente con su número queda
/// (FR-23, FR-13 de FEAT-008). Toda baja de una pendiente quita antes su observación (D-24). Si la factura autorizada no
/// se puede grabar, la pendiente queda y el resultado es <c>pendiente_verificacion</c> (D-31 de FEAT-007).</item>
/// </list>
/// Nunca reintenta. Las transacciones que graban no usan el token del plazo (D-10): una pendiente grabada sin envío es
/// segura, la consulta dirá "no autorizado".
/// </summary>
/// <remarks>
/// <para>
/// Lo usan <see cref="EmitirFactura"/> (modo normal: toma el turno con el plazo único de D-32 de FEAT-007 y llama) y la
/// verificación de FR-06 (modo reemplazo: con el turno que ya tiene tomado desde la consulta, crea un plazo nuevo de
/// <see cref="IAutorizadorComprobantes.TiempoMaximo"/>, 30 s, con <see cref="PlazoEmision.Iniciar"/>, y pasa en
/// <c>reemplazada</c> la pendiente que consultó, D-10, D-11).
/// </para>
/// <para>
/// El log (R-07 de FEAT-007) nunca lleva el DNI, la solicitud, los datos del cliente ni la descripción de un rechazo de
/// ARCA, ni adjunta excepciones (su texto o sus <c>Entries</c> pueden llevar el DNI). Solo D-31 registra tipo, punto de
/// venta, número y CAE.
/// </para>
/// </remarks>
public sealed class ProcesoEmision(
    IPresupuestoRepository presupuestos,
    IFacturaRepository facturas,
    IEmisionPendienteRepository pendientes,
    IObservacionEmisionRepository observaciones,
    IUnidadDeTrabajo unidadDeTrabajo,
    IAutorizadorComprobantes autorizador,
    TimeProvider reloj,
    ILogger<ProcesoEmision> registro)
{
    /// <summary>
    /// El tope del margen que tiene que quedar del plazo para enviar la solicitud (D-32 de FEAT-007, D-10): 10 s, el que
    /// rige con el plazo de 30 s de producción. El margen es el menor entre este tope y un tercio del plazo: con un plazo
    /// más corto (el harness de e2e usa 2 s) es ~0,67 s, así una emisión sin espera no queda siempre "no enviada".
    /// </summary>
    public static readonly TimeSpan MargenMaximo = TimeSpan.FromSeconds(10);

    private const string MotivoSinMargen =
        "El plazo de la emisión no alcanzaba para esperar la respuesta: la solicitud no se envió a ARCA.";

    private const string MotivoSinPendiente =
        "La emisión pendiente verificada ya no está: la solicitud no se envió a ARCA.";

    /// <summary>
    /// Registra el número y envía la solicitud de <paramref name="presupuesto"/> (ver la descripción del tipo).
    /// </summary>
    /// <param name="presupuesto">El presupuesto en Final, leído por quien llama.</param>
    /// <param name="solicitud">La solicitud armada con la configuración vigente; lleva el DNI y no va al log.</param>
    /// <param name="turno">
    /// El turno del candado de emisión, ya tomado por quien llama. No se libera acá: lo libera quien lo tomó, después.
    /// </param>
    /// <param name="plazo">
    /// El plazo del pedido del próximo número y de la autorización, y el que mide el margen. En la emisión es el único
    /// plazo que ya corría desde que se pidió el candado; en la verificación (FR-06) es uno nuevo de 30 s.
    /// </param>
    /// <param name="reemplazada">
    /// Null en una emisión nueva. En la verificación, la pendiente que se consultó y ARCA no tiene autorizada: se reemplaza
    /// por la del número nuevo en la misma transacción (D-11).
    /// </param>
    /// <exception cref="ArgumentNullException">El presupuesto, la solicitud, el turno o el plazo son null.</exception>
    public async Task<ResultadoEmision> EjecutarAsync(
        Presupuesto presupuesto,
        SolicitudComprobante solicitud,
        IDisposable turno,
        PlazoEmision plazo,
        EmisionPendiente? reemplazada = null)
    {
        ArgumentNullException.ThrowIfNull(presupuesto);
        ArgumentNullException.ThrowIfNull(solicitud);
        ArgumentNullException.ThrowIfNull(turno);
        ArgumentNullException.ThrowIfNull(plazo);

        var numeroPresupuesto = presupuesto.Numero;
        var proximo = await autorizador.ProximoNumeroAsync(solicitud.Tipo, solicitud.PuntoVenta, plazo.Token);
        if (proximo is ResultadoProximoNumero.NoEnviada sinNumero)
        {
            return NoEnviadaSinPendiente(numeroPresupuesto, sinNumero.Motivo);
        }

        var numero = proximo is ResultadoProximoNumero.Numero conNumero
            ? conNumero.Valor
            : throw new InvalidOperationException("Resultado del próximo número de ARCA no previsto.");

        if (await RegistrarAsync(presupuesto, solicitud, numero, reemplazada) is { } rechazo)
        {
            return rechazo;
        }

        if (plazo.Restante < Margen(plazo.TiempoMaximo))
        {
            return await NoEnviadaAsync(numeroPresupuesto, MotivoSinMargen);
        }

        var resultado = await autorizador.AutorizarAsync(solicitud, numero, plazo.Token);

        // Sin cancelación a propósito (D-32 de FEAT-007, D-10): un pedido cortado no pierde un CAE ya autorizado.
        return resultado switch
        {
            ResultadoAutorizacion.Autorizada autorizada => await GrabarFacturaAsync(presupuesto, solicitud, autorizada),
            ResultadoAutorizacion.Rechazada rechazada => await RechazadaAsync(
                numeroPresupuesto, rechazada, solicitud.Receptor.Dni, presupuesto.Cliente.Dni),
            ResultadoAutorizacion.NoEnviada noEnviada => await NoEnviadaAsync(numeroPresupuesto, noEnviada.Motivo),
            ResultadoAutorizacion.SinRespuesta => SinRespuesta(numeroPresupuesto),
            _ => throw new InvalidOperationException("Resultado de la autorización de ARCA no previsto."),
        };
    }

    /// <summary>
    /// El menor entre <see cref="MargenMaximo"/> y un tercio del plazo. Público para que la raíz de composición valide la
    /// espera de la verificación contra el mismo criterio (D-31 de FEAT-008).
    /// </summary>
    public static TimeSpan Margen(TimeSpan tiempoMaximo)
    {
        var tercio = tiempoMaximo / 3;
        return tercio < MargenMaximo ? tercio : MargenMaximo;
    }

    /// <summary>
    /// La transacción que repite las comprobaciones y graba la pendiente con número (D-08, D-11). Devuelve el rechazo, o
    /// null si la pendiente quedó grabada. En el modo reemplazo, un conflicto después de confirmar la baja de la vieja se
    /// lanza dentro de la operación para revertirla entera.
    /// </summary>
    private async Task<ResultadoEmision?> RegistrarAsync(
        Presupuesto presupuesto, SolicitudComprobante solicitud, long numero, EmisionPendiente? reemplazada)
    {
        try
        {
            return await unidadDeTrabajo.EjecutarEnTransaccionAsync(
                operacion => RegistrarEnTransaccionAsync(presupuesto.Numero, solicitud, numero, reemplazada, operacion),
                CancellationToken.None);
        }
        catch (RegistroRechazado rechazado)
        {
            return rechazado.Resultado;
        }
    }

    private async Task<ResultadoEmision?> RegistrarEnTransaccionAsync(
        long numeroPresupuesto, SolicitudComprobante solicitud, long numero, EmisionPendiente? reemplazada,
        CancellationToken operacion)
    {
        var guardado = await presupuestos.ObtenerAsync(numeroPresupuesto, operacion);
        if (guardado is null)
        {
            return ResultadoEmision.NoEncontrado;
        }

        if (guardado.Estado != EstadoPresupuesto.Final)
        {
            return ResultadoEmision.Conflicto(MotivoConflictoEmision.Borrador);
        }

        if (await facturas.ExisteParaPresupuestoAsync(numeroPresupuesto, operacion))
        {
            return ResultadoEmision.Conflicto(MotivoConflictoEmision.Facturado);
        }

        if (reemplazada is null)
        {
            if (await pendientes.ExisteAsync(numeroPresupuesto, operacion))
            {
                return ResultadoEmision.Conflicto(MotivoConflictoEmision.Pendiente);
            }
        }
        else
        {
            var actual = await pendientes.ObtenerPendienteAsync(numeroPresupuesto, operacion);
            if (actual is null)
            {
                registro.LogWarning(
                    "La emisión del presupuesto {Presupuesto} no se envió a ARCA: {Motivo}",
                    numeroPresupuesto,
                    MotivoSinPendiente);
                return ResultadoEmision.Conflicto(MotivoConflictoEmision.NoEnviada);
            }

            if (actual.Numero != reemplazada.Numero || actual.Momento != reemplazada.Momento)
            {
                return ResultadoEmision.Conflicto(MotivoConflictoEmision.Pendiente);
            }

            // D-11 y D-24: la vieja (con su observación) se quita y se confirma antes de agregar la nueva; si después algo
            // falla, la transacción entera se revierte y la vieja queda.
            observaciones.QuitarObservacion(numeroPresupuesto);
            pendientes.Quitar(numeroPresupuesto);
            if (await ConfirmarAsync(numeroPresupuesto, operacion) is { } conflictoAlQuitar)
            {
                throw new RegistroRechazado(conflictoAlQuitar);
            }
        }

        pendientes.Agregar(new EmisionPendiente(
            numeroPresupuesto, reloj.GetUtcNow(), solicitud, numero, autorizador.Entorno));
        if (await ConfirmarAsync(numeroPresupuesto, operacion) is { } conflicto)
        {
            // En una emisión nueva no se confirmó nada antes: el conflicto se devuelve. En el reemplazo, la baja de la
            // vieja ya se confirmó en esta transacción: se lanza para revertirla (D-11).
            return reemplazada is null ? conflicto : throw new RegistroRechazado(conflicto);
        }

        return null;
    }

    /// <summary>
    /// Confirma; un conflicto de unicidad (otro pedido grabó la pendiente o la factura primero; <c>BEGIN IMMEDIATE</c> lo
    /// impide y queda como defensa, R-09 de FEAT-007) se devuelve como <c>facturado</c> o <c>pendiente</c>.
    /// </summary>
    private async Task<ResultadoEmision?> ConfirmarAsync(long numeroPresupuesto, CancellationToken operacion)
    {
        try
        {
            await unidadDeTrabajo.ConfirmarAsync(operacion);
            return null;
        }
        catch (ConflictoAlConfirmarException conflicto) when (conflicto.Motivo == MotivoConflicto.Unicidad)
        {
            var facturado = await facturas.ExisteParaPresupuestoAsync(numeroPresupuesto, operacion);
            return ResultadoEmision.Conflicto(
                facturado ? MotivoConflictoEmision.Facturado : MotivoConflictoEmision.Pendiente);
        }
    }

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
                    observaciones.QuitarObservacion(presupuesto.Numero);
                    pendientes.Quitar(presupuesto.Numero);
                    await unidadDeTrabajo.ConfirmarAsync(operacion);
                    return true;
                },
                CancellationToken.None);
        }
        catch (Exception error)
        {
            // D-31 de FEAT-007: ARCA autorizó y la factura no quedó grabada. La pendiente sigue (la revirtió la
            // transacción) para la verificación. Solo el tipo de la falla: su texto o sus Entries pueden llevar el DNI.
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

    /// <summary>
    /// ARCA rechazó: quita la pendiente y devuelve el código y la descripción, con el DNI del receptor y el del cliente del
    /// presupuesto enmascarados (D-34): la descripción de ARCA puede repetir el número de documento enviado.
    /// </summary>
    private async Task<ResultadoEmision> RechazadaAsync(
        long numero, ResultadoAutorizacion.Rechazada rechazada, params string?[] dnis)
    {
        await IntentarQuitarPendienteAsync(numero);
        // Solo el código: la descripción de ARCA puede repetir datos de la solicitud (R-07).
        registro.LogInformation(
            "ARCA rechazó la factura del presupuesto {Presupuesto} con el código {Codigo}.", numero, rechazada.Codigo);
        return ResultadoEmision.Conflicto(
            MotivoConflictoEmision.Rechazada, rechazada.Codigo, OcultarDni(rechazada.Descripcion, dnis));
    }

    /// <summary>
    /// D-34: reemplaza por asteriscos (uno por carácter) cada aparición de los <paramref name="dnis"/> en
    /// <paramref name="texto"/>, con o sin ceros a la izquierda y con o sin puntos entre los dígitos (<c>00718294</c>,
    /// <c>718294</c>, <c>718.294</c>, <c>00.718.294</c>). Solo números completos: un número más largo que contiene los
    /// dígitos del DNI no se toca. Un DNI nulo, vacío o sin dígitos significativos se ignora.
    /// </summary>
    public static string OcultarDni(string texto, params string?[] dnis)
    {
        ArgumentNullException.ThrowIfNull(texto);
        foreach (var dni in dnis)
        {
            var digitos = new string((dni ?? string.Empty).Where(char.IsAsciiDigit).ToArray()).TrimStart('0');
            if (digitos.Length == 0)
            {
                continue;
            }

            // Ceros a la izquierda (cada uno con un punto opcional) y los dígitos con un punto opcional entre ellos.
            var patron = @"(?<![0-9])(?:0\.?)*" + string.Join(@"\.?", digitos.Select(c => c.ToString())) +
                @"(?![0-9])";
            texto = System.Text.RegularExpressions.Regex.Replace(
                texto,
                patron,
                coincidencia => new string('*', coincidencia.Length),
                System.Text.RegularExpressions.RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1));
        }

        return texto;
    }

    private async Task<ResultadoEmision> NoEnviadaAsync(long numero, string motivo)
    {
        await IntentarQuitarPendienteAsync(numero);
        return NoEnviadaSinPendiente(numero, motivo);
    }

    private ResultadoEmision NoEnviadaSinPendiente(long numero, string motivo)
    {
        // El motivo de "no enviada" es un texto fijo, sin datos del cliente ni secretos.
        registro.LogWarning("La factura del presupuesto {Presupuesto} no se envió a ARCA: {Motivo}", numero, motivo);
        return ResultadoEmision.Conflicto(MotivoConflictoEmision.NoEnviada);
    }

    private ResultadoEmision SinRespuesta(long numero)
    {
        // FR-23 de FEAT-007 y FR-13: no se sabe si ARCA autorizó; la pendiente con su número queda y no se reintenta.
        registro.LogWarning(
            "ARCA no respondió a la emisión del presupuesto {Presupuesto}: queda pendiente de verificación.", numero);
        return ResultadoEmision.Conflicto(MotivoConflictoEmision.SinRespuesta);
    }

    /// <summary>
    /// La baja de la pendiente (y antes de su observación, D-24) de una rechazada, no enviada o sin margen. Si la baja
    /// falla, la pendiente queda grabada (la resuelve la verificación) y quien llama recibe igual el motivo real (WARN-2 de
    /// FEAT-007). Al log va solo el tipo de la falla.
    /// </summary>
    private async Task IntentarQuitarPendienteAsync(long numero)
    {
        try
        {
            await unidadDeTrabajo.EjecutarEnTransaccionAsync(
                async operacion =>
                {
                    observaciones.QuitarObservacion(numero);
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

    /// <summary>Un conflicto al grabar la pendiente: revierte la transacción entera y lleva el resultado.</summary>
    private sealed class RegistroRechazado(ResultadoEmision resultado) : Exception
    {
        public ResultadoEmision Resultado { get; } = resultado;
    }
}
