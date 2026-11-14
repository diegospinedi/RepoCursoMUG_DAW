using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Optica.Application.Comun;
using Optica.Domain.Facturacion;
using Optica.Infrastructure.Arca;

namespace Optica.Infrastructure.Tests.Arca;

/// <summary>
/// Lo que necesitan las pruebas del cliente de WSAA y WSFEv1 sin tocar la red: un directorio de datos temporal, un
/// certificado autofirmado generado en la prueba (PFX con clave), un <see cref="ManejadorArcaFalso"/> que responde con
/// las respuestas grabadas de <c>Arca/Respuestas</c> (datos ficticios), un reloj controlado y un protector de prueba. Los
/// valores secretos o personales son centinelas fáciles de buscar en el log.
/// </summary>
internal sealed class EscenarioArca : IDisposable
{
    /// <summary>CUIT ficticio con dígito verificador válido (DNI 0: no es de una persona ni de una empresa real).</summary>
    public const string Cuit = "20000000001";

    /// <summary>DNI ficticio de las pruebas de privacidad (D-50): empieza con 00, no se emite.</summary>
    public const string Dni = "00718294";

    /// <summary>El token de la respuesta grabada de WSAA.</summary>
    public const string Token = "VE9LRU4tQ0VOVElORUxBLWFyY2EtN2YzYQ==";

    /// <summary>La firma de la respuesta grabada de WSAA.</summary>
    public const string Firma = "RklSTUEtQ0VOVElORUxBLWFyY2EtOWMxZQ==";

    /// <summary>La clave del PFX generado en la prueba.</summary>
    public const string ClaveCertificado = "clave-pfx-centinela-4d2b";

    /// <summary>El vencimiento del ticket de la respuesta grabada de WSAA.</summary>
    public static readonly DateTimeOffset VencimientoTicket = new(2026, 10, 31, 0, 0, 0, TimeSpan.FromHours(-3));

    /// <summary>El momento de las pruebas: 30/10/2026 12:00 en Argentina, 12 horas antes del vencimiento del ticket.</summary>
    public static readonly DateTimeOffset Ahora = new(2026, 10, 30, 12, 0, 0, TimeSpan.FromHours(-3));

