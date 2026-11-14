using System.Net;
using Microsoft.Extensions.Logging;
using Optica.Application.Facturacion;
using Optica.Domain.Facturacion;
using Optica.Infrastructure.Arca;

namespace Optica.Infrastructure.Tests.Arca;

/// <summary>
/// Spec FEAT-007, Block 6: <see cref="AutorizadorWsfe"/> orquesta token → último número → solicitud y clasifica el
/// resultado (D-16): autorizada, rechazada con el primer código y su descripción (hasta 500 caracteres), no enviada
/// (falla antes de enviar <c>FECAESolicitar</c>) o sin respuesta (después). Lee el XML sin DTD y con tope de 1 MB (R-12)
/// y nunca registra el token, la firma, el DNI, el CUIT ni la clave o la ruta del certificado (R-02, R-07). Sin red.
/// </summary>
public sealed class AutorizadorWsfeTests : IDisposable
{
    private readonly EscenarioArca _escenario = new();

    public void Dispose() => _escenario.Dispose();

    /// <summary>AC-18 contra la respuesta grabada: número último + 1, CAE y vencimiento.</summary>
    [Fact]
    public async Task Autorizador_ResultadoA_Autorizada()
    {
        var autorizador = _escenario.CrearAutorizador();

        var resultado = await autorizador.AutorizarAsync(SolicitudesArca.FacturaB(), _escenario.Abierto);

        var autorizada = Assert.IsType<ResultadoAutorizacion.Autorizada>(resultado);
        Assert.Equal(125, autorizada.Numero);
        Assert.Equal("12345678901234", autorizada.Cae);
        Assert.Equal(new DateOnly(2026, 11, 9), autorizada.Vencimiento);
        Assert.Equal(EntornoArca.Homologacion, autorizador.Entorno);
        Assert.Equal(TimeSpan.FromSeconds(30), autorizador.TiempoMaximo);
        Assert.Contains(_escenario.Registro.Entradas, entrada => entrada.Nivel == LogLevel.Information
            && entrada.Texto.Contains("0003-00000125", StringComparison.Ordinal));
    }

