using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Optica.Api.Acceso;

namespace Optica.Api.Tests.Acceso;

public sealed class ProteccionTests
{
    [Fact]
    public async Task EndpointsProtegidos_SinSesion_NoAutorizado401()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();

        // Lo realmente registrado en la Api, no una lista escrita a mano.
        var endpoints = EndpointsRegistrados(fabrica);

        // Si la enumeración no encontrara las rutas protegidas conocidas, la prueba no estaría probando nada.
        Assert.Contains((HttpMethods.Get, "/api/sesion"), endpoints);
        Assert.Contains((HttpMethods.Post, "/api/acceso/salir"), endpoints);
        // Los artículos traen costos y márgenes (R-01): la ruta con parámetro también se pide, no se excluye.
        Assert.Contains((HttpMethods.Post, "/api/articulos"), endpoints);
        Assert.Contains((HttpMethods.Get, "/api/articulos/{codigo:long}"), endpoints);

        var protegidos = endpoints.Where(e => !RutasAnonimas.Contiene(e.Metodo, e.Ruta)).ToList();
        Assert.NotEmpty(protegidos);
        var pedidas = new List<string>();
        foreach (var (metodo, patron) in protegidos)
        {
            // Cada parámetro de ruta se pide con un valor de ejemplo que cumple cualquier restricción en uso.
            var ruta = ConValoresDeEjemplo(patron);
            Assert.DoesNotContain('{', ruta);
            pedidas.Add($"{metodo} {ruta}");
            var contenido = metodo == HttpMethods.Get
                ? null
                : new StringContent("{}", Encoding.UTF8, "application/json");
            var respuesta = await Http.EnviarAsync(cliente, new HttpMethod(metodo), ruta, contenido);

            Assert.True(
                respuesta.StatusCode == HttpStatusCode.Unauthorized,
                $"{metodo} {ruta} respondió {(int)respuesta.StatusCode} sin sesión; se esperaba 401.");
            Assert.Null(respuesta.Headers.Location);
            Assert.Equal(string.Empty, await respuesta.Content.ReadAsStringAsync());
        }

