using Microsoft.Extensions.Logging;
using Optica.Application.Comun;
using Optica.Application.Facturacion;
using Optica.Domain.Facturacion;

namespace Optica.Infrastructure.Arca;

/// <summary>
/// El autorizador de homologación y producción (FR-17, ADR-022, ADR-023, ADR-025), con tres operaciones sobre el ticket
/// de WSAA: el próximo número (<c>FECompUltimoAutorizado</c> + 1), la autorización con el número recibido
/// (<c>FECAESolicitar</c>) y la consulta por número (<c>FECompConsultar</c>). Todo lo que falla antes de enviar
/// (certificado, WSAA, último número, la cancelación recibida o una falla no prevista) es "no enviada" o "no consultada";
/// después del envío de la autorización o de la consulta, una respuesta ilegible, una falla de la red, la cancelación o
/// cualquier falla no prevista es "sin respuesta" (D-16). No aplica un plazo propio: el de quien llama llega en la
/// cancelación (D-32), y un token que no se puede cancelar termina sin tocar la red. No reintenta (FR-23) y no lanza por
/// fallas de ARCA. Singleton: el ticket de WSAA vive en memoria mientras dure la Api.
/// </summary>
public sealed class AutorizadorWsfe : IAutorizadorComprobantes, IAsyncDisposable, IDisposable
{
    /// <summary>El PFX no existe, no se lee o su clave es incorrecta.</summary>
    public const string MotivoCertificado = "No se pudo leer el certificado de ARCA.";

    /// <summary>WSAA rechazó el pedido de acceso, no respondió o respondió algo ilegible.</summary>
    public const string MotivoAcceso = "ARCA no entregó el acceso al servicio de facturación.";

    /// <summary>WSFEv1 no informó el último número autorizado.</summary>
    public const string MotivoUltimoNumero = "No se pudo obtener el último número autorizado de ARCA.";

    /// <summary>La cancelación (el plazo de la operación) llegó antes de enviar el pedido a WSFEv1.</summary>
    public const string MotivoCancelada = "La emisión se canceló antes de enviar la solicitud.";

    /// <summary>
    /// La emisión llegó con un token que no se puede cancelar: sin plazo, una espera de ARCA podría no terminar nunca
    /// (D-32). No se toca la red.
    /// </summary>
    public const string MotivoSinPlazo = "La emisión no tiene un tiempo máximo de espera de ARCA.";

    /// <summary>Una falla no prevista antes de enviar el pedido a WSFEv1 (solo su tipo va al log).</summary>
    public const string MotivoInesperado = "No se pudo preparar la solicitud a ARCA.";

    private readonly ClienteWsaa _wsaa;
    private readonly ClienteWsfe _wsfe;
    private readonly ILogger<AutorizadorWsfe> _registro;

    internal AutorizadorWsfe(
        EntornoArca entorno, TimeSpan tiempoMaximo, ClienteWsaa wsaa, ClienteWsfe wsfe, ILogger<AutorizadorWsfe> registro)
    {
        ArgumentNullException.ThrowIfNull(wsaa);
        ArgumentNullException.ThrowIfNull(wsfe);
        ArgumentNullException.ThrowIfNull(registro);
        if (entorno is not (EntornoArca.Homologacion or EntornoArca.Produccion))
        {
            throw new ArgumentException("El entorno no es homologación ni producción.", nameof(entorno));
        }

        Entorno = entorno;
        TiempoMaximo = tiempoMaximo;
        _wsaa = wsaa;
        _wsfe = wsfe;
        _registro = registro;
    }

    public EntornoArca Entorno { get; }

    public TimeSpan TiempoMaximo { get; }

    /// <summary>
    /// Al apagar la Api (el contenedor descarta el singleton): espera el pedido de WSAA en curso hasta 5 s, para no
    /// perder un ticket que está por llegar, y si no terminó lo cancela (queda como información, no como error).
    /// </summary>
    public ValueTask DisposeAsync() => new(_wsaa.DetenerAsync(ClienteWsaa.EsperaAlApagar));

    /// <summary>Lo mismo que <see cref="DisposeAsync"/>, para un contenedor que se descarta sin async.</summary>
    public void Dispose() => _wsaa.DetenerAsync(ClienteWsaa.EsperaAlApagar).GetAwaiter().GetResult();

