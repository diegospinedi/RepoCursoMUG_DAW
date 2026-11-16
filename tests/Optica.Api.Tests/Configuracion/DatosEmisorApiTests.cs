using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Optica.Api.Controllers;
using Optica.Api.Errores;
using Optica.Application.Configuracion;
using Optica.Infrastructure.Persistencia;

namespace Optica.Api.Tests.Configuracion;

/// <summary>
/// <c>GET</c> y <c>POST /api/configuracion/emisor</c> (FEAT-009, Block 2, ADR-028): con sesión, la Api traduce los casos
/// de uso de los datos del emisor; el POST graba los cinco juntos o responde 400 con un error por campo; el cuerpo exige
/// JSON y hasta 2 KB (D-21). Todos los datos son inventados; el CUIT es el ficticio 20-00000000-1 (D-33).
/// </summary>
public sealed class DatosEmisorApiTests
{
    private const string Ruta = "/api/configuracion/emisor";
    private const int Limite = 2 * 1024;

    private const string RazonSocial = "Zq7Óptica Ficticia SA";
    private const string Domicilio = "Zq7Calle Inventada 123, Ciudad Ficticia";
    private const string CuitConGuiones = "20-00000000-1";
    private const string Cuit = "20000000001";
    private const string IngresosBrutos = "Zq7IB-0001";
    private const string Inicio = "2020-03-01";

    private static readonly string[] Claves =
        ["razonSocial", "domicilioComercial", "cuit", "ingresosBrutos", "inicioActividades"];

