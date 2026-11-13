using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Optica.Application.Comun;
using Optica.Domain.Facturacion;

namespace Optica.Infrastructure.Arca;

/// <summary>
/// El acceso a WSAA de ARCA (FR-17, D-12, D-14): arma el pedido de acceso (TRA) para el servicio <c>wsfe</c>, lo firma en
/// CMS con el certificado de la empresa (R-01) y lo envía a <c>loginCms</c>. El ticket (token y firma, válidos unas 12
/// horas) queda en memoria y en <c>arca-ta-&lt;entorno&gt;.json</c> del directorio de datos, cifrado con
/// <see cref="IProtectorDatos"/> y el propósito <see cref="PropositoTicket"/> (D-30), atado al CUIT y a la huella del
/// certificado con que se pidió; se reutiliza mientras falten más de <see cref="MargenRenovacion"/> para su vencimiento.
/// Nunca registra el token, la firma, el CMS, el CUIT, la ruta ni la clave del certificado (R-02).
/// </summary>
/// <remarks>
/// WSAA rechaza otro ticket mientras haya uno vigente, así que un ticket pedido no se pierde:
/// <list type="bullet">
/// <item>El pedido a <c>loginCms</c> corre con su propio límite (<see cref="LimitePedido"/>), no con el plazo de la
/// emisión: si la emisión vence antes, termina "no enviada" sin esperar más, pero la respuesta se sigue leyendo y, si
/// llega, el ticket se guarda. Mientras tanto, otra emisión espera ese mismo pedido en lugar de enviar otro.</item>
/// <item>Si al renovar dentro del margen WSAA contesta que ya hay un ticket vigente (<c>coe.alreadyAuthenticated</c>), se
/// usa el de memoria o el del disco hasta su vencimiento.</item>
/// <item>Un archivo que no se descifra, no se entiende, venció o es de otro CUIT o de otro certificado se descarta y se
/// pide otro.</item>
/// </list>
/// </remarks>
internal sealed class ClienteWsaa
{
    /// <summary>El propósito con que se cifra el archivo del ticket (D-30).</summary>
    public const string PropositoTicket = "Optica.Arca.Ticket";

    /// <summary>El servicio de ARCA para el que se pide el ticket.</summary>
    public const string Servicio = "wsfe";

    /// <summary>El código de WSAA cuando ya entregó un ticket que sigue vigente.</summary>
    public const string CodigoYaPoseeTicket = "coe.alreadyAuthenticated";

    /// <summary>El ticket se renueva cuando faltan esto o menos para su vencimiento (D-14).</summary>
    public static readonly TimeSpan MargenRenovacion = TimeSpan.FromMinutes(10);

    /// <summary>El límite propio de un pedido a <c>loginCms</c>, independiente del plazo de la emisión.</summary>
    public static readonly TimeSpan LimitePedido = TimeSpan.FromSeconds(60);

    // La ventana del pedido de acceso: WSAA acepta pedidos generados hace poco y que todavía no vencieron.
    private static readonly TimeSpan VentanaPedido = TimeSpan.FromMinutes(10);

    private static readonly XNamespace EspacioWsaa = "http://wsaa.view.sua.dvadac.desein.afip.gov";

    // El archivo del ticket es chico: uno más grande no es el del ticket.
    private const long TamanoMaximoArchivo = 64 * 1024;

