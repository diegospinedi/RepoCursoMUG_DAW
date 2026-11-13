using System.Net;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Optica.Domain.Facturacion;

namespace Optica.Infrastructure.Arca;

/// <summary>
/// Lo común a WSAA y WSFEv1: las URLs de cada entorno (constantes, nunca configurables, R-01, R-14), el
/// <see cref="HttpClient"/> propio de ARCA y el envío y la lectura de mensajes SOAP 1.1. La respuesta se lee con un tope
/// de <see cref="TamanoMaximoRespuesta"/> y sin DTD (R-12). Nunca registra un cuerpo SOAP.
/// </summary>
internal static class ServiciosArca
{
    /// <summary>El tope de una respuesta de ARCA (R-12): 1 MB.</summary>
    public const int TamanoMaximoRespuesta = 1024 * 1024;

    public static readonly XNamespace Soap = "http://schemas.xmlsoap.org/soap/envelope/";

    private static readonly Uri WsaaHomologacion = new("https://wsaahomo.afip.gov.ar/ws/services/LoginCms");
    private static readonly Uri WsaaProduccion = new("https://wsaa.afip.gov.ar/ws/services/LoginCms");
    private static readonly Uri WsfeHomologacion = new("https://wswhomo.afip.gov.ar/wsfev1/service.asmx");
    private static readonly Uri WsfeProduccion = new("https://servicios1.afip.gov.ar/wsfev1/service.asmx");

    /// <summary>La URL de <c>loginCms</c> de WSAA del entorno.</summary>
    /// <exception cref="ArgumentOutOfRangeException">El entorno es el simulado, que no sale del proceso.</exception>
    public static Uri UrlWsaa(EntornoArca entorno) => entorno switch
    {
        EntornoArca.Homologacion => WsaaHomologacion,
        EntornoArca.Produccion => WsaaProduccion,
        _ => throw new ArgumentOutOfRangeException(nameof(entorno), "El entorno no es homologación ni producción."),
    };

    /// <summary>La URL del servicio WSFEv1 del entorno.</summary>
    /// <exception cref="ArgumentOutOfRangeException">El entorno es el simulado, que no sale del proceso.</exception>
    public static Uri UrlWsfe(EntornoArca entorno) => entorno switch
    {
        EntornoArca.Homologacion => WsfeHomologacion,
        EntornoArca.Produccion => WsfeProduccion,
        _ => throw new ArgumentOutOfRangeException(nameof(entorno), "El entorno no es homologación ni producción."),
    };