    public EscenarioArca()
    {
        Directory.CreateDirectory(Directorio);
        Directory.CreateDirectory(DirectorioCertificado);
        using var clave = RSA.Create(2048);
        var pedido = new CertificateRequest(
            "CN=optica-pruebas, O=Prueba sin validez", clave, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var autofirmado = pedido.CreateSelfSigned(Ahora.AddDays(-1), Ahora.AddYears(1));
        File.WriteAllBytes(RutaCertificado, autofirmado.Export(X509ContentType.Pfx, ClaveCertificado));
        Certificado = X509CertificateLoader.LoadCertificate(autofirmado.RawData);
        Reloj = new FakeTimeProvider(Ahora);
        Http = new HttpClient(Manejador);
    }

    public string Directorio { get; } =
        Path.Combine(Path.GetTempPath(), "optica-pruebas-arca", Guid.NewGuid().ToString("N"));

    /// <summary>El PFX vive fuera del directorio de datos, como en una instalación.</summary>
    public string DirectorioCertificado => Path.Combine(Directorio + "-certificado", "privado");

    public string RutaCertificado => Path.Combine(DirectorioCertificado, "empresa-centinela.pfx");

    /// <summary>El certificado público (sin clave privada), para verificar la firma.</summary>
    public X509Certificate2 Certificado { get; }

    public FakeTimeProvider Reloj { get; }

    public ManejadorArcaFalso Manejador { get; } = new();

    public HttpClient Http { get; }

    public ProtectorDePrueba Protector { get; } = new();

    public RegistroCapturadoArca Registro { get; } = new();

    public string ArchivoTicket => Path.Combine(Directorio, "arca-ta-homologacion.json");

    /// <summary>Las claves <c>Arca:*</c> de homologación con credenciales completas.</summary>
    public OpcionesArca Opciones(string? claveCertificado = null, string? rutaCertificado = null) =>
        OpcionesArca.Leer(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [OpcionesArca.ClaveEntorno] = "Homologacion",
                [OpcionesArca.ClaveCuit] = Cuit,
                [OpcionesArca.ClaveCertificadoRuta] = rutaCertificado ?? RutaCertificado,
                [OpcionesArca.ClaveCertificadoClave] = claveCertificado ?? ClaveCertificado,
            })
            .Build());

    public AutorizadorWsfe CrearAutorizador(string? claveCertificado = null, string? rutaCertificado = null) =>
        AutorizadorWsfe.Crear(
            Opciones(claveCertificado, rutaCertificado), Directorio, Http, Protector, Reloj, FabricaRegistro());

    public ClienteWsaa CrearWsaa() =>
        new(
            EntornoArca.Homologacion, Http, Cuit, RutaCertificado, ClaveCertificado, Directorio, Protector, Reloj,
            Registro.Para<ClienteWsaa>());

    /// <summary>Un token que se puede cancelar y nunca se cancela: la emisión sin plazo vencido.</summary>
    public CancellationToken Abierto => _abierto.Token;

    private readonly CancellationTokenSource _abierto = new();

    /// <summary>El JSON del ticket guardado, con el CUIT y la huella del certificado de la prueba salvo que se cambien.</summary>
    public byte[] TicketGuardado(
        string token, DateTimeOffset vencimiento, string? cuit = Cuit, string? huella = null, string firma = "firma") =>
        Protector.Proteger(
            "Optica.Arca.Ticket",
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
            {
                token,
                firma,
                vencimiento,
                cuit,
                huellaCertificado = huella ?? ClienteWsaa.Huella(Certificado),
            }));

    /// <summary>Espera (en tiempo real, hasta 10 s) que se cumpla la condición.</summary>
    public static async Task<bool> EsperarAsync(Func<bool> condicion)
    {
        for (var intento = 0; intento < 200; intento++)
        {
            if (condicion())
            {
                return true;
            }

            await Task.Delay(50);
        }

        return condicion();
    }

    public ILoggerFactory FabricaRegistro() =>
        LoggerFactory.Create(log => log.AddProvider(Registro).SetMinimumLevel(LogLevel.Trace));

    /// <summary>Un plazo de 30 s medido con el reloj controlado, como el de <c>EmitirFactura</c> (D-32).</summary>
    public CancellationTokenSource Plazo() => new(TimeSpan.FromSeconds(30), Reloj);

    /// <summary>Los textos que nunca pueden aparecer en el log ni en un mensaje (R-02, R-07, D-13).</summary>
    public IEnumerable<string> Centinelas() =>
    [
        Token, Firma, ClaveCertificado, Cuit, Dni, Dni.TrimStart('0'), RutaCertificado, DirectorioCertificado,
        Directorio,
    ];

    public static string Respuesta(string nombre) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Arca", "Respuestas", nombre + ".xml"), Encoding.UTF8);

    public void Dispose()
    {
        _abierto.Dispose();
        Http.Dispose();
        Certificado.Dispose();
        foreach (var directorio in new[] { Directorio, Directorio + "-certificado" })
        {
            if (Directory.Exists(directorio))
            {
                Directory.Delete(directorio, recursive: true);
            }
        }
    }
}

/// <summary>Un pedido que recibió el manejador falso: URL, acción SOAP y cuerpo.</summary>
internal sealed record PedidoArca(Uri Url, string? AccionSoap, string Cuerpo)
{
    private static readonly XNamespace Soap = "http://schemas.xmlsoap.org/soap/envelope/";

    /// <summary>El nombre del primer elemento del cuerpo SOAP: <c>loginCms</c>, <c>FECAESolicitar</c>, etc.</summary>
    public string Operacion =>
        XDocument.Parse(Cuerpo).Root!.Element(Soap + "Body")!.Elements().First().Name.LocalName;

    public XDocument Xml => XDocument.Parse(Cuerpo);