    /// <summary>AC-20: rechazo por resultado R (observación) o por Errors, con el primer código y su descripción.</summary>
    [Theory]
    [InlineData("wsfe-cae-rechazado", 10016, "El numero o fecha del comprobante no se corresponde con el proximo a autorizar.")]
    [InlineData("wsfe-cae-errors", 10015, "Factura B (CbteDesde igual a CbteHasta), DocTipo: 96 (DNI)")]
    public async Task Autorizador_ResultadoR_RechazadaConCodigo(string respuesta, int codigo, string inicio)
    {
        _escenario.Manejador.Responder("FECAESolicitar", respuesta);

        var resultado = await _escenario.CrearAutorizador()
            .AutorizarAsync(SolicitudesArca.FacturaB(), _escenario.Abierto);

        var rechazada = Assert.IsType<ResultadoAutorizacion.Rechazada>(resultado);
        Assert.Equal(codigo, rechazada.Codigo);
        Assert.StartsWith(inicio, rechazada.Descripcion, StringComparison.Ordinal);
        Assert.DoesNotContain("Segund", rechazada.Descripcion, StringComparison.Ordinal);
        Assert.Single(_escenario.Manejador.PedidosDe("FECAESolicitar"));

        // El log lleva el código, no la descripción (ARCA puede repetir ahí el documento del receptor).
        Assert.Contains(_escenario.Registro.Entradas, entrada =>
            entrada.Texto.Contains(codigo.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal));
        Assert.DoesNotContain(_escenario.Registro.Entradas, entrada =>
            entrada.Texto.Contains(inicio, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Autorizador_ResultadoR_DescripcionRecortadaA500()
    {
        var larga = new string('x', 800);
        _escenario.Manejador.ResponderTexto(
            "FECAESolicitar",
            EscenarioArca.Respuesta("wsfe-cae-rechazado").Replace(
                "El numero o fecha del comprobante no se corresponde con el proximo a autorizar. Consultar metodo FECompUltimoAutorizado.",
                larga));

        var resultado = await _escenario.CrearAutorizador()
            .AutorizarAsync(SolicitudesArca.FacturaB(), _escenario.Abierto);

        Assert.Equal(new string('x', 500), Assert.IsType<ResultadoAutorizacion.Rechazada>(resultado).Descripcion);
    }

    /// <summary>AC-21 con reloj controlado: enviado FECAESolicitar, el plazo de 30 s vence sin respuesta.</summary>
    [Fact]
    public async Task Autorizador_Timeout30sDespuesDeEnviar_SinRespuesta()
    {
        _escenario.Manejador.NoResponder("FECAESolicitar");
        var autorizador = _escenario.CrearAutorizador();
        using var plazo = _escenario.Plazo();

        var emision = autorizador.AutorizarAsync(SolicitudesArca.FacturaB(), plazo.Token);
        await _escenario.Manejador.Recibido("FECAESolicitar").WaitAsync(TimeSpan.FromSeconds(10));
        _escenario.Reloj.Advance(TimeSpan.FromSeconds(29));
        await Task.Delay(50);
        Assert.False(emision.IsCompleted);
        _escenario.Reloj.Advance(TimeSpan.FromSeconds(1));
        var resultado = await emision.WaitAsync(TimeSpan.FromSeconds(10));

        // Se abandona la espera sin reintentar: un solo pedido de autorización (FR-23).
        Assert.IsType<ResultadoAutorizacion.SinRespuesta>(resultado);
        Assert.Single(_escenario.Manejador.PedidosDe("FECAESolicitar"));
    }

    [Fact]
    public async Task Autorizador_CanceladoAntesDeEmpezar_NoEnviadaSinPedidos()
    {
        using var cancelado = new CancellationTokenSource();
        await cancelado.CancelAsync();

        var resultado = await _escenario.CrearAutorizador().AutorizarAsync(SolicitudesArca.FacturaB(), cancelado.Token);

        Assert.Equal(
            AutorizadorWsfe.MotivoCancelada, Assert.IsType<ResultadoAutorizacion.NoEnviada>(resultado).Motivo);
        Assert.Empty(_escenario.Manejador.Pedidos);
    }

    public static TheoryData<string> RespuestasIlegibles() => new()
    {
        "no-xml",
        "vacia",
        "sin-resultado",
        "fault-500",
        "cae-invalido",
        "vencimiento-invalido",
        "numero-distinto",
        "mayor-1mb",
        "falla-de-red",
    };

    [Theory]
    [MemberData(nameof(RespuestasIlegibles))]
    public async Task Autorizador_RespuestaIlegible_SinRespuestaError(string caso)
    {
        var aprobado = EscenarioArca.Respuesta("wsfe-cae-aprobado");
        switch (caso)
        {
            case "no-xml":
                _escenario.Manejador.ResponderTexto("FECAESolicitar", "esto no es XML");
                break;
            case "vacia":
                _escenario.Manejador.ResponderTexto("FECAESolicitar", string.Empty);
                break;
            case "sin-resultado":
                _escenario.Manejador.ResponderTexto(
                    "FECAESolicitar",
                    """<soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/"><soap:Body/></soap:Envelope>""");
                break;
            case "fault-500":
                _escenario.Manejador.ResponderTexto(
                    "FECAESolicitar",
                    """<soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/"><soap:Body><soap:Fault><faultcode>soap:Server</faultcode><faultstring>Error</faultstring></soap:Fault></soap:Body></soap:Envelope>""",
                    HttpStatusCode.InternalServerError);
                break;
            case "cae-invalido":
                _escenario.Manejador.ResponderTexto(
                    "FECAESolicitar", aprobado.Replace("<CAE>12345678901234</CAE>", "<CAE>1234</CAE>"));
                break;
            case "vencimiento-invalido":
                _escenario.Manejador.ResponderTexto(
                    "FECAESolicitar", aprobado.Replace("<CAEFchVto>20261109</CAEFchVto>", "<CAEFchVto>2026-11-09</CAEFchVto>"));
                break;
            case "numero-distinto":
                _escenario.Manejador.ResponderTexto(
                    "FECAESolicitar", aprobado.Replace("<CbteDesde>125</CbteDesde>", "<CbteDesde>126</CbteDesde>"));
                break;
            case "mayor-1mb":
                _escenario.Manejador.ResponderTexto(
                    "FECAESolicitar",
                    aprobado.Replace("<Reproceso>N</Reproceso>", $"<Reproceso>N</Reproceso><!--{new string('x', 1024 * 1024)}-->"));
                break;
            default:
                _escenario.Manejador.Responder(
                    "FECAESolicitar", (_, _) => throw new HttpRequestException("conexión cortada"));
                break;
        }

        var resultado = await _escenario.CrearAutorizador()
            .AutorizarAsync(SolicitudesArca.FacturaB(), _escenario.Abierto);

        // Ya se envió el pedido de autorización: no se sabe si ARCA autorizó (D-16, FR-24).
        Assert.IsType<ResultadoAutorizacion.SinRespuesta>(resultado);
        Assert.Contains(_escenario.Registro.Entradas, entrada => entrada.Nivel >= LogLevel.Warning);
        Assert.All(_escenario.Registro.Entradas, entrada => Assert.Null(entrada.Excepcion));
        Assert.DoesNotContain(_escenario.Registro.Entradas, entrada =>
            entrada.Texto.Contains("conexión cortada", StringComparison.Ordinal));
    }

    public static TheoryData<string> CertificadosIlegibles() => new() { "clave-incorrecta", "archivo-inexistente", "archivo-basura" };

    [Theory]
    [MemberData(nameof(CertificadosIlegibles))]
    public async Task Autorizador_PfxClaveIncorrecta_NoEnviadaError(string caso)
    {
        const string claveIncorrecta = "clave-incorrecta-centinela-81e0";
        var ruta = _escenario.RutaCertificado;
        var clave = EscenarioArca.ClaveCertificado;
        switch (caso)
        {
            case "clave-incorrecta":
                clave = claveIncorrecta;
                break;
            case "archivo-inexistente":
                ruta = Path.Combine(_escenario.DirectorioCertificado, "no-existe-centinela.pfx");
                break;
            default:
                ruta = Path.Combine(_escenario.DirectorioCertificado, "basura-centinela.pfx");
                File.WriteAllText(ruta, "esto no es un PFX");
                break;
        }

        var resultado = await _escenario.CrearAutorizador(clave, ruta)
            .AutorizarAsync(SolicitudesArca.FacturaB(), _escenario.Abierto);

        Assert.Equal(
            AutorizadorWsfe.MotivoCertificado, Assert.IsType<ResultadoAutorizacion.NoEnviada>(resultado).Motivo);
        Assert.Empty(_escenario.Manejador.Pedidos);

        // Un error en el log, sin la clave, sin la ruta ni el directorio del certificado y sin la excepción.
        Assert.Contains(_escenario.Registro.Entradas, entrada => entrada.Nivel == LogLevel.Error);
        Assert.All(_escenario.Registro.Entradas, entrada => Assert.Null(entrada.Excepcion));
        foreach (var secreto in new[] { claveIncorrecta, EscenarioArca.ClaveCertificado, ruta, _escenario.DirectorioCertificado })
        {
            Assert.DoesNotContain(_escenario.Registro.Entradas, entrada =>
                entrada.Texto.Contains(secreto, StringComparison.Ordinal));
        }
    }

    /// <summary>R-12: una respuesta con DTD no se procesa (ni sus entidades): no cuenta como autorización.</summary>
    [Fact]
    public async Task Autorizador_XmlConDtd_Rechazado()
    {
        _escenario.Manejador.Responder("FECAESolicitar", "wsfe-cae-con-dtd");

        var resultado = await _escenario.CrearAutorizador()
            .AutorizarAsync(SolicitudesArca.FacturaB(), _escenario.Abierto);

        Assert.IsType<ResultadoAutorizacion.SinRespuesta>(resultado);

        // Y en WSAA, antes de enviar nada a WSFEv1: no enviada.
        using var otro = new EscenarioArca();
        otro.Manejador.ResponderTexto(
            "loginCms",
            EscenarioArca.Respuesta("wsaa-login-ok").Replace(
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><!DOCTYPE soapenv:Envelope [<!ENTITY x \"y\">]>"));
        var enWsaa = await otro.CrearAutorizador().AutorizarAsync(SolicitudesArca.FacturaB(), _escenario.Abierto);
        Assert.IsType<ResultadoAutorizacion.NoEnviada>(enWsaa);
        Assert.Empty(otro.Manejador.PedidosDe("FECAESolicitar"));
    }

    /// <summary>R-02 y R-07: en ningún camino el log lleva el token, la firma, el CMS, el DNI, el CUIT, la clave o rutas.</summary>
    [Fact]
    public async Task Autorizador_Log_SinTokenNiDniNiClave()
    {
        var conDni = new SolicitudComprobante(
            TipoComprobante.FacturaB,
            3,
            SolicitudesArca.Fecha,
            ConceptoFactura.Productos,
            ReceptorFactura.ConDni(EscenarioArca.Dni),
            ImportesFactura.ParaFacturaB(1815m, 21m));

        // Autorizada, rechazada (con el DNI en la descripción de ARCA), sin respuesta y certificado ilegible.
        var autorizador = _escenario.CrearAutorizador();
        Assert.IsType<ResultadoAutorizacion.Autorizada>(await autorizador.AutorizarAsync(conDni, _escenario.Abierto));
        _escenario.Manejador.Responder("FECAESolicitar", "wsfe-cae-errors");
        Assert.IsType<ResultadoAutorizacion.Rechazada>(await autorizador.AutorizarAsync(conDni, _escenario.Abierto));
        _escenario.Manejador.ResponderTexto("FECAESolicitar", "ilegible");
        Assert.IsType<ResultadoAutorizacion.SinRespuesta>(await autorizador.AutorizarAsync(conDni, _escenario.Abierto));
        File.Delete(_escenario.ArchivoTicket);
        Assert.IsType<ResultadoAutorizacion.NoEnviada>(await _escenario.CrearAutorizador(claveCertificado: "otra-clave")
            .AutorizarAsync(conDni, _escenario.Abierto));
        _escenario.Manejador.Responder("loginCms", "wsaa-fault-ya-posee-ta", HttpStatusCode.InternalServerError);
        Assert.IsType<ResultadoAutorizacion.NoEnviada>(await _escenario.CrearAutorizador()
            .AutorizarAsync(conDni, _escenario.Abierto));

        var cms = _escenario.Manejador.PedidosDe("loginCms")[0].Valor("in0")!;
        var entradas = _escenario.Registro.Entradas;
        Assert.NotEmpty(entradas);
        Assert.All(entradas, entrada => Assert.Null(entrada.Excepcion));
        foreach (var secreto in _escenario.Centinelas().Append(cms[..40]).Append("otra-clave"))
        {
            Assert.DoesNotContain(entradas, entrada => entrada.Texto.Contains(secreto, StringComparison.Ordinal));
        }

        // Ni el cuerpo SOAP: ningún elemento de los pedidos en el log.
        Assert.DoesNotContain(entradas, entrada => entrada.Texto.Contains("<", StringComparison.Ordinal)
            && entrada.Texto.Contains("Envelope", StringComparison.Ordinal));
    }

    /// <summary>
    /// AC-24 contra respuestas grabadas de homologación: el punto de venta de la solicitud (el de la configuración) va en
    /// los dos pedidos, solo a los hosts de homologación, y el resultado no lleva nada del certificado. La obtención de
    /// un CAE real queda como verificación manual del usuario (D-02).
    /// </summary>
    [Fact]
    public async Task Autorizador_Homologacion_PuntoVentaDeLaConfiguracionSinCertificadoEnPantalla()
    {
        var autorizador = _escenario.CrearAutorizador();

        var resultado = await autorizador.AutorizarAsync(SolicitudesArca.FacturaB(puntoVenta: 7), _escenario.Abierto);

        Assert.IsType<ResultadoAutorizacion.Autorizada>(resultado);
        Assert.Equal(EntornoArca.Homologacion, autorizador.Entorno);
        Assert.Equal("7", Assert.Single(_escenario.Manejador.PedidosDe("FECompUltimoAutorizado")).Valor("PtoVta"));
        Assert.Equal("7", Assert.Single(_escenario.Manejador.PedidosDe("FECAESolicitar")).Valor("PtoVta"));
        Assert.All(_escenario.Manejador.Pedidos, pedido =>
            Assert.Contains(pedido.Url.Host, new[] { "wsaahomo.afip.gov.ar", "wswhomo.afip.gov.ar" }));

        // El certificado y su clave no salen del cliente: ni en el resultado ni en los pedidos a WSFEv1.
        Assert.Equal(
            ["Cae", "Numero", "Vencimiento"],
            typeof(ResultadoAutorizacion.Autorizada).GetProperties().Select(propiedad => propiedad.Name).Order().ToArray());
        var base64Certificado = Convert.ToBase64String(_escenario.Certificado.RawData);
        Assert.All(_escenario.Manejador.Pedidos.Where(pedido => pedido.Operacion != "loginCms"), pedido =>
        {
            Assert.DoesNotContain(EscenarioArca.ClaveCertificado, pedido.Cuerpo, StringComparison.Ordinal);
            Assert.DoesNotContain(base64Certificado[..40], pedido.Cuerpo, StringComparison.Ordinal);
        });
        Assert.All(_escenario.Manejador.Pedidos, pedido =>
            Assert.DoesNotContain(EscenarioArca.ClaveCertificado, pedido.Cuerpo, StringComparison.Ordinal));
    }

    /// <summary>Ronda 2: un token que no se puede cancelar no trae plazo; no se toca la red (nunca colgar la emisión).</summary>
    [Fact]
    public async Task Autorizador_TokenSinPlazo_NoEnviadaSinRed()
    {
        var resultado = await _escenario.CrearAutorizador()
            .AutorizarAsync(SolicitudesArca.FacturaB(), CancellationToken.None);

        Assert.Equal(
            AutorizadorWsfe.MotivoSinPlazo, Assert.IsType<ResultadoAutorizacion.NoEnviada>(resultado).Motivo);
        Assert.Empty(_escenario.Manejador.Pedidos);
    }

    /// <summary>Ronda 2: resultado A con Errors pudo haber dejado un CAE: sin respuesta (queda pendiente), nunca rechazada.</summary>
    [Fact]
    public async Task Autorizador_ResultadoAConErrors_SinRespuesta()
    {
        _escenario.Manejador.Responder("FECAESolicitar", "wsfe-cae-aprobado-con-errors");

        var resultado = await _escenario.CrearAutorizador()
            .AutorizarAsync(SolicitudesArca.FacturaB(), _escenario.Abierto);

        Assert.IsType<ResultadoAutorizacion.SinRespuesta>(resultado);
    }

    /// <summary>Ronda 2: una excepción no prevista después de enviar FECAESolicitar es "sin respuesta"; el log, solo el tipo.</summary>
    [Theory]
    [InlineData("invalid-operation")]
    [InlineData("object-disposed")]
    public async Task Autorizador_ExcepcionImprevistaDespuesDeEnviar_SinRespuesta(string caso)
    {
        const string detalle = "detalle-que-no-debe-aparecer";
        Exception error = caso == "invalid-operation"
            ? new InvalidOperationException(detalle)
            : new ObjectDisposedException(detalle);
        _escenario.Manejador.Responder("FECAESolicitar", (_, _) => throw error);
        ResultadoAutorizacion? resultado = null;

        var excepcion = await Record.ExceptionAsync(async () => resultado = await _escenario.CrearAutorizador()
            .AutorizarAsync(SolicitudesArca.FacturaB(), _escenario.Abierto));

        Assert.Null(excepcion);
        Assert.IsType<ResultadoAutorizacion.SinRespuesta>(resultado);
        Assert.Contains(_escenario.Registro.Entradas, entrada =>
            entrada.Texto.Contains(error.GetType().Name, StringComparison.Ordinal));
        Assert.DoesNotContain(_escenario.Registro.Entradas, entrada =>
            entrada.Texto.Contains(detalle, StringComparison.Ordinal) || entrada.Excepcion is not null);
    }

    /// <summary>Ronda 2 (simétrico): una excepción no prevista antes de enviar FECAESolicitar es "no enviada".</summary>
    [Fact]
    public async Task Autorizador_ExcepcionImprevistaAntesDeEnviar_NoEnviada()
    {
        const string detalle = "detalle-que-no-debe-aparecer";
        _escenario.Manejador.Responder("loginCms", (_, _) => throw new InvalidOperationException(detalle));
        ResultadoAutorizacion? resultado = null;

        var excepcion = await Record.ExceptionAsync(async () => resultado = await _escenario.CrearAutorizador()
            .AutorizarAsync(SolicitudesArca.FacturaB(), _escenario.Abierto));

        Assert.Null(excepcion);
        Assert.Equal(
            AutorizadorWsfe.MotivoInesperado, Assert.IsType<ResultadoAutorizacion.NoEnviada>(resultado).Motivo);
        Assert.Empty(_escenario.Manejador.PedidosDe("FECAESolicitar"));
        Assert.Contains(_escenario.Registro.Entradas, entrada =>
            entrada.Texto.Contains(nameof(InvalidOperationException), StringComparison.Ordinal));
        Assert.DoesNotContain(_escenario.Registro.Entradas, entrada =>
            entrada.Texto.Contains(detalle, StringComparison.Ordinal) || entrada.Excepcion is not null);
    }

    /// <summary>Ronda 2: la descripción del rechazo se devuelve sin caracteres de control ni de formato invisibles.</summary>
    [Fact]
    public async Task Autorizador_ResultadoR_DescripcionSaneada()
    {
        _escenario.Manejador.ResponderTexto(
            "FECAESolicitar",
            EscenarioArca.Respuesta("wsfe-cae-rechazado").Replace(
                "El numero o fecha del comprobante no se corresponde con el proximo a autorizar. Consultar metodo FECompUltimoAutorizado.",
                "Uno\tdos\r\ntres\u0085cuatro‮cinco\u009B seis"));

        var resultado = await _escenario.CrearAutorizador()
            .AutorizarAsync(SolicitudesArca.FacturaB(), _escenario.Abierto);

        var rechazada = Assert.IsType<ResultadoAutorizacion.Rechazada>(resultado);
        Assert.Equal("Uno dos tres cuatrocinco seis", rechazada.Descripcion);
        Assert.DoesNotContain(rechazada.Descripcion, char.IsControl);
    }

    /// <summary>Cobertura: rechazo sin descripción (y sin observaciones): código y texto fijos.</summary>
    [Theory]
    [InlineData("sin-msg", 10016)]
    [InlineData("sin-obs", 0)]
    public async Task Autorizador_ResultadoRSinDescripcion_TextoFijo(string caso, int codigo)
    {
        var rechazo = EscenarioArca.Respuesta("wsfe-cae-rechazado");
        var inicio = rechazo.IndexOf("<Observaciones>", StringComparison.Ordinal);
        var fin = rechazo.IndexOf("</Observaciones>", StringComparison.Ordinal) + "</Observaciones>".Length;
        _escenario.Manejador.ResponderTexto(
            "FECAESolicitar",
            caso == "sin-msg"
                ? rechazo[..inicio] + "<Observaciones><Obs><Code>10016</Code><Msg> \r\n </Msg></Obs></Observaciones>" + rechazo[fin..]
                : rechazo[..inicio] + rechazo[fin..]);

        var resultado = await _escenario.CrearAutorizador()
            .AutorizarAsync(SolicitudesArca.FacturaB(), _escenario.Abierto);

        var rechazada = Assert.IsType<ResultadoAutorizacion.Rechazada>(resultado);
        Assert.Equal(codigo, rechazada.Codigo);
        Assert.Equal("ARCA rechazó el comprobante sin informar el motivo.", rechazada.Descripcion);
    }

    /// <summary>Cobertura: el plazo vence esperando FECompUltimoAutorizado: no enviada, sin pedir autorización.</summary>
    [Fact]
    public async Task Autorizador_CanceladoDuranteUltimoNumero_NoEnviada()
    {
        _escenario.Manejador.NoResponder("FECompUltimoAutorizado");
        using var plazo = _escenario.Plazo();

        var emision = _escenario.CrearAutorizador().AutorizarAsync(SolicitudesArca.FacturaB(), plazo.Token);
        await _escenario.Manejador.Recibido("FECompUltimoAutorizado").WaitAsync(TimeSpan.FromSeconds(10));
        _escenario.Reloj.Advance(TimeSpan.FromSeconds(30));
        var resultado = await emision.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(
            AutorizadorWsfe.MotivoCancelada, Assert.IsType<ResultadoAutorizacion.NoEnviada>(resultado).Motivo);
        Assert.Empty(_escenario.Manejador.PedidosDe("FECAESolicitar"));
    }

    /// <summary>
    /// Cobertura del límite de D-16: el último número llegó completo y el plazo vence justo después, antes de enviar
    /// FECAESolicitar. No enviada, y no por el camino de "WSFEv1 no informó el último número".
    /// </summary>
    [Fact]
    public async Task Autorizador_CanceladoEntreUltimoNumeroYSolicitud_NoEnviada()
    {
        using var plazo = new CancellationTokenSource();
        _escenario.Manejador.Responder("FECompUltimoAutorizado", (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ContenidoQueCancelaAlTerminar(EscenarioArca.Respuesta("wsfe-ultimo-ok"), plazo),
        }));

        var resultado = await _escenario.CrearAutorizador().AutorizarAsync(SolicitudesArca.FacturaB(), plazo.Token);

        Assert.True(plazo.IsCancellationRequested);
        Assert.Equal(
            AutorizadorWsfe.MotivoCancelada, Assert.IsType<ResultadoAutorizacion.NoEnviada>(resultado).Motivo);
        Assert.Empty(_escenario.Manejador.PedidosDe("FECAESolicitar"));
        Assert.DoesNotContain(_escenario.Registro.Entradas, entrada =>
            entrada.Texto.Contains("no informó el último número", StringComparison.Ordinal));
    }

    /// <summary>Cobertura: el último autorizado ya es el número máximo de WSFEv1: no enviada, sin pedir autorización.</summary>
    [Fact]
    public async Task Autorizador_NumeroMaximoAlcanzado_NoEnviada()
    {
        _escenario.Manejador.ResponderTexto(
            "FECompUltimoAutorizado",
            EscenarioArca.Respuesta("wsfe-ultimo-ok").Replace("<CbteNro>124</CbteNro>", "<CbteNro>99999999</CbteNro>"));

        var resultado = await _escenario.CrearAutorizador()
            .AutorizarAsync(SolicitudesArca.FacturaB(), _escenario.Abierto);

        Assert.Equal(
            AutorizadorWsfe.MotivoUltimoNumero, Assert.IsType<ResultadoAutorizacion.NoEnviada>(resultado).Motivo);
        Assert.Empty(_escenario.Manejador.PedidosDe("FECAESolicitar"));
    }

    /// <summary>Cobertura: un PFX con clave ECDSA (no RSA) o sin clave privada no firma: no enviada, sin salir a la red.</summary>
    [Theory]
    [InlineData("ecdsa")]
    [InlineData("sin-clave-privada")]
    public async Task Autorizador_PfxSinClaveRsa_NoEnviadaError(string caso)
    {
        var ruta = Path.Combine(_escenario.DirectorioCertificado, caso + "-centinela.pfx");
        if (caso == "ecdsa")
        {
            using var clave = System.Security.Cryptography.ECDsa.Create();
            var pedido = new System.Security.Cryptography.X509Certificates.CertificateRequest(
                "CN=optica-pruebas-ecdsa", clave, System.Security.Cryptography.HashAlgorithmName.SHA256);
            using var certificado = pedido.CreateSelfSigned(EscenarioArca.Ahora.AddDays(-1), EscenarioArca.Ahora.AddYears(1));
            File.WriteAllBytes(
                ruta,
                certificado.Export(
                    System.Security.Cryptography.X509Certificates.X509ContentType.Pfx, EscenarioArca.ClaveCertificado));
        }
        else
        {
            File.WriteAllBytes(
                ruta,
                _escenario.Certificado.Export(
                    System.Security.Cryptography.X509Certificates.X509ContentType.Pfx, EscenarioArca.ClaveCertificado));
        }

        var resultado = await _escenario.CrearAutorizador(rutaCertificado: ruta)
            .AutorizarAsync(SolicitudesArca.FacturaB(), _escenario.Abierto);

        Assert.Equal(
            AutorizadorWsfe.MotivoCertificado, Assert.IsType<ResultadoAutorizacion.NoEnviada>(resultado).Motivo);
        Assert.Empty(_escenario.Manejador.Pedidos);
        Assert.Contains(_escenario.Registro.Entradas, entrada => entrada.Nivel == LogLevel.Error);
        Assert.DoesNotContain(_escenario.Registro.Entradas, entrada =>
            entrada.Texto.Contains(ruta, StringComparison.Ordinal) || entrada.Excepcion is not null);
    }
}
