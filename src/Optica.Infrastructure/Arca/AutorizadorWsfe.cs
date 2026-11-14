using Microsoft.Extensions.Logging;
using Optica.Application.Comun;
using Optica.Application.Facturacion;
using Optica.Domain.Facturacion;

namespace Optica.Infrastructure.Arca;

/// <summary>
/// El autorizador de homologación y producción (FR-17, ADR-022, ADR-023): ticket de WSAA → último número autorizado →
/// <c>FECAESolicitar</c> con ese número + 1, y la clasificación de D-16. Todo lo que falla antes de enviar
/// <c>FECAESolicitar</c> (certificado, WSAA, último número, la cancelación recibida o una falla no prevista) es "no
/// enviada"; después, una respuesta ilegible, una falla de la red, la cancelación o cualquier falla no prevista es "sin
/// respuesta". No aplica un plazo propio: el de <c>EmitirFactura</c> llega en la cancelación (D-32), y un token que no se
/// puede cancelar termina "no enviada" sin tocar la red. No reintenta (FR-23) y no lanza. Singleton: el ticket de WSAA
/// vive en memoria mientras dure la Api.
/// </summary>
public sealed class AutorizadorWsfe : IAutorizadorComprobantes, IAsyncDisposable, IDisposable
{
    /// <summary>El PFX no existe, no se lee o su clave es incorrecta.</summary>
    public const string MotivoCertificado = "No se pudo leer el certificado de ARCA.";

    /// <summary>WSAA rechazó el pedido de acceso, no respondió o respondió algo ilegible.</summary>
    public const string MotivoAcceso = "ARCA no entregó el acceso al servicio de facturación.";

    /// <summary>WSFEv1 no informó el último número autorizado.</summary>
    public const string MotivoUltimoNumero = "No se pudo obtener el último número autorizado de ARCA.";

    /// <summary>La cancelación (el plazo de la emisión) llegó antes de enviar el pedido de autorización.</summary>
    public const string MotivoCancelada = "La emisión se canceló antes de enviar la solicitud.";

    /// <summary>
    /// La emisión llegó con un token que no se puede cancelar: sin plazo, una espera de ARCA podría no terminar nunca
    /// (D-32). No se toca la red.
    /// </summary>
    public const string MotivoSinPlazo = "La emisión no tiene un tiempo máximo de espera de ARCA.";

    /// <summary>Una falla no prevista antes de enviar el pedido de autorización (solo su tipo va al log).</summary>
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

    public async Task<ResultadoAutorizacion> AutorizarAsync(SolicitudComprobante solicitud, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(solicitud);

        if (!cancelacion.CanBeCanceled)
        {
            _registro.LogError(
                "ARCA ({Entorno}): la emisión llegó sin tiempo máximo de espera; no se envió nada a ARCA.", Entorno);
            return new ResultadoAutorizacion.NoEnviada(MotivoSinPlazo);
        }

        long numero;
        TicketAcceso ticket;
        try
        {
            cancelacion.ThrowIfCancellationRequested();
            ticket = await _wsaa.ObtenerTicketAsync(cancelacion);
            var ultimo = await _wsfe.UltimoAutorizadoAsync(ticket, solicitud.Tipo, solicitud.PuntoVenta, cancelacion);
            numero = ultimo + 1;
        }
        catch (OperationCanceledException) when (cancelacion.IsCancellationRequested)
        {
            _registro.LogWarning(
                "ARCA ({Entorno}): el plazo de la emisión venció antes de enviar el pedido de autorización ({Tipo}, " +
                "punto de venta {PuntoVenta}). La emisión no se envió.",
                Entorno,
                solicitud.Tipo,
                solicitud.PuntoVenta);
            return new ResultadoAutorizacion.NoEnviada(MotivoCancelada);
        }
        catch (CertificadoArcaException)
        {
            return new ResultadoAutorizacion.NoEnviada(MotivoCertificado);
        }
        catch (FallaArcaException error)
        {
            // El texto es uno de los motivos fijos de esta clase; la causa ya quedó en el log.
            return new ResultadoAutorizacion.NoEnviada(error.Message);
        }
        catch (Exception error)
        {
            // No prevista, y todavía no se envió el pedido de autorización: no enviada. Solo el tipo: el texto de la
            // excepción puede traer datos (R-02, R-07).
            _registro.LogError(
                "ARCA ({Entorno}): falla no prevista antes de enviar el pedido de autorización ({TipoFalla}). La emisión " +
                "no se envió.",
                Entorno,
                error.GetType().Name);
            return new ResultadoAutorizacion.NoEnviada(MotivoInesperado);
        }

        if (numero > NumeroComprobante.NumeroMaximo)
        {
            _registro.LogError(
                "ARCA ({Entorno}): {Tipo}, punto de venta {PuntoVenta}, llegó al número máximo de comprobante. La emisión " +
                "no se envió.",
                Entorno,
                solicitud.Tipo,
                solicitud.PuntoVenta);
            return new ResultadoAutorizacion.NoEnviada(MotivoUltimoNumero);
        }

        // El límite de D-16: hasta acá no se envió nada; desde el envío, la cancelación es "sin respuesta".
        if (cancelacion.IsCancellationRequested)
        {
            _registro.LogWarning(
                "ARCA ({Entorno}): el plazo de la emisión venció antes de enviar el pedido de autorización ({Tipo}, " +
                "punto de venta {PuntoVenta}). La emisión no se envió.",
                Entorno,
                solicitud.Tipo,
                solicitud.PuntoVenta);
            return new ResultadoAutorizacion.NoEnviada(MotivoCancelada);
        }

        return await _wsfe.SolicitarCaeAsync(ticket, solicitud, numero, cancelacion);
    }
}
