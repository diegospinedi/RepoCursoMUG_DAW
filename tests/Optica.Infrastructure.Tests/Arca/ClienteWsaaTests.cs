using System.Net;
using System.Security.Cryptography.Pkcs;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Optica.Application.Facturacion;
using Optica.Infrastructure.Arca;

namespace Optica.Infrastructure.Tests.Arca;

/// <summary>
/// Spec FEAT-007, Block 6: el pedido de acceso a WSAA (TRA para <c>wsfe</c>, firmado en CMS con el certificado de la
/// empresa), el ticket en memoria y en <c>arca-ta-homologacion.json</c> cifrado con <c>IProtectorDatos</c> (D-14, D-30),
/// reutilizado mientras falten más de 10 minutos para su vencimiento. Sin red: respuestas grabadas y certificado
/// autofirmado generado en la prueba.
/// </summary>
public sealed class ClienteWsaaTests : IDisposable
{
    private readonly EscenarioArca _escenario = new();

    public void Dispose() => _escenario.Dispose();

    [Fact]
    public async Task Wsaa_FirmaCms_VerificableConElCertificado()
    {
        var wsaa = _escenario.CrearWsaa();

        var ticket = await wsaa.ObtenerTicketAsync(CancellationToken.None);

        var pedido = Assert.Single(_escenario.Manejador.Pedidos);
        Assert.Equal("https://wsaahomo.afip.gov.ar/ws/services/LoginCms", pedido.Url.ToString());
        Assert.Equal("loginCms", pedido.Operacion);
        var cms = new SignedCms();
        cms.Decode(Convert.FromBase64String(pedido.Valor("in0")!));

        // La firma es válida y la hizo el certificado de la empresa (FR-17, R-01).
        cms.CheckSignature(verifySignatureOnly: true);
        var firmante = Assert.Single(cms.SignerInfos.Cast<SignerInfo>());
        Assert.Equal(_escenario.Certificado.Thumbprint, firmante.Certificate!.Thumbprint);

        // El contenido firmado es el pedido de acceso al servicio wsfe, vigente en este momento.
        var tra = XDocument.Parse(Encoding.UTF8.GetString(cms.ContentInfo.Content)).Root!;
        Assert.Equal("loginTicketRequest", tra.Name.LocalName);
        Assert.Equal("wsfe", tra.Element("service")!.Value);
        var cabecera = tra.Element("header")!;
        Assert.False(string.IsNullOrWhiteSpace(cabecera.Element("uniqueId")!.Value));
        var generacion = DateTimeOffset.Parse(cabecera.Element("generationTime")!.Value, null);
        var vencimiento = DateTimeOffset.Parse(cabecera.Element("expirationTime")!.Value, null);
        Assert.True(generacion < EscenarioArca.Ahora && EscenarioArca.Ahora < vencimiento);

        Assert.Equal(EscenarioArca.Token, ticket.Token);
        Assert.Equal(EscenarioArca.Firma, ticket.Firma);
        Assert.Equal(EscenarioArca.VencimientoTicket, ticket.Vencimiento);
    }

    [Fact]
    public async Task Wsaa_TicketVigente_SeReutiliza()
    {
        var wsaa = _escenario.CrearWsaa();

        await wsaa.ObtenerTicketAsync(CancellationToken.None);
        await wsaa.ObtenerTicketAsync(CancellationToken.None);

        // En memoria: un solo pedido a WSAA (D-14: WSAA rechaza otro mientras haya uno vigente).
        Assert.Single(_escenario.Manejador.PedidosDe("loginCms"));

        // Después de un reinicio: el del archivo, sin pedir otro.
        var reiniciado = _escenario.CrearWsaa();
        var leido = await reiniciado.ObtenerTicketAsync(CancellationToken.None);
        Assert.Equal(EscenarioArca.Token, leido.Token);
        Assert.Single(_escenario.Manejador.PedidosDe("loginCms"));

        // Faltan 11 minutos para el vencimiento: se sigue usando.
        _escenario.Reloj.SetUtcNow(EscenarioArca.VencimientoTicket.AddMinutes(-11));
        await reiniciado.ObtenerTicketAsync(CancellationToken.None);
        Assert.Single(_escenario.Manejador.PedidosDe("loginCms"));

        // Faltan 10 minutos: se pide otro.
        _escenario.Reloj.SetUtcNow(EscenarioArca.VencimientoTicket.AddMinutes(-10));
        await reiniciado.ObtenerTicketAsync(CancellationToken.None);
        Assert.Equal(2, _escenario.Manejador.PedidosDe("loginCms").Count);
    }