    /// <summary>
    /// El cliente HTTP de ARCA (singleton de la raíz de composición). Validación TLS por defecto, sin
    /// <c>ServerCertificateCustomValidationCallback</c> (R-12); sin seguir redirecciones (el token no viaja a otro
    /// host); sin plazo propio: la espera la corta la cancelación de <c>EmitirFactura</c> (D-32).
    /// </summary>
    public static HttpClient CrearClienteHttp() =>
        new(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            MaxResponseHeadersLength = 64,
        })
        {
            Timeout = Timeout.InfiniteTimeSpan,
            DefaultRequestVersion = HttpVersion.Version11,
        };

    /// <summary>Un sobre SOAP 1.1 con <paramref name="operacion"/> como único elemento del cuerpo.</summary>
    public static XDocument Sobre(XElement operacion, params XAttribute[] espacios) =>
        new(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(
                Soap + "Envelope",
                new object[] { new XAttribute(XNamespace.Xmlns + "soap", Soap) }.Concat(espacios),
                new XElement(Soap + "Header"),
                new XElement(Soap + "Body", operacion)));

    /// <summary>
    /// Envía el sobre y lee la respuesta, sea cual sea su estado HTTP (un SOAP Fault llega con 500).
    /// </summary>
    /// <exception cref="RespuestaArcaIlegibleException">La respuesta supera el tope, tiene DTD o no es XML.</exception>
    /// <exception cref="HttpRequestException">Falla de la red.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancelacion"/> se canceló.</exception>
    public static async Task<RespuestaSoap> EnviarAsync(
        HttpClient http, Uri url, string accion, XDocument sobre, CancellationToken cancelacion)
    {
        using var pedido = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(sobre.Declaration + sobre.ToString(SaveOptions.DisableFormatting), Encoding.UTF8, "text/xml"),
        };
        pedido.Headers.TryAddWithoutValidation("SOAPAction", "\"" + accion + "\"");
        using var respuesta = await http.SendAsync(pedido, HttpCompletionOption.ResponseHeadersRead, cancelacion);
        var documento = await LeerAsync(respuesta.Content, cancelacion);
        return new RespuestaSoap(respuesta.IsSuccessStatusCode, documento);
    }

    /// <summary>Parsea un XML de ARCA que llegó como texto (el ticket de WSAA viaja escapado dentro del SOAP).</summary>
    /// <exception cref="RespuestaArcaIlegibleException">Supera el tope, tiene DTD o no es XML.</exception>
    public static XDocument Parsear(string texto)
    {
        if (texto.Length > TamanoMaximoRespuesta)
        {
            throw new RespuestaArcaIlegibleException();
        }

        using var lector = new StringReader(texto);
        return Parsear(XmlReader.Create(lector, Opciones()));
    }

    private static async Task<XDocument> LeerAsync(HttpContent contenido, CancellationToken cancelacion)
    {
        if (contenido.Headers.ContentLength > TamanoMaximoRespuesta)
        {
            throw new RespuestaArcaIlegibleException();
        }

        await using var flujo = await contenido.ReadAsStreamAsync(cancelacion);
        using var copia = new MemoryStream();
        var bloque = new byte[16 * 1024];
        int leidos;
        while ((leidos = await flujo.ReadAsync(bloque, cancelacion)) > 0)
        {
            if (copia.Length + leidos > TamanoMaximoRespuesta)
            {
                throw new RespuestaArcaIlegibleException();
            }

            copia.Write(bloque, 0, leidos);
        }

        copia.Position = 0;
        return Parsear(XmlReader.Create(copia, Opciones()));
    }

    private static XDocument Parsear(XmlReader lector)
    {
        try
        {
            using (lector)
            {
                return XDocument.Load(lector, LoadOptions.None);
            }
        }
        catch (XmlException)
        {
            // El texto de la excepción puede citar el contenido: no se conserva.
            throw new RespuestaArcaIlegibleException();
        }
    }

    // R-12: sin DTD (ni entidades), sin resolver nada externo y con el mismo tope en caracteres.
    private static XmlReaderSettings Opciones() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersInDocument = TamanoMaximoRespuesta,
        MaxCharactersFromEntities = 0,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
    };
}

/// <summary>La respuesta SOAP leída: si el estado HTTP fue de éxito, y el documento.</summary>
internal sealed record RespuestaSoap(bool Exito, XDocument Documento)
{
    /// <summary>El <c>faultcode</c> del SOAP Fault, sin prefijo y solo con caracteres seguros para el log; o null.</summary>
    public string? CodigoFalla()
    {
        var falla = Documento.Root?.Element(ServiciosArca.Soap + "Body")?.Element(ServiciosArca.Soap + "Fault");
        if (falla is null)
        {
            return null;
        }

        var codigo = falla.Element("faultcode")?.Value.Trim() ?? string.Empty;
        codigo = codigo[(codigo.LastIndexOf(':') + 1)..];
        var seguro = new string(codigo.Where(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-').Take(64).ToArray());
        return seguro.Length == 0 ? "sin código" : seguro;
    }

    public override string ToString() => nameof(RespuestaSoap);
}

/// <summary>Una respuesta de ARCA que supera el tope, trae DTD o no es XML (R-12). Sin el contenido.</summary>
internal sealed class RespuestaArcaIlegibleException : Exception
{
    public RespuestaArcaIlegibleException()
        : base("La respuesta de ARCA no se pudo leer.")
    {
    }
}

/// <summary>
/// Una falla de ARCA antes de enviar el pedido de autorización (WSAA o último número), con un texto fijo: el motivo
/// "no enviada" que corresponde. Nunca lleva la excepción original (R-02, R-07).
/// </summary>
internal sealed class FallaArcaException : Exception
{
    public FallaArcaException(string motivo)
        : base(motivo)
    {
    }
}