    [Fact]
    public async Task GetEmisor_SinDatos_200ConNulls()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);

        var respuesta = await Http.GetAsync(cliente, Ruta, cookie);

        // AC-05 (estado previo): sin datos cargados no es un 404, es el 200 con los cinco en null (D-22).
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("application/json", respuesta.Content.Headers.ContentType?.MediaType);
        await AfirmarSinDatosAsync(cliente, cookie);
    }

    [Fact]
    public async Task PostEmisor_CuitConGuiones_200SinGuiones()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);

        var respuesta = await GrabarAsync(cliente, Cuerpo(cuit: CuitConGuiones), cookie);

        // AC-03: el CUIT se guarda y se devuelve sin guiones; AC-01: el GET posterior devuelve los cinco.
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        AfirmarDatos(await Http.JsonAsync(respuesta), RazonSocial, Domicilio, Cuit, IngresosBrutos, Inicio);
        var posterior = await Http.GetAsync(cliente, Ruta, cookie);
        Assert.Equal(HttpStatusCode.OK, posterior.StatusCode);
        AfirmarDatos(await Http.JsonAsync(posterior), RazonSocial, Domicilio, Cuit, IngresosBrutos, Inicio);

        // La grabación quedó en el log con los nombres de los campos, y ningún valor en ninguna línea (R-06).
        var lineas = fabrica.Registro.Lineas;
        var grabacion = Assert.Single(lineas, linea => linea.Categoria == GrabarDatosEmisor.CategoriaRegistro);
        Assert.Contains("razonSocial", grabacion.Mensaje, StringComparison.Ordinal);
        foreach (var linea in lineas)
        {
            foreach (var valor in new[] { "Zq7", CuitConGuiones, Cuit, Inicio })
            {
                Assert.DoesNotContain(valor, linea.TextoCompleto, StringComparison.Ordinal);
            }
        }
    }

    [Theory]
    [InlineData("20-00000000-5")]
    [InlineData("2000000000")]
    public async Task PostEmisor_CuitInvalido_400JuntoAlCampo(string cuit)
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);

        var respuesta = await GrabarAsync(cliente, Cuerpo(cuit: cuit), cookie);

        // AC-04: no se graba y el error va junto al campo del CUIT, sin repetir lo enviado.
        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            new Dictionary<string, string[]> { [ValidacionConfiguracion.CampoCuit] = [ValidacionConfiguracion.AccionCuitInvalido] },
            await ErroresAsync(respuesta));
        Assert.DoesNotContain(cuit, await respuesta.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await AfirmarSinDatosAsync(cliente, cookie);
    }

    [Fact]
    public async Task PostEmisor_VariosInvalidos_400ConTodosLosCampos()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);

        var respuesta = await GrabarAsync(
            cliente, "{\"razonSocial\":\"\",\"cuit\":\"20-00000000-5\",\"inicioActividades\":\"2099-01-01\"}", cookie);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal(
            new Dictionary<string, string[]>
            {
                [ValidacionConfiguracion.CampoRazonSocial] = [ValidacionConfiguracion.AccionRazonSocialFaltante],
                [ValidacionConfiguracion.CampoDomicilioComercial] = [ValidacionConfiguracion.AccionDomicilioComercialFaltante],
                [ValidacionConfiguracion.CampoCuit] = [ValidacionConfiguracion.AccionCuitInvalido],
                [ValidacionConfiguracion.CampoIngresosBrutos] = [ValidacionConfiguracion.AccionIngresosBrutosFaltante],
                [ValidacionConfiguracion.CampoInicioActividades] = [ValidacionConfiguracion.AccionInicioActividadesPosterior],
            },
            await ErroresAsync(respuesta));
        await AfirmarSinDatosAsync(cliente, cookie);
    }

    [Theory]
    [InlineData("esto no es json", "razonSocial")]
    [InlineData("[1,2,3]", "razonSocial")]
    [InlineData("{\"razonSocial\":\"Zq7-marcado", "razonSocial")]
    [InlineData(
        "{\"razonSocial\":\"Zq7-marcado\",\"domicilioComercial\":\"Zq7-marcado\",\"cuit\":20000000001,"
        + "\"ingresosBrutos\":\"Zq7-marcado\",\"inicioActividades\":\"2020-03-01\"}",
        "cuit")]
    [InlineData(
        "{\"razonSocial\":\"Zq7-marcado\",\"domicilioComercial\":\"Zq7-marcado\",\"cuit\":\"20000000001\","
        + "\"ingresosBrutos\":\"Zq7-marcado\",\"inicioActividades\":20200301}",
        "inicioActividades")]
    [InlineData(
        "{\"razonSocial\":\"Zq7-marcado\",\"domicilioComercial\":[\"Zq7-marcado\"],\"cuit\":\"20000000001\","
        + "\"ingresosBrutos\":\"Zq7-marcado\",\"inicioActividades\":\"2020-03-01\"}",
        "domicilioComercial")]
    public async Task PostEmisor_CuerpoIlegible_400(string cuerpo, string campo)
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);

        var respuesta = await GrabarAsync(cliente, cuerpo, cookie);

        // Un error en el campo del cuerpo, con una acción fija: ni los mensajes del lector de JSON ni lo enviado.
        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            new Dictionary<string, string[]> { [campo] = [RespuestasValidacion.AccionCuerpoIlegibleEmisor] },
            await ErroresAsync(respuesta));
        var texto = await respuesta.Content.ReadAsStringAsync();
        Assert.DoesNotContain("JSON", texto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LineNumber", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("System.", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("Zq7", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("20000000001", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("$.", texto, StringComparison.Ordinal);
        await AfirmarSinDatosAsync(cliente, cookie);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PostEmisor_CuerpoGrande_413(bool conLargo)
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);

        // Un cuerpo válido de un byte más que el límite: solo el tamaño lo rechaza.
        var grande = CuerpoDeBytes(Limite + 1);
        var rechazo = await EnviarAsync(cliente, grande, conLargo, cookie);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, rechazo.StatusCode);
        Assert.DoesNotContain("Zq7", await rechazo.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await AfirmarSinDatosAsync(cliente, cookie);

        // Control del 413 (D-21): en el límite exacto se acepta, y una carga válida con caracteres de 3 bytes que pasa
        // 1 KB también.
        var exacto = await EnviarAsync(cliente, CuerpoDeBytes(Limite), conLargo, cookie);
        Assert.Equal(HttpStatusCode.OK, exacto.StatusCode);
        var tresBytes = Cuerpo(
            razonSocial: new string('€', 120),
            domicilio: new string('€', 200),
            ingresosBrutos: new string('€', 30));
        Assert.InRange(Encoding.UTF8.GetByteCount(tresBytes), 1025, Limite);
        var anchos = await EnviarAsync(cliente, tresBytes, conLargo, cookie);
        Assert.Equal(HttpStatusCode.OK, anchos.StatusCode);
        AfirmarDatos(
            await Http.JsonAsync(anchos), new string('€', 120), new string('€', 200), Cuit, new string('€', 30), Inicio);
    }

    /// <summary>
    /// Un cuerpo vacío con <c>Content-Type: application/json</c> se toma como un pedido sin datos, igual que en
    /// <c>POST /api/configuracion</c>: 400 con un error por cada uno de los cinco campos faltantes, sin grabar. No es un
    /// cuerpo ilegible.
    /// </summary>
    [Fact]
    public async Task PostEmisor_CuerpoVacio_400PorCampo()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);

        var respuesta = await GrabarAsync(cliente, string.Empty, cookie);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            new Dictionary<string, string[]>
            {
                [ValidacionConfiguracion.CampoRazonSocial] = [ValidacionConfiguracion.AccionRazonSocialFaltante],
                [ValidacionConfiguracion.CampoDomicilioComercial] = [ValidacionConfiguracion.AccionDomicilioComercialFaltante],
                [ValidacionConfiguracion.CampoCuit] = [ValidacionConfiguracion.AccionCuitFaltante],
                [ValidacionConfiguracion.CampoIngresosBrutos] = [ValidacionConfiguracion.AccionIngresosBrutosFaltante],
                [ValidacionConfiguracion.CampoInicioActividades] = [ValidacionConfiguracion.AccionInicioActividadesFaltante],
            },
            await ErroresAsync(respuesta));
        await AfirmarSinDatosAsync(cliente, cookie);
    }

    /// <summary>
    /// Una fila corrupta (un CUIT con dígito verificador inválido, que el CHECK de la base no controla, cargada a mano)
    /// responde el 500 genérico en el GET y en el POST, sin el texto de la excepción ni los valores guardados.
    /// </summary>
    [Fact]
    public async Task Emisor_FilaCorrupta_Error500Generico()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        using (var alcance = fabrica.Services.CreateScope())
        {
            var contexto = alcance.ServiceProvider.GetRequiredService<OpticaDbContext>();
            Assert.Equal(
                1,
                await contexto.Database.ExecuteSqlAsync(
                    $"INSERT INTO DatosEmisor (Id, RazonSocial, DomicilioComercial, Cuit, IngresosBrutos, InicioActividades) VALUES (1, {"Zq7Corrupta SA"}, {"Zq7Calle Inventada 9"}, {"20000000005"}, {"Zq7IB-9"}, {"2020-03-01"})"));
        }

        var lectura = await Http.GetAsync(cliente, Ruta, cookie);
        var grabacion = await GrabarAsync(cliente, Cuerpo(), cookie);

        foreach (var respuesta in new[] { lectura, grabacion })
        {
            Assert.Equal(HttpStatusCode.InternalServerError, respuesta.StatusCode);
            Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
            var json = await Http.JsonAsync(respuesta);
            Assert.Equal("No se pudo completar el pedido.", json.GetProperty("title").GetString());
            var texto = json.GetRawText();
            Assert.DoesNotContain("emisor", texto, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("InvalidOperationException", texto, StringComparison.Ordinal);
            Assert.DoesNotContain(" at ", texto, StringComparison.Ordinal);
            Assert.DoesNotContain("Zq7", texto, StringComparison.Ordinal);
            Assert.DoesNotContain("20000000005", texto, StringComparison.Ordinal);
        }

        // La fila corrupta no se pisó en silencio.
        using var despues = fabrica.Services.CreateScope();
        var cuit = await despues.ServiceProvider.GetRequiredService<OpticaDbContext>().Database
            .SqlQuery<string>($"SELECT Cuit AS Value FROM DatosEmisor").SingleAsync();
        Assert.Equal("20000000005", cuit);
    }

    /// <summary>
    /// Alerta de datos (R-06): MVC registra en Trace los argumentos de la acción con su <c>ToString()</c>; el cuerpo del
    /// POST no muestra los datos del emisor.
    /// </summary>
    [Fact]
    public void SolicitudDatosEmisor_ToString_NoMuestraDatos()
    {
        var texto = new SolicitudDatosEmisor(RazonSocial, Domicilio, CuitConGuiones, IngresosBrutos, Inicio).ToString();

        Assert.Equal("SolicitudDatosEmisor { }", texto);
        Assert.DoesNotContain("Zq7", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("00000000", texto, StringComparison.Ordinal);
        Assert.DoesNotContain(Inicio, texto, StringComparison.Ordinal);
    }

    /// <summary>
    /// El cuerpo de los cinco datos; con <see cref="JsonSerializer"/>, los caracteres fuera de ASCII viajan escapados
    /// (<c>€</c>, 6 bytes) y no como UTF-8, así que se escribe a mano.
    /// </summary>
    private static string Cuerpo(
        string razonSocial = RazonSocial,
        string domicilio = Domicilio,
        string cuit = Cuit,
        string ingresosBrutos = IngresosBrutos,
        string inicio = Inicio,
        string? relleno = null) =>
        "{\"razonSocial\":\"" + razonSocial + "\",\"domicilioComercial\":\"" + domicilio + "\",\"cuit\":\"" + cuit
        + "\",\"ingresosBrutos\":\"" + ingresosBrutos + "\",\"inicioActividades\":\"" + inicio + "\""
        + (relleno is null ? string.Empty : ",\"relleno\":\"" + relleno + "\"") + "}";

    /// <summary><see cref="Cuerpo"/> con una propiedad que se ignora, hasta ocupar <paramref name="bytes"/>.</summary>
    private static string CuerpoDeBytes(int bytes)
    {
        var faltan = bytes - Encoding.UTF8.GetByteCount(Cuerpo(relleno: string.Empty));
        Assert.True(faltan >= 0, "El cuerpo sin relleno ya es más largo que el pedido.");
        var cuerpo = Cuerpo(relleno: new string('a', faltan));
        Assert.Equal(bytes, Encoding.UTF8.GetByteCount(cuerpo));
        return cuerpo;
    }

    private static Task<HttpResponseMessage> GrabarAsync(HttpClient cliente, string cuerpo, string cookie) =>
        Http.PostJsonAsync(cliente, Ruta, cuerpo, cookie);

    private static Task<HttpResponseMessage> EnviarAsync(HttpClient cliente, string cuerpo, bool conLargo, string cookie)
    {
        HttpContent contenido = conLargo
            ? new StringContent(cuerpo, Encoding.UTF8, "application/json")
            : new Http.ContenidoSinLargo(cuerpo, "application/json");
        return Http.EnviarAsync(cliente, HttpMethod.Post, Ruta, contenido, cookie);
    }

    /// <summary>Los cinco datos siguen sin cargar: el pedido rechazado no grabó nada.</summary>
    private static async Task AfirmarSinDatosAsync(HttpClient cliente, string cookie)
    {
        var respuesta = await Http.GetAsync(cliente, Ruta, cookie);
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var json = await Http.JsonAsync(respuesta);
        Assert.Equal(Claves, json.EnumerateObject().Select(propiedad => propiedad.Name).ToArray());
        foreach (var clave in Claves)
        {
            Assert.Equal(JsonValueKind.Null, json.GetProperty(clave).ValueKind);
        }
    }

    private static void AfirmarDatos(
        JsonElement json, string razonSocial, string domicilio, string cuit, string ingresosBrutos, string inicio)
    {
        Assert.Equal(Claves, json.EnumerateObject().Select(propiedad => propiedad.Name).ToArray());
        Assert.Equal(razonSocial, json.GetProperty("razonSocial").GetString());
        Assert.Equal(domicilio, json.GetProperty("domicilioComercial").GetString());
        Assert.Equal(cuit, json.GetProperty("cuit").GetString());
        Assert.Equal(ingresosBrutos, json.GetProperty("ingresosBrutos").GetString());
        Assert.Equal(inicio, json.GetProperty("inicioActividades").GetString());
    }

    private static async Task<Dictionary<string, string[]>> ErroresAsync(HttpResponseMessage respuesta)
    {
        var json = await Http.JsonAsync(respuesta);
        return json.GetProperty("errors").EnumerateObject().ToDictionary(
            campo => campo.Name,
            campo => campo.Value.EnumerateArray().Select(valor => valor.GetString()!).ToArray());
    }
}