        Assert.Contains("GET /api/articulos/1", pedidas);
        Assert.Contains("GET /api/articulos", pedidas);
        Assert.Contains("PUT /api/articulos/1", pedidas);
        Assert.Contains("PUT /api/articulos/1/estado", pedidas);
    }

    /// <summary>Método y ruta de cada endpoint registrado en la Api.</summary>
    private static List<(string Metodo, string Ruta)> EndpointsRegistrados(FabricaApi fabrica) =>
        fabrica.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(endpoint =>
            {
                var ruta = "/" + (endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/');
                var metodos = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods
                    ?? [HttpMethods.Get, HttpMethods.Post, HttpMethods.Put];
                return metodos.Select(metodo => (Metodo: metodo, Ruta: ruta));
            })
            .Distinct()
            .ToList();

    private const string RutaArticulos = "/api/articulos";

    /// <summary>
    /// Las tres rutas de FEAT-002b y las dos de presupuestos de FEAT-003a exigen sesión (R-01, D-17): están
    /// registradas, no figuran en <see cref="RutasAnonimas"/> y, sin sesión, responden 401 sin cuerpo antes del 415 y del
    /// 413 (el Content-Type y el tamaño no se miran).
    /// </summary>
    [Fact]
    public async Task RutasNuevas_SinSesion_401()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var (codigo, _) = await GrabarArticuloAsync(cliente, cookie);

        var endpoints = EndpointsRegistrados(fabrica);
        var nuevas = new[]
        {
            (HttpMethods.Get, "/api/articulos"),
            (HttpMethods.Put, "/api/articulos/{codigo:long}"),
            (HttpMethods.Put, "/api/articulos/{codigo:long}/estado"),
            // FEAT-003a: el alta y la consulta del presupuesto llevan datos personales (D-17).
            (HttpMethods.Post, RutaPresupuestos),
            (HttpMethods.Get, RutaPresupuestos + "/{numero:long}"),
            // FEAT-003c: la modificación y el paso a Final también llevan datos personales.
            (HttpMethods.Put, RutaPresupuestos + "/{numero:long}"),
            (HttpMethods.Put, RutaPresupuestos + "/{numero:long}/estado"),
            // FEAT-006: la búsqueda lleva Apellido, Nombre y DNI en el cuerpo y en la respuesta (ADR-019, R-03).
            (HttpMethods.Post, RutaBusqueda),
        };
        foreach (var (metodo, patron) in nuevas)
        {
            Assert.Contains((metodo, patron), endpoints);
            Assert.False(RutasAnonimas.Contiene(metodo, patron), $"{metodo} {patron} no puede ser anónima.");
        }

        var datos = $"{RutaArticulos}/{codigo}";
        var estado = $"{RutaArticulos}/{codigo}/estado";
        var cuerpoDatos = CuerpoDatos(version: 0);
        var pedidos = new List<(string Descripcion, Task<HttpResponseMessage> Pedido)>
        {
            ("GET listado", Http.GetAsync(cliente, $"{RutaArticulos}?texto=armaz&pagina=1")),
            ("PUT datos", EnviarJsonAsync(cliente, HttpMethod.Put, datos, cuerpoDatos)),
            ("PUT estado", EnviarJsonAsync(cliente, HttpMethod.Put, estado, "{\"activo\":false}")),
            ("PUT datos texto plano", Http.EnviarAsync(
                cliente, HttpMethod.Put, datos, new StringContent(cuerpoDatos, Encoding.UTF8, "text/plain"))),
            ("PUT estado texto plano", Http.EnviarAsync(
                cliente, HttpMethod.Put, estado, new StringContent("{\"activo\":false}", Encoding.UTF8, "text/plain"))),
            ("PUT datos grande", EnviarJsonAsync(cliente, HttpMethod.Put, datos, CuerpoDatos(0, new string('a', 2000)))),
            ("PUT estado grande", EnviarJsonAsync(cliente, HttpMethod.Put, estado, CuerpoEstado(false, new string('a', 200)))),
            ("POST presupuesto", EnviarJsonAsync(cliente, HttpMethod.Post, RutaPresupuestos, CuerpoPresupuesto(codigo))),
            ("POST presupuesto texto plano", Http.EnviarAsync(
                cliente,
                HttpMethod.Post,
                RutaPresupuestos,
                new StringContent(CuerpoPresupuesto(codigo), Encoding.UTF8, "text/plain"))),
            ("POST presupuesto grande", EnviarJsonAsync(
                cliente, HttpMethod.Post, RutaPresupuestos, CuerpoPresupuesto(codigo, new string('a', 20_000)))),
            ("GET presupuesto", Http.GetAsync(cliente, $"{RutaPresupuestos}/1")),
            ("PUT presupuesto", EnviarJsonAsync(
                cliente, HttpMethod.Put, $"{RutaPresupuestos}/1", CuerpoModificacionPresupuesto(codigo))),
            ("PUT presupuesto texto plano", Http.EnviarAsync(
                cliente,
                HttpMethod.Put,
                $"{RutaPresupuestos}/1",
                new StringContent(CuerpoModificacionPresupuesto(codigo), Encoding.UTF8, "text/plain"))),
            ("PUT presupuesto grande", EnviarJsonAsync(
                cliente, HttpMethod.Put, $"{RutaPresupuestos}/1", CuerpoPresupuesto(codigo, new string('a', 20_000)))),
            ("PUT estado presupuesto", EnviarJsonAsync(
                cliente, HttpMethod.Put, $"{RutaPresupuestos}/1/estado", "{\"estado\":\"final\",\"version\":0}")),
            ("PUT estado presupuesto texto plano", Http.EnviarAsync(
                cliente,
                HttpMethod.Put,
                $"{RutaPresupuestos}/1/estado",
                new StringContent("{\"estado\":\"final\",\"version\":0}", Encoding.UTF8, "text/plain"))),
            ("POST busqueda", EnviarJsonAsync(cliente, HttpMethod.Post, RutaBusqueda, CuerpoBusqueda)),
            ("POST busqueda texto plano", Http.EnviarAsync(
                cliente, HttpMethod.Post, RutaBusqueda, new StringContent(CuerpoBusqueda, Encoding.UTF8, "text/plain"))),
            ("POST busqueda grande", EnviarJsonAsync(
                cliente, HttpMethod.Post, RutaBusqueda, CuerpoBusquedaDeBytes(LimiteBusqueda + 1))),
        };

        foreach (var (descripcion, pedido) in pedidos)
        {
            var respuesta = await pedido;
            Assert.True(
                respuesta.StatusCode == HttpStatusCode.Unauthorized,
                $"{descripcion} respondió {(int)respuesta.StatusCode} sin sesión; se esperaba 401.");
            Assert.Null(respuesta.Headers.Location);
            Assert.Equal(string.Empty, await respuesta.Content.ReadAsStringAsync());
        }

        // Nada cambió: ni los datos ni el estado ni la versión.
        var leido = await Http.JsonAsync(await Http.GetAsync(cliente, datos, cookie));
        Assert.Equal("Armazón", leido.GetProperty("descripcion").GetString());
        Assert.True(leido.GetProperty("activo").GetBoolean());
        Assert.Equal(0, leido.GetProperty("version").GetInt64());
        // Ni se grabó un presupuesto: el GET con sesión responde el 404 de la acción.
        await AfirmarSinPresupuestoAsync(cliente, cookie);
    }

    private const string RutaPresupuestos = "/api/presupuestos";

    // FEAT-006: la búsqueda de presupuestos (ADR-019).

    private const string RutaBusqueda = RutaPresupuestos + "/busqueda";

    /// <summary>El límite del cuerpo de la búsqueda: 4 KB (<c>[RequestSizeLimit]</c>).</summary>
    private const int LimiteBusqueda = 4 * 1024;

    /// <summary>Una búsqueda válida con datos ficticios.</summary>
    private const string CuerpoBusqueda = "{\"apellido\":\"Ficticio\",\"dni\":\"99.999\",\"pagina\":1}";

    /// <summary>
    /// Sad path (R-03): sin sesión, <c>POST /api/presupuestos/busqueda</c> responde 401 sin cuerpo, antes de leer el
    /// cuerpo y antes del 415 y del 413; la ruta existe y no es anónima.
    /// </summary>
    [Fact]
    public async Task PostBusqueda_SinSesion_401NoAutorizado()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        AfirmarQueElEndpointExiste(fabrica, RutaBusqueda);
        Assert.False(RutasAnonimas.Contiene(HttpMethods.Post, RutaBusqueda));

        foreach (var contenido in new HttpContent?[]
        {
            new StringContent(CuerpoBusqueda, Encoding.UTF8, "application/json"),
            new StringContent("esto no es json", Encoding.UTF8, "application/json"),
            new StringContent(CuerpoBusqueda, Encoding.UTF8, "text/plain"),
            new StringContent(CuerpoBusquedaDeBytes(LimiteBusqueda + 1), Encoding.UTF8, "application/json"),
            null,
        })
        {
            var respuesta = await Http.EnviarAsync(cliente, HttpMethod.Post, RutaBusqueda, contenido);

            Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
            Assert.Null(respuesta.Headers.Location);
            Assert.Equal(string.Empty, await respuesta.Content.ReadAsStringAsync());
        }
    }

    /// <summary>
    /// La búsqueda exige <c>Content-Type: application/json</c> (<see cref="LimitesDelCuerpo"/>): texto plano, ningún tipo,
    /// o ningún tipo y ningún cuerpo, 415 sin cuerpo.
    /// </summary>
    [Theory]
    [InlineData("textoPlano")]
    [InlineData("sinTipo")]
    [InlineData("sinTipoNiCuerpo")]
    public async Task PostBusqueda_SinJson_415(string variante)
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);

        HttpContent contenido = variante switch
        {
            "textoPlano" => new StringContent(CuerpoBusqueda, Encoding.UTF8, "text/plain"),
            "sinTipo" => new ByteArrayContent(Encoding.UTF8.GetBytes(CuerpoBusqueda)),
            _ => new ByteArrayContent([]),
        };
        var respuesta = await Http.EnviarAsync(cliente, HttpMethod.Post, RutaBusqueda, contenido, cookie);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, respuesta.StatusCode);
        Assert.Equal(string.Empty, await respuesta.Content.ReadAsStringAsync());
        AfirmarQueElEndpointExiste(fabrica, RutaBusqueda);
    }

    /// <summary>
    /// La búsqueda acepta hasta 4 KB (<c>[RequestSizeLimit]</c>): un byte más, 413, con <c>Content-Length</c> y sin él (en
    /// partes); en el límite exacto, el pedido llega a la acción y responde 200.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PostBusqueda_DemasiadoGrande_413(bool conLargo)
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);

        var rechazo = await EnviarBusquedaAsync(cliente, CuerpoBusquedaDeBytes(LimiteBusqueda + 1), conLargo, cookie);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, rechazo.StatusCode);
        var texto = await rechazo.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Ficticio", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("99.999", texto, StringComparison.Ordinal);

        // Control del 413: el mismo cuerpo en el límite exacto se acepta.
        var aceptado = await EnviarBusquedaAsync(cliente, CuerpoBusquedaDeBytes(LimiteBusqueda), conLargo, cookie);
        Assert.Equal(HttpStatusCode.OK, aceptado.StatusCode);
    }

    /// <summary><see cref="CuerpoBusqueda"/> con una propiedad que se ignora, hasta ocupar <paramref name="bytes"/>.</summary>
    private static string CuerpoBusquedaDeBytes(int bytes)
    {
        var sinRelleno = CuerpoBusqueda[..^1] + ",\"relleno\":\"\"}";
        var faltan = bytes - Encoding.UTF8.GetByteCount(sinRelleno);
        Assert.True(faltan >= 0, "El cuerpo sin relleno ya es más largo que el pedido.");
        var cuerpo = CuerpoBusqueda[..^1] + ",\"relleno\":\"" + new string('a', faltan) + "\"}";
        Assert.Equal(bytes, Encoding.UTF8.GetByteCount(cuerpo));
        return cuerpo;
    }

    private static Task<HttpResponseMessage> EnviarBusquedaAsync(
        HttpClient cliente, string cuerpo, bool conLargo, string cookie)
    {
        HttpContent contenido = conLargo
            ? new StringContent(cuerpo, Encoding.UTF8, "application/json")
            : new Http.ContenidoSinLargo(cuerpo, "application/json");
        return Http.EnviarAsync(cliente, HttpMethod.Post, RutaBusqueda, contenido, cookie);
    }

    /// <summary>
    /// <c>POST /api/presupuestos</c> exige <c>Content-Type: application/json</c> (R-02, D-16): texto plano, ningún tipo,
    /// o ningún tipo y ningún cuerpo, 415 sin cuerpo y sin grabar el presupuesto.
    /// </summary>
    [Theory]
    [InlineData("textoPlano")]
    [InlineData("sinTipo")]
    [InlineData("sinTipoNiCuerpo")]
    public async Task Post_SinJson_415(string variante)
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var (codigo, _) = await GrabarArticuloAsync(cliente, cookie);
        var cuerpo = CuerpoPresupuesto(codigo);

        HttpContent contenido = variante switch
        {
            "textoPlano" => new StringContent(cuerpo, Encoding.UTF8, "text/plain"),
            "sinTipo" => new ByteArrayContent(Encoding.UTF8.GetBytes(cuerpo)),
            _ => new ByteArrayContent([]),
        };
        var respuesta = await Http.EnviarAsync(cliente, HttpMethod.Post, RutaPresupuestos, contenido, cookie);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, respuesta.StatusCode);
        Assert.Equal(string.Empty, await respuesta.Content.ReadAsStringAsync());
        AfirmarQueElEndpointExiste(fabrica, RutaPresupuestos, HttpMethods.Post);
        await AfirmarSinPresupuestoAsync(cliente, cookie);
    }

    /// <summary>
    /// <c>POST /api/presupuestos</c> acepta hasta 16 KB (D-16): un byte más, 413, con <c>Content-Length</c> y sin él (en
    /// partes), sin grabar; en el límite exacto, el pedido llega a la acción y graba.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Post_DemasiadoGrande_413(bool conLargo)
    {
        const int Limite = 16 * 1024;
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var (codigo, _) = await GrabarArticuloAsync(cliente, cookie);

        var rechazo = await EnviarPresupuestoAsync(cliente, CuerpoPresupuestoDeBytes(codigo, Limite + 1), conLargo, cookie);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, rechazo.StatusCode);
        await AfirmarSinPresupuestoAsync(cliente, cookie);

        // Control del 413: el mismo cuerpo en el límite exacto se acepta.
        var aceptado = await EnviarPresupuestoAsync(cliente, CuerpoPresupuestoDeBytes(codigo, Limite), conLargo, cookie);
        Assert.Equal(HttpStatusCode.Created, aceptado.StatusCode);
    }

    /// <summary>
    /// Un alta válida con datos ficticios y una línea; con <paramref name="relleno"/>, una propiedad que se ignora.
    /// </summary>
    private static string CuerpoPresupuesto(long codigo, string? relleno = null) =>
        "{\"cliente\":{\"apellido\":\"Perez\",\"nombre\":\"Ana\",\"dni\":\"30111222\"},\"lineas\":[{\"codigoArticulo\":"
        + codigo + ",\"cantidad\":1,\"porcentajeDescuento\":0}]"
        + (relleno is null ? string.Empty : ",\"relleno\":\"" + relleno + "\"") + "}";

    /// <summary>Una modificación válida del presupuesto 1 (versión 0, la línea 1), con datos ficticios.</summary>
    private static string CuerpoModificacionPresupuesto(long codigo) =>
        "{\"version\":0,\"cliente\":{\"apellido\":\"Perez\",\"nombre\":\"Ana\",\"dni\":\"99999999\"},"
        + "\"lineas\":[{\"orden\":1,\"codigoArticulo\":" + codigo + ",\"cantidad\":2,\"porcentajeDescuento\":0}]}";

    private static string CuerpoPresupuestoDeBytes(long codigo, int bytes)
    {
        var base_ = Encoding.UTF8.GetByteCount(CuerpoPresupuesto(codigo, string.Empty));
        Assert.True(bytes >= base_, "El cuerpo sin relleno ya es más largo que el pedido.");
        var cuerpo = CuerpoPresupuesto(codigo, new string('a', bytes - base_));
        Assert.Equal(bytes, Encoding.UTF8.GetByteCount(cuerpo));
        return cuerpo;
    }

    private static Task<HttpResponseMessage> EnviarPresupuestoAsync(
        HttpClient cliente, string cuerpo, bool conLargo, string cookie)
    {
        HttpContent contenido = conLargo
            ? new StringContent(cuerpo, Encoding.UTF8, "application/json")
            : new Http.ContenidoSinLargo(cuerpo, "application/json");
        return Http.EnviarAsync(cliente, HttpMethod.Post, RutaPresupuestos, contenido, cookie);
    }

    /// <summary>No hay presupuesto 1: el 404 de la acción (con ProblemDetails), no el de una ruta inexistente.</summary>
    private static async Task AfirmarSinPresupuestoAsync(HttpClient cliente, string cookie)
    {
        var respuesta = await Http.GetAsync(cliente, $"{RutaPresupuestos}/1", cookie);
        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>
    /// Los dos PUT exigen <c>Content-Type: application/json</c> (D-11, R-02): texto plano, ningún tipo, o ningún tipo y
    /// ningún cuerpo, 415 sin cuerpo, sin tocar el artículo. El 415 lo da <see cref="LimitesDelCuerpo"/>, no MVC: el de
    /// MVC llevaría un ProblemDetails, y sin cuerpo MVC ni siquiera respondería 415.
    /// </summary>
    [Theory]
    [InlineData("", "textoPlano")]
    [InlineData("", "sinTipo")]
    [InlineData("", "sinTipoNiCuerpo")]
    [InlineData("/estado", "textoPlano")]
    [InlineData("/estado", "sinTipo")]
    [InlineData("/estado", "sinTipoNiCuerpo")]
    public async Task Put_SinJson_415(string sufijo, string variante)
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var (codigo, version) = await GrabarArticuloAsync(cliente, cookie);
        var ruta = $"{RutaArticulos}/{codigo}{sufijo}";
        var cuerpo = sufijo.Length == 0 ? CuerpoDatos(version, descripcion: "Otra") : CuerpoEstado(false);

        // ByteArrayContent no lleva Content-Type.
        HttpContent contenido = variante switch
        {
            "textoPlano" => new StringContent(cuerpo, Encoding.UTF8, "text/plain"),
            "sinTipo" => new ByteArrayContent(Encoding.UTF8.GetBytes(cuerpo)),
            _ => new ByteArrayContent([]),
        };
        Assert.Equal(variante == "textoPlano", contenido.Headers.ContentType is not null);
        var respuesta = await Http.EnviarAsync(cliente, HttpMethod.Put, ruta, contenido, cookie);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, respuesta.StatusCode);
        Assert.Equal(string.Empty, await respuesta.Content.ReadAsStringAsync());
        // El 415 lo da un middleware para todo PUT: sin este control, la fila pasaría aunque la acción no existiera.
        AfirmarQueElEndpointExiste(fabrica, $"{RutaArticulos}/{{codigo:long}}{sufijo}", HttpMethods.Put);
        await AfirmarSinCambiosAsync(cliente, cookie, codigo);
    }

    /// <summary>
    /// El PUT de datos acepta hasta 1024 bytes y el de estado hasta 64 (D-11, R-05): un byte más, 413, con
    /// <c>Content-Length</c> y sin él (en partes); en el límite exacto, el pedido llega a la acción y graba.
    /// </summary>
    [Theory]
    [InlineData("", 1024, true)]
    [InlineData("", 1024, false)]
    [InlineData("/estado", 64, true)]
    [InlineData("/estado", 64, false)]
    public async Task Put_DemasiadoGrande_413(string sufijo, int limite, bool conLargo)
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var (codigo, version) = await GrabarArticuloAsync(cliente, cookie);
        var ruta = $"{RutaArticulos}/{codigo}{sufijo}";

        var grande = CuerpoDeBytes(sufijo, version, limite + 1);
        var rechazo = await EnviarConLargoAsync(cliente, ruta, grande, conLargo, cookie);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, rechazo.StatusCode);
        await AfirmarSinCambiosAsync(cliente, cookie, codigo);

        // Control del 413: el mismo cuerpo en el límite exacto se acepta.
        var justo = CuerpoDeBytes(sufijo, version, limite);
        var aceptado = await EnviarConLargoAsync(cliente, ruta, justo, conLargo, cookie);
        Assert.Equal(HttpStatusCode.OK, aceptado.StatusCode);
    }

    /// <summary>Un cuerpo válido de exactamente <paramref name="bytes"/> bytes para la ruta, completado con relleno.</summary>
    private static string CuerpoDeBytes(string sufijo, long version, int bytes)
    {
        Func<string, string> armar = sufijo.Length == 0
            ? relleno => CuerpoDatos(version, relleno, descripcion: "Otra")
            : relleno => CuerpoEstado(false, relleno);
        var base_ = Encoding.UTF8.GetByteCount(armar(string.Empty));
        Assert.True(bytes >= base_, "El cuerpo sin relleno ya es más largo que el pedido.");
        var cuerpo = armar(new string('a', bytes - base_));
        Assert.Equal(bytes, Encoding.UTF8.GetByteCount(cuerpo));
        return cuerpo;
    }

    /// <summary>El cuerpo del PUT de datos; con <paramref name="relleno"/> (aun vacío), una propiedad que se ignora.</summary>
    private static string CuerpoDatos(long version, string? relleno = null, string descripcion = "Armazón") =>
        "{\"codigoProveedor\":\"ABC-1\",\"descripcion\":\"" + descripcion
        + "\",\"precioCosto\":1210,\"margenUtilidad\":60,\"version\":" + version
        + (relleno is null ? string.Empty : ",\"relleno\":\"" + relleno + "\"") + "}";

    /// <summary>El cuerpo del PUT de estado; con <paramref name="relleno"/> (aun vacío), una propiedad que se ignora.</summary>
    private static string CuerpoEstado(bool activo, string? relleno = null) =>
        "{\"activo\":" + (activo ? "true" : "false")
        + (relleno is null ? string.Empty : ",\"r\":\"" + relleno + "\"") + "}";

    private static Task<HttpResponseMessage> EnviarJsonAsync(
        HttpClient cliente, HttpMethod metodo, string ruta, string json, string? cookie = null) =>
        Http.EnviarAsync(cliente, metodo, ruta, new StringContent(json, Encoding.UTF8, "application/json"), cookie);

    private static Task<HttpResponseMessage> EnviarConLargoAsync(
        HttpClient cliente, string ruta, string cuerpo, bool conLargo, string cookie)
    {
        HttpContent contenido = conLargo
            ? new StringContent(cuerpo, Encoding.UTF8, "application/json")
            : new Http.ContenidoSinLargo(cuerpo, "application/json");
        return Http.EnviarAsync(cliente, HttpMethod.Put, ruta, contenido, cookie);
    }

    /// <summary>Graba un artículo activo con costo 1210 y margen 50; devuelve su código y su versión.</summary>
    private static async Task<(long Codigo, long Version)> GrabarArticuloAsync(HttpClient cliente, string cookie)
    {
        var respuesta = await Http.PostJsonAsync(
            cliente,
            RutaArticulos,
            """{"codigoProveedor":"ABC-1","descripcion":"Armazón","precioCosto":1210,"margenUtilidad":50}""",
            cookie);
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var json = await Http.JsonAsync(respuesta);
        return (json.GetProperty("codigo").GetInt64(), json.GetProperty("version").GetInt64());
    }

    /// <summary>El artículo sigue como lo dejó <see cref="GrabarArticuloAsync"/>: mismos datos, activo y versión 0.</summary>
    private static async Task AfirmarSinCambiosAsync(HttpClient cliente, string cookie, long codigo)
    {
        var respuesta = await Http.GetAsync(cliente, $"{RutaArticulos}/{codigo}", cookie);
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var json = await Http.JsonAsync(respuesta);
        Assert.Equal("Armazón", json.GetProperty("descripcion").GetString());
        Assert.Equal(50m, json.GetProperty("margenUtilidad").GetDecimal());
        Assert.True(json.GetProperty("activo").GetBoolean());
        Assert.Equal(0, json.GetProperty("version").GetInt64());
    }

    /// <summary>Restricciones de ruta que el valor de ejemplo <c>1</c> cumple siempre.</summary>
    private static readonly HashSet<string> RestriccionesQueUnoCumple = new(["int", "long"], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// La ruta con cada parámetro cambiado por <c>1</c>. Solo vale para parámetros sin restricción (<c>{id}</c>) o con
    /// <c>:int</c> o <c>:long</c>. Cualquier otra restricción (<c>:guid</c>, <c>:alpha</c>, <c>:length</c>,
    /// <c>:regex</c>, <c>:min</c>…) hace fallar la prueba con un mensaje explícito: con un valor que no la cumple, el
    /// pedido no llegaría al endpoint y el 401 se daría igual en una ruta inexistente, así que la ruta no quedaría
    /// probada. Sin parámetros, la ruta tal cual.
    /// </summary>
    internal static string ConValoresDeEjemplo(string patron) =>
        System.Text.RegularExpressions.Regex.Replace(patron, "\\{([^}]*)\\}", parametro =>
        {
            var partes = parametro.Groups[1].Value.Split(':');
            foreach (var restriccion in partes.Skip(1))
            {
                var nombre = restriccion.Split('(')[0].TrimEnd('?');
                if (!RestriccionesQueUnoCumple.Contains(nombre))
                {
                    Assert.Fail(
                        $"La ruta {patron} tiene el parámetro {{{parametro.Groups[1].Value}}} con la restricción " +
                        $"'{nombre}', que el valor de ejemplo 1 puede no cumplir: agregá un valor de ejemplo para ella.");
                }
            }

            return "1";
        });

    [Fact]
    public void ConValoresDeEjemplo_CambiaCadaParametroPorUno()
    {
        Assert.Equal("/api/articulos/1", ConValoresDeEjemplo("/api/articulos/{codigo:long}"));
        Assert.Equal("/api/a/1/b/1", ConValoresDeEjemplo("/api/a/{x}/b/{y:int}"));
        Assert.Equal("/api/sesion", ConValoresDeEjemplo("/api/sesion"));
    }

    [Theory]
    [InlineData("/api/a/{id:guid}", "guid")]
    [InlineData("/api/a/{nombre:alpha}", "alpha")]
    [InlineData("/api/a/{x:length(6)}", "length")]
    [InlineData("/api/a/{x:regex(^z+$)}", "regex")]
    [InlineData("/api/a/{x:int:min(5)}", "min")]
    public void ConValoresDeEjemplo_RestriccionQueUnoNoCumple_FallaConMensaje(string patron, string restriccion)
    {
        var error = Assert.Throws<Xunit.Sdk.FailException>(() => ConValoresDeEjemplo(patron));

        Assert.Contains(patron, error.Message, StringComparison.Ordinal);
        Assert.Contains($"'{restriccion}'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RutasAnonimas_SonExactamenteCinco()
    {
        var esperadas = new HashSet<(string, string)>
        {
            ("GET", "/api/acceso/estado"),
            ("POST", "/api/acceso/definir"),
            ("POST", "/api/acceso/ingresar"),
            ("GET", "/api/marca"),
            ("GET", "/api/marca/logo"),
        };

        var reales = RutasAnonimas.Todas.Select(r => (r.Metodo, r.Ruta)).ToHashSet();

        Assert.Equal(5, RutasAnonimas.Todas.Count);
        Assert.Equal(esperadas, reales);
    }

    [Theory]
    [InlineData("/api/acceso/definir")]
    [InlineData("/api/acceso/ingresar")]
    [InlineData("/api/acceso/cambiar")]
    public async Task Post_ConTextoPlano_Rechazado415(string ruta)
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        // cambiar exige sesión: sin cookie respondería 401 antes del 415.
        var cookie = EsCambiar(ruta) ? await Http.DefinirAsync(cliente) : null;
        var eventosAntes = fabrica.Eventos().Count;

        var respuesta = await Http.EnviarAsync(
            cliente,
            HttpMethod.Post,
            ruta,
            new StringContent(CuerpoDe(ruta), Encoding.UTF8, "text/plain"),
            cookie);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, respuesta.StatusCode);
        Assert.Equal(eventosAntes, fabrica.Eventos().Count);
        if (cookie is null)
        {
            Assert.Null(Http.SetCookie(respuesta));
        }

        // El 415 lo da un middleware para todo POST: sin este control, la fila pasaría aunque la acción no existiera.
        AfirmarQueElEndpointExiste(fabrica, ruta);
    }

    [Fact]
    public async Task Salir_ConSesionYTextoPlano_Rechazado415()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);

        var respuesta = await Http.EnviarAsync(
            cliente, HttpMethod.Post, "/api/acceso/salir", new StringContent("x", Encoding.UTF8, "text/plain"), cookie);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, respuesta.StatusCode);
        Assert.DoesNotContain(fabrica.Eventos(), e => e.Tipo == "salida");
    }

    [Theory]
    [InlineData("/api/acceso/definir")]
    [InlineData("/api/acceso/ingresar")]
    [InlineData("/api/acceso/cambiar")]
    public async Task Post_CuerpoMayorA4KB_Rechazado413(string ruta)
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = EsCambiar(ruta) ? await Http.DefinirAsync(cliente) : null;
        var eventosAntes = fabrica.Eventos().Count;

        var respuesta = await Http.PostJsonAsync(cliente, ruta, CuerpoDe(ruta, new string('a', 4200)), cookie);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, respuesta.StatusCode);
        Assert.Equal(eventosAntes, fabrica.Eventos().Count);
        if (cookie is null)
        {
            Assert.Null(Http.SetCookie(respuesta));
        }
    }

    [Theory]
    [InlineData("/api/acceso/definir", true)]
    [InlineData("/api/acceso/ingresar", true)]
    [InlineData("/api/acceso/cambiar", true)]
    [InlineData("/api/acceso/salir", true)]
    [InlineData("/api/acceso/salir", false)]
    public async Task Post_SinContentType_Rechazado415(string ruta, bool conCuerpo)
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var eventosAntes = fabrica.Eventos().Count;

        // ByteArrayContent no lleva Content-Type; sin contenido, el pedido tampoco lo lleva.
        HttpContent? contenido = conCuerpo ? new ByteArrayContent(Encoding.UTF8.GetBytes(CuerpoDe(ruta))) : null;
        Assert.Null(contenido?.Headers.ContentType);
        // salir y cambiar exigen sesión: sin cookie responderían 401 antes del 415.
        var esSalir = ruta.EndsWith("salir", StringComparison.Ordinal);
        var conSesion = esSalir || EsCambiar(ruta);
        var respuesta = await Http.EnviarAsync(cliente, HttpMethod.Post, ruta, contenido, conSesion ? cookie : null);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, respuesta.StatusCode);
        Assert.Equal(eventosAntes, fabrica.Eventos().Count);
        AfirmarQueElEndpointExiste(fabrica, ruta);
        if (conSesion)
        {
            // La sesión no se cerró: a lo sumo se renovó (cualquier pedido con sesión la renueva), nunca se borró.
            Assert.DoesNotContain("1970", Http.SetCookie(respuesta) ?? string.Empty, StringComparison.Ordinal);
        }
        else
        {
            // No se emitió una sesión nueva.
            Assert.Null(Http.SetCookie(respuesta));
        }
    }

    [Theory]
    [InlineData("/api/acceso/definir")]
    [InlineData("/api/acceso/ingresar")]
    [InlineData("/api/acceso/cambiar")]
    public async Task Post_CuerpoMayorA4KBSinContentLength_Rechazado413(string ruta)
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = EsCambiar(ruta) ? await Http.DefinirAsync(cliente) : null;
        var eventosAntes = fabrica.Eventos().Count;

        var contenido = new Http.ContenidoSinLargo(CuerpoDe(ruta, new string('a', 4200)), "application/json");
        var respuesta = await Http.EnviarAsync(cliente, HttpMethod.Post, ruta, contenido, cookie);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, respuesta.StatusCode);
        if (cookie is null)
        {
            Assert.Null(Http.SetCookie(respuesta));
        }

        Assert.Equal(eventosAntes, fabrica.Eventos().Count);
    }

    private static bool EsCambiar(string ruta) => ruta.EndsWith("cambiar", StringComparison.Ordinal);

    /// <summary>Un cuerpo válido para la ruta: las dos contraseñas en <c>cambiar</c>, una en las demás.</summary>
    private static string CuerpoDe(string ruta, string contrasena = Http.Contrasena) =>
        EsCambiar(ruta) ? Http.CuerpoCambio(Http.Contrasena, contrasena) : Http.CuerpoContrasena(contrasena);

    /// <summary>
    /// Hay una acción registrada en la ruta (el patrón, por ejemplo <c>/api/articulos/{codigo:long}</c>) para el método
    /// indicado, POST si no se indica. Lo necesitan las pruebas cuya respuesta (415, 401) también se daría en una ruta
    /// inexistente: el patrón solo no alcanza, porque la misma ruta puede tener un GET y no un PUT.
    /// </summary>
    internal static void AfirmarQueElEndpointExiste(FabricaApi fabrica, string ruta, string metodo = "POST")
    {
        var existe = fabrica.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Any(endpoint =>
                "/" + (endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/') == ruta
                && endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods
                    .Contains(metodo, StringComparer.OrdinalIgnoreCase) == true);
        Assert.True(existe, $"No hay un endpoint {metodo} {ruta}.");
    }

    [Fact]
    public void AfirmarQueElEndpointExiste_DistingueElMetodo()
    {
        using var fabrica = new FabricaApi();

        AfirmarQueElEndpointExiste(fabrica, RutaArticulos, HttpMethods.Post);
        // GET /api/configuracion existe y no tiene PUT: con solo la ruta, la afirmación pasaría.
        AfirmarQueElEndpointExiste(fabrica, "/api/configuracion", HttpMethods.Get);
        var error = Assert.Throws<Xunit.Sdk.TrueException>(
            () => AfirmarQueElEndpointExiste(fabrica, "/api/configuracion", HttpMethods.Put));
        Assert.Contains("PUT /api/configuracion", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Post_CuerpoChicoSinContentLength_SeAcepta()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();

        var contenido = new Http.ContenidoSinLargo(Http.CuerpoContrasena(Http.Contrasena), "application/json");
        var respuesta = await Http.EnviarAsync(cliente, HttpMethod.Post, "/api/acceso/definir", contenido);

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
    }

    [Fact]
    public async Task RutaInexistente_SinSesion401_ConSesion404()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);

        Assert.Equal(HttpStatusCode.Unauthorized, (await Http.GetAsync(cliente, "/api/no-existe")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Http.GetAsync(cliente, "/api/no-existe", cookie)).StatusCode);
    }

    [Fact]
    public void Api_SoloLaRaizDeComposicionConoceInfraestructura_YNadieUsaDominioNiLaBase()
    {
        var api = typeof(Program).Assembly;

        // La Api no compila contra el dominio, EF Core, SQLite ni ADO.NET: solo contra Application e Infrastructure.
        var referencias = api.GetReferencedAssemblies().Select(r => r.Name).ToList();
        Assert.DoesNotContain("Optica.Domain", referencias);
        Assert.DoesNotContain(referencias, r => r!.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
        Assert.DoesNotContain("Microsoft.Data.Sqlite", referencias);
        Assert.DoesNotContain("System.Data.Common", referencias);

        // Fuera de Program (la raíz de composición), ningún tipo de la Api recibe tipos de Infrastructure.
        var infraestructura = typeof(Optica.Infrastructure.ConfiguracionInfraestructura).Assembly;
        var usos = api.GetTypes()
            .Where(t => t != typeof(Program) && t.Namespace?.StartsWith("Optica.Api", StringComparison.Ordinal) == true)
            .SelectMany(t => t.GetConstructors().SelectMany(c => c.GetParameters()).Select(p => (Tipo: t, p.ParameterType)))
            .Where(u => u.ParameterType.Assembly == infraestructura)
            .Select(u => $"{u.Tipo.Name} recibe {u.ParameterType.Name}")
            .ToList();
        Assert.Empty(usos);
    }

    [Fact]
    public async Task DataProtection_ClavesEnElDirectorioDeDatos()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();

        // Emitir la cookie obliga a crear la primera clave.
        await Http.DefinirAsync(cliente);

        var claves = Path.Combine(fabrica.DirectorioDatos, "claves");
        Assert.True(Directory.Exists(claves), "No se creó <Optica:Datos:Directorio>/claves.");
        Assert.NotEmpty(Directory.GetFiles(claves, "key-*.xml"));
    }

    [Fact]
    public async Task Cookie_EsHttpOnlySecureSameSiteStrict()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();

        var respuesta = await Http.PostJsonAsync(cliente, "/api/acceso/definir", Http.CuerpoContrasena(Http.Contrasena));

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var atributos = (Http.SetCookie(respuesta) ?? string.Empty)
            .Split(';', StringSplitOptions.TrimEntries)
            .Skip(1)
            .Select(a => a.ToLowerInvariant())
            .ToList();
        Assert.Contains("httponly", atributos);
        Assert.Contains("secure", atributos);
        Assert.Contains("samesite=strict", atributos);
        Assert.Contains("path=/", atributos);
        // Cookie de sesión del navegador: el vencimiento de 60 minutos lo controla el servidor.
        Assert.DoesNotContain(atributos, a => a.StartsWith("expires=", StringComparison.Ordinal));
    }
}
