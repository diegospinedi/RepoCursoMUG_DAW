using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Optica.Application.Facturacion;
using Optica.Domain.Facturacion;

namespace Optica.Infrastructure.Arca;

/// <summary>
/// Las dos operaciones de WSFEv1 que usa la factura a consumidor final (D-12, D-20): <c>FECompUltimoAutorizado</c> y
/// <c>FECAESolicitar</c>, en SOAP 1.1 armado con <see cref="System.Xml.Linq"/> (escapa todos los valores). Los códigos
/// salen de <see cref="CodigosWsfe"/>; los importes van con punto decimal y 2 decimales (NFR-04). El cuerpo lleva el
/// token, la firma, el CUIT y, sobre el tope, el DNI del receptor: nunca se registra (R-02, R-07); el log lleva tipo,
/// punto de venta, número, resultado y códigos de error.
/// </summary>
internal sealed class ClienteWsfe
{
    /// <summary>El largo máximo de la descripción de un rechazo que se devuelve a la operadora.</summary>
    public const int LargoMaximoDescripcion = 500;

    /// <summary>La descripción cuando ARCA rechaza sin informar el motivo.</summary>
    public const string DescripcionSinMotivo = "ARCA rechazó el comprobante sin informar el motivo.";

    private static readonly XNamespace Fev1 = "http://ar.gov.afip.dif.FEV1/";

    private readonly EntornoArca _entorno;
    private readonly HttpClient _http;
    private readonly string _cuit;
    private readonly ILogger<ClienteWsfe> _registro;

    /// <param name="cuit">11 dígitos con dígito verificador válido (ya validado por <see cref="OpcionesArca"/>).</param>
    public ClienteWsfe(EntornoArca entorno, HttpClient http, string cuit, ILogger<ClienteWsfe> registro)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(cuit);
        ArgumentNullException.ThrowIfNull(registro);
        if (!OpcionesArca.EsCuitValido(cuit))
        {
            throw new ArgumentException("El CUIT no es válido.", nameof(cuit));
        }

