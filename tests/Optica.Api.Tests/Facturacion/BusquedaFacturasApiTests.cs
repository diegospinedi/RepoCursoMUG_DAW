using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Optica.Pruebas.Compartido;

namespace Optica.Api.Tests.Facturacion;

/// <summary>
/// Spec FEAT-009, Block 6: <c>POST /api/facturacion/busqueda</c> de punta a punta con la base SQLite (D-28, ADR-019). Los
/// filtros viajan en el cuerpo (hasta 4 KB) y no se registran (R-01); la respuesta lleva el comprobante como texto
/// (D-15) y Apellido, Nombre y DNI del cliente del presupuesto (FR-25). Datos ficticios del PRD con DNI que empiezan
/// con 00 (D-08).
/// </summary>
public sealed class BusquedaFacturasApiTests
{
    private const string Ruta = "/api/facturacion/busqueda";

    private const string AccionCuerpoIlegible = "No se pudieron leer los filtros enviados. Revisalos y buscá de nuevo.";

    private const int Limite = 4 * 1024;

    private static readonly string[] CamposRespuesta = ["facturas", "pagina", "tamanoPagina", "total"];

    private static readonly string[] CamposFactura =
        ["presupuesto", "comprobante", "fecha", "apellido", "nombre", "dni", "total"];

    /// <summary>
    /// AC-23 y AC-26: la Factura B 0003-00000125 emitida sin identificar al receptor desde el presupuesto 155 de "Pérez,
    /// Ana" (DNI 00123456) se encuentra por su DNI escrito con puntos y trae el tipo con el punto de venta y número como
    /// texto, la fecha, Apellido, Nombre, DNI, total y el presupuesto 155; la respuesta lleva la página y el tamaño 50.
    /// </summary>
    [Fact]
    public async Task PostBusqueda_Resultado_ColumnasDeAC23()
    {
        using var fabrica = new FabricaApi();
        await SiembraFacturas.SembrarAsync(
            fabrica.Services,
            [
                new FacturaSembrada(155, new DateOnly(2026, 3, 20), "Pérez", "Ana", "00123456", 3, 125, Identificado: false),
                new FacturaSembrada(156, new DateOnly(2026, 3, 21), "Gómez", "María José", "00345678", 3, 126),
            ]);
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);

        using var respuesta = await Http.PostJsonAsync(cliente, Ruta, "{\"dni\":\"00.123.456\",\"pagina\":1}", cookie);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var json = await Http.JsonAsync(respuesta);
        Assert.Equal(CamposRespuesta, json.EnumerateObject().Select(propiedad => propiedad.Name).ToArray());
        Assert.Equal((1, 50, 1), (json.GetProperty("pagina").GetInt32(), json.GetProperty("tamanoPagina").GetInt32(),
            json.GetProperty("total").GetInt32()));
        var factura = Assert.Single(json.GetProperty("facturas").EnumerateArray());
        Assert.Equal(CamposFactura, factura.EnumerateObject().Select(propiedad => propiedad.Name).ToArray());
        Assert.Equal(155, factura.GetProperty("presupuesto").GetInt64());
        Assert.Equal("Factura B 0003-00000125", factura.GetProperty("comprobante").GetString());
        Assert.Equal("2026-03-20", factura.GetProperty("fecha").GetString());
        Assert.Equal("Pérez", factura.GetProperty("apellido").GetString());
        Assert.Equal("Ana", factura.GetProperty("nombre").GetString());
        Assert.Equal("00123456", factura.GetProperty("dni").GetString());
        Assert.Equal(SiembraFacturas.TotalSembrado, factura.GetProperty("total").GetDecimal());