    /// <summary>
    /// Arma el autorizador con las opciones leídas. Lo usa la raíz de composición con el <see cref="HttpClient"/> propio
    /// de ARCA; las pruebas, con uno que no sale a la red.
    /// </summary>
    /// <param name="directorio">El directorio de datos, donde vive el ticket de WSAA cifrado.</param>
    /// <exception cref="ArgumentException">
    /// El entorno es el simulado o faltan el CUIT, el certificado o su clave (<see cref="OpcionesArca.CredencialesCompletas"/>).
    /// </exception>
    public static AutorizadorWsfe Crear(
        OpcionesArca opciones, string directorio, HttpClient http, IProtectorDatos protector, TimeProvider reloj,
        ILoggerFactory registro)
    {
        ArgumentNullException.ThrowIfNull(opciones);
        ArgumentNullException.ThrowIfNull(registro);
        if (!opciones.CredencialesCompletas)
        {
            throw new ArgumentException("Faltan las credenciales de ARCA.", nameof(opciones));
        }

        return new AutorizadorWsfe(
            opciones.Entorno,
            opciones.TiempoMaximo,
            new ClienteWsaa(
                opciones.Entorno, http, opciones.Cuit!, opciones.RutaCertificado!, opciones.ClaveCertificado!, directorio, protector, reloj,
                registro.CreateLogger<ClienteWsaa>()),
            new ClienteWsfe(opciones.Entorno, http, opciones.Cuit!, registro.CreateLogger<ClienteWsfe>()),
            registro.CreateLogger<AutorizadorWsfe>());
    }

    /// <summary>
    /// Ticket de WSAA → <c>FECompUltimoAutorizado</c> + 1. Es de solo lectura en ARCA: cualquier falla, la cancelación o
    /// un token sin plazo es "no enviada".
    /// </summary>
    public async Task<ResultadoProximoNumero> ProximoNumeroAsync(
        TipoComprobante tipo, int puntoVenta, CancellationToken cancelacion)
    {
        _ = new NumeroComprobante(tipo, puntoVenta, NumeroComprobante.NumeroMinimo); // valida tipo y punto de venta
        if (!cancelacion.CanBeCanceled)
        {
            RegistrarSinPlazo();
            return new ResultadoProximoNumero.NoEnviada(MotivoSinPlazo);
        }

        var preparado = await AntesDeEnviarAsync(
            async () =>
            {
                var ticket = await _wsaa.ObtenerTicketAsync(cancelacion);
                return await _wsfe.UltimoAutorizadoAsync(ticket, tipo, puntoVenta, cancelacion);
            },
            "el pedido del próximo número",
            tipo,
            puntoVenta,
            cancelacion);
        if (preparado.Motivo is { } motivo)
        {
            return new ResultadoProximoNumero.NoEnviada(motivo);
        }

        var numero = preparado.Valor + 1;
        if (numero > NumeroComprobante.NumeroMaximo)
        {
            _registro.LogError(
                "ARCA ({Entorno}): {Tipo}, punto de venta {PuntoVenta}, llegó al número máximo de comprobante. La emisión " +
                "no se envió.",
                Entorno,
                tipo,
                puntoVenta);
            return new ResultadoProximoNumero.NoEnviada(MotivoUltimoNumero);
        }

        return new ResultadoProximoNumero.Numero(numero);
    }

    /// <summary>
    /// Ticket de WSAA → <c>FECAESolicitar</c> con <paramref name="numero"/>, con la clasificación de D-16: hasta el envío,
    /// "no enviada"; desde el envío, "sin respuesta" ante cualquier falla.
    /// </summary>
    public async Task<ResultadoAutorizacion> AutorizarAsync(
        SolicitudComprobante solicitud, long numero, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        _ = new NumeroComprobante(solicitud.Tipo, solicitud.PuntoVenta, numero);
        if (!cancelacion.CanBeCanceled)
        {
            RegistrarSinPlazo();
            return new ResultadoAutorizacion.NoEnviada(MotivoSinPlazo);
        }

        var preparado = await AntesDeEnviarAsync(
            () => _wsaa.ObtenerTicketAsync(cancelacion),
            "el pedido de autorización",
            solicitud.Tipo,
            solicitud.PuntoVenta,
            cancelacion);
        if (preparado.Motivo is { } motivo)
        {
            return new ResultadoAutorizacion.NoEnviada(motivo);
        }

        // El límite de D-16: hasta acá no se envió nada; desde el envío, la cancelación es "sin respuesta".
        if (cancelacion.IsCancellationRequested)
        {
            RegistrarCancelada("el pedido de autorización", solicitud.Tipo, solicitud.PuntoVenta);
            return new ResultadoAutorizacion.NoEnviada(MotivoCancelada);
        }

        return await _wsfe.SolicitarCaeAsync(preparado.Valor!, solicitud, numero, cancelacion);
    }

