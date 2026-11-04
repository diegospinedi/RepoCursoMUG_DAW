using System.Net;
using System.Text;
using System.Text.Json;
using Optica.Api.Controllers;
using Optica.Application.Catalogo;

namespace Optica.Api.Tests.Catalogo;

/// <summary>
/// <c>POST /api/articulos</c> y <c>GET /api/articulos/{codigo}</c> (FEAT-002a): con sesión, la Api traduce el alta y la
/// consulta de artículos; sin sesión, 401 (R-01); el POST exige JSON (415, R-02) y hasta 1 KB (413, R-05); un cuerpo
/// ilegible da 400 con un texto fijo y un artículo inexistente, 404 sin detalles (R-06).
/// </summary>
public sealed class ArticulosApiTests
{
    private const string Ruta = "/api/articulos";
    private const string AccionCuerpoIlegible = "No se pudieron leer los valores enviados. Revisalos y grabá de nuevo.";

    [Fact]
    public async Task Post_Valido_201ConCodigoYPrecio()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);

        // AC-07 con la configuración inicial: Responsable Inscripto, alícuota 21, múltiplo 0,01.
        var respuesta = await GrabarAsync(cliente, Cuerpo("ABC-1", "Armazón", 1210m, 50m), cookie);

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var json = await Http.JsonAsync(respuesta);
        var codigo = json.GetProperty("codigo").GetInt64();
        Assert.True(codigo > 0);
        AfirmarArticulo(json, codigo, "ABC-1", "Armazón", 1210m, 50m, 1815m, null);
        Assert.NotNull(respuesta.Headers.Location);
        var ubicacion = respuesta.Headers.Location!;
        var camino = ubicacion.IsAbsoluteUri ? ubicacion.AbsolutePath : ubicacion.OriginalString;
        Assert.Equal($"{Ruta}/{codigo}", camino);

        // AC-01: quedó guardado; el GET de Location devuelve lo mismo que el 201.
        var lectura = await Http.GetAsync(cliente, camino, cookie);
        Assert.Equal(HttpStatusCode.OK, lectura.StatusCode);
        AfirmarArticulo(await Http.JsonAsync(lectura), codigo, "ABC-1", "Armazón", 1210m, 50m, 1815m, null);
    }

    [Fact]
    public async Task Post_CodigoDuplicado_Rechazado400EnElCampo()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var primero = await GrabarAsync(cliente, Cuerpo("ABC-1", "Armazón Zq7-marcado", 1210m, 50m), cookie);
        Assert.Equal(HttpStatusCode.Created, primero.StatusCode);
        var codigo = (await Http.JsonAsync(primero)).GetProperty("codigo").GetInt64();

        var respuesta = await GrabarAsync(cliente, Cuerpo(" abc-1 ", "Otro armazón", 100m, 10m), cookie);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            new Dictionary<string, string[]>
            {
                [ValidacionArticulo.CampoCodigoProveedor] = [ValidacionArticulo.AccionCodigoDuplicado],
            },
            await ErroresAsync(respuesta));
        // El mensaje no revela datos del otro artículo (D-07, R-06).
        var texto = await respuesta.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Zq7-marcado", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("ABC-1", texto, StringComparison.Ordinal);
        await AfirmarSinArticuloAsync(cliente, cookie, codigo + 1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Post_SinDescripcion_Rechazado400EnElCampo(string? descripcion)
    {
        await AfirmarRechazoEnUnCampoAsync(
            Cuerpo("ABC-1", descripcion, 1210m, 50m),
            ValidacionArticulo.CampoDescripcion,
            ValidacionArticulo.AccionDescripcionFaltante);
    }

    [Theory]
    [InlineData("1000.01")]
    [InlineData("-1")]
    public async Task Post_Margen1000coma01_Rechazado400EnElCampo(string margen)
    {
        await AfirmarRechazoEnUnCampoAsync(
            Cuerpo("ABC-1", "Armazón", 1210m, decimal.Parse(margen, System.Globalization.CultureInfo.InvariantCulture)),
            ValidacionArticulo.CampoMargenUtilidad,
            ValidacionArticulo.AccionMargenFueraDeRango);
    }

    [Fact]
    public async Task Post_VariosInvalidos_400ConTodosLosCampos()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);

        var respuesta = await GrabarAsync(cliente, Cuerpo(null, null, null, null), cookie);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal(
            new Dictionary<string, string[]>
            {
                [ValidacionArticulo.CampoCodigoProveedor] = [ValidacionArticulo.AccionCodigoFaltante],
                [ValidacionArticulo.CampoDescripcion] = [ValidacionArticulo.AccionDescripcionFaltante],
                [ValidacionArticulo.CampoPrecioCosto] = [ValidacionArticulo.AccionCostoFaltante],
                [ValidacionArticulo.CampoMargenUtilidad] = [ValidacionArticulo.AccionMargenFaltante],
            },
            await ErroresAsync(respuesta));
        await AfirmarSinArticuloAsync(cliente, cookie, 1);
    }

    [Fact]
    public async Task Post_CostoNegativo_201ConAdvertencia()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);

        // AC-06: Responsable Inscripto, alícuota 21, múltiplo 0,01.
        var respuesta = await GrabarAsync(cliente, Cuerpo("NEG-1", "Estuche", -100m, 50m), cookie);

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var json = await Http.JsonAsync(respuesta);
        AfirmarArticulo(
            json,
            json.GetProperty("codigo").GetInt64(),
            "NEG-1",
            "Estuche",
            -100m,
            50m,
            -150m,
            "Precio de costo negativo o cero");
    }

    [Fact]
    public async Task Post_Monotributo_201Precio1815()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        await ConfigurarAsync(cliente, cookie, "monotributo", 0.01m);

        // AC-09.
        var respuesta = await GrabarAsync(cliente, Cuerpo("MONO-1", "Armazón", 1210m, 50m), cookie);

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        Assert.Equal(1815m, (await Http.JsonAsync(respuesta)).GetProperty("precioVenta").GetDecimal());
    }

    [Fact]
    public async Task Post_Multiplo50_201Precio1850()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        await ConfigurarAsync(cliente, cookie, "responsable_inscripto", 50m);

        // AC-10: el precio de venta calculado es 1.815,37 y el múltiplo, 50.
        var respuesta = await GrabarAsync(cliente, Cuerpo("M50-1", "Armazón", 1815.37m, 0m), cookie);

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        Assert.Equal(1850m, (await Http.JsonAsync(respuesta)).GetProperty("precioVenta").GetDecimal());
    }

    [Fact]
    public async Task PostYGet_SinSesion_NoAutorizado401()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        // Con una contraseña definida, para que el 401 no se deba a que todavía no hay acceso.
        var cookie = await Http.DefinirAsync(cliente);
        var grabado = await GrabarAsync(cliente, Cuerpo("ABC-1", "Armazón", 1210m, 50m), cookie);
        Assert.Equal(HttpStatusCode.Created, grabado.StatusCode);
        var codigo = (await Http.JsonAsync(grabado)).GetProperty("codigo").GetInt64();

        var pedidos = new List<HttpResponseMessage>
        {
            // Un artículo que existe: sin sesión no se ve su costo ni su margen.
            await Http.GetAsync(cliente, $"{Ruta}/{codigo}"),
            await GrabarAsync(cliente, Cuerpo("SIN-1", "Armazón", 1210m, 50m), cookie: null),
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
        await AfirmarSinArticuloAsync(cliente, cookie, codigo + 1);
    }

    [Fact]
    public async Task Post_SinContentTypeJson_Rechazado415()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var cuerpo = Cuerpo("ABC-1", "Armazón", 1210m, 50m);

        var textoPlano = await Http.EnviarAsync(
            cliente, HttpMethod.Post, Ruta, new StringContent(cuerpo, Encoding.UTF8, "text/plain"), cookie);
        // ByteArrayContent no lleva Content-Type.
        var sinTipo = await Http.EnviarAsync(
            cliente, HttpMethod.Post, Ruta, new ByteArrayContent(Encoding.UTF8.GetBytes(cuerpo)), cookie);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, textoPlano.StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, sinTipo.StatusCode);
        Acceso.ProteccionTests.AfirmarQueElEndpointExiste(fabrica, Ruta);
        await AfirmarSinArticuloAsync(cliente, cookie, 1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Post_CuerpoGrande_Rechazado413(bool conLargo)
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        // Un cuerpo válido de más de 2 KB: solo el tamaño lo rechaza.
        var cuerpo = CuerpoConRelleno(2048);
        Assert.True(Encoding.UTF8.GetByteCount(cuerpo) > 2048);

        HttpContent contenido = conLargo
            ? new StringContent(cuerpo, Encoding.UTF8, "application/json")
            : new Http.ContenidoSinLargo(cuerpo, "application/json");
        var respuesta = await Http.EnviarAsync(cliente, HttpMethod.Post, Ruta, contenido, cookie);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, respuesta.StatusCode);
        await AfirmarSinArticuloAsync(cliente, cookie, 1);
    }

    [Fact]
    public void CuerpoMaximo_Es1024Bytes()
    {
        Assert.Equal(1024, ArticulosController.CuerpoMaximo);
    }

    /// <summary>
    /// El borde exacto del límite: un cuerpo JSON válido de exactamente 1024 bytes se acepta y uno de 1025 (o más) se
    /// rechaza con 413, con <c>Content-Length</c> y sin él (en partes).
    /// </summary>
    [Theory]
    [InlineData(1024, true, HttpStatusCode.Created)]
    [InlineData(1024, false, HttpStatusCode.Created)]
    [InlineData(1025, true, HttpStatusCode.RequestEntityTooLarge)]
    [InlineData(1025, false, HttpStatusCode.RequestEntityTooLarge)]
    [InlineData(1100, true, HttpStatusCode.RequestEntityTooLarge)]
    [InlineData(1100, false, HttpStatusCode.RequestEntityTooLarge)]
    public async Task Post_CuerpoEnElBordeDelLimite_1024SeAcepta1025Rechazado413(
        int bytes, bool conLargo, HttpStatusCode esperado)
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var cuerpo = CuerpoDeBytes(bytes);
        Assert.Equal(bytes, Encoding.UTF8.GetByteCount(cuerpo));

        HttpContent contenido = conLargo
            ? new StringContent(cuerpo, Encoding.UTF8, "application/json")
            : new Http.ContenidoSinLargo(cuerpo, "application/json");
        var respuesta = await Http.EnviarAsync(cliente, HttpMethod.Post, Ruta, contenido, cookie);

        Assert.Equal(esperado, respuesta.StatusCode);
        if (esperado == HttpStatusCode.RequestEntityTooLarge)
        {
            await AfirmarSinArticuloAsync(cliente, cookie, 1);
        }
    }

    [Fact]
    public async Task Post_CuerpoChicoValido_SeAceptaBajoElLimite()
    {
        // Control del 413: el mismo cuerpo con menos relleno, de menos de 1 KB, se acepta.
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var cuerpo = CuerpoConRelleno(800);
        Assert.True(Encoding.UTF8.GetByteCount(cuerpo) < 1024);

        var respuesta = await GrabarAsync(cliente, cuerpo, cookie);

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
    }

    [Theory]
    [InlineData("esto no es json", ValidacionArticulo.CampoCodigoProveedor)]
    [InlineData("[1,2,3]", ValidacionArticulo.CampoCodigoProveedor)]
    [InlineData(
        "{\"codigoProveedor\":7,\"descripcion\":\"Armazón\",\"precioCosto\":1210,\"margenUtilidad\":50}",
        ValidacionArticulo.CampoCodigoProveedor)]
    [InlineData(
        "{\"codigoProveedor\":\"ABC-1\",\"descripcion\":[\"Zq7-marcado\"],\"precioCosto\":1210,\"margenUtilidad\":50}",
        ValidacionArticulo.CampoDescripcion)]
    [InlineData(
        "{\"codigoProveedor\":\"ABC-1\",\"descripcion\":\"Zq7-marcado",
        ValidacionArticulo.CampoDescripcion)]
    [InlineData(
        "{\"codigoProveedor\":\"ABC-1\",\"descripcion\":\"Armazón\",\"precioCosto\":\"Zq7-marcado\",\"margenUtilidad\":50}",
        ValidacionArticulo.CampoPrecioCosto)]
    [InlineData(
        "{\"codigoProveedor\":\"ABC-1\",\"descripcion\":\"Armazón\",\"precioCosto\":\"1210\",\"margenUtilidad\":50}",
        ValidacionArticulo.CampoPrecioCosto)]
    [InlineData(
        "{\"codigoProveedor\":\"ABC-1\",\"descripcion\":\"Armazón\",\"precioCosto\":1e30,\"margenUtilidad\":50}",
        ValidacionArticulo.CampoPrecioCosto)]
    [InlineData(
        "{\"codigoProveedor\":\"ABC-1\",\"descripcion\":\"Armazón\",\"precioCosto\":1210,\"margenUtilidad\":\"50\"}",
        ValidacionArticulo.CampoMargenUtilidad)]
    [InlineData(
        "{\"codigoProveedor\":\"ABC-1\",\"descripcion\":\"Armazón\",\"precioCosto\":1210,\"margenUtilidad\":[50]}",
        ValidacionArticulo.CampoMargenUtilidad)]
    public async Task Post_CuerpoIlegible_Invalido400SinDetallesInternos(string cuerpo, string campo)
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);

        var respuesta = await GrabarAsync(cliente, cuerpo, cookie);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        Assert.Equal(new Dictionary<string, string[]> { [campo] = [AccionCuerpoIlegible] }, await ErroresAsync(respuesta));
        // Ni los mensajes internos del lector de JSON ni lo enviado.
        var texto = await respuesta.Content.ReadAsStringAsync();
        Assert.DoesNotContain("JSON", texto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LineNumber", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("System.", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("Zq7-marcado", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("$.", texto, StringComparison.Ordinal);
        await AfirmarSinArticuloAsync(cliente, cookie, 1);
    }

    [Fact]
    public async Task Get_Inexistente_Error404SinDetalles()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var grabado = await GrabarAsync(cliente, Cuerpo("ABC-1", "Armazón Zq7-marcado", 1210m, 50m), cookie);
        Assert.Equal(HttpStatusCode.Created, grabado.StatusCode);
        var codigo = (await Http.JsonAsync(grabado)).GetProperty("codigo").GetInt64();

        var respuesta = await Http.GetAsync(cliente, $"{Ruta}/{codigo + 1}", cookie);

        // ProblemDetails de la acción (una ruta inexistente daría 404 sin cuerpo), sin detalles ni datos de otros.
        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        var json = await Http.JsonAsync(respuesta);
        Assert.Equal(404, json.GetProperty("status").GetInt32());
        Assert.False(json.TryGetProperty("detail", out _));
        Assert.False(json.TryGetProperty("errors", out _));
        var texto = json.GetRawText();
        Assert.DoesNotContain("Zq7-marcado", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("ABC-1", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", texto, StringComparison.Ordinal);

        // Un código que no es un entero de 64 bits no llega a la acción.
        Assert.Equal(HttpStatusCode.NotFound, (await Http.GetAsync(cliente, $"{Ruta}/abc", cookie)).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound, (await Http.GetAsync(cliente, $"{Ruta}/99999999999999999999", cookie)).StatusCode);
    }

    private static string Cuerpo(string? codigoProveedor, string? descripcion, decimal? precioCosto, decimal? margenUtilidad) =>
        JsonSerializer.Serialize(new { codigoProveedor, descripcion, precioCosto, margenUtilidad });

    private static string CuerpoConRelleno(int largo) =>
        "{\"codigoProveedor\":\"ABC-1\",\"descripcion\":\"Armazón\",\"precioCosto\":1210,\"margenUtilidad\":50,"
        + "\"relleno\":\"" + new string('a', largo) + "\"}";

    /// <summary>Un cuerpo válido de exactamente <paramref name="bytes"/> bytes en UTF-8, completado con el relleno.</summary>
    private static string CuerpoDeBytes(int bytes)
    {
        var base_ = Encoding.UTF8.GetByteCount(CuerpoConRelleno(0));
        if (bytes < base_)
        {
            throw new ArgumentOutOfRangeException(nameof(bytes), "El cuerpo sin relleno ya es más largo.");
        }

        return CuerpoConRelleno(bytes - base_);
    }

    private static Task<HttpResponseMessage> GrabarAsync(HttpClient cliente, string cuerpo, string? cookie) =>
        Http.PostJsonAsync(cliente, Ruta, cuerpo, cookie);

    /// <summary>Cambia la configuración por la Api (FR-08: el alta siguiente la usa).</summary>
    private static async Task ConfigurarAsync(HttpClient cliente, string cookie, string condicionFiscal, decimal multiplo)
    {
        var cuerpo = JsonSerializer.Serialize(new
        {
            alicuotaIva = 21m,
            condicionFiscal,
            multiploRedondeo = multiplo,
            topeIdentificacion = (decimal?)null,
        });
        var respuesta = await Http.PostJsonAsync(cliente, "/api/configuracion", cuerpo, cookie);
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
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
        await AfirmarSinArticuloAsync(cliente, cookie, 1);
    }

    /// <summary>
    /// No hay artículo con ese código: el pedido rechazado no grabó nada. Exige el 404 de la acción (con
    /// ProblemDetails), no el de una ruta inexistente.
    /// </summary>
    private static async Task AfirmarSinArticuloAsync(HttpClient cliente, string cookie, long codigo)
    {
        var respuesta = await Http.GetAsync(cliente, $"{Ruta}/{codigo}", cookie);
        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
    }

    private static void AfirmarArticulo(
        JsonElement json,
        long codigo,
        string codigoProveedor,
        string descripcion,
        decimal precioCosto,
        decimal margenUtilidad,
        decimal precioVenta,
        string? advertencia)
    {
        Assert.Equal(
            ["codigo", "codigoProveedor", "descripcion", "precioCosto", "margenUtilidad", "precioVenta", "advertencia"],
            json.EnumerateObject().Select(propiedad => propiedad.Name).ToArray());
        Assert.Equal(codigo, json.GetProperty("codigo").GetInt64());
        Assert.Equal(codigoProveedor, json.GetProperty("codigoProveedor").GetString());
        Assert.Equal(descripcion, json.GetProperty("descripcion").GetString());
        Assert.Equal(precioCosto, json.GetProperty("precioCosto").GetDecimal());
        Assert.Equal(margenUtilidad, json.GetProperty("margenUtilidad").GetDecimal());
        Assert.Equal(precioVenta, json.GetProperty("precioVenta").GetDecimal());
        var aviso = json.GetProperty("advertencia");
        if (advertencia is null)
        {
            Assert.Equal(JsonValueKind.Null, aviso.ValueKind);
        }
        else
        {
            Assert.Equal(advertencia, aviso.GetString());
        }
    }

    private static async Task<Dictionary<string, string[]>> ErroresAsync(HttpResponseMessage respuesta)
    {
        var json = await Http.JsonAsync(respuesta);
        return json.GetProperty("errors").EnumerateObject().ToDictionary(
            campo => campo.Name,
            campo => campo.Value.EnumerateArray().Select(valor => valor.GetString()!).ToArray());
    }
}