        _ = ServiciosArca.UrlWsfe(entorno); // solo homologación o producción
        _entorno = entorno;
        _http = http;
        _cuit = cuit;
        _registro = registro;
    }

    /// <summary>El último número autorizado del tipo en el punto de venta (0 si no hay ninguno).</summary>
    /// <exception cref="FallaArcaException">ARCA informó errores, no respondió algo legible o falló la red (ya registrado).</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancelacion"/> se canceló.</exception>
    public async Task<long> UltimoAutorizadoAsync(
        TicketAcceso ticket, TipoComprobante tipo, int puntoVenta, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        var operacion = new XElement(
            Fev1 + "FECompUltimoAutorizado",
            Autenticacion(ticket),
            new XElement(Fev1 + "PtoVta", Entero(puntoVenta)),
            new XElement(Fev1 + "CbteTipo", Entero(CodigosWsfe.Tipo(tipo))));

        RespuestaSoap respuesta;
        try
        {
            respuesta = await EnviarAsync("FECompUltimoAutorizado", operacion, cancelacion);
        }
        catch (OperationCanceledException) when (cancelacion.IsCancellationRequested)
        {
            _registro.LogWarning(
                "ARCA ({Entorno}): WSFEv1 no informó el último número dentro del plazo de la emisión. La emisión no se " +
                "envió.",
                _entorno);
            throw;
        }
        catch (Exception error) when (error is HttpRequestException or RespuestaArcaIlegibleException or IOException
            or OperationCanceledException)
        {
            _registro.LogError(
                "ARCA ({Entorno}): WSFEv1 no respondió el último número o respondió algo ilegible ({TipoFalla}). La " +
                "emisión no se envió.",
                _entorno,
                error.GetType().Name);
            throw new FallaArcaException(AutorizadorWsfe.MotivoUltimoNumero);
        }

        var resultado = respuesta.Documento.Descendants(Fev1 + "FECompUltimoAutorizadoResult").FirstOrDefault();
        var errores = resultado?.Element(Fev1 + "Errors")?.Elements(Fev1 + "Err").ToList() ?? [];
        if (errores.Count > 0)
        {
            _registro.LogError(
                "ARCA ({Entorno}): WSFEv1 informó errores al pedir el último número de {Tipo}, punto de venta " +
                "{PuntoVenta} (códigos {Codigos}). La emisión no se envió.",
                _entorno,
                tipo,
                puntoVenta,
                string.Join(", ", errores.Select(Codigo)));
            throw new FallaArcaException(AutorizadorWsfe.MotivoUltimoNumero);
        }

        if (!respuesta.Exito
            || !long.TryParse(
                resultado?.Element(Fev1 + "CbteNro")?.Value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture,
                out var ultimo)
            || ultimo > NumeroComprobante.NumeroMaximo)
        {
            _registro.LogError(
                "ARCA ({Entorno}): la respuesta de WSFEv1 no trae un último número legible para {Tipo}, punto de venta " +
                "{PuntoVenta}. La emisión no se envió.",
                _entorno,
                tipo,
                puntoVenta);
            throw new FallaArcaException(AutorizadorWsfe.MotivoUltimoNumero);
        }

        return ultimo;
    }

    /// <summary>
    /// Envía <c>FECAESolicitar</c> con el número <paramref name="numero"/> y clasifica la respuesta: autorizada (CAE de 14
    /// dígitos y vencimiento <c>yyyyMMdd</c>), rechazada (resultado <c>R</c> o <c>Errors</c>, con el primer código y su
    /// descripción) o sin respuesta (cualquier otra cosa, una falla de la red, la cancelación o cualquier falla no
    /// prevista: ya se envió y no se sabe si ARCA autorizó, D-16). Si el pedido no se pudo armar, no enviada. No lanza.
    /// </summary>
    public async Task<ResultadoAutorizacion> SolicitarCaeAsync(
        TicketAcceso ticket, SolicitudComprobante solicitud, long numero, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        ArgumentNullException.ThrowIfNull(solicitud);
        string comprobante;
        XElement operacion;
        try
        {
            comprobante = new NumeroComprobante(solicitud.Tipo, solicitud.PuntoVenta, numero).ToString();
            operacion = new XElement(
                Fev1 + "FECAESolicitar",
                Autenticacion(ticket),
                new XElement(
                    Fev1 + "FeCAEReq",
                    new XElement(
                        Fev1 + "FeCabReq",
                        new XElement(Fev1 + "CantReg", "1"),
                        new XElement(Fev1 + "PtoVta", Entero(solicitud.PuntoVenta)),
                        new XElement(Fev1 + "CbteTipo", Entero(CodigosWsfe.Tipo(solicitud.Tipo)))),
                    new XElement(Fev1 + "FeDetReq", Detalle(solicitud, numero))));
        }
        catch (Exception error)
        {
            // Todavía no se envió nada.
            _registro.LogError(
                "ARCA ({Entorno}): no se pudo armar el pedido de autorización ({TipoFalla}). La emisión no se envió.",
                _entorno,
                error.GetType().Name);
            return new ResultadoAutorizacion.NoEnviada(AutorizadorWsfe.MotivoInesperado);
        }

        try
        {
            var respuesta = await EnviarAsync("FECAESolicitar", operacion, cancelacion);
            return Clasificar(respuesta, numero, comprobante);
        }
        catch (Exception error)
        {
            // Desde el envío, cualquier falla (prevista o no) es "sin respuesta": puede haber un CAE. Solo el tipo va al
            // log: el texto de la excepción puede traer datos (R-02, R-07).
            _registro.LogError(
                "ARCA ({Entorno}): sin respuesta legible al pedido de autorización de {Comprobante} ({TipoFalla}). La " +
                "emisión queda pendiente de verificación.",
                _entorno,
                comprobante,
                error is OperationCanceledException ? "plazo vencido" : error.GetType().Name);
            return new ResultadoAutorizacion.SinRespuesta();
        }
    }

    private ResultadoAutorizacion Clasificar(RespuestaSoap respuesta, long numero, string comprobante)
    {
        var resultado = respuesta.Documento.Descendants(Fev1 + "FECAESolicitarResult").FirstOrDefault();
        var detalle = resultado?.Element(Fev1 + "FeDetResp")?.Element(Fev1 + "FECAEDetResponse");
        var estado = (detalle?.Element(Fev1 + "Resultado") ?? resultado?.Element(Fev1 + "FeCabResp")?.Element(Fev1 + "Resultado"))
            ?.Value.Trim();
        var errores = resultado?.Element(Fev1 + "Errors")?.Elements(Fev1 + "Err").ToList() ?? [];

        // "A" con Errors pudo haber dejado un CAE: nunca es rechazada, queda pendiente de verificación.
        if (estado == "A" && errores.Count > 0)
        {
            _registro.LogError(
                "ARCA ({Entorno}): la autorización de {Comprobante} vino con resultado A y errores (códigos {Codigos}). La " +
                "emisión queda pendiente de verificación.",
                _entorno,
                comprobante,
                string.Join(", ", errores.Select(Codigo)));
            return new ResultadoAutorizacion.SinRespuesta();
        }

        if (respuesta.Exito && resultado is not null && estado == "A")
        {
            var cae = detalle?.Element(Fev1 + "CAE")?.Value.Trim() ?? string.Empty;
            var textoVencimiento = detalle?.Element(Fev1 + "CAEFchVto")?.Value.Trim();
            var desde = detalle?.Element(Fev1 + "CbteDesde")?.Value.Trim();
            if (cae.Length == ResultadoAutorizacion.Autorizada.LargoCae && cae.All(char.IsAsciiDigit)
                && DateOnly.TryParseExact(
                    textoVencimiento, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var vencimiento)
                && (desde is null || desde == Entero(numero)))
            {
                _registro.LogInformation(
                    "ARCA ({Entorno}) autorizó {Comprobante}.", _entorno, comprobante);
                return new ResultadoAutorizacion.Autorizada(numero, cae, vencimiento);
            }

            _registro.LogError(
                "ARCA ({Entorno}): la autorización de {Comprobante} no trae un CAE, un vencimiento o un número legibles. " +
                "La emisión queda pendiente de verificación.",
                _entorno,
                comprobante);
            return new ResultadoAutorizacion.SinRespuesta();
        }

        // Rechazada solo con resultado R, o con Errors sin resultado A (por ejemplo, una falla de autenticación).
        if (resultado is not null && estado != "A" && (estado == "R" || errores.Count > 0))
        {
            var primero = errores.FirstOrDefault()
                ?? detalle?.Element(Fev1 + "Observaciones")?.Elements(Fev1 + "Obs").FirstOrDefault();
            var codigo = int.TryParse(
                primero?.Element(Fev1 + "Code")?.Value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture,
                out var numeroCodigo)
                ? numeroCodigo
                : 0;
            var descripcion = Sanear(primero?.Element(Fev1 + "Msg")?.Value ?? string.Empty);
            if (descripcion.Length == 0)
            {
                descripcion = DescripcionSinMotivo;
            }

            // Solo el código: la descripción de ARCA puede repetir el documento del receptor (R-07).
            _registro.LogInformation(
                "ARCA ({Entorno}) rechazó {Comprobante} con el código {Codigo}.", _entorno, comprobante, codigo);
            return new ResultadoAutorizacion.Rechazada(codigo, Recortar(descripcion));
        }

        _registro.LogError(
            "ARCA ({Entorno}): la respuesta al pedido de autorización de {Comprobante} no trae un resultado legible. La " +
            "emisión queda pendiente de verificación.",
            _entorno,
            comprobante);
        return new ResultadoAutorizacion.SinRespuesta();
    }

    /// <summary>El detalle del comprobante, en el orden del WSDL de WSFEv1.</summary>
    private static XElement Detalle(SolicitudComprobante solicitud, long numero)
    {
        var importes = solicitud.Importes;
        var esB = solicitud.Tipo == TipoComprobante.FacturaB;
        var (docTipo, docNro) = solicitud.Receptor.Dni is { } dni
            ? (CodigosWsfe.DocTipoDni, long.Parse(dni, NumberStyles.None, CultureInfo.InvariantCulture))
            : (CodigosWsfe.DocTipoSinIdentificar, CodigosWsfe.DocNroSinIdentificar);

        var detalle = new XElement(
            Fev1 + "FECAEDetRequest",
            new XElement(Fev1 + "Concepto", Entero(CodigosWsfe.Concepto(solicitud.Concepto))),
            new XElement(Fev1 + "DocTipo", Entero(docTipo)),
            new XElement(Fev1 + "DocNro", Entero(docNro)),
            new XElement(Fev1 + "CbteDesde", Entero(numero)),
            new XElement(Fev1 + "CbteHasta", Entero(numero)),
            new XElement(Fev1 + "CbteFch", Fecha(solicitud.Fecha)),
            new XElement(Fev1 + "ImpTotal", Importe(importes.Total)),
            new XElement(Fev1 + "ImpTotConc", Importe(0m)),
            // La C manda el total como neto, sin IVA (D-20, FR-16).
            new XElement(Fev1 + "ImpNeto", Importe(esB ? importes.Neto : importes.Total)),
            new XElement(Fev1 + "ImpOpEx", Importe(0m)),
            new XElement(Fev1 + "ImpTrib", Importe(0m)),
            new XElement(Fev1 + "ImpIVA", Importe(esB ? importes.Iva : 0m)));

        // FR-09: con Productos y servicios, las fechas de servicio y de vencimiento del pago.
        if (solicitud.FechaServicioDesde is { } desde && solicitud.FechaServicioHasta is { } hasta
            && solicitud.FechaVencimientoPago is { } vencimientoPago)
        {
            detalle.Add(
                new XElement(Fev1 + "FchServDesde", Fecha(desde)),
                new XElement(Fev1 + "FchServHasta", Fecha(hasta)),
                new XElement(Fev1 + "FchVtoPago", Fecha(vencimientoPago)));
        }

        detalle.Add(
            new XElement(Fev1 + "MonId", CodigosWsfe.MonedaPesos),
            new XElement(Fev1 + "MonCotiz", Entero(CodigosWsfe.CotizacionPesos)),
            new XElement(Fev1 + "CondicionIVAReceptorId", Entero(CodigosWsfe.CondicionIvaConsumidorFinal)));

        // FR-14 y FR-16: el IVA discriminado solo en la B.
        if (esB && importes.Alicuota is { } alicuota)
        {
            detalle.Add(new XElement(
                Fev1 + "Iva",
                new XElement(
                    Fev1 + "AlicIva",
                    new XElement(Fev1 + "Id", Entero(CodigosWsfe.Alicuota(alicuota))),
                    new XElement(Fev1 + "BaseImp", Importe(importes.Neto)),
                    new XElement(Fev1 + "Importe", Importe(importes.Iva)))));
        }

        return detalle;
    }

    private XElement Autenticacion(TicketAcceso ticket) =>
        new(
            Fev1 + "Auth",
            new XElement(Fev1 + "Token", ticket.Token),
            new XElement(Fev1 + "Sign", ticket.Firma),
            new XElement(Fev1 + "Cuit", _cuit));

    private Task<RespuestaSoap> EnviarAsync(string accion, XElement operacion, CancellationToken cancelacion) =>
        ServiciosArca.EnviarAsync(
            _http,
            ServiciosArca.UrlWsfe(_entorno),
            Fev1.NamespaceName + accion,
            ServiciosArca.Sobre(operacion, new XAttribute(XNamespace.Xmlns + "ar", Fev1)),
            cancelacion);

    private static string Codigo(XElement error) =>
        int.TryParse(error.Element(Fev1 + "Code")?.Value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var codigo)
            ? codigo.ToString(CultureInfo.InvariantCulture)
            : "sin código";

    /// <summary>
    /// La descripción de ARCA para la operadora: los caracteres de control (saltos de línea, tabulaciones, C1) pasan a
    /// ser un espacio y los de formato invisibles (por ejemplo, los de dirección del texto) se quitan; los espacios
    /// repetidos quedan en uno y sin espacios en los extremos.
    /// </summary>
    internal static string Sanear(string texto)
    {
        var limpio = new StringBuilder(texto.Length);
        foreach (var caracter in texto)
        {
            if (char.IsControl(caracter) || char.IsWhiteSpace(caracter))
            {
                if (limpio.Length > 0 && limpio[^1] != ' ')
                {
                    limpio.Append(' ');
                }
            }
            else if (CharUnicodeInfo.GetUnicodeCategory(caracter) != UnicodeCategory.Format)
            {
                limpio.Append(caracter);
            }
        }

        return limpio.ToString().Trim();
    }

    /// <summary>Hasta <see cref="LargoMaximoDescripcion"/> caracteres, sin cortar un par sustituto por la mitad.</summary>
    private static string Recortar(string texto)
    {
        if (texto.Length <= LargoMaximoDescripcion)
        {
            return texto;
        }

        var largo = char.IsHighSurrogate(texto[LargoMaximoDescripcion - 1])
            ? LargoMaximoDescripcion - 1
            : LargoMaximoDescripcion;
        return texto[..largo];
    }

    private static string Entero(long valor) => valor.ToString(CultureInfo.InvariantCulture);

    /// <summary>NFR-04: punto decimal y 2 decimales.</summary>
    private static string Importe(decimal valor) => valor.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Fecha(DateOnly fecha) => fecha.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
}
