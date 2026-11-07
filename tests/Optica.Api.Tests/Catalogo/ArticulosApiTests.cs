using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Optica.Api.Controllers;
using Optica.Application.Catalogo;
using Optica.Application.Comun;
using Optica.Domain.Catalogo;
using Optica.Infrastructure.Persistencia;

namespace Optica.Api.Tests.Catalogo;

/// <summary>
/// <c>POST /api/articulos</c> y <c>GET /api/articulos/{codigo}</c> (FEAT-002a): con sesión, la Api traduce el alta y la
/// consulta de artículos; sin sesión, 401 (R-01); el POST exige JSON (415, R-02) y hasta 1 KB (413, R-05); un cuerpo
/// ilegible da 400 con un texto fijo y un artículo inexistente, 404 sin detalles (R-06). FEAT-002b suma
/// <c>GET /api/articulos</c> (listado paginado sin costo ni margen), <c>PUT /api/articulos/{codigo}</c> (modificación
/// con versión) y <c>PUT /api/articulos/{codigo}/estado</c> (desactivar y reactivar), con 409 por <c>motivo</c>.
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
        Acceso.ProteccionTests.AfirmarQueElEndpointExiste(fabrica, Ruta, "POST");
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
        Acceso.ProteccionTests.AfirmarQueElEndpointExiste(fabrica, Ruta, "POST");
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

    // FEAT-002b: listado, modificación y cambio de estado.

    /// <summary>
    /// Una página ilegible (no entera, o que no entra en un entero) o fuera de 1 a 10.000 responde 400 con el texto fijo
    /// de D-07 en <c>pagina</c>, sin repetir el valor recibido.
    /// </summary>
    [Theory]
    [InlineData("Zq7marcado")]
    [InlineData("1.5")]
    [InlineData("99999999999")]
    [InlineData("0")]
    [InlineData("10001")]
    public async Task Get_Listado_PaginaIlegible_400(string pagina)
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);

        var respuesta = await Http.GetAsync(cliente, $"{Ruta}?texto=armaz&pagina={pagina}", cookie);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            new Dictionary<string, string[]>
            {
                [ValidacionArticulo.CampoPagina] = ["Pedí una página entre 1 y 10.000."],
            },
            await ErroresAsync(respuesta));
        var texto = await respuesta.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Zq7marcado", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("99999999999", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("1.5", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("System.", texto, StringComparison.Ordinal);
    }

    // FEAT-003a: soloActivos, la búsqueda de las líneas del presupuesto (D-15).

    /// <summary>
    /// D-26: un <c>soloActivos</c> que no es booleano responde 400 en <c>soloActivos</c> con el texto fijo, sin repetir
    /// el valor recibido; con una página también ilegible, los dos errores.
    /// </summary>
    [Theory]
    [InlineData("Zq7marcado")]
    [InlineData("1")]
    [InlineData("si")]
    public async Task Get_Listado_SoloActivosIlegible_400(string soloActivos)
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);

        var respuesta = await Http.GetAsync(cliente, $"{Ruta}?texto=armaz&soloActivos={soloActivos}", cookie);
        var conPagina = await Http.GetAsync(
            cliente, $"{Ruta}?texto=armaz&pagina=Zq7pagina&soloActivos={soloActivos}", cookie);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            new Dictionary<string, string[]> { ["soloActivos"] = ["Indicá true o false."] },
            await ErroresAsync(respuesta));
        var texto = await respuesta.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Zq7marcado", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("System.", texto, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, conPagina.StatusCode);
        Assert.Equal(
            new Dictionary<string, string[]>
            {
                [ValidacionArticulo.CampoPagina] = ["Pedí una página entre 1 y 10.000."],
                ["soloActivos"] = ["Indicá true o false."],
            },
            await ErroresAsync(conPagina));
        Assert.DoesNotContain("Zq7", await conPagina.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    /// <summary>
    /// AC-13: buscar "armazon" con <c>soloActivos=true</c> lista los activos con y sin tilde y ningún inactivo, también
    /// en el total. Sin el parámetro, o con <c>false</c>, el listado del catálogo sigue con activos e inactivos.
    /// </summary>
    [Fact]
    public async Task Get_Listado_SoloActivosArmazon_SinInactivos()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var metal = await GrabarArticuloAsync(cliente, cookie, "Armazón metal", "M-A");
        var acetato = await GrabarArticuloAsync(cliente, cookie, "ARMAZON acetato", "A-A");
        var inactivo = await GrabarArticuloAsync(cliente, cookie, "Armazón carey", "C-A");
        await GrabarArticuloAsync(cliente, cookie, "Estuche rígido", "E-A");
        var baja = await PutAsync(cliente, $"{Ruta}/{inactivo}/estado", CuerpoEstado(false), cookie);
        Assert.Equal(HttpStatusCode.OK, baja.StatusCode);

        var respuesta = await Http.GetAsync(cliente, $"{Ruta}?texto=armazon&soloActivos=true", cookie);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var json = await Http.JsonAsync(respuesta);
        Assert.Equal(2, json.GetProperty("total").GetInt32());
        var articulos = json.GetProperty("articulos").EnumerateArray().ToList();
        Assert.Equal([acetato, metal], articulos.Select(a => a.GetProperty("codigo").GetInt64()).ToArray());
        Assert.All(articulos, a => Assert.True(a.GetProperty("activo").GetBoolean()));

        foreach (var consulta in new[] { "?texto=armazon", "?texto=armazon&soloActivos=false" })
        {
            var todos = await Http.JsonAsync(await Http.GetAsync(cliente, Ruta + consulta, cookie));
            Assert.Equal(3, todos.GetProperty("total").GetInt32());
            Assert.Contains(
                todos.GetProperty("articulos").EnumerateArray(),
                a => a.GetProperty("codigo").GetInt64() == inactivo && !a.GetProperty("activo").GetBoolean());
        }
    }

    /// <summary>
    /// AC-14: buscar con <c>soloActivos=true</c> el Código de un artículo activo lo lista; el de uno inactivo no lista
    /// nada, aunque el listado del catálogo sí lo muestre.
    /// </summary>
    [Fact]
    public async Task Get_Listado_SoloActivosPorCodigo()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        // Descripciones y códigos en el proveedor sin dígitos: solo el Código coincide con el texto buscado.
        var activo = await GrabarArticuloAsync(cliente, cookie, "Armazón metal", "M-A");
        var inactivo = await GrabarArticuloAsync(cliente, cookie, "Estuche rígido", "E-A");
        Assert.Equal(HttpStatusCode.OK, (await PutAsync(
            cliente, $"{Ruta}/{inactivo}/estado", CuerpoEstado(false), cookie)).StatusCode);

        var porActivo = await Http.JsonAsync(
            await Http.GetAsync(cliente, $"{Ruta}?texto={activo}&soloActivos=true", cookie));
        var porInactivo = await Http.JsonAsync(
            await Http.GetAsync(cliente, $"{Ruta}?texto={inactivo}&soloActivos=true", cookie));
        var catalogo = await Http.JsonAsync(await Http.GetAsync(cliente, $"{Ruta}?texto={inactivo}", cookie));

        var encontrado = Assert.Single(porActivo.GetProperty("articulos").EnumerateArray());
        Assert.Equal(activo, encontrado.GetProperty("codigo").GetInt64());
        Assert.True(encontrado.GetProperty("activo").GetBoolean());
        Assert.Empty(porInactivo.GetProperty("articulos").EnumerateArray());
        Assert.Equal(0, porInactivo.GetProperty("total").GetInt32());
        Assert.Equal(
            inactivo,
            Assert.Single(catalogo.GetProperty("articulos").EnumerateArray()).GetProperty("codigo").GetInt64());
    }

    /// <summary>
    /// Un cuerpo ilegible en cualquiera de los dos PUT responde 400 con un solo error y la acción fija del alta, en el
    /// campo que el lector de JSON nombra (también <c>version</c> y <c>activo</c>), sin detalles internos ni lo enviado;
    /// el artículo no cambia.
    /// </summary>
    [Theory]
    [InlineData("", "esto no es json", ValidacionArticulo.CampoCodigoProveedor)]
    [InlineData(
        "",
        "{\"codigoProveedor\":\"ABC-1\",\"descripcion\":\"Otra\",\"precioCosto\":\"Zq7-marcado\",\"margenUtilidad\":60,\"version\":0}",
        ValidacionArticulo.CampoPrecioCosto)]
    [InlineData(
        "",
        "{\"codigoProveedor\":\"ABC-1\",\"descripcion\":\"Otra\",\"precioCosto\":1210,\"margenUtilidad\":60,\"version\":\"0\"}",
        ValidacionArticulo.CampoVersion)]
    [InlineData(
        "",
        "{\"codigoProveedor\":\"ABC-1\",\"descripcion\":\"Otra\",\"precioCosto\":1210,\"margenUtilidad\":60,\"version\":1.5}",
        ValidacionArticulo.CampoVersion)]
    [InlineData(
        "",
        "{\"codigoProveedor\":\"ABC-1\",\"descripcion\":\"Otra\",\"precioCosto\":1210,\"margenUtilidad\":60,\"version\":99999999999999999999}",
        ValidacionArticulo.CampoVersion)]
    [InlineData("/estado", "esto no es json", ValidacionArticulo.CampoActivo)]
    [InlineData("/estado", "{\"activo\":\"false\"}", ValidacionArticulo.CampoActivo)]
    [InlineData("/estado", "{\"activo\":0}", ValidacionArticulo.CampoActivo)]
    [InlineData("/estado", "{\"activo\":[\"Zq7-marcado\"]}", ValidacionArticulo.CampoActivo)]
    public async Task Put_CuerpoIlegible_400(string sufijo, string cuerpo, string campo)
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);

        var respuesta = await PutAsync(cliente, $"{Ruta}/{codigo}{sufijo}", cuerpo, cookie);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        Assert.Equal(new Dictionary<string, string[]> { [campo] = [AccionCuerpoIlegible] }, await ErroresAsync(respuesta));
        var texto = await respuesta.Content.ReadAsStringAsync();
        Assert.DoesNotContain("JSON", texto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LineNumber", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("System.", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("Zq7-marcado", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("$.", texto, StringComparison.Ordinal);
        await AfirmarSinCambiosAsync(cliente, cookie, codigo);
    }

    /// <summary>Los dos PUT sobre un código que no existe: 404 con ProblemDetails sin detalles (R-06).</summary>
    [Theory]
    [InlineData("")]
    [InlineData("/estado")]
    public async Task Put_Inexistente_404(string sufijo)
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie, "Armazón Zq7-marcado");
        var cuerpo = sufijo.Length == 0 ? CuerpoModificacion("ABC-1", "Otra", 1210m, 60m, 0) : CuerpoEstado(false);

        var respuesta = await PutAsync(cliente, $"{Ruta}/{codigo + 1}{sufijo}", cuerpo, cookie);

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        var json = await Http.JsonAsync(respuesta);
        Assert.Equal(404, json.GetProperty("status").GetInt32());
        Assert.False(json.TryGetProperty("detail", out _));
        Assert.False(json.TryGetProperty("errors", out _));
        Assert.DoesNotContain("Zq7-marcado", json.GetRawText(), StringComparison.Ordinal);
        await AfirmarSinArticuloAsync(cliente, cookie, codigo + 1);
    }

    /// <summary>Modificar un artículo inactivo: 409 con <c>motivo</c> <c>inactivo</c> y sin grabar (D-02, D-19).</summary>
    [Fact]
    public async Task Put_Inactivo_409()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);
        var baja = await PutAsync(cliente, $"{Ruta}/{codigo}/estado", CuerpoEstado(false), cookie);
        Assert.Equal(HttpStatusCode.OK, baja.StatusCode);
        var version = (await Http.JsonAsync(baja)).GetProperty("version").GetInt64();

        // Con la versión vigente: el rechazo es por el estado, no por la versión.
        var respuesta = await PutAsync(
            cliente, $"{Ruta}/{codigo}", CuerpoModificacion("ABC-1", "Otra", 1210m, 60m, version), cookie);

        await AfirmarConflictoAsync(respuesta, "inactivo", ValidacionArticulo.DetalleInactivo);
        var leido = await Http.JsonAsync(await Http.GetAsync(cliente, $"{Ruta}/{codigo}", cookie));
        AfirmarArticulo(leido, codigo, "ABC-1", "Armazón", 1210m, 50m, 1815m, null);
        Assert.False(leido.GetProperty("activo").GetBoolean());
        Assert.Equal(version, leido.GetProperty("version").GetInt64());
    }

    /// <summary>
    /// Modificar con una versión que ya no es la guardada (otra pestaña grabó antes): 409 con <c>motivo</c>
    /// <c>cambiado</c>, y queda lo que grabó la otra pestaña (D-08, D-17, D-19).
    /// </summary>
    [Fact]
    public async Task Put_Cambiado_409()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);
        var primera = await PutAsync(
            cliente, $"{Ruta}/{codigo}", CuerpoModificacion("ABC-1", "Armazón", 1210m, 60m, 0), cookie);
        Assert.Equal(HttpStatusCode.OK, primera.StatusCode);

        var respuesta = await PutAsync(
            cliente, $"{Ruta}/{codigo}", CuerpoModificacion("ABC-1", "Otra descripción", 1210m, 50m, 0), cookie);

        await AfirmarConflictoAsync(respuesta, "cambiado", ValidacionArticulo.DetalleCambiadoAlEditar);
        var leido = await Http.JsonAsync(await Http.GetAsync(cliente, $"{Ruta}/{codigo}", cookie));
        AfirmarArticulo(leido, codigo, "ABC-1", "Armazón", 1210m, 60m, 1936m, null);
        Assert.Equal(1, leido.GetProperty("version").GetInt64());
    }

    /// <summary>
    /// Sin <c>version</c>, con null o fuera de 0 a 2^53 − 1: 400 en <c>version</c>, nunca una grabación sin protección
    /// (D-17).
    /// </summary>
    [Theory]
    [InlineData("{\"codigoProveedor\":\"ABC-1\",\"descripcion\":\"Otra\",\"precioCosto\":1210,\"margenUtilidad\":60}")]
    [InlineData("{\"codigoProveedor\":\"ABC-1\",\"descripcion\":\"Otra\",\"precioCosto\":1210,\"margenUtilidad\":60,\"version\":null}")]
    [InlineData("{\"codigoProveedor\":\"ABC-1\",\"descripcion\":\"Otra\",\"precioCosto\":1210,\"margenUtilidad\":60,\"version\":-1}")]
    [InlineData("{\"codigoProveedor\":\"ABC-1\",\"descripcion\":\"Otra\",\"precioCosto\":1210,\"margenUtilidad\":60,\"version\":9007199254740992}")]
    public async Task Put_VersionFaltante_400(string cuerpo)
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);

        var respuesta = await PutAsync(cliente, $"{Ruta}/{codigo}", cuerpo, cookie);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal(
            new Dictionary<string, string[]>
            {
                [ValidacionArticulo.CampoVersion] = [ValidacionArticulo.AccionVersionInvalida],
            },
            await ErroresAsync(respuesta));
        await AfirmarSinCambiosAsync(cliente, cookie, codigo);
    }

    /// <summary>
    /// Otra escritura confirma entre la lectura y la grabación del cambio de estado: 409 con <c>motivo</c>
    /// <c>cambiado</c> y el texto de D-16; el estado no cambia.
    /// </summary>
    [Fact]
    public async Task PutEstado_Cambiado_409()
    {
        var cruce = new Cruce();
        using var fabrica = new FabricaApi(servicios =>
        {
            servicios.RemoveAll<IUnidadDeTrabajo>();
            servicios.AddScoped<IUnidadDeTrabajo>(proveedor => new UnidadQueSeCruza(
                new UnidadDeTrabajoEf(proveedor.GetRequiredService<OpticaDbContext>()),
                proveedor.GetRequiredService<IServiceScopeFactory>(),
                cruce));
        });
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);
        cruce.Articulo = codigo;

        var respuesta = await PutAsync(cliente, $"{Ruta}/{codigo}/estado", CuerpoEstado(false), cookie);

        Assert.Null(cruce.Articulo);
        await AfirmarConflictoAsync(respuesta, "cambiado", ValidacionArticulo.DetalleCambiadoAlCambiarEstado);
        var leido = await Http.JsonAsync(await Http.GetAsync(cliente, $"{Ruta}/{codigo}", cookie));
        Assert.True(leido.GetProperty("activo").GetBoolean());
        // Solo la escritura cruzada incrementó la versión.
        Assert.Equal(1, leido.GetProperty("version").GetInt64());
    }

    /// <summary>AC-01: el detalle que abre la edición trae el precio de venta calculado, el estado y la versión.</summary>
    [Fact]
    public async Task Get_Articulo_MuestraPrecioVentaYVersion()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);

        var alta = await Http.JsonAsync(await Http.GetAsync(cliente, $"{Ruta}/{codigo}", cookie));
        var editado = await PutAsync(
            cliente, $"{Ruta}/{codigo}", CuerpoModificacion("ABC-1", "Armazón", 1210m, 60m, 0), cookie);
        Assert.Equal(HttpStatusCode.OK, editado.StatusCode);
        var despues = await Http.JsonAsync(await Http.GetAsync(cliente, $"{Ruta}/{codigo}", cookie));

        AfirmarArticulo(alta, codigo, "ABC-1", "Armazón", 1210m, 50m, 1815m, null);
        Assert.True(alta.GetProperty("activo").GetBoolean());
        Assert.Equal(0, alta.GetProperty("version").GetInt64());
        AfirmarArticulo(despues, codigo, "ABC-1", "Armazón", 1210m, 60m, 1936m, null);
        Assert.Equal(1, despues.GetProperty("version").GetInt64());
    }

    /// <summary>AC-02: alícuota 21, múltiplo 0,01, costo 1210; el margen pasa de 50 a 60 y se graba 1936.</summary>
    [Fact]
    public async Task Put_Margen50A60_200Con1936()
    {
        await AfirmarModificacionAsync(1210m, 60m, 1936m);
    }

    /// <summary>AC-03: alícuota 21, múltiplo 0,01, margen 50; el costo pasa de 1210 a 2420 y se graba 3630.</summary>
    [Fact]
    public async Task Put_Costo1210A2420_200Con3630()
    {
        await AfirmarModificacionAsync(2420m, 50m, 3630m);
    }

    /// <summary>
    /// AC-04: buscar "armazón" lista los artículos con "Armazón" o "ARMAZON" en la descripción y ningún otro, ordenados
    /// por descripción, en la primera página de 50; el listado no trae costo ni margen (D-12).
    /// </summary>
    [Fact]
    public async Task Get_Listado_Armazon_SinCostoNiMargen()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var metal = await GrabarArticuloAsync(cliente, cookie, "Armazón metal", "M-1");
        await GrabarArticuloAsync(cliente, cookie, "Estuche rígido", "E-1");
        var acetato = await GrabarArticuloAsync(cliente, cookie, "ARMAZON acetato", "A-1");

        var respuesta = await Http.GetAsync(cliente, $"{Ruta}?texto={Uri.EscapeDataString("armazón")}", cookie);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var json = await Http.JsonAsync(respuesta);
        Assert.Equal(
            ["articulos", "pagina", "tamanoPagina", "total"],
            json.EnumerateObject().Select(propiedad => propiedad.Name).ToArray());
        Assert.Equal(1, json.GetProperty("pagina").GetInt32());
        Assert.Equal(50, json.GetProperty("tamanoPagina").GetInt32());
        Assert.Equal(2, json.GetProperty("total").GetInt32());
        var articulos = json.GetProperty("articulos").EnumerateArray().ToList();
        Assert.Equal([acetato, metal], articulos.Select(a => a.GetProperty("codigo").GetInt64()).ToArray());
        foreach (var articulo in articulos)
        {
            Assert.Equal(
                ["codigo", "codigoProveedor", "descripcion", "precioVenta", "activo"],
                articulo.EnumerateObject().Select(propiedad => propiedad.Name).ToArray());
            Assert.Equal(1815m, articulo.GetProperty("precioVenta").GetDecimal());
            Assert.True(articulo.GetProperty("activo").GetBoolean());
        }

        Assert.Equal("ARMAZON acetato", articulos[0].GetProperty("descripcion").GetString());
        Assert.Equal("A-1", articulos[0].GetProperty("codigoProveedor").GetString());
        var texto = json.GetRawText();
        Assert.DoesNotContain("precioCosto", texto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("margen", texto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("1210", texto, StringComparison.Ordinal);

        // Sin texto, todo el catálogo; la página ausente vale 1.
        var todo = await Http.JsonAsync(await Http.GetAsync(cliente, Ruta, cookie));
        Assert.Equal(3, todo.GetProperty("total").GetInt32());
        Assert.Equal(1, todo.GetProperty("pagina").GetInt32());
    }

    /// <summary>
    /// AC-05: desactivar responde el artículo inactivo con la versión nueva y los datos intactos (costo, margen y precio
    /// de venta, con sesión), y el listado lo muestra inactivo.
    /// </summary>
    [Fact]
    public async Task PutEstado_Desactivar_ListadoInactivo()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);

        var respuesta = await PutAsync(cliente, $"{Ruta}/{codigo}/estado", CuerpoEstado(false), cookie);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var json = await Http.JsonAsync(respuesta);
        AfirmarArticulo(json, codigo, "ABC-1", "Armazón", 1210m, 50m, 1815m, null);
        Assert.False(json.GetProperty("activo").GetBoolean());
        Assert.Equal(1, json.GetProperty("version").GetInt64());
        var listado = await ListadoAsync(cliente, cookie);
        var fila = Assert.Single(listado);
        Assert.Equal(codigo, fila.GetProperty("codigo").GetInt64());
        Assert.Equal("Armazón", fila.GetProperty("descripcion").GetString());
        Assert.Equal(1815m, fila.GetProperty("precioVenta").GetDecimal());
        Assert.False(fila.GetProperty("activo").GetBoolean());
    }

    /// <summary>AC-06: reactivar un inactivo lo deja activo en la respuesta y en el listado.</summary>
    [Fact]
    public async Task PutEstado_Reactivar_ListadoActivo()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);
        var baja = await PutAsync(cliente, $"{Ruta}/{codigo}/estado", CuerpoEstado(false), cookie);
        Assert.Equal(HttpStatusCode.OK, baja.StatusCode);
        Assert.False(Assert.Single(await ListadoAsync(cliente, cookie)).GetProperty("activo").GetBoolean());

        var respuesta = await PutAsync(cliente, $"{Ruta}/{codigo}/estado", CuerpoEstado(true), cookie);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var json = await Http.JsonAsync(respuesta);
        AfirmarArticulo(json, codigo, "ABC-1", "Armazón", 1210m, 50m, 1815m, null);
        Assert.True(json.GetProperty("activo").GetBoolean());
        Assert.Equal(2, json.GetProperty("version").GetInt64());
        Assert.True(Assert.Single(await ListadoAsync(cliente, cookie)).GetProperty("activo").GetBoolean());
    }

    /// <summary>
    /// D-12, R-06: el texto buscado viaja en la query string y no llega al log, ni en una búsqueda que termina con el 500
    /// genérico. Con los niveles de appsettings (lo que se escribe en producción) no aparece en ninguna línea; con todos
    /// los niveles, solo puede aparecer en el registro de pedidos del propio ASP.NET Core, que appsettings deja en
    /// Warning, nunca en una línea de la aplicación, de EF Core ni del manejador de errores.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Get_Listado_ConError500_NoRegistraElTexto(bool nivelesDeConfiguracion)
    {
        const string Buscado = "Zq7-buscado-secreto";
        using var fabrica = new FabricaApi(
            servicios =>
            {
                servicios.RemoveAll<IArticuloRepository>();
                servicios.AddScoped<IArticuloRepository, ArticulosQueFallan>();
            },
            nivelesDeConfiguracion: nivelesDeConfiguracion);
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);

        var respuesta = await Http.GetAsync(cliente, $"{Ruta}?texto={Buscado}&pagina=2", cookie);

        Assert.Equal(HttpStatusCode.InternalServerError, respuesta.StatusCode);
        Assert.DoesNotContain(Buscado, await respuesta.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var lineas = fabrica.Registro.Lineas;
        // Control: el manejador registró el error con la ruta, sin la consulta.
        Assert.Contains(
            lineas,
            l => l.Nivel == LogLevel.Error
                && l.Mensaje.Contains("GET /api/articulos", StringComparison.Ordinal)
                && !l.Mensaje.Contains("texto=", StringComparison.Ordinal));
        var conElTexto = lineas.Where(l => l.TextoCompleto.Contains(Buscado, StringComparison.Ordinal)).ToList();
        if (nivelesDeConfiguracion)
        {
            Assert.Empty(conElTexto);
        }
        else
        {
            Assert.All(conElTexto, l => Assert.StartsWith("Microsoft.AspNetCore.", l.Categoria, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// Alerta de datos (R-01 de FEAT-002a, R-06): la descripción, el costo y el margen de un POST y de un PUT no llegan
    /// al log en ningún nivel ni en ninguna categoría, Trace incluido (donde MVC registra los argumentos de la acción),
    /// sin depender de los niveles de appsettings.
    /// </summary>
    [Fact]
    public async Task Log_PostYPut_NoRegistraDescripcionCostoNiMargen()
    {
        const string Descripcion = "Zq7-descripcion-secreta";
        const string DescripcionNueva = "Zq7-otra-descripcion-secreta";
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);

        var alta = await GrabarAsync(cliente, Cuerpo("ABC-1", Descripcion, 7654.31m, 43.21m), cookie);
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);
        var codigo = (await Http.JsonAsync(alta)).GetProperty("codigo").GetInt64();
        var modificacion = await PutAsync(
            cliente, $"{Ruta}/{codigo}", CuerpoModificacion("ABC-1", DescripcionNueva, 8765.42m, 54.32m, 0), cookie);
        Assert.Equal(HttpStatusCode.OK, modificacion.StatusCode);

        var lineas = fabrica.Registro.Lineas;
        // Control: la captura ve Trace, y MVC registró la ejecución de las dos acciones.
        Assert.Contains(lineas, l => l.Nivel == LogLevel.Trace);
        Assert.Contains(lineas, l => l.Mensaje.Contains(nameof(ArticulosController.Modificar), StringComparison.Ordinal));
        foreach (var secreto in new[] { "Zq7", "7654.31", "7654,31", "43.21", "43,21", "8765.42", "8765,42", "54.32", "54,32" })
        {
            var conElDato = lineas.Where(l => l.TextoCompleto.Contains(secreto, StringComparison.Ordinal))
                .Select(l => $"{l.Categoria} ({l.Nivel})")
                .ToList();
            Assert.True(conElDato.Count == 0, $"\"{secreto}\" llegó al log en: {string.Join(", ", conElDato)}");
        }
    }

    /// <summary>Un texto de búsqueda de 201 caracteres: 400 en <c>texto</c>, sin buscar.</summary>
    [Fact]
    public async Task Get_Listado_Texto201Caracteres_400EnTexto()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        await GrabarArticuloAsync(cliente, cookie, "Armazón " + new string('a', 190));

        var largo = await Http.GetAsync(cliente, $"{Ruta}?texto={new string('a', 201)}", cookie);
        var justo = await Http.GetAsync(cliente, $"{Ruta}?texto={new string('a', 190)}", cookie);

        Assert.Equal(HttpStatusCode.BadRequest, largo.StatusCode);
        Assert.Equal(
            new Dictionary<string, string[]> { [ValidacionArticulo.CampoTexto] = [ValidacionArticulo.AccionTextoLargo] },
            await ErroresAsync(largo));
        // Control: con menos de 200 caracteres la misma búsqueda encuentra el artículo.
        Assert.Equal(HttpStatusCode.OK, justo.StatusCode);
        Assert.Equal(1, (await Http.JsonAsync(justo)).GetProperty("total").GetInt32());
    }

    /// <summary>
    /// Modificar el código en el proveedor por el de otro artículo (sin distinguir mayúsculas ni espacios): 400 en
    /// <c>codigoProveedor</c>, sin revelar el otro artículo y sin grabar.
    /// </summary>
    [Fact]
    public async Task Put_CodigoDuplicado_400EnCodigoProveedor()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        await GrabarArticuloAsync(cliente, cookie, "Armazón Zq7-marcado", "ABC-1");
        var codigo = await GrabarArticuloAsync(cliente, cookie, "Estuche", "XYZ-9");

        var respuesta = await PutAsync(
            cliente, $"{Ruta}/{codigo}", CuerpoModificacion(" abc-1 ", "Estuche", 1210m, 60m, 0), cookie);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal(
            new Dictionary<string, string[]>
            {
                [ValidacionArticulo.CampoCodigoProveedor] = [ValidacionArticulo.AccionCodigoDuplicado],
            },
            await ErroresAsync(respuesta));
        Assert.DoesNotContain("Zq7-marcado", await respuesta.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var leido = await Http.JsonAsync(await Http.GetAsync(cliente, $"{Ruta}/{codigo}", cookie));
        AfirmarArticulo(leido, codigo, "XYZ-9", "Estuche", 1210m, 50m, 1815m, null);
        Assert.Equal(0, leido.GetProperty("version").GetInt64());
    }

    /// <summary>Un cambio de estado sin <c>activo</c>: 400 en <c>activo</c>, sin cambiar el estado.</summary>
    [Fact]
    public async Task PutEstado_SinActivo_400EnActivo()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);

        var respuesta = await PutAsync(cliente, $"{Ruta}/{codigo}/estado", "{}", cookie);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal(
            new Dictionary<string, string[]>
            {
                [ValidacionArticulo.CampoActivo] = [ValidacionArticulo.AccionActivoFaltante],
            },
            await ErroresAsync(respuesta));
        await AfirmarSinCambiosAsync(cliente, cookie, codigo);
    }

    private static string CuerpoModificacion(
        string? codigoProveedor, string? descripcion, decimal? precioCosto, decimal? margenUtilidad, long? version) =>
        JsonSerializer.Serialize(new { codigoProveedor, descripcion, precioCosto, margenUtilidad, version });

    private static string CuerpoEstado(bool activo) => activo ? "{\"activo\":true}" : "{\"activo\":false}";

    private static Task<HttpResponseMessage> PutAsync(HttpClient cliente, string ruta, string cuerpo, string cookie) =>
        Http.EnviarAsync(
            cliente, HttpMethod.Put, ruta, new StringContent(cuerpo, Encoding.UTF8, "application/json"), cookie);

    /// <summary>Graba un artículo activo con costo 1210 y margen 50 (precio de venta 1815) y devuelve su código.</summary>
    private static async Task<long> GrabarArticuloAsync(
        HttpClient cliente, string cookie, string descripcion = "Armazón", string codigoProveedor = "ABC-1")
    {
        var respuesta = await GrabarAsync(cliente, Cuerpo(codigoProveedor, descripcion, 1210m, 50m), cookie);
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await Http.JsonAsync(respuesta)).GetProperty("codigo").GetInt64();
    }

    /// <summary>Los artículos de la primera página del listado sin texto.</summary>
    private static async Task<List<JsonElement>> ListadoAsync(HttpClient cliente, string cookie)
    {
        var respuesta = await Http.GetAsync(cliente, Ruta, cookie);
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        return [.. (await Http.JsonAsync(respuesta)).GetProperty("articulos").EnumerateArray()];
    }

    /// <summary>Modifica el artículo de AC-02 y AC-03 y comprueba la respuesta y lo que quedó grabado.</summary>
    private static async Task AfirmarModificacionAsync(decimal costo, decimal margen, decimal precioVenta)
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);

        var respuesta = await PutAsync(
            cliente, $"{Ruta}/{codigo}", CuerpoModificacion("ABC-1", "Armazón", costo, margen, 0), cookie);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var json = await Http.JsonAsync(respuesta);
        AfirmarArticulo(json, codigo, "ABC-1", "Armazón", costo, margen, precioVenta, null);
        Assert.True(json.GetProperty("activo").GetBoolean());
        Assert.Equal(1, json.GetProperty("version").GetInt64());
        var leido = await Http.JsonAsync(await Http.GetAsync(cliente, $"{Ruta}/{codigo}", cookie));
        Assert.Equal(json.GetRawText(), leido.GetRawText());
    }

    /// <summary>409 con ProblemDetails: <c>title</c> fijo, el texto del caso de uso y el <c>motivo</c> (D-19).</summary>
    private static async Task AfirmarConflictoAsync(HttpResponseMessage respuesta, string motivo, string detalle)
    {
        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        var json = await Http.JsonAsync(respuesta);
        Assert.Equal(409, json.GetProperty("status").GetInt32());
        Assert.Equal("No se pudo grabar el artículo.", json.GetProperty("title").GetString());
        Assert.Equal(detalle, json.GetProperty("detail").GetString());
        Assert.Equal(motivo, json.GetProperty("motivo").GetString());
        Assert.False(json.TryGetProperty("errors", out _));
    }

    /// <summary>El artículo sigue como lo dejó <see cref="GrabarArticuloAsync"/>: mismos datos, activo y versión 0.</summary>
    private static async Task AfirmarSinCambiosAsync(HttpClient cliente, string cookie, long codigo)
    {
        var respuesta = await Http.GetAsync(cliente, $"{Ruta}/{codigo}", cookie);
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var json = await Http.JsonAsync(respuesta);
        AfirmarArticulo(json, codigo, "ABC-1", "Armazón", 1210m, 50m, 1815m, null);
        Assert.True(json.GetProperty("activo").GetBoolean());
        Assert.Equal(0, json.GetProperty("version").GetInt64());
    }

    /// <summary>El artículo en el que la próxima confirmación cruza otra escritura; null después de cruzarla.</summary>
    private sealed class Cruce
    {
        public long? Articulo { get; set; }
    }

    /// <summary>
    /// La unidad de trabajo real, precedida una vez por otra escritura confirmada sobre el mismo artículo (incrementa su
    /// versión desde otro alcance), como otra pestaña que graba entre la lectura y la grabación (D-16).
    /// </summary>
    /// <remarks>
    /// La escritura cruzada abre otra conexión que escribe: dentro de <see cref="EjecutarEnTransaccionAsync"/> esperaría
    /// la transacción de la propia operación hasta agotar el tiempo de espera. Por eso, con un cruce pendiente, recibir
    /// una operación lanza en lugar de bloquearse (D-19 de FEAT-002c).
    /// </remarks>
    private sealed class UnidadQueSeCruza(IUnidadDeTrabajo interna, IServiceScopeFactory alcances, Cruce cruce)
        : IUnidadDeTrabajo
    {
        public Task<T> EjecutarEnTransaccionAsync<T>(
            Func<CancellationToken, Task<T>> operacion, CancellationToken cancelacion = default) =>
            cruce.Articulo is not null
                ? throw new InvalidOperationException(
                    "La escritura cruzada no puede correr dentro de una operación en transacción: se bloquearía.")
                : interna.EjecutarEnTransaccionAsync(operacion, cancelacion);

        public async Task ConfirmarAsync(CancellationToken cancelacion = default)
        {
            if (cruce.Articulo is { } codigo)
            {
                cruce.Articulo = null;
                using var alcance = alcances.CreateScope();
                var otra = alcance.ServiceProvider.GetRequiredService<OpticaDbContext>();
                await otra.Database.ExecuteSqlAsync(
                    $"UPDATE \"Articulo\" SET \"Version\" = \"Version\" + 1 WHERE \"Id\" = {codigo}", cancelacion);
            }

            await interna.ConfirmarAsync(cancelacion);
        }
    }

    /// <summary>Un repositorio cuya búsqueda falla con un error que no es de la base: el manejador responde 500.</summary>
    private sealed class ArticulosQueFallan : IArticuloRepository
    {
        private static InvalidOperationException Falla() => new("falla de prueba");

        public Task<bool> ExisteClaveAsync(string clave, CancellationToken cancelacion = default) => throw Falla();

        public void Agregar(Articulo articulo) => throw Falla();

        public Task<Articulo?> ObtenerAsync(long codigo, CancellationToken cancelacion = default) => throw Falla();

        public Task<Articulo?> ObtenerPorClaveAsync(string clave, CancellationToken cancelacion = default) =>
            throw Falla();

        public Task<PaginaArticulos> BuscarAsync(
            string? textoPlegado, int pagina, int tamano, bool soloActivos = false,
            CancellationToken cancelacion = default) => throw Falla();

        public Task<bool> ExisteClaveDeOtroAsync(string clave, long codigo, CancellationToken cancelacion = default) =>
            throw Falla();

        public void Actualizar(Articulo articulo, long versionLeida) => throw Falla();

        public void CambiarEstado(long codigo, bool activo) => throw Falla();

        public Task<IReadOnlyList<Articulo>> ListarParaRecalculoAsync(CancellationToken cancelacion = default) =>
            throw Falla();

        public void RegistrarPrecioRecalculado(Articulo articulo) => throw Falla();
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
            [
                "codigo", "codigoProveedor", "descripcion", "precioCosto", "margenUtilidad", "precioVenta", "advertencia",
                "activo", "version",
            ],
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
