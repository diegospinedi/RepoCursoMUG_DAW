using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Optica.Api.Controllers;
using Optica.Application.Catalogo;
using Optica.Application.Configuracion;
using Optica.Infrastructure.Persistencia;

namespace Optica.Api.Tests.Configuracion;

/// <summary>
/// <c>GET</c> y <c>POST /api/configuracion</c>: con sesión, la Api traduce los casos de uso de configuración; sin
/// sesión, 401; el cuerpo exige JSON (415) y hasta 1 KB (413); un cuerpo ilegible da 400 con un texto fijo.
/// </summary>
public sealed class ConfiguracionApiTests
{
    private const string Ruta = "/api/configuracion";
    private const string AccionCuerpoIlegible = "No se pudieron leer los valores enviados. Revisalos y grabá de nuevo.";

    /// <summary>Con margen 50 da 92.233.720.368.547.758, justo bajo el importe máximo; con múltiplo 50 lo supera.</summary>
    private const decimal CostoAlBorde = 61_489_146_912_365_172m;

    [Fact]
    public async Task Get_ConSesion_DevuelveValoresIniciales()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);

        var respuesta = await Http.GetAsync(cliente, Ruta, cookie);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        AfirmarConfiguracion(await Http.JsonAsync(respuesta), 21m, "responsable_inscripto", 0.01m, null);
    }

    [Fact]
    public async Task Post_TopeNuevo_GetPosteriorLoDevuelve()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);

        var respuesta = await GrabarAsync(cliente, Cuerpo(21m, "responsable_inscripto", 0.01m, 250000m), cookie);

        // La respuesta es lo leído después de grabar (D-03), con la forma del GET más la cantidad (D-23 de FEAT-002c):
        // solo cambió el tope, que no recalcula (D-07).
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        AfirmarConfiguracion(
            await Http.JsonAsync(respuesta), 21m, "responsable_inscripto", 0.01m, 250000m, preciosCambiados: 0);
        var posterior = await Http.GetAsync(cliente, Ruta, cookie);
        AfirmarConfiguracion(await Http.JsonAsync(posterior), 21m, "responsable_inscripto", 0.01m, 250000m);
    }

    [Fact]
    public async Task Post_Monotributo_GetPosteriorLoDevuelve()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);

        var respuesta = await GrabarAsync(cliente, Cuerpo(10.5m, "monotributo", 10m, null), cookie);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        AfirmarConfiguracion(await Http.JsonAsync(respuesta), 10.5m, "monotributo", 10m, null, preciosCambiados: 0);
        var posterior = await Http.GetAsync(cliente, Ruta, cookie);
        AfirmarConfiguracion(await Http.JsonAsync(posterior), 10.5m, "monotributo", 10m, null);
    }

    [Fact]
    public async Task Post_Alicuota101_Rechazado400EnElCampo()
    {
        await AfirmarRechazoEnUnCampoAsync(
            Cuerpo(101m, "responsable_inscripto", 0.01m, null),
            ValidacionConfiguracion.CampoAlicuotaIva,
            ValidacionConfiguracion.AccionAlicuotaFueraDeRango);
    }

    [Fact]
    public async Task Post_Multiplo0coma001_Rechazado400EnElCampo()
    {
        await AfirmarRechazoEnUnCampoAsync(
            Cuerpo(21m, "responsable_inscripto", 0.001m, null),
            ValidacionConfiguracion.CampoMultiploRedondeo,
            ValidacionConfiguracion.AccionMultiploMinimo);
    }

    [Fact]
    public async Task Post_Tope0_Rechazado400EnElCampo()
    {
        await AfirmarRechazoEnUnCampoAsync(
            Cuerpo(21m, "responsable_inscripto", 0.01m, 0m),
            ValidacionConfiguracion.CampoTopeIdentificacion,
            ValidacionConfiguracion.AccionTopeNoPositivo);
    }

    [Fact]
    public async Task Post_VariosInvalidos_400ConTodosLosCampos()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);

        var respuesta = await GrabarAsync(cliente, Cuerpo(101m, "otro", 0.01m, 0m), cookie);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        var errores = await ErroresAsync(respuesta);
        Assert.Equal(
            new Dictionary<string, string[]>
            {
                [ValidacionConfiguracion.CampoAlicuotaIva] = [ValidacionConfiguracion.AccionAlicuotaFueraDeRango],
                [ValidacionConfiguracion.CampoCondicionFiscal] = [ValidacionConfiguracion.AccionCondicionFiscal],
                [ValidacionConfiguracion.CampoTopeIdentificacion] = [ValidacionConfiguracion.AccionTopeNoPositivo],
            },
            errores);
        await AfirmarSinCambiosAsync(cliente, cookie);
    }

    [Fact]
    public async Task GetYPost_SinSesion_NoAutorizado401()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        // Con una contraseña definida, para que el 401 no se deba a que todavía no hay acceso.
        await Http.DefinirAsync(cliente);

        var pedidos = new List<HttpResponseMessage>
        {
            await Http.GetAsync(cliente, Ruta),
            await GrabarAsync(cliente, Cuerpo(10.5m, "monotributo", 10m, 1m), cookie: null),
            // Sin sesión, 401 antes del 415 y del 400: el cuerpo no se mira.
            await Http.EnviarAsync(
                cliente, HttpMethod.Post, Ruta, new StringContent("x", Encoding.UTF8, "text/plain")),
            await GrabarAsync(cliente, "esto no es json", cookie: null),
        };

        foreach (var respuesta in pedidos)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
            Assert.Null(respuesta.Headers.Location);
            Assert.Equal(string.Empty, await respuesta.Content.ReadAsStringAsync());
        }

        // El endpoint existe: un 401 también lo daría una ruta inexistente.
        Acceso.ProteccionTests.AfirmarQueElEndpointExiste(fabrica, Ruta);
        var cookie = await IngresarAsync(cliente);
        await AfirmarSinCambiosAsync(cliente, cookie);
    }

    [Fact]
    public async Task Post_SinContentTypeJson_Rechazado415()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);

        var respuesta = await Http.EnviarAsync(
            cliente,
            HttpMethod.Post,
            Ruta,
            new StringContent(Cuerpo(10.5m, "monotributo", 10m, 1m), Encoding.UTF8, "text/plain"),
            cookie);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, respuesta.StatusCode);
        Acceso.ProteccionTests.AfirmarQueElEndpointExiste(fabrica, Ruta);
        await AfirmarSinCambiosAsync(cliente, cookie);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Post_CuerpoGrande_Rechazado413(bool conLargo)
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        // Un cuerpo válido de 2 KB: solo el tamaño lo rechaza.
        var cuerpo = "{\"alicuotaIva\":10.5,\"condicionFiscal\":\"monotributo\",\"multiploRedondeo\":10,"
            + "\"topeIdentificacion\":1,\"relleno\":\"" + new string('a', 2048) + "\"}";
        Assert.True(Encoding.UTF8.GetByteCount(cuerpo) > 2048);

        HttpContent contenido = conLargo
            ? new StringContent(cuerpo, Encoding.UTF8, "application/json")
            : new Http.ContenidoSinLargo(cuerpo, "application/json");
        var respuesta = await Http.EnviarAsync(cliente, HttpMethod.Post, Ruta, contenido, cookie);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, respuesta.StatusCode);
        await AfirmarSinCambiosAsync(cliente, cookie);
    }

    [Fact]
    public async Task Post_CuerpoChicoValido_SeAceptaBajoElLimite()
    {
        // Control del 413: el mismo cuerpo sin relleno, de menos de 1 KB, se acepta.
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var cuerpo = "{\"alicuotaIva\":10.5,\"condicionFiscal\":\"monotributo\",\"multiploRedondeo\":10,"
            + "\"topeIdentificacion\":1,\"relleno\":\"" + new string('a', 800) + "\"}";
        Assert.True(Encoding.UTF8.GetByteCount(cuerpo) < 1024);

        var respuesta = await GrabarAsync(cliente, cuerpo, cookie);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
    }

    [Theory]
    [InlineData("esto no es json", ValidacionConfiguracion.CampoAlicuotaIva)]
    [InlineData("[1,2,3]", ValidacionConfiguracion.CampoAlicuotaIva)]
    [InlineData(
        "{\"alicuotaIva\":\"Zq7-marcado\",\"condicionFiscal\":\"monotributo\",\"multiploRedondeo\":10,\"topeIdentificacion\":null}",
        ValidacionConfiguracion.CampoAlicuotaIva)]
    [InlineData(
        "{\"alicuotaIva\":\"21\",\"condicionFiscal\":\"monotributo\",\"multiploRedondeo\":10,\"topeIdentificacion\":null}",
        ValidacionConfiguracion.CampoAlicuotaIva)]
    [InlineData(
        "{\"alicuotaIva\":21,\"condicionFiscal\":7,\"multiploRedondeo\":10,\"topeIdentificacion\":null}",
        ValidacionConfiguracion.CampoCondicionFiscal)]
    [InlineData(
        "{\"alicuotaIva\":21,\"condicionFiscal\":\"Zq7-marcado",
        ValidacionConfiguracion.CampoCondicionFiscal)]
    [InlineData(
        "{\"alicuotaIva\":21,\"condicionFiscal\":\"monotributo\",\"multiploRedondeo\":\"0,01\",\"topeIdentificacion\":null}",
        ValidacionConfiguracion.CampoMultiploRedondeo)]
    [InlineData(
        "{\"alicuotaIva\":21,\"condicionFiscal\":\"monotributo\",\"multiploRedondeo\":10,\"topeIdentificacion\":1e30}",
        ValidacionConfiguracion.CampoTopeIdentificacion)]
    [InlineData(
        "{\"alicuotaIva\":21,\"condicionFiscal\":\"monotributo\",\"multiploRedondeo\":10,\"topeIdentificacion\":[1]}",
        ValidacionConfiguracion.CampoTopeIdentificacion)]
    public async Task Post_CuerpoIlegible_Invalido400SinDetallesInternos(string cuerpo, string campo)
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);

        var respuesta = await GrabarAsync(cliente, cuerpo, cookie);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        var errores = await ErroresAsync(respuesta);
        Assert.Equal(new Dictionary<string, string[]> { [campo] = [AccionCuerpoIlegible] }, errores);
        // Ni los mensajes internos del lector de JSON ni lo enviado.
        var texto = await respuesta.Content.ReadAsStringAsync();
        Assert.DoesNotContain("JSON", texto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LineNumber", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("System.", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("Zq7-marcado", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("$.", texto, StringComparison.Ordinal);
        await AfirmarSinCambiosAsync(cliente, cookie);
    }

    // ---- FEAT-002c: recálculo del catálogo (D-01, D-06, D-09, D-23) ----

    [Fact]
    public async Task Post_Multiplo50_200ConPreciosCambiados()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        // 1210 con margen 50 da 1815,00; 1000 con margen 50 da 1500,00, que ya es múltiplo de 50.
        var cambia = await GrabarArticuloAsync(cliente, cookie, "ABC-1", 1210m);
        var noCambia = await GrabarArticuloAsync(cliente, cookie, "ABC-2", 1000m);

        var respuesta = await GrabarAsync(cliente, Cuerpo(21m, "responsable_inscripto", 50m, null), cookie);

        // AC-02: la configuración vigente más la cantidad de precios que cambiaron (D-09, D-23).
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var json = await Http.JsonAsync(respuesta);
        Assert.Equal(
            ["alicuotaIva", "condicionFiscal", "multiploRedondeo", "topeIdentificacion", "faltaTope", "preciosCambiados"],
            json.EnumerateObject().Select(propiedad => propiedad.Name).ToArray());
        Assert.Equal(1, json.GetProperty("preciosCambiados").GetInt32());
        Assert.Equal(50m, json.GetProperty("multiploRedondeo").GetDecimal());
        await AfirmarArticuloAsync(cliente, cookie, cambia, 1850m, version: 1);
        await AfirmarArticuloAsync(cliente, cookie, noCambia, 1500m, version: 0);
    }

    [Fact]
    public async Task Get_Configuracion_SinCambiosEnElContrato()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        await GrabarArticuloAsync(cliente, cookie, "ABC-1", 1210m);
        var grabacion = await GrabarAsync(cliente, Cuerpo(10.5m, "monotributo", 50m, 250000m), cookie);
        var grabada = await Http.JsonAsync(grabacion);

        var respuesta = await Http.GetAsync(cliente, Ruta, cookie);

        // El GET responde lo mismo que antes de FEAT-002c: sin preciosCambiados, que es solo del POST (D-23).
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var json = await Http.JsonAsync(respuesta);
        AfirmarConfiguracion(json, 10.5m, "monotributo", 50m, 250000m);
        Assert.Equal(HttpStatusCode.OK, grabacion.StatusCode);
        Assert.True(grabada.TryGetProperty("preciosCambiados", out _));
        var grabadaSinCantidad = grabada.EnumerateObject()
            .Where(propiedad => propiedad.Name != "preciosCambiados")
            .Select(propiedad => $"{propiedad.Name}={propiedad.Value.GetRawText()}");
        Assert.Equal(
            grabadaSinCantidad,
            json.EnumerateObject().Select(propiedad => $"{propiedad.Name}={propiedad.Value.GetRawText()}"));
    }

    [Fact]
    public async Task Post_RespuestaEsLaRelecturaYNoLoEnviado()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        // Valores válidos con ceros a la derecha: lo enviado (21.0, 0.010) se escribe distinto de lo releído.
        const string cuerpo =
            "{\"alicuotaIva\":21.0,\"condicionFiscal\":\"monotributo\",\"multiploRedondeo\":0.010,\"topeIdentificacion\":null}";

        var respuesta = await GrabarAsync(cliente, cuerpo, cookie);

        // D-23: la respuesta se arma con la lectura posterior al commit, igual que el GET, no con lo enviado.
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var grabada = await Http.JsonAsync(respuesta);
        var leida = await Http.JsonAsync(await Http.GetAsync(cliente, Ruta, cookie));
        foreach (var clave in new[] { "alicuotaIva", "multiploRedondeo" })
        {
            Assert.Equal(leida.GetProperty(clave).GetRawText(), grabada.GetProperty(clave).GetRawText());
        }

        Assert.Equal("0.01", grabada.GetProperty("multiploRedondeo").GetRawText());
    }

    [Fact]
    public async Task Post_PrecioFueraDeRango_400EnMultiplo()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var normal = await GrabarArticuloAsync(cliente, cookie, "ABC-1", 1210m);
        // Con margen 50 da justo bajo el importe máximo; con múltiplo 50 lo supera.
        var alBorde = await GrabarArticuloAsync(cliente, cookie, "Zq7-proveedor", CostoAlBorde);

        var respuesta = await GrabarAsync(cliente, Cuerpo(21m, "responsable_inscripto", 50m, null), cookie);

        // AC-04 y D-01: 400 con el error en el múltiplo y el código autonumérico, sin el código en el proveedor (D-12).
        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            new Dictionary<string, string[]>
            {
                [ValidacionConfiguracion.CampoMultiploRedondeo] =
                [
                    $"No se grabó el cambio: el precio de venta de 1 artículo sería demasiado grande (código {alBorde}). " +
                    "Revisá esos artículos o el múltiplo.",
                ],
            },
            await ErroresAsync(respuesta));
        Assert.DoesNotContain("Zq7", await respuesta.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        // Nada grabado: ni la configuración ni los precios (FR-02).
        await AfirmarSinCambiosAsync(cliente, cookie);
        await AfirmarArticuloAsync(cliente, cookie, normal, 1815m, version: 0);
    }

    /// <summary>
    /// Alerta de datos (D-28 de FEAT-002b): el <c>ToString()</c> de un record muestra todos sus valores; la respuesta
    /// de la grabación no muestra ninguno, por si llega a un log.
    /// </summary>
    [Fact]
    public void ConfiguracionGrabada_ToString_NoMuestraDatos()
    {
        var texto = ConfiguracionGrabada.Desde(
            new ConfiguracionVigente(17.35m, "monotributo", 43.21m, 765432.1m, false), 98765).ToString();

        Assert.Equal("ConfiguracionGrabada { }", texto);
    }

    /// <summary>
    /// Alerta de datos (D-28 de FEAT-002c): MVC registra en Trace los argumentos de la acción con su <c>ToString()</c>;
    /// el cuerpo del POST no muestra la configuración comercial.
    /// </summary>
    [Fact]
    public void SolicitudConfiguracion_ToString_NoMuestraDatos()
    {
        var texto = new SolicitudConfiguracion(17.35m, "Zq7-condicion", 43.21m, 765_432.1m).ToString();

        Assert.Equal("SolicitudConfiguracion { }", texto);
        Assert.DoesNotContain("17.35", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("Zq7", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("43.21", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("765432", texto, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_SinFila_Error500Generico()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        using (var alcance = fabrica.Services.CreateScope())
        {
            // Base alterada a mano: la fila no está (D-04).
            var contexto = alcance.ServiceProvider.GetRequiredService<OpticaDbContext>();
            Assert.Equal(1, await contexto.ConfiguracionesNegocio.ExecuteDeleteAsync());
        }

        var lectura = await Http.GetAsync(cliente, Ruta, cookie);
        var grabacion = await GrabarAsync(cliente, Cuerpo(10.5m, "monotributo", 10m, 1m), cookie);

        foreach (var respuesta in new[] { lectura, grabacion })
        {
            Assert.Equal(HttpStatusCode.InternalServerError, respuesta.StatusCode);
            Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
            var json = await Http.JsonAsync(respuesta);
            Assert.Equal("No se pudo completar el pedido.", json.GetProperty("title").GetString());
            var texto = json.GetRawText();
            Assert.DoesNotContain("configuración de negocio", texto, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("InvalidOperationException", texto, StringComparison.Ordinal);
            Assert.DoesNotContain(" at ", texto, StringComparison.Ordinal);
        }

        // No se recreó en silencio.
        using var despues = fabrica.Services.CreateScope();
        Assert.Equal(
            0, await despues.ServiceProvider.GetRequiredService<OpticaDbContext>().ConfiguracionesNegocio.CountAsync());
    }

    /// <summary>
    /// R-04 de FEAT-001c y D-17 de FEAT-002c: el registro de cada grabación (categoría
    /// <c>Optica.Application.Configuracion.Grabacion</c>, la constante del caso de uso, nivel Information) va al
    /// Registro de eventos de Windows por la clave <c>Optica.Application.Configuracion</c>. El host de Windows deja el
    /// proveedor <c>EventLog</c> en Warning con un filtro propio; appsettings.json tiene que subir esa clave a
    /// Information sin cambiar ese valor por defecto para las demás. La prueba aplica la sección <c>Logging</c> del
    /// archivo versionado con el mismo filtro del host sobre un proveedor con el alias <c>EventLog</c>, porque el real
    /// solo existe en Windows.
    /// </summary>
    [Fact]
    public void Appsettings_GrabacionDeConfiguracion_VaAlRegistroDeEventos()
    {
        // D-17: la categoría pedida es esta; si cambia de prefijo, dejaría de llegar al Registro de eventos.
        Assert.Equal("Optica.Application.Configuracion.Grabacion", GrabarConfiguracionConRecalculo.CategoriaRegistro);

        var archivo = Path.Combine(Repositorio.Raiz(), "src", "Optica.Api", "appsettings.json");
        var configuracion = new ConfigurationBuilder().AddJsonFile(archivo, optional: false).Build();
        var registroDeEventos = new RegistroDeEventosSimulado();
        using (var servicios = new ServiceCollection()
            .AddLogging(log =>
            {
                // Como HostingHostBuilderExtensions en Windows: EventLog en Warning, luego la configuración.
                log.AddFilter<RegistroDeEventosSimulado>(nivel => nivel >= LogLevel.Warning);
                log.AddConfiguration(configuracion.GetSection("Logging"));
                log.AddProvider(registroDeEventos);
            })
            .BuildServiceProvider())
        {
            var fabricaRegistros = servicios.GetRequiredService<ILoggerFactory>();
            fabricaRegistros.CreateLogger(GrabarConfiguracionConRecalculo.CategoriaRegistro)
                .LogInformation("grabacion-de-configuracion");
            fabricaRegistros.CreateLogger("Optica.Prueba").LogInformation("otra-categoria-information");
            fabricaRegistros.CreateLogger("Optica.Prueba").LogWarning("otra-categoria-warning");
        }

        Assert.Contains("grabacion-de-configuracion", registroDeEventos.Mensajes);
        // Las demás categorías siguen con el valor por defecto del Registro de eventos.
        Assert.DoesNotContain("otra-categoria-information", registroDeEventos.Mensajes);
        Assert.Contains("otra-categoria-warning", registroDeEventos.Mensajes);
        Assert.Equal(
            "Information",
            configuracion["Logging:EventLog:LogLevel:Optica.Application.Configuracion"]);
        Assert.Null(configuracion["Logging:EventLog:LogLevel:Default"]);
    }

    private static string Cuerpo(decimal? alicuotaIva, string? condicionFiscal, decimal? multiploRedondeo, decimal? topeIdentificacion) =>
        JsonSerializer.Serialize(new { alicuotaIva, condicionFiscal, multiploRedondeo, topeIdentificacion });

    private static Task<HttpResponseMessage> GrabarAsync(HttpClient cliente, string cuerpo, string? cookie) =>
        Http.PostJsonAsync(cliente, Ruta, cuerpo, cookie);

    /// <summary>Graba por la Api un artículo activo con margen 50 y devuelve su código.</summary>
    private static async Task<long> GrabarArticuloAsync(HttpClient cliente, string cookie, string codigoProveedor, decimal costo)
    {
        var cuerpo = JsonSerializer.Serialize(
            new { codigoProveedor, descripcion = "Armazón", precioCosto = costo, margenUtilidad = 50m });
        var respuesta = await Http.PostJsonAsync(cliente, "/api/articulos", cuerpo, cookie);
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await Http.JsonAsync(respuesta)).GetProperty("codigo").GetInt64();
    }

    /// <summary>El precio de venta y la versión que la Api tiene grabados para el artículo.</summary>
    private static async Task AfirmarArticuloAsync(
        HttpClient cliente, string cookie, long codigo, decimal precioVenta, long version)
    {
        var respuesta = await Http.GetAsync(cliente, $"/api/articulos/{codigo}", cookie);
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var json = await Http.JsonAsync(respuesta);
        Assert.Equal(precioVenta, json.GetProperty("precioVenta").GetDecimal());
        Assert.Equal(version, json.GetProperty("version").GetInt64());
    }

    private static async Task<string> IngresarAsync(HttpClient cliente)
    {
        var respuesta = await Http.PostJsonAsync(cliente, "/api/acceso/ingresar", Http.CuerpoContrasena(Http.Contrasena));
        Assert.Equal(HttpStatusCode.NoContent, respuesta.StatusCode);
        return Http.Cookie(respuesta) ?? throw new InvalidOperationException("Ingresar no emitió la cookie de sesión.");
    }

    private static async Task AfirmarRechazoEnUnCampoAsync(string cuerpo, string campo, string accion)
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);

        var respuesta = await GrabarAsync(cliente, cuerpo, cookie);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        Assert.Equal(new Dictionary<string, string[]> { [campo] = [accion] }, await ErroresAsync(respuesta));
        await AfirmarSinCambiosAsync(cliente, cookie);
    }

    /// <summary>La configuración sigue con los valores iniciales: el pedido rechazado no grabó nada.</summary>
    private static async Task AfirmarSinCambiosAsync(HttpClient cliente, string cookie)
    {
        var respuesta = await Http.GetAsync(cliente, Ruta, cookie);
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        AfirmarConfiguracion(await Http.JsonAsync(respuesta), 21m, "responsable_inscripto", 0.01m, null);
    }

    /// <summary>
    /// La configuración con la forma del GET; con <paramref name="preciosCambiados"/>, la del POST, que agrega esa clave
    /// al final (D-23 de FEAT-002c).
    /// </summary>
    private static void AfirmarConfiguracion(
        JsonElement json,
        decimal alicuotaIva,
        string condicionFiscal,
        decimal multiploRedondeo,
        decimal? topeIdentificacion,
        int? preciosCambiados = null)
    {
        string[] claves = ["alicuotaIva", "condicionFiscal", "multiploRedondeo", "topeIdentificacion", "faltaTope"];
        Assert.Equal(
            preciosCambiados is null ? claves : [.. claves, "preciosCambiados"],
            json.EnumerateObject().Select(propiedad => propiedad.Name).ToArray());
        if (preciosCambiados is not null)
        {
            Assert.Equal(preciosCambiados.Value, json.GetProperty("preciosCambiados").GetInt32());
        }

        Assert.Equal(alicuotaIva, json.GetProperty("alicuotaIva").GetDecimal());
        Assert.Equal(condicionFiscal, json.GetProperty("condicionFiscal").GetString());
        Assert.Equal(multiploRedondeo, json.GetProperty("multiploRedondeo").GetDecimal());
        var tope = json.GetProperty("topeIdentificacion");
        if (topeIdentificacion is null)
        {
            Assert.Equal(JsonValueKind.Null, tope.ValueKind);
        }
        else
        {
            Assert.Equal(topeIdentificacion.Value, tope.GetDecimal());
        }

        Assert.Equal(topeIdentificacion is null, json.GetProperty("faltaTope").GetBoolean());
    }

    private static async Task<Dictionary<string, string[]>> ErroresAsync(HttpResponseMessage respuesta)
    {
        var json = await Http.JsonAsync(respuesta);
        return json.GetProperty("errors").EnumerateObject().ToDictionary(
            campo => campo.Name,
            campo => campo.Value.EnumerateArray().Select(valor => valor.GetString()!).ToArray());
    }

    /// <summary>Proveedor con el alias del Registro de eventos: la configuración <c>Logging:EventLog</c> lo alcanza.</summary>
    [ProviderAlias("EventLog")]
    private sealed class RegistroDeEventosSimulado : ILoggerProvider
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _mensajes = new();

        public IReadOnlyList<string> Mensajes => _mensajes.ToArray();

        public ILogger CreateLogger(string categoryName) => new Registrador(_mensajes);

        public void Dispose()
        {
        }

        private sealed class Registrador(System.Collections.Concurrent.ConcurrentQueue<string> mensajes) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                mensajes.Enqueue(formatter(state, exception));
        }
    }
}