        // Sin filtros, las dos, de la más reciente a la más antigua (FR-22).
        using var todas = await Http.PostJsonAsync(cliente, Ruta, "{}", cookie);
        Assert.Equal(HttpStatusCode.OK, todas.StatusCode);
        Assert.Equal(
            [156L, 155L],
            (await Http.JsonAsync(todas)).GetProperty("facturas").EnumerateArray()
                .Select(f => f.GetProperty("presupuesto").GetInt64()));
    }

    /// <summary>
    /// Sad path (AC-22): Desde posterior a Hasta responde 400 con el error junto a <c>desde</c> y no busca. Una fecha
    /// inválida, una página fuera de rango o un texto de más de 100 caracteres, 400 por campo, sin repetir lo enviado.
    /// </summary>
    [Fact]
    public async Task PostBusqueda_DesdePosterior_400JuntoADesde()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);

        using var rango = await Http.PostJsonAsync(
            cliente, Ruta, "{\"desde\":\"2026-03-20\",\"hasta\":\"2026-03-10\"}", cookie);

        Assert.Equal(HttpStatusCode.BadRequest, rango.StatusCode);
        Assert.Equal("application/problem+json", rango.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            new Dictionary<string, string[]> { ["desde"] = ["La fecha Desde no puede ser posterior a Hasta."] },
            await ErroresAsync(rango));

        using var campos = await Http.PostJsonAsync(
            cliente,
            Ruta,
            JsonSerializer.Serialize(new
            {
                apellido = "Zq7" + new string('a', 98),
                nombre = "Zq7" + new string('b', 98),
                dni = "Zq7" + new string('1', 98),
                numero = "Zq7" + new string('2', 98),
                desde = "2026-02-30",
                hasta = "Zq7-fecha",
                pagina = 0,
            }),
            cookie);

        Assert.Equal(HttpStatusCode.BadRequest, campos.StatusCode);
        Assert.Equal(
            new Dictionary<string, string[]>
            {
                ["apellido"] = ["Usá hasta 100 caracteres."],
                ["nombre"] = ["Usá hasta 100 caracteres."],
                ["dni"] = ["Usá hasta 100 caracteres."],
                ["numero"] = ["Usá hasta 100 caracteres."],
                ["desde"] = ["La fecha no es válida."],
                ["hasta"] = ["La fecha no es válida."],
                ["pagina"] = ["Pedí una página entre 1 y 1.000.000."],
            },
            await ErroresAsync(campos));
        var texto = await campos.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Zq7", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("2026-02-30", texto, StringComparison.Ordinal);

        // Con 100 caracteres justos se busca (los textos se miden recortados).
        using var justo = await Http.PostJsonAsync(
            cliente, Ruta, JsonSerializer.Serialize(new { apellido = "  " + new string('a', 100) + "  " }), cookie);
        Assert.Equal(HttpStatusCode.OK, justo.StatusCode);

        // El recorte es el del molde (NormalizacionCodigo.Recortar): también quita U+FEFF pegado desde una planilla.
        using var conMarca = await Http.PostJsonAsync(
            cliente, Ruta, JsonSerializer.Serialize(new { apellido = "﻿" + new string('a', 100) + "﻿" }), cookie);
        Assert.Equal(HttpStatusCode.OK, conMarca.StatusCode);
    }

    /// <summary>
    /// D-42: el error de rango (Desde posterior a Hasta) lo da el dominio, así que aparece solo cuando los demás campos
    /// son válidos. Un Apellido de 101 caracteres con fechas válidas y el rango invertido da un único error, en
    /// <c>apellido</c>.
    /// </summary>
    [Fact]
    public async Task PostBusqueda_TextoLargoYRangoInvertido_SoloErrorDelCampo()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);

        using var respuesta = await Http.PostJsonAsync(
            cliente,
            Ruta,
            JsonSerializer.Serialize(new { apellido = new string('a', 101), desde = "2026-03-20", hasta = "2026-03-10" }),
            cookie);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal(
            new Dictionary<string, string[]> { ["apellido"] = ["Usá hasta 100 caracteres."] },
            await ErroresAsync(respuesta));
    }

    /// <summary>
    /// Sad path: un cuerpo ilegible (JSON roto, la página como texto, un número donde va un texto) responde 400 con un
    /// solo error y la acción fija de la búsqueda, en el campo que nombra el lector o en <c>apellido</c>, sin lo enviado.
    /// </summary>
    [Fact]
    public async Task PostBusqueda_CuerpoIlegible_400()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);

        foreach (var (cuerpo, campo) in new[]
        {
            ("esto no es json", "apellido"),
            ("{\"apellido\":\"Zq7-marcado", "apellido"),
            ("{\"pagina\":\"2\"}", "pagina"),
            ("{\"numero\":34561}", "numero"),
            ("{\"dni\":345678}", "dni"),
            ("{\"desde\":20260310}", "desde"),
        })
        {
            using var respuesta = await Http.PostJsonAsync(cliente, Ruta, cuerpo, cookie);

            Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
            Assert.Equal(
                new Dictionary<string, string[]> { [campo] = [AccionCuerpoIlegible] }, await ErroresAsync(respuesta));
            var texto = await respuesta.Content.ReadAsStringAsync();
            Assert.DoesNotContain("Zq7", texto, StringComparison.Ordinal);
            Assert.DoesNotContain("34561", texto, StringComparison.Ordinal);
            Assert.DoesNotContain("345678", texto, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Sad path: un cuerpo de más de 4 KB responde 413, con <c>Content-Length</c> y sin él; en el límite exacto, 200.
    /// </summary>
    [Fact]
    public async Task PostBusqueda_CuerpoGrande_413()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);

        foreach (var conLargo in new[] { true, false })
        {
            using var rechazo = await EnviarAsync(cliente, CuerpoDeBytes(Limite + 1), conLargo, cookie);
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, rechazo.StatusCode);
            Assert.DoesNotContain("00345", await rechazo.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            using var aceptado = await EnviarAsync(cliente, CuerpoDeBytes(Limite), conLargo, cookie);
            Assert.Equal(HttpStatusCode.OK, aceptado.StatusCode);
        }
    }

    /// <summary>
    /// Sad path: sin sesión responde 401 sin cuerpo, antes de leer el cuerpo y antes del 413; la ruta existe y no es
    /// anónima.
    /// </summary>
    [Fact]
    public async Task PostBusqueda_SinSesion_401()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        Optica.Api.Tests.Acceso.ProteccionTests.AfirmarQueElEndpointExiste(fabrica, Ruta);
        Assert.False(Optica.Api.Acceso.RutasAnonimas.Contiene("POST", Ruta));

        foreach (var contenido in new HttpContent[]
        {
            new StringContent("{\"dni\":\"00.345\"}", Encoding.UTF8, "application/json"),
            new StringContent("esto no es json", Encoding.UTF8, "application/json"),
            new StringContent(CuerpoDeBytes(Limite + 1), Encoding.UTF8, "application/json"),
        })
        {
            using var respuesta = await Http.EnviarAsync(cliente, HttpMethod.Post, Ruta, contenido);

            Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
            Assert.Equal(string.Empty, await respuesta.Content.ReadAsStringAsync());
        }
    }

    /// <summary>
    /// R-01 (Ley 25.326): con la captura del log en Trace, ni una búsqueda válida ni una rechazada ni un cuerpo ilegible
    /// dejan Apellido, Nombre, DNI o número en ningún mensaje.
    /// </summary>
    [Fact]
    public async Task PostBusqueda_Registro_SinDatosPersonales()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);

        using var valida = await Http.PostJsonAsync(
            cliente,
            Ruta,
            "{\"apellido\":\"Zq7Apellido\",\"nombre\":\"Zq7Nombre\",\"dni\":\"00.765.432\",\"numero\":\"0009-00076543\"}",
            cookie);
        Assert.Equal(HttpStatusCode.OK, valida.StatusCode);
        using var invalida = await Http.PostJsonAsync(
            cliente,
            Ruta,
            "{\"apellido\":\"Zq7Apellido\",\"dni\":\"00.765.432\",\"desde\":\"2026-03-20\",\"hasta\":\"2026-03-10\"}",
            cookie);
        Assert.Equal(HttpStatusCode.BadRequest, invalida.StatusCode);
        using var ilegible = await Http.PostJsonAsync(cliente, Ruta, "{\"apellido\":\"Zq7Apellido\",\"dni\":\"ZZMARCA", cookie);
        Assert.Equal(HttpStatusCode.BadRequest, ilegible.StatusCode);

        var lineas = fabrica.Registro.Lineas;
        // Control: la captura ve Trace y MVC registró la ejecución de la acción (depende del texto de MVC en Trace).
        Assert.Contains(lineas, l => l.Nivel == LogLevel.Trace);
        Assert.Contains(lineas, l => l.Mensaje.Contains("FacturacionController.Buscar", StringComparison.Ordinal));
        foreach (var secreto in new[] { "Zq7", "zq7", "00765432", "00.765.432", "076543", "ZZMARCA" })
        {
            var conElDato = lineas.Where(l => l.TextoCompleto.Contains(secreto, StringComparison.Ordinal))
                .Select(l => $"{l.Categoria} ({l.Nivel})")
                .ToList();
            Assert.True(conElDato.Count == 0, $"\"{secreto}\" llegó al log en: {string.Join(", ", conElDato)}");
        }
    }

    /// <summary>Una búsqueda válida con un DNI ficticio y una propiedad que se ignora, hasta ocupar <paramref name="bytes"/>.</summary>
    private static string CuerpoDeBytes(int bytes)
    {
        const string Base = "{\"dni\":\"00345\",\"relleno\":\"";
        var faltan = bytes - Encoding.UTF8.GetByteCount(Base + "\"}");
        var cuerpo = Base + new string('a', faltan) + "\"}";
        Assert.Equal(bytes, Encoding.UTF8.GetByteCount(cuerpo));
        return cuerpo;
    }

    private static Task<HttpResponseMessage> EnviarAsync(HttpClient cliente, string cuerpo, bool conLargo, string cookie)
    {
        HttpContent contenido = conLargo
            ? new StringContent(cuerpo, Encoding.UTF8, "application/json")
            : new Http.ContenidoSinLargo(cuerpo, "application/json");
        return Http.EnviarAsync(cliente, HttpMethod.Post, Ruta, contenido, cookie);
    }

    private static async Task<Dictionary<string, string[]>> ErroresAsync(HttpResponseMessage respuesta)
    {
        var json = await Http.JsonAsync(respuesta);
        return json.GetProperty("errors").EnumerateObject().ToDictionary(
            campo => campo.Name,
            campo => campo.Value.EnumerateArray().Select(valor => valor.GetString()!).ToArray());
    }
}