    [Fact]
    public async Task Wsaa_TicketGuardado_CifradoEnDisco()
    {
        var wsaa = _escenario.CrearWsaa();

        await wsaa.ObtenerTicketAsync(CancellationToken.None);

        // D-30: el archivo existe, sin el token ni la firma en claro, y no quedan temporales.
        Assert.True(File.Exists(_escenario.ArchivoTicket));
        var contenido = File.ReadAllBytes(_escenario.ArchivoTicket);
        foreach (var secreto in new[] { EscenarioArca.Token, EscenarioArca.Firma })
        {
            foreach (var codificacion in new[] { Encoding.UTF8, Encoding.Unicode, Encoding.Latin1 })
            {
                Assert.DoesNotContain(secreto, codificacion.GetString(contenido), StringComparison.Ordinal);
            }

            Assert.DoesNotContain(secreto, Convert.ToBase64String(contenido), StringComparison.Ordinal);
        }

        Assert.Equal(
            [Path.GetFileName(_escenario.ArchivoTicket)],
            Directory.GetFiles(_escenario.Directorio).Select(ruta => Path.GetFileName(ruta)!).ToArray());

        // Lo protegió con el propósito del ticket, y con ese propósito se descifra.
        Assert.All(_escenario.Protector.Propositos, proposito => Assert.Equal("Optica.Arca.Ticket", proposito));
        Assert.NotEmpty(_escenario.Protector.Propositos);
        var claro = Encoding.UTF8.GetString(_escenario.Protector.Desproteger("Optica.Arca.Ticket", contenido));
        Assert.Contains(EscenarioArca.Token, claro, StringComparison.Ordinal);

        // Ronda 2: el ticket queda atado al CUIT y al certificado con que se pidió.
        using var json = JsonDocument.Parse(claro);
        Assert.True(json.RootElement.TryGetProperty("cuit", out var cuit));
        Assert.Equal(EscenarioArca.Cuit, cuit.GetString());
        Assert.True(json.RootElement.TryGetProperty("huellaCertificado", out var huella));
        Assert.Equal(ClienteWsaa.Huella(_escenario.Certificado), huella.GetString());
        Assert.Matches("^[0-9A-F]{64}$", ClienteWsaa.Huella(_escenario.Certificado));
    }

    public static TheoryData<string> ArchivosIlegibles() => new()
    {
        "basura",
        "otro-proposito",
        "json-invalido",
        "json-sin-token",
        "vencido",
        "otro-cuit",
        "otra-huella",
        "sin-cuit",
    };

