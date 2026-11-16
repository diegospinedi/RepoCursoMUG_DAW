using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Optica.Api.Controllers;
using Optica.Api.Errores;
using Optica.Application.Backup;

namespace Optica.Api.Tests.Backup;

/// <summary>
/// <c>GET</c> y <c>POST /api/configuracion/backup</c> (FEAT-010, Block 2): con sesión, la Api traduce los casos de uso de
/// la ubicación del backup; el POST graba o responde 400 en el campo <c>ruta</c> sin repetirla; el cuerpo exige JSON y
/// hasta 2 KB. Las rutas son ficticias, sobre la carpeta temporal; las pruebas que graban reemplazan el inspector por uno
/// que acepta todo, porque la carpeta de datos de las pruebas y la temporal suelen estar en la misma unidad.
/// </summary>
public sealed class UbicacionBackupApiTests
{
    private const string Ruta = "/api/configuracion/backup";
    private const int Limite = 2 * 1024;

    /// <summary>Valor testigo dentro de la ruta: no puede aparecer en el log ni en un error.</summary>
    private const string Testigo = "Zq7TestigoCarpeta";

    [Fact]
    public async Task GetBackup_SinUbicacion_200ConNull()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);

        var respuesta = await Http.GetAsync(cliente, Ruta, cookie);

        // Sin ubicación elegida no es un 404: es el 200 con la ruta en null.
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("application/json", respuesta.Content.Headers.ContentType?.MediaType);
        await AfirmarSinUbicacionAsync(cliente, cookie);
    }

    [Theory]
    [InlineData("Zq7Relativa/Backups")]
    [InlineData("Zq7Relativa")]
    [InlineData("   ")]
    public async Task PostBackup_RutaRelativa_400SinRepetirla(string ruta)
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);

        var respuesta = await Http.PostJsonAsync(cliente, Ruta, Cuerpo(ruta), cookie);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        var errores = await ErroresAsync(respuesta);
        Assert.Equal(["ruta"], errores.Keys);
        Assert.Equal(
            string.IsNullOrWhiteSpace(ruta) ? ValidacionUbicacionBackup.AccionFaltante : ValidacionUbicacionBackup.AccionNoAbsoluta,
            Assert.Single(errores["ruta"]));
        Assert.DoesNotContain("Zq7", await respuesta.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await AfirmarSinUbicacionAsync(cliente, cookie);
    }

    [Theory]
    [InlineData("esto no es json")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"ruta\":\"Zq7-marcado")]
    [InlineData("{\"ruta\":123}")]
    [InlineData("{\"ruta\":[\"Zq7-marcado\"]}")]
    public async Task PostBackup_CuerpoIlegible_400(string cuerpo)
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);

        var respuesta = await Http.PostJsonAsync(cliente, Ruta, cuerpo, cookie);

        // Un solo error en el campo ruta, con una acción fija: ni los mensajes del lector de JSON ni lo enviado.
        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            new Dictionary<string, string[]> { ["ruta"] = [RespuestasValidacion.AccionCuerpoIlegibleBackup] },
            await ErroresAsync(respuesta));
        var texto = await respuesta.Content.ReadAsStringAsync();
        Assert.DoesNotContain("JSON", texto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LineNumber", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("System.", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("Zq7", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("$.", texto, StringComparison.Ordinal);
        await AfirmarSinUbicacionAsync(cliente, cookie);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PostBackup_CuerpoGrande_413(bool conLargo)
    {
        using var fabrica = new FabricaApi(reemplazos: AceptarTodo);
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);

        // Un cuerpo válido de un byte más que el límite: solo el tamaño lo rechaza.
        var rechazo = await EnviarAsync(cliente, CuerpoDeBytes(Limite + 1), conLargo, cookie);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, rechazo.StatusCode);
        Assert.DoesNotContain(Testigo, await rechazo.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await AfirmarSinUbicacionAsync(cliente, cookie);

        // Control del 413: en el límite exacto se acepta y se graba.
        var exacto = await EnviarAsync(cliente, CuerpoDeBytes(Limite), conLargo, cookie);
        Assert.Equal(HttpStatusCode.OK, exacto.StatusCode);
        Assert.Equal(RutaTestigo, (await Http.JsonAsync(exacto)).GetProperty("ruta").GetString());
    }

    [Fact]
    public async Task PostBackup_Registro_SinRuta()
    {
        using var fabrica = new FabricaApi(reemplazos: AceptarTodo);
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);

        var grabada = await Http.PostJsonAsync(cliente, Ruta, Cuerpo(RutaTestigo), cookie);

        // AC-01: 200 con la ruta grabada, y el GET posterior la devuelve.
        Assert.Equal(HttpStatusCode.OK, grabada.StatusCode);
        var json = await Http.JsonAsync(grabada);
        Assert.Equal(["ruta"], json.EnumerateObject().Select(propiedad => propiedad.Name).ToArray());
        Assert.Equal(RutaTestigo, json.GetProperty("ruta").GetString());
        var leida = await Http.GetAsync(cliente, Ruta, cookie);
        Assert.Equal(RutaTestigo, (await Http.JsonAsync(leida)).GetProperty("ruta").GetString());

        // Un rechazo con la misma ruta testigo, para que también pase por el camino del error.
        var relativa = await Http.PostJsonAsync(cliente, Ruta, Cuerpo(Testigo + "/Relativa"), cookie);
        Assert.Equal(HttpStatusCode.BadRequest, relativa.StatusCode);

        // La grabación quedó en el log con la fecha UTC; la ruta no está en ninguna línea, de ningún nivel (D-12, R-05).
        var lineas = fabrica.Registro.Lineas;
        var registro = Assert.Single(lineas, linea => linea.Categoria == GrabarUbicacionBackup.CategoriaRegistro);
        Assert.Contains(FabricaApi.Inicio.UtcDateTime.ToString("O"), registro.Mensaje, StringComparison.Ordinal);
        foreach (var linea in lineas)
        {
            Assert.DoesNotContain(Testigo, linea.TextoCompleto, StringComparison.Ordinal);
            Assert.DoesNotContain(RutaTestigo, linea.TextoCompleto, StringComparison.Ordinal);
        }

        // La solicitud tampoco la muestra en su ToString (MVC registra en Trace los argumentos de la acción).
        Assert.Equal("SolicitudUbicacionBackup { }", new SolicitudUbicacionBackup(RutaTestigo).ToString());
        Assert.Equal("UbicacionBackupVigente { }", new UbicacionBackupVigente(RutaTestigo).ToString());
    }

    private static readonly string RutaTestigo = Path.Combine(Path.GetTempPath(), Testigo, "Backups");

    /// <summary>Reemplaza el inspector real por uno que acepta toda ruta absoluta como otra unidad, existente y escribible.</summary>
    private static void AceptarTodo(IServiceCollection servicios)
    {
        servicios.RemoveAll<IInspectorUbicaciones>();
        servicios.AddSingleton<IInspectorUbicaciones, InspectorQueAceptaTodo>();
    }

    private static string Cuerpo(string ruta, string? relleno = null) =>
        "{\"ruta\":" + JsonSerializer.Serialize(ruta)
        + (relleno is null ? string.Empty : ",\"relleno\":\"" + relleno + "\"") + "}";

    private static string CuerpoDeBytes(int bytes)
    {
        var faltan = bytes - Encoding.UTF8.GetByteCount(Cuerpo(RutaTestigo, relleno: string.Empty));
        Assert.True(faltan >= 0, "El cuerpo sin relleno ya es más largo que el pedido.");
        var cuerpo = Cuerpo(RutaTestigo, relleno: new string('a', faltan));
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

    /// <summary>Sigue sin ubicación: el pedido rechazado no grabó nada.</summary>
    private static async Task AfirmarSinUbicacionAsync(HttpClient cliente, string cookie)
    {
        var respuesta = await Http.GetAsync(cliente, Ruta, cookie);
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var json = await Http.JsonAsync(respuesta);
        Assert.Equal(["ruta"], json.EnumerateObject().Select(propiedad => propiedad.Name).ToArray());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("ruta").ValueKind);
    }

    private static async Task<Dictionary<string, string[]>> ErroresAsync(HttpResponseMessage respuesta)
    {
        var json = await Http.JsonAsync(respuesta);
        return json.GetProperty("errors").EnumerateObject().ToDictionary(
            campo => campo.Name,
            campo => campo.Value.EnumerateArray().Select(valor => valor.GetString()!).ToArray());
    }

    private sealed class InspectorQueAceptaTodo : IInspectorUbicaciones
    {
        public string? Normalizar(string ruta) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(ruta));

        public bool Superpuestas(string rutaA, string rutaB) => false;

        public bool EsLocal(string ruta) => true;

        public bool MismaUnidad(string rutaA, string rutaB) => false;

        public bool EnNubeConocida(string ruta) => false;

        public bool Existe(string ruta) => true;

        public bool PuedeEscribir(string ruta) => true;
    }
}