    /// <summary>El valor del único elemento con ese nombre local (sin importar el espacio de nombres), o null.</summary>
    public string? Valor(string nombre) =>
        Xml.Descendants().SingleOrDefault(elemento => elemento.Name.LocalName == nombre)?.Value;
}

/// <summary>
/// Reemplaza la red en las pruebas de ARCA: registra cada pedido y responde con las respuestas grabadas. Solo acepta los
/// hosts de homologación: un pedido a cualquier otro (producción incluida) falla la prueba (R-14, AGENTS.md).
/// </summary>
internal sealed class ManejadorArcaFalso : HttpMessageHandler
{
    private static readonly string[] HostsPermitidos = ["wsaahomo.afip.gov.ar", "wswhomo.afip.gov.ar"];

    private readonly ConcurrentQueue<PedidoArca> _pedidos = new();
    private readonly ConcurrentDictionary<string, Func<PedidoArca, CancellationToken, Task<HttpResponseMessage>>>
        _respuestas = new(StringComparer.Ordinal);

    public ManejadorArcaFalso()
    {
        Responder("loginCms", "wsaa-login-ok");
        Responder("FECompUltimoAutorizado", "wsfe-ultimo-ok");
        Responder("FECAESolicitar", "wsfe-cae-aprobado");
    }

    public IReadOnlyList<PedidoArca> Pedidos => _pedidos.ToArray();

    public IReadOnlyList<PedidoArca> PedidosDe(string operacion) =>
        Pedidos.Where(pedido => pedido.Operacion == operacion).ToList();

    /// <summary>Se completa cuando llega el primer pedido de <paramref name="operacion"/>.</summary>
    public Task Recibido(string operacion) => _recibidos.GetOrAdd(
        operacion, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task;

    private readonly ConcurrentDictionary<string, TaskCompletionSource> _recibidos = new(StringComparer.Ordinal);

    /// <summary>Responde <paramref name="operacion"/> con la respuesta grabada <paramref name="nombre"/>.</summary>
    public void Responder(string operacion, string nombre, HttpStatusCode estado = HttpStatusCode.OK) =>
        ResponderTexto(operacion, EscenarioArca.Respuesta(nombre), estado);

    public void ResponderTexto(string operacion, string texto, HttpStatusCode estado = HttpStatusCode.OK) =>
        _respuestas[operacion] = (_, _) => Task.FromResult(new HttpResponseMessage(estado)
        {
            Content = new StringContent(texto, Encoding.UTF8, "text/xml"),
        });

    public void Responder(string operacion, Func<PedidoArca, CancellationToken, Task<HttpResponseMessage>> respuesta) =>
        _respuestas[operacion] = respuesta;

    /// <summary>No responde nunca: espera hasta que se cancela el pedido.</summary>
    public void NoResponder(string operacion) =>
        _respuestas[operacion] = async (_, cancelacion) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancelacion);
            throw new InvalidOperationException("No debería llegar acá.");
        };

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri ?? throw new InvalidOperationException("Pedido sin URL.");
        if (!HostsPermitidos.Contains(url.Host, StringComparer.OrdinalIgnoreCase) || url.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("La prueba intentó salir de la homologación de ARCA.");
        }

        var cuerpo = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        // SOAP 1.1: la acción va entre comillas.
        var accion = request.Headers.TryGetValues("SOAPAction", out var valores) ? valores.Single().Trim('"') : null;
        var pedido = new PedidoArca(url, accion, cuerpo);
        _pedidos.Enqueue(pedido);
        var operacion = pedido.Operacion;
        _recibidos.GetOrAdd(operacion, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
            .TrySetResult();
        if (!_respuestas.TryGetValue(operacion, out var responder))
        {
            throw new InvalidOperationException("Operación sin respuesta grabada.");
        }

        return await responder(pedido, cancellationToken);
    }
}