    [Theory]
    [MemberData(nameof(ArchivosIlegibles))]
    public async Task Wsaa_TicketGuardadoIlegible_SePideOtro(string caso)
    {
        var protector = _escenario.Protector;
        byte[] Proteger(string texto) => protector.Proteger("Optica.Arca.Ticket", Encoding.UTF8.GetBytes(texto));
        var contenido = caso switch
        {
            "basura" => Encoding.UTF8.GetBytes("esto no es un ticket cifrado"),
            "otro-proposito" => protector.Proteger(
                "Otro.Proposito",
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
                {
                    token = "otro",
                    firma = "otra",
                    vencimiento = EscenarioArca.VencimientoTicket,
                }))),
            "json-invalido" => Proteger("{ no es json"),
            "json-sin-token" => Proteger(JsonSerializer.Serialize(new { vencimiento = EscenarioArca.VencimientoTicket })),
            "vencido" => Proteger(JsonSerializer.Serialize(new
            {
                token = "vencido",
                firma = "vencida",
                vencimiento = EscenarioArca.Ahora.AddHours(-1),
            })),
            // Ronda 2: un ticket vigente de otro CUIT o de otro certificado no sirve para esta configuración.
            "otro-cuit" => _escenario.TicketGuardado("otro", EscenarioArca.VencimientoTicket, cuit: "20000000028"),
            "otra-huella" => _escenario.TicketGuardado("otro", EscenarioArca.VencimientoTicket, huella: new string('A', 64)),
            "sin-cuit" => _escenario.TicketGuardado("otro", EscenarioArca.VencimientoTicket, cuit: null),
            _ => throw new ArgumentOutOfRangeException(nameof(caso)),
        };
        File.WriteAllBytes(_escenario.ArchivoTicket, contenido);
        var wsaa = _escenario.CrearWsaa();

        var ticket = await wsaa.ObtenerTicketAsync(CancellationToken.None);

        // Se descartó y se pidió otro, que quedó guardado en su lugar.
        Assert.Equal(EscenarioArca.Token, ticket.Token);
        Assert.Single(_escenario.Manejador.PedidosDe("loginCms"));
        var guardado = Encoding.UTF8.GetString(
            protector.Desproteger("Optica.Arca.Ticket", File.ReadAllBytes(_escenario.ArchivoTicket)));
        Assert.Contains(EscenarioArca.Token, guardado, StringComparison.Ordinal);
        Assert.DoesNotContain(_escenario.Registro.Entradas, entrada =>
            entrada.Texto.Contains("esto no es", StringComparison.Ordinal) || entrada.Excepcion is not null);
    }

    [Fact]
    public async Task Wsaa_Rechazo_NoEnviadaError()
    {
        // "ya posee un TA válido" sin ticket guardado: WSAA no da otro (D-14).
        _escenario.Manejador.Responder("loginCms", "wsaa-fault-ya-posee-ta", HttpStatusCode.InternalServerError);
        var autorizador = _escenario.CrearAutorizador();

        var resultado = await autorizador.AutorizarAsync(SolicitudesArca.FacturaB(), _escenario.Abierto);

        Assert.Equal(AutorizadorWsfe.MotivoAcceso, Assert.IsType<ResultadoAutorizacion.NoEnviada>(resultado).Motivo);
        Assert.Empty(_escenario.Manejador.PedidosDe("FECompUltimoAutorizado"));
        Assert.Empty(_escenario.Manejador.PedidosDe("FECAESolicitar"));
        Assert.False(File.Exists(_escenario.ArchivoTicket));

        // El log lleva el código de WSAA para el diagnóstico, sin la excepción adjunta.
        Assert.Contains(_escenario.Registro.Entradas, entrada => entrada.Nivel >= LogLevel.Warning
            && entrada.Texto.Contains("coe.alreadyAuthenticated", StringComparison.Ordinal));
        Assert.All(_escenario.Registro.Entradas, entrada => Assert.Null(entrada.Excepcion));
    }

    [Fact]
    public async Task Wsaa_SinRespuesta_NoEnviadaTimeout()
    {
        _escenario.Manejador.NoResponder("loginCms");
        var autorizador = _escenario.CrearAutorizador();
        using var plazo = _escenario.Plazo();

        var emision = autorizador.AutorizarAsync(SolicitudesArca.FacturaB(), plazo.Token);
        await _escenario.Manejador.Recibido("loginCms").WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(emision.IsCompleted);
        _escenario.Reloj.Advance(TimeSpan.FromSeconds(30));
        var resultado = await emision.WaitAsync(TimeSpan.FromSeconds(10));

        // Vencido el plazo antes de enviar FECAESolicitar: no enviada, sin pendiente (D-16, D-32).
        Assert.IsType<ResultadoAutorizacion.NoEnviada>(resultado);
        Assert.Empty(_escenario.Manejador.PedidosDe("FECAESolicitar"));
    }

    /// <summary>
    /// Ronda 2 (ticket perdido): el plazo de la emisión vence con <c>loginCms</c> ya enviado. La emisión termina "no
    /// enviada" sin esperar más, pero la respuesta se sigue leyendo y, cuando llega, el ticket se guarda: la próxima
    /// emisión lo usa sin pedir otro (WSAA rechazaría otro mientras ese esté vigente).
    /// </summary>
    [Fact]
    public async Task Wsaa_PlazoVencidoConLoginEnviado_ElTicketQueLlegaDespuesSeGuarda()
    {
        var liberar = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _escenario.Manejador.Responder("loginCms", async (_, cancelacion) =>
        {
            await liberar.Task.WaitAsync(cancelacion);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(EscenarioArca.Respuesta("wsaa-login-ok"), Encoding.UTF8, "text/xml"),
            };
        });
        var autorizador = _escenario.CrearAutorizador();
        using var plazo = _escenario.Plazo();

        var emision = autorizador.AutorizarAsync(SolicitudesArca.FacturaB(), plazo.Token);
        await _escenario.Manejador.Recibido("loginCms").WaitAsync(TimeSpan.FromSeconds(10));
        _escenario.Reloj.Advance(TimeSpan.FromSeconds(30));
        var resultado = await emision.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.IsType<ResultadoAutorizacion.NoEnviada>(resultado);
        Assert.Empty(_escenario.Manejador.PedidosDe("FECAESolicitar"));

        // WSAA contesta después del plazo: el ticket se guarda igual.
        liberar.SetResult();
        Assert.True(await EscenarioArca.EsperarAsync(() => File.Exists(_escenario.ArchivoTicket)));

        var siguiente = await autorizador.AutorizarAsync(SolicitudesArca.FacturaB(), _escenario.Abierto);
        Assert.IsType<ResultadoAutorizacion.Autorizada>(siguiente);
        Assert.Single(_escenario.Manejador.PedidosDe("loginCms"));
        Assert.Equal(EscenarioArca.Token, _escenario.Manejador.PedidosDe("FECAESolicitar").Single().Valor("Token"));
    }

    /// <summary>Ronda 2: la lectura en segundo plano tiene su propio límite de 60 s; pasado, la próxima pide otro.</summary>
    [Fact]
    public async Task Wsaa_PlazoVencidoConLoginEnviado_LimitePropioDe60s()
    {
        _escenario.Manejador.NoResponder("loginCms");
        var autorizador = _escenario.CrearAutorizador();
        using var plazo = _escenario.Plazo();

        var emision = autorizador.AutorizarAsync(SolicitudesArca.FacturaB(), plazo.Token);
        await _escenario.Manejador.Recibido("loginCms").WaitAsync(TimeSpan.FromSeconds(10));
        _escenario.Reloj.Advance(TimeSpan.FromSeconds(30));
        Assert.IsType<ResultadoAutorizacion.NoEnviada>(await emision.WaitAsync(TimeSpan.FromSeconds(10)));

        // A los 59 s el pedido sigue abierto; a los 60 s se abandona y queda en el log, sin la excepción.
        _escenario.Reloj.Advance(TimeSpan.FromSeconds(29));
        await Task.Delay(100);
        Assert.DoesNotContain(_escenario.Registro.Entradas, entrada =>
            entrada.Texto.Contains("no respondió el pedido de acceso en 60 s", StringComparison.Ordinal));
        _escenario.Reloj.Advance(TimeSpan.FromSeconds(1));
        Assert.True(await EscenarioArca.EsperarAsync(() => _escenario.Registro.Entradas.Any(entrada =>
            entrada.Texto.Contains("no respondió el pedido de acceso en 60 s", StringComparison.Ordinal))));
        Assert.All(_escenario.Registro.Entradas, entrada => Assert.Null(entrada.Excepcion));

        _escenario.Manejador.Responder("loginCms", "wsaa-login-ok");
        var siguiente = await autorizador.AutorizarAsync(SolicitudesArca.FacturaB(), _escenario.Abierto);
        Assert.IsType<ResultadoAutorizacion.Autorizada>(siguiente);
        Assert.Equal(2, _escenario.Manejador.PedidosDe("loginCms").Count);
    }

    /// <summary>
    /// Ronda 2: al renovar dentro de los 10 minutos de margen, WSAA puede contestar que ya hay un ticket vigente
    /// (<c>coe.alreadyAuthenticated</c>). Si el de memoria o el del disco todavía no venció, se usa ese y la emisión sigue.
    /// </summary>
    [Theory]
    [InlineData("memoria")]
    [InlineData("disco")]
    public async Task Wsaa_YaPoseeTaDentroDelMargen_UsaElVigente(string origen)
    {
        var autorizador = _escenario.CrearAutorizador();
        var tokenEsperado = EscenarioArca.Token;
        if (origen == "memoria")
        {
            Assert.IsType<ResultadoAutorizacion.Autorizada>(
                await autorizador.AutorizarAsync(SolicitudesArca.FacturaB(), _escenario.Abierto));
            _escenario.Reloj.SetUtcNow(EscenarioArca.VencimientoTicket.AddMinutes(-5));
        }
        else
        {
            tokenEsperado = "dGlja2V0LWRlbC1kaXNjbw==";
            File.WriteAllBytes(
                _escenario.ArchivoTicket, _escenario.TicketGuardado(tokenEsperado, EscenarioArca.Ahora.AddMinutes(5)));
        }

        _escenario.Manejador.Responder("loginCms", "wsaa-fault-ya-posee-ta", HttpStatusCode.InternalServerError);
        var loginsAntes = _escenario.Manejador.PedidosDe("loginCms").Count;

        var resultado = await autorizador.AutorizarAsync(SolicitudesArca.FacturaB(), _escenario.Abierto);

        Assert.IsType<ResultadoAutorizacion.Autorizada>(resultado);
        Assert.Equal(loginsAntes + 1, _escenario.Manejador.PedidosDe("loginCms").Count);
        Assert.Equal(tokenEsperado, _escenario.Manejador.PedidosDe("FECAESolicitar").Last().Valor("Token"));
    }

    /// <summary>Ronda 2: ya vencido del todo, el "ya posee un TA válido" sigue siendo no enviada.</summary>
    [Fact]
    public async Task Wsaa_YaPoseeTaConTicketVencido_NoEnviada()
    {
        File.WriteAllBytes(_escenario.ArchivoTicket, _escenario.TicketGuardado("vencido", EscenarioArca.Ahora.AddMinutes(-1)));
        _escenario.Manejador.Responder("loginCms", "wsaa-fault-ya-posee-ta", HttpStatusCode.InternalServerError);

        var resultado = await _escenario.CrearAutorizador()
            .AutorizarAsync(SolicitudesArca.FacturaB(), _escenario.Abierto);

        Assert.Equal(AutorizadorWsfe.MotivoAcceso, Assert.IsType<ResultadoAutorizacion.NoEnviada>(resultado).Motivo);
    }

    /// <summary>Cobertura: si el ticket no se puede guardar, queda en memoria y se avisa en el log, sin la excepción.</summary>
    [Fact]
    public async Task Wsaa_FallaAlGrabarElTicket_QuedaEnMemoria()
    {
        _escenario.Protector.FallarAlProteger = true;
        var wsaa = _escenario.CrearWsaa();

        var ticket = await wsaa.ObtenerTicketAsync(_escenario.Abierto);
        await wsaa.ObtenerTicketAsync(_escenario.Abierto);

        Assert.Equal(EscenarioArca.Token, ticket.Token);
        Assert.False(File.Exists(_escenario.ArchivoTicket));
        Assert.Empty(Directory.GetFiles(_escenario.Directorio));
        Assert.Single(_escenario.Manejador.PedidosDe("loginCms"));
        Assert.Contains(_escenario.Registro.Entradas, entrada => entrada.Nivel == LogLevel.Warning
            && entrada.Texto.Contains("no se pudo guardar el ticket", StringComparison.Ordinal));
        Assert.All(_escenario.Registro.Entradas, entrada => Assert.Null(entrada.Excepcion));
    }

    /// <summary>Ronda 3 (R1): grabar el ticket es de mejor esfuerzo también ante una falla no prevista.</summary>
    [Fact]
    public async Task Wsaa_FallaImprevistaAlGrabar_QuedaEnMemoriaYRegistraElTipo()
    {
        _escenario.Protector.ExcepcionAlProteger = new InvalidOperationException("detalle-que-no-debe-aparecer");
        var wsaa = _escenario.CrearWsaa();
        TicketAcceso? ticket = null;

        var excepcion = await Record.ExceptionAsync(async () => ticket = await wsaa.ObtenerTicketAsync(_escenario.Abierto));

        Assert.Null(excepcion);
        Assert.Equal(EscenarioArca.Token, ticket!.Token);
        Assert.False(File.Exists(_escenario.ArchivoTicket));
        Assert.Contains(_escenario.Registro.Entradas, entrada => entrada.Nivel == LogLevel.Warning
            && entrada.Texto.Contains("no se pudo guardar el ticket", StringComparison.Ordinal)
            && entrada.Texto.Contains(nameof(InvalidOperationException), StringComparison.Ordinal));
        Assert.DoesNotContain(_escenario.Registro.Entradas, entrada =>
            entrada.Texto.Contains("detalle-que-no-debe-aparecer", StringComparison.Ordinal) || entrada.Excepcion is not null);
    }

    /// <summary>
    /// Ronda 3 (R1): una falla no prevista del pedido a WSAA (ni de ARCA ni del certificado) queda en el log con solo su
    /// tipo, aunque nadie espere ya el resultado.
    /// </summary>
    [Fact]
    public async Task Wsaa_FallaImprevistaDelPedido_SeRegistraElTipo()
    {
        _escenario.Manejador.Responder("loginCms", (_, _) => throw new InvalidOperationException("detalle-que-no-debe-aparecer"));
        var wsaa = _escenario.CrearWsaa();

        await Record.ExceptionAsync(() => wsaa.ObtenerTicketAsync(_escenario.Abierto));

        Assert.True(await EscenarioArca.EsperarAsync(() => _escenario.Registro.Entradas.Any(entrada =>
            entrada.Nivel == LogLevel.Error
            && entrada.Texto.Contains("pedido de acceso a WSAA", StringComparison.Ordinal)
            && entrada.Texto.Contains(nameof(InvalidOperationException), StringComparison.Ordinal))));
        Assert.DoesNotContain(_escenario.Registro.Entradas, entrada =>
            entrada.Texto.Contains("detalle-que-no-debe-aparecer", StringComparison.Ordinal) || entrada.Excepcion is not null);
    }

    /// <summary>Ronda 3 (R2-a): dos pedidos concurrentes con loginCms retenido: un solo loginCms y el mismo ticket.</summary>
    [Fact]
    public async Task Wsaa_DosPedidosConcurrentes_UnSoloLoginYElMismoTicket()
    {
        var liberar = RetenerLogin();
        var wsaa = _escenario.CrearWsaa();

        var primero = Task.Run(() => wsaa.ObtenerTicketAsync(_escenario.Abierto));
        var segundo = Task.Run(() => wsaa.ObtenerTicketAsync(_escenario.Abierto));
        await _escenario.Manejador.Recibido("loginCms").WaitAsync(TimeSpan.FromSeconds(10));
        await Task.Delay(100);
        Assert.False(primero.IsCompleted || segundo.IsCompleted);
        liberar.SetResult();
        var tickets = await Task.WhenAll(primero, segundo).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Single(_escenario.Manejador.PedidosDe("loginCms"));
        Assert.Same(tickets[0], tickets[1]);
        Assert.Equal(EscenarioArca.Token, tickets[0].Token);
    }

    /// <summary>
    /// Ronda 3 (R2-b): la primera emisión vence con loginCms en curso; una segunda, que llegó mientras tanto, recibe el
    /// ticket de ese mismo pedido, sin un segundo loginCms.
    /// </summary>
    [Fact]
    public async Task Wsaa_PrimeraEmisionVenceSegundaRecibeElTicketSinOtroLogin()
    {
        var liberar = RetenerLogin();
        var autorizador = _escenario.CrearAutorizador();
        using var plazo = _escenario.Plazo();

        var primera = autorizador.AutorizarAsync(SolicitudesArca.FacturaB(), plazo.Token);
        await _escenario.Manejador.Recibido("loginCms").WaitAsync(TimeSpan.FromSeconds(10));
        var segunda = autorizador.AutorizarAsync(SolicitudesArca.FacturaB(), _escenario.Abierto);
        _escenario.Reloj.Advance(TimeSpan.FromSeconds(30));
        Assert.IsType<ResultadoAutorizacion.NoEnviada>(await primera.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.False(segunda.IsCompleted);

        liberar.SetResult();
        Assert.IsType<ResultadoAutorizacion.Autorizada>(await segunda.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Single(_escenario.Manejador.PedidosDe("loginCms"));
        Assert.Single(_escenario.Manejador.PedidosDe("FECAESolicitar"));
    }

    /// <summary>
    /// Ronda 3 (R4): después de un <c>alreadyAuthenticated</c> que reusó un ticket vigente, ese ticket se usa hasta su
    /// vencimiento real sin volver a llamar a WSAA en cada emisión dentro del margen.
    /// </summary>
    [Fact]
    public async Task Wsaa_YaPoseeTaReusado_NoVuelveALlamarAWsaaDentroDelMargen()
    {
        File.WriteAllBytes(
            _escenario.ArchivoTicket,
            _escenario.TicketGuardado("dGlja2V0LWRlbC1kaXNjbw==", EscenarioArca.Ahora.AddMinutes(5)));
        _escenario.Manejador.Responder("loginCms", "wsaa-fault-ya-posee-ta", HttpStatusCode.InternalServerError);
        var autorizador = _escenario.CrearAutorizador();

        var primera = await autorizador.AutorizarAsync(SolicitudesArca.FacturaB(), _escenario.Abierto);
        _escenario.Reloj.Advance(TimeSpan.FromMinutes(2));
        var segunda = await autorizador.AutorizarAsync(SolicitudesArca.FacturaB(), _escenario.Abierto);

        Assert.IsType<ResultadoAutorizacion.Autorizada>(primera);
        Assert.IsType<ResultadoAutorizacion.Autorizada>(segunda);
        Assert.Single(_escenario.Manejador.PedidosDe("loginCms"));

        // Vencido del todo, se vuelve a pedir.
        _escenario.Reloj.Advance(TimeSpan.FromMinutes(4));
        _escenario.Manejador.Responder("loginCms", "wsaa-login-ok");
        Assert.IsType<ResultadoAutorizacion.Autorizada>(
            await autorizador.AutorizarAsync(SolicitudesArca.FacturaB(), _escenario.Abierto));
        Assert.Equal(2, _escenario.Manejador.PedidosDe("loginCms").Count);
        Assert.Equal(EscenarioArca.Token, _escenario.Manejador.PedidosDe("FECAESolicitar").Last().Valor("Token"));
    }

    /// <summary>
    /// Ronda 3 (R3): al descartar el autorizador (apagado de la Api) se espera el pedido de WSAA en curso hasta 5 s; si
    /// llega, el ticket se guarda.
    /// </summary>
    [Fact]
    public async Task Autorizador_Descartar_EsperaElPedidoDeWsaaEnCurso()
    {
        var liberar = RetenerLogin();
        var autorizador = _escenario.CrearAutorizador();
        using var plazo = _escenario.Plazo();
        var emision = autorizador.AutorizarAsync(SolicitudesArca.FacturaB(), plazo.Token);
        await _escenario.Manejador.Recibido("loginCms").WaitAsync(TimeSpan.FromSeconds(10));
        _escenario.Reloj.Advance(TimeSpan.FromSeconds(30));
        await emision.WaitAsync(TimeSpan.FromSeconds(10));

        var desechable = Assert.IsAssignableFrom<IAsyncDisposable>(autorizador);
        var descarte = desechable.DisposeAsync().AsTask();
        await Task.Delay(100);
        Assert.False(descarte.IsCompleted);
        liberar.SetResult();
        await descarte.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(File.Exists(_escenario.ArchivoTicket));
    }

    /// <summary>
    /// Ronda 3 (R3): si el pedido de WSAA no termina en 5 s, el apagado lo cancela y eso queda como Information, no como
    /// error ni como "WSAA no respondió".
    /// </summary>
    [Fact]
    public async Task Autorizador_Descartar_PasadosCincoSegundosCancelaComoInformacion()
    {
        _escenario.Manejador.NoResponder("loginCms");
        var autorizador = _escenario.CrearAutorizador();
        using var plazo = _escenario.Plazo();
        var emision = autorizador.AutorizarAsync(SolicitudesArca.FacturaB(), plazo.Token);
        await _escenario.Manejador.Recibido("loginCms").WaitAsync(TimeSpan.FromSeconds(10));
        _escenario.Reloj.Advance(TimeSpan.FromSeconds(30));
        await emision.WaitAsync(TimeSpan.FromSeconds(10));
        var antes = _escenario.Registro.Entradas.Count;

        var desechable = Assert.IsAssignableFrom<IAsyncDisposable>(autorizador);
        var descarte = desechable.DisposeAsync().AsTask();
        await Task.Delay(100);
        Assert.False(descarte.IsCompleted);
        _escenario.Reloj.Advance(TimeSpan.FromSeconds(5));
        await descarte.WaitAsync(TimeSpan.FromSeconds(10));

        var nuevas = _escenario.Registro.Entradas.Skip(antes).ToList();
        Assert.Contains(nuevas, entrada => entrada.Nivel == LogLevel.Information
            && entrada.Texto.Contains("apagado", StringComparison.Ordinal));
        Assert.DoesNotContain(nuevas, entrada => entrada.Nivel >= LogLevel.Warning);
    }

    /// <summary>Retiene la respuesta de loginCms hasta completar lo que devuelve.</summary>
    private TaskCompletionSource RetenerLogin()
    {
        var liberar = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _escenario.Manejador.Responder("loginCms", async (_, cancelacion) =>
        {
            await liberar.Task.WaitAsync(cancelacion);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(EscenarioArca.Respuesta("wsaa-login-ok"), Encoding.UTF8, "text/xml"),
            };
        });
        return liberar;
    }
}