    private static readonly JsonSerializerOptions OpcionesJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        MaxDepth = 4,
    };

    private readonly EntornoArca _entorno;
    private readonly HttpClient _http;
    private readonly string _cuit;
    private readonly string _rutaCertificado;
    private readonly string _claveCertificado;
    private readonly string _archivo;
    private readonly IProtectorDatos _protector;
    private readonly TimeProvider _reloj;
    private readonly ILogger<ClienteWsaa> _registro;
    private readonly SemaphoreSlim _candado = new(1, 1);
    private TicketAcceso? _ticket;
    private bool _archivoLeido;
    private Task<TicketAcceso>? _pedidoEnCurso;
    private volatile string? _huella;

    // El ticket que WSAA confirmó vigente con alreadyAuthenticated: se usa hasta su vencimiento real, sin renovar.
    private TicketAcceso? _confirmadoVigente;

    // Se cancela al apagar la Api, si el pedido en curso no termina a tiempo.
    private readonly CancellationTokenSource _apagado = new();

    /// <summary>Lo que <see cref="DetenerAsync"/> espera al pedido de WSAA en curso antes de cancelarlo.</summary>
    public static readonly TimeSpan EsperaAlApagar = TimeSpan.FromSeconds(5);

    /// <param name="cuit">El CUIT de la configuración: el ticket guardado tiene que ser de ese CUIT.</param>
    /// <param name="directorio">El directorio de datos, donde vive el archivo del ticket.</param>
    public ClienteWsaa(
        EntornoArca entorno, HttpClient http, string cuit, string rutaCertificado, string claveCertificado,
        string directorio, IProtectorDatos protector, TimeProvider reloj, ILogger<ClienteWsaa> registro)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentException.ThrowIfNullOrWhiteSpace(cuit);
        ArgumentException.ThrowIfNullOrWhiteSpace(rutaCertificado);
        ArgumentNullException.ThrowIfNull(claveCertificado);
        ArgumentException.ThrowIfNullOrWhiteSpace(directorio);
        ArgumentNullException.ThrowIfNull(protector);
        ArgumentNullException.ThrowIfNull(reloj);
        ArgumentNullException.ThrowIfNull(registro);
        _ = ServiciosArca.UrlWsaa(entorno); // solo homologación o producción
        _entorno = entorno;
        _http = http;
        _cuit = cuit;
        _rutaCertificado = rutaCertificado;
        _claveCertificado = claveCertificado;
        _archivo = Path.Combine(directorio, NombreArchivo(entorno));
        _protector = protector;
        _reloj = reloj;
        _registro = registro;
    }

    /// <summary><c>arca-ta-homologacion.json</c> o <c>arca-ta-produccion.json</c>: un ticket por entorno.</summary>
    public static string NombreArchivo(EntornoArca entorno) =>
        $"arca-ta-{entorno.ToString().ToLowerInvariant()}.json";

    /// <summary>
    /// Lo que se guarda del certificado junto al ticket: el SHA-256 (en hexadecimal) de su huella SHA-256. Identifica el
    /// certificado sin guardar la huella misma.
    /// </summary>
    public static string Huella(X509Certificate2 certificado)
    {
        ArgumentNullException.ThrowIfNull(certificado);
        return Convert.ToHexString(SHA256.HashData(certificado.GetCertHash(HashAlgorithmName.SHA256)));
    }

    /// <summary>
    /// El ticket vigente: el de memoria, el del archivo o uno nuevo de WSAA (que reemplaza al del archivo). Si
    /// <paramref name="cancelacion"/> se cancela con el pedido a WSAA en curso, lanza enseguida y el pedido sigue hasta su
    /// propio límite para guardar el ticket.
    /// </summary>
    /// <exception cref="CertificadoArcaException">El certificado no se pudo cargar (ya registrado).</exception>
    /// <exception cref="FallaArcaException">WSAA rechazó el pedido, no respondió algo legible o falló la red (ya registrado).</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancelacion"/> se canceló.</exception>
    public async Task<TicketAcceso> ObtenerTicketAsync(CancellationToken cancelacion)
    {
        Task<TicketAcceso> pedido;
        await _candado.WaitAsync(cancelacion);
        try
        {
            if (!_archivoLeido)
            {
                _archivoLeido = true;
                _ticket = LeerArchivo();
            }

            if (_ticket is not null
                && (EstaVigente(_ticket) || (ReferenceEquals(_ticket, _confirmadoVigente) && NoVencio(_ticket))))
            {
                return _ticket;
            }

            pedido = _pedidoEnCurso ??= IniciarPedido();
        }
        finally
        {
            _candado.Release();
        }

        try
        {
            return await pedido.WaitAsync(cancelacion);
        }
        catch (OperationCanceledException) when (cancelacion.IsCancellationRequested && !pedido.IsCompleted)
        {
            _registro.LogWarning(
                "ARCA ({Entorno}): el plazo de la emisión venció esperando a WSAA. La emisión no se envió; la respuesta " +
                "de WSAA se sigue esperando hasta {Segundos} s para guardar el ticket.",
                _entorno,
                (int)LimitePedido.TotalSeconds);
            throw;
        }
    }

    private bool EstaVigente(TicketAcceso ticket) => ticket.Vencimiento - _reloj.GetUtcNow() > MargenRenovacion;

    private bool NoVencio(TicketAcceso ticket) => ticket.Vencimiento > _reloj.GetUtcNow();

    private Task<TicketAcceso> IniciarPedido()
    {
        var tarea = Task.Run(PedirYGuardarAsync);

        // La falla se observa siempre, aunque nadie espere ya el resultado (la emisión venció). Las de ARCA y del
        // certificado ya quedaron registradas donde ocurrieron; cualquier otra se registra acá, con solo su tipo (su texto
        // puede traer datos, R-02).
        _ = tarea.ContinueWith(
            terminada =>
            {
                var error = terminada.Exception?.GetBaseException();
                if (error is not null and not FallaArcaException and not CertificadoArcaException)
                {
                    _registro.LogError(
                        "ARCA ({Entorno}): falla no prevista en el pedido de acceso a WSAA ({TipoFalla}).",
                        _entorno,
                        error.GetType().Name);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return tarea;
    }

    /// <summary>
    /// Para el apagado de la Api: espera el pedido de WSAA en curso hasta <paramref name="tope"/> (para no perder un ticket
    /// que está por llegar) y, si no terminó, lo cancela; esa cancelación se registra como información. No lanza.
    /// </summary>
    public async Task DetenerAsync(TimeSpan tope)
    {
        Task<TicketAcceso>? pendiente;
        await _candado.WaitAsync();
        try
        {
            pendiente = _pedidoEnCurso;
        }
        finally
        {
            _candado.Release();
        }

        if (pendiente is not null)
        {
            using var espera = new CancellationTokenSource();
            var terminado = await Task.WhenAny(pendiente, Task.Delay(tope, _reloj, espera.Token));
            await espera.CancelAsync();
            if (terminado != pendiente)
            {
                await _apagado.CancelAsync();
            }

            try
            {
                await pendiente;
            }
            catch (Exception)
            {
                // Ya registrada por el pedido o por su continuación.
            }
        }

        // _apagado no se descarta: no tiene temporizador, y así un segundo DetenerAsync no falla.
    }

    /// <summary>Pide un ticket con el límite propio y lo deja en memoria y en el archivo.</summary>
    private async Task<TicketAcceso> PedirYGuardarAsync()
    {
        try
        {
            using var limitePropio = new CancellationTokenSource(LimitePedido, _reloj);
            using var limite = CancellationTokenSource.CreateLinkedTokenSource(limitePropio.Token, _apagado.Token);
            TicketAcceso nuevo;
            try
            {
                nuevo = await PedirTicketAsync(limite.Token);
            }
            catch (YaPoseeTicketException)
            {
                return await UsarTicketVigenteAsync();
            }

            await _candado.WaitAsync();
            try
            {
                _ticket = nuevo;
                GrabarArchivo(nuevo);
            }
            finally
            {
                _candado.Release();
            }

            _registro.LogInformation(
                "ARCA ({Entorno}): se obtuvo un ticket de acceso de WSAA que vence {Vencimiento:yyyy-MM-dd HH:mm zzz}.",
                _entorno,
                nuevo.Vencimiento);
            return nuevo;
        }
        finally
        {
            await _candado.WaitAsync();
            try
            {
                _pedidoEnCurso = null;
            }
            finally
            {
                _candado.Release();
            }
        }
    }

    /// <summary>WSAA dice que ya hay un ticket vigente: el de memoria o el del disco, si todavía no venció.</summary>
    private async Task<TicketAcceso> UsarTicketVigenteAsync()
    {
        await _candado.WaitAsync();
        try
        {
            var vigente = _ticket is { } enMemoria && NoVencio(enMemoria) ? enMemoria : LeerArchivo();
            if (vigente is not null && NoVencio(vigente))
            {
                _ticket = vigente;
                _confirmadoVigente = vigente;
                _registro.LogWarning(
                    "ARCA ({Entorno}): WSAA indica que ya hay un ticket vigente; se usa el guardado hasta su vencimiento " +
                    "({Vencimiento:yyyy-MM-dd HH:mm zzz}).",
                    _entorno,
                    vigente.Vencimiento);
                return vigente;
            }
        }
        finally
        {
            _candado.Release();
        }

        _registro.LogError(
            "ARCA ({Entorno}): WSAA indica que ya hay un ticket vigente, pero no hay ninguno guardado sin vencer. La " +
            "emisión no se envió; WSAA va a entregar otro cuando venza el anterior.",
            _entorno);
        throw new FallaArcaException(AutorizadorWsfe.MotivoAcceso);
    }

    private async Task<TicketAcceso> PedirTicketAsync(CancellationToken limite)
    {
        string cms;
        try
        {
            using var certificado = CertificadoArca.Cargar(_rutaCertificado, _claveCertificado);
            _huella = Huella(certificado);
            cms = Firmar(ArmarPedidoAcceso(), certificado);
        }
        catch (CertificadoArcaException error)
        {
            // Solo la clave de configuración y la causa general: nunca la ruta ni la clave (D-13).
            _registro.LogError(
                "ARCA ({Entorno}): no se pudo usar el certificado de {Clave}: {Causa}. La emisión no se envió.",
                _entorno,
                OpcionesArca.ClaveCertificadoRuta,
                error.Message);
            throw;
        }
        catch (CryptographicException error)
        {
            _registro.LogError(
                "ARCA ({Entorno}): no se pudo firmar el pedido de acceso con el certificado de {Clave} ({TipoFalla}). " +
                "La emisión no se envió.",
                _entorno,
                OpcionesArca.ClaveCertificadoRuta,
                error.GetType().Name);
            throw new CertificadoArcaException("no se pudo firmar con el certificado");
        }

        var sobre = ServiciosArca.Sobre(
            new XElement(EspacioWsaa + "loginCms", new XElement(EspacioWsaa + "in0", cms)),
            new XAttribute(XNamespace.Xmlns + "wsaa", EspacioWsaa));

        RespuestaSoap respuesta;
        try
        {
            respuesta = await ServiciosArca.EnviarAsync(
                _http, ServiciosArca.UrlWsaa(_entorno), string.Empty, sobre, limite);
        }
        catch (OperationCanceledException) when (limite.IsCancellationRequested && _apagado.IsCancellationRequested)
        {
            // El apagado de la Api, no una falla de WSAA.
            _registro.LogInformation(
                "ARCA ({Entorno}): el pedido de acceso a WSAA en curso se canceló por el apagado de la Api.", _entorno);
            throw new FallaArcaException(AutorizadorWsfe.MotivoAcceso);
        }
        catch (OperationCanceledException) when (limite.IsCancellationRequested)
        {
            _registro.LogWarning(
                "ARCA ({Entorno}): WSAA no respondió el pedido de acceso en {Segundos} s; se abandona y la próxima " +
                "emisión pide otro.",
                _entorno,
                (int)LimitePedido.TotalSeconds);
            throw new FallaArcaException(AutorizadorWsfe.MotivoAcceso);
        }
        catch (Exception error) when (error is HttpRequestException or RespuestaArcaIlegibleException or IOException
            or OperationCanceledException)
        {
            _registro.LogError(
                "ARCA ({Entorno}): WSAA no respondió o respondió algo ilegible ({TipoFalla}). La emisión no se envió.",
                _entorno,
                error.GetType().Name);
            throw new FallaArcaException(AutorizadorWsfe.MotivoAcceso);
        }

        if (respuesta.CodigoFalla() is { } codigo)
        {
            // El código de WSAA sirve para el diagnóstico y no es secreto.
            if (codigo == CodigoYaPoseeTicket)
            {
                _registro.LogWarning(
                    "ARCA ({Entorno}): WSAA rechazó el pedido de acceso con el código {CodigoWsaa}.", _entorno, codigo);
                throw new YaPoseeTicketException();
            }

            _registro.LogError(
                "ARCA ({Entorno}): WSAA rechazó el pedido de acceso con el código {CodigoWsaa}. La emisión no se envió.",
                _entorno,
                codigo);
            throw new FallaArcaException(AutorizadorWsfe.MotivoAcceso);
        }

        if (!respuesta.Exito || LeerTicket(respuesta.Documento) is not { } ticket)
        {
            _registro.LogError(
                "ARCA ({Entorno}): la respuesta de WSAA no trae un ticket de acceso legible. La emisión no se envió.",
                _entorno);
            throw new FallaArcaException(AutorizadorWsfe.MotivoAcceso);
        }

        return ticket;
    }

    /// <summary>El pedido de acceso (TRA) al servicio <see cref="Servicio"/>, vigente unos minutos.</summary>
    private string ArmarPedidoAcceso()
    {
        var ahora = _reloj.GetUtcNow();
        var pedido = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(
                "loginTicketRequest",
                new XAttribute("version", "1.0"),
                new XElement(
                    "header",
                    new XElement("uniqueId", ahora.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
                    new XElement("generationTime", Fecha(ahora - VentanaPedido)),
                    new XElement("expirationTime", Fecha(ahora + VentanaPedido))),
                new XElement("service", Servicio)));
        return pedido.Declaration + pedido.ToString(SaveOptions.DisableFormatting);
    }

    private static string Fecha(DateTimeOffset momento) =>
        momento.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    /// <summary>CMS (PKCS#7) con el TRA adentro, firmado con SHA-256 y con el certificado incluido; en base64.</summary>
    private static string Firmar(string pedido, X509Certificate2 certificado)
    {
        using var clavePrivada = certificado.GetRSAPrivateKey()
            ?? throw new CertificadoArcaException("el certificado no tiene una clave privada RSA");
        var cms = new SignedCms(new ContentInfo(Encoding.UTF8.GetBytes(pedido)), detached: false);
        var firmante = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, certificado, clavePrivada)
        {
            DigestAlgorithm = new Oid("2.16.840.1.101.3.4.2.1"), // SHA-256
            IncludeOption = X509IncludeOption.EndCertOnly,
        };
        cms.ComputeSignature(firmante, silent: true);
        return Convert.ToBase64String(cms.Encode());
    }

    /// <summary>El ticket de <c>loginCmsReturn</c>, o null si falta algo o no se entiende.</summary>
    private static TicketAcceso? LeerTicket(XDocument respuesta)
    {
        var texto = respuesta.Descendants().FirstOrDefault(elemento => elemento.Name.LocalName == "loginCmsReturn")?.Value;
        if (string.IsNullOrWhiteSpace(texto))
        {
            return null;
        }

        XElement? raiz;
        try
        {
            raiz = ServiciosArca.Parsear(texto).Root;
        }
        catch (RespuestaArcaIlegibleException)
        {
            return null;
        }

        var token = raiz?.Element("credentials")?.Element("token")?.Value.Trim();
        var firma = raiz?.Element("credentials")?.Element("sign")?.Value.Trim();
        var vencimiento = raiz?.Element("header")?.Element("expirationTime")?.Value.Trim();
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(firma)
            || !DateTimeOffset.TryParse(vencimiento, CultureInfo.InvariantCulture, DateTimeStyles.None, out var momento))
        {
            return null;
        }

        return new TicketAcceso(token, firma, momento);
    }

    /// <summary>
    /// El ticket del archivo si todavía no venció y es del CUIT y del certificado configurados; si no, null (y lo
    /// descarta). Si el certificado no se puede leer para comprobarlo, no lo usa pero tampoco lo borra.
    /// </summary>
    private TicketAcceso? LeerArchivo()
    {
        if (!File.Exists(_archivo))
        {
            return null;
        }

        try
        {
            if (new FileInfo(_archivo).Length > TamanoMaximoArchivo)
            {
                throw new FormatException();
            }

            var claro = _protector.Desproteger(PropositoTicket, File.ReadAllBytes(_archivo));
            var guardado = JsonSerializer.Deserialize<TicketGuardado>(claro, OpcionesJson);
            if (guardado is null || string.IsNullOrWhiteSpace(guardado.Token) || string.IsNullOrWhiteSpace(guardado.Firma)
                || guardado.Vencimiento == default)
            {
                throw new FormatException();
            }

            if (!string.Equals(guardado.Cuit, _cuit, StringComparison.Ordinal))
            {
                return DescartarAjeno();
            }

            if (HuellaActual() is not { } huella)
            {
                _registro.LogWarning(
                    "ARCA ({Entorno}): no se pudo leer el certificado para comprobar el ticket guardado {Archivo}; no se " +
                    "usa.",
                    _entorno,
                    NombreArchivo(_entorno));
                return null;
            }

            if (!string.Equals(guardado.HuellaCertificado, huella, StringComparison.Ordinal))
            {
                return DescartarAjeno();
            }

            var ticket = new TicketAcceso(guardado.Token, guardado.Firma, guardado.Vencimiento);
            if (NoVencio(ticket))
            {
                return ticket;
            }

            // Vencido: no es un error, se pide otro.
            Descartar();
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException
            or JsonException or FormatException or NotSupportedException)
        {
            // Solo el nombre del archivo y el tipo de la falla: nunca su contenido.
            _registro.LogWarning(
                "ARCA ({Entorno}): el ticket de acceso guardado {Archivo} no se pudo leer ({TipoFalla}); se descarta y " +
                "se pide otro a WSAA.",
                _entorno,
                NombreArchivo(_entorno),
                error.GetType().Name);
            Descartar();
            return null;
        }
    }

    private TicketAcceso? DescartarAjeno()
    {
        _registro.LogWarning(
            "ARCA ({Entorno}): el ticket de acceso guardado {Archivo} no corresponde al CUIT o al certificado " +
            "configurados; se descarta y se pide otro a WSAA.",
            _entorno,
            NombreArchivo(_entorno));
        Descartar();
        return null;
    }

    /// <summary>La huella del certificado configurado, leída una vez; null si no se puede leer.</summary>
    private string? HuellaActual()
    {
        if (_huella is { } conocida)
        {
            return conocida;
        }

        try
        {
            using var certificado = CertificadoArca.Cargar(_rutaCertificado, _claveCertificado);
            return _huella = Huella(certificado);
        }
        catch (CertificadoArcaException)
        {
            return null;
        }
    }

    private void Descartar()
    {
        try
        {
            File.Delete(_archivo);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Se reemplaza al grabar el próximo ticket.
        }
    }

    /// <summary>
    /// Graba el ticket cifrado en un temporal y lo mueve sobre el archivo. De mejor esfuerzo: ante cualquier falla
    /// registra solo su tipo y el ticket sigue en memoria.
    /// </summary>
    private void GrabarArchivo(TicketAcceso ticket)
    {
        var temporal = $"{_archivo}.{Guid.NewGuid():N}.tmp";
        try
        {
            var claro = JsonSerializer.SerializeToUtf8Bytes(
                new TicketGuardado
                {
                    Token = ticket.Token,
                    Firma = ticket.Firma,
                    Vencimiento = ticket.Vencimiento,
                    Cuit = _cuit,
                    HuellaCertificado = _huella,
                },
                OpcionesJson);
            File.WriteAllBytes(temporal, _protector.Proteger(PropositoTicket, claro));
            File.Move(temporal, _archivo, overwrite: true);
        }
        catch (Exception error)
        {
            _registro.LogWarning(
                "ARCA ({Entorno}): no se pudo guardar el ticket de acceso en {Archivo} ({TipoFalla}); queda en memoria " +
                "hasta reiniciar la Api.",
                _entorno,
                NombreArchivo(_entorno),
                error.GetType().Name);
        }
        finally
        {
            try
            {
                if (File.Exists(temporal))
                {
                    File.Delete(temporal);
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // Un temporal que no se pudo borrar no cambia el resultado.
            }
        }
    }

    /// <summary>La forma del archivo del ticket, antes de cifrarlo.</summary>
    private sealed class TicketGuardado
    {
        public string? Token { get; init; }

        public string? Firma { get; init; }

        public DateTimeOffset Vencimiento { get; init; }

        public string? Cuit { get; init; }

        public string? HuellaCertificado { get; init; }

        public override string ToString() => nameof(TicketGuardado);
    }

    /// <summary>WSAA contestó <see cref="CodigoYaPoseeTicket"/>: hay un ticket vigente que este pedido no recibe.</summary>
    private sealed class YaPoseeTicketException : Exception
    {
        public YaPoseeTicketException()
            : base("WSAA ya entregó un ticket vigente.")
        {
        }
    }
}

/// <summary>
/// El ticket de acceso de WSAA: token, firma y vencimiento. Son credenciales de la empresa durante horas (R-02):
/// <see cref="ToString"/> no los muestra.
/// </summary>
internal sealed class TicketAcceso
{
    public TicketAcceso(string token, string firma, DateTimeOffset vencimiento)
    {
        Token = token;
        Firma = firma;
        Vencimiento = vencimiento;
    }

    public string Token { get; }

    public string Firma { get; }

    public DateTimeOffset Vencimiento { get; }

    public override string ToString() => nameof(TicketAcceso);
}