    /// <summary>
    /// Ticket de WSAA → <c>FECompConsultar</c>. Hasta el envío, cualquier falla es "no consultada"; desde el envío, lo
    /// clasifica <see cref="ClienteWsfe.ConsultarComprobanteAsync"/> (autorizado, no autorizado o sin respuesta).
    /// </summary>
    public async Task<ResultadoConsulta> ConsultarAsync(
        TipoComprobante tipo, int puntoVenta, long numero, CancellationToken cancelacion)
    {
        _ = new NumeroComprobante(tipo, puntoVenta, numero);
        if (!cancelacion.CanBeCanceled)
        {
            RegistrarSinPlazo();
            return new ResultadoConsulta.NoConsultada(MotivoSinPlazo);
        }

        var preparado = await AntesDeEnviarAsync(
            () => _wsaa.ObtenerTicketAsync(cancelacion), "la consulta", tipo, puntoVenta, cancelacion);
        if (preparado.Motivo is { } motivo)
        {
            return new ResultadoConsulta.NoConsultada(motivo);
        }

        if (cancelacion.IsCancellationRequested)
        {
            RegistrarCancelada("la consulta", tipo, puntoVenta);
            return new ResultadoConsulta.NoConsultada(MotivoCancelada);
        }

        return await _wsfe.ConsultarComprobanteAsync(preparado.Valor!, tipo, puntoVenta, numero, cancelacion);
    }

    /// <summary>
    /// Corre lo que va antes de enviar a WSFEv1 (ticket y, si corresponde, el último número) y convierte cada falla en el
    /// motivo fijo de "no enviada" o "no consultada": la cancelación, el certificado, WSAA o el último número, y una falla
    /// no prevista (solo su tipo va al log: su texto puede traer datos, R-02, R-07).
    /// </summary>
    private async Task<(T? Valor, string? Motivo)> AntesDeEnviarAsync<T>(
        Func<Task<T>> paso, string operacion, TipoComprobante tipo, int puntoVenta, CancellationToken cancelacion)
    {
        try
        {
            cancelacion.ThrowIfCancellationRequested();
            return (await paso(), null);
        }
        catch (OperationCanceledException) when (cancelacion.IsCancellationRequested)
        {
            RegistrarCancelada(operacion, tipo, puntoVenta);
            return (default, MotivoCancelada);
        }
        catch (CertificadoArcaException)
        {
            return (default, MotivoCertificado);
        }
        catch (FallaArcaException error)
        {
            // El texto es uno de los motivos fijos de esta clase; la causa ya quedó en el log.
            return (default, error.Message);
        }
        catch (Exception error)
        {
            _registro.LogError(
                "ARCA ({Entorno}): falla no prevista antes de enviar {Operacion} ({TipoFalla}). No se envió nada a ARCA.",
                Entorno,
                operacion,
                error.GetType().Name);
            return (default, MotivoInesperado);
        }
    }

    private void RegistrarSinPlazo() =>
        _registro.LogError(
            "ARCA ({Entorno}): la operación llegó sin tiempo máximo de espera; no se envió nada a ARCA.", Entorno);

    private void RegistrarCancelada(string operacion, TipoComprobante tipo, int puntoVenta) =>
        _registro.LogWarning(
            "ARCA ({Entorno}): el plazo venció antes de enviar {Operacion} ({Tipo}, punto de venta {PuntoVenta}). No se " +
            "envió.",
            Entorno,
            operacion,
            tipo,
            puntoVenta);
}