/// <summary>
/// Un <see cref="IProtectorDatos"/> de prueba que cifra de verdad (AES-GCM con una clave al azar y el propósito como
/// datos asociados): un archivo protegido con otro propósito, o de otra instancia, no se descifra. Anota los propósitos
/// usados.
/// </summary>
internal sealed class ProtectorDePrueba : IProtectorDatos
{
    private readonly byte[] _clave = RandomNumberGenerator.GetBytes(32);
    private readonly ConcurrentQueue<string> _propositos = new();

    public IReadOnlyList<string> Propositos => _propositos.ToArray();

    /// <summary>Si es verdadero, <see cref="Proteger"/> falla como falla Data Protection sin claves utilizables.</summary>
    public bool FallarAlProteger { get; set; }

    /// <summary>Si no es null, <see cref="Proteger"/> lanza esta excepción (una falla no prevista).</summary>
    public Exception? ExcepcionAlProteger { get; set; }

    public byte[] Proteger(string proposito, byte[] datos)
    {
        if (ExcepcionAlProteger is { } excepcion)
        {
            throw excepcion;
        }

        if (FallarAlProteger)
        {
            throw new CryptographicException("Protección de prueba fallida.");
        }

        _propositos.Enqueue(proposito);
        var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        var cifrado = new byte[datos.Length];
        var etiqueta = new byte[AesGcm.TagByteSizes.MaxSize];
        using var aes = new AesGcm(_clave, etiqueta.Length);
        aes.Encrypt(nonce, datos, cifrado, etiqueta, Encoding.UTF8.GetBytes(proposito));
        return [.. nonce, .. etiqueta, .. cifrado];
    }

    public byte[] Desproteger(string proposito, byte[] datos)
    {
        _propositos.Enqueue(proposito);
        var largoNonce = AesGcm.NonceByteSizes.MaxSize;
        var largoEtiqueta = AesGcm.TagByteSizes.MaxSize;
        if (datos.Length < largoNonce + largoEtiqueta)
        {
            throw new CryptographicException("Datos protegidos inválidos.");
        }

        var nonce = datos.AsSpan(0, largoNonce);
        var etiqueta = datos.AsSpan(largoNonce, largoEtiqueta);
        var cifrado = datos.AsSpan(largoNonce + largoEtiqueta);
        var claro = new byte[cifrado.Length];
        using var aes = new AesGcm(_clave, largoEtiqueta);
        aes.Decrypt(nonce, cifrado, etiqueta, claro, Encoding.UTF8.GetBytes(proposito));
        return claro;
    }
}

/// <summary>Un manejador que no sale a ningún lado: cuenta los intentos y falla cada uno.</summary>
internal sealed class ManejadorSinRed : HttpMessageHandler
{
    private int _intentos;

    public int Intentos => Volatile.Read(ref _intentos);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _intentos);
        throw new HttpRequestException("La prueba no puede salir a la red.");
    }
}

/// <summary>
/// Un cuerpo de respuesta que cancela <see cref="_plazo"/> cuando termina de leerse: la cancelación llega justo después
/// de recibir el último número, antes de enviar <c>FECAESolicitar</c>.
/// </summary>
internal sealed class ContenidoQueCancelaAlTerminar(string texto, CancellationTokenSource plazo) : HttpContent
{
    private readonly byte[] _bytes = Encoding.UTF8.GetBytes(texto);
    private readonly CancellationTokenSource _plazo = plazo;

    protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) =>
        stream.WriteAsync(_bytes).AsTask();

    protected override bool TryComputeLength(out long length)
    {
        length = _bytes.Length;
        return true;
    }

    protected override Task<Stream> CreateContentReadStreamAsync() =>
        Task.FromResult<Stream>(new FlujoQueCancela(_bytes, _plazo));

    protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken) =>
        CreateContentReadStreamAsync();

    private sealed class FlujoQueCancela(byte[] bytes, CancellationTokenSource plazo) : MemoryStream(bytes)
    {
        // Ignora la cancelación al leer (ya leyó todo) y cancela al llegar al final.
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var leidos = Read(buffer.Span);
            if (leidos == 0)
            {
                plazo.Cancel();
            }

            return ValueTask.FromResult(leidos);
        }
    }
}
