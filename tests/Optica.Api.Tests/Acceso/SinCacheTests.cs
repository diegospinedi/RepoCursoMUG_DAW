using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Optica.Domain.Acceso;

namespace Optica.Api.Tests.Acceso;

/// <summary>
/// Toda respuesta de <c>/api</c> lleva <c>Cache-Control: no-store</c> (FEAT-003a, Block 7, D-37, R-08): las de
/// presupuestos traen los datos personales del cliente y no deben quedar en la caché del navegador de una PC compartida
/// ni de un intermediario, tampoco las de error. La SPA conserva su <c>no-cache</c>. Todos los datos del cliente de
/// estas pruebas son ficticios.
/// </summary>
public sealed class SinCacheTests : IDisposable
{
    private const string ClaveWeb = "Optica:Web:Directorio";
    private const string RutaPresupuestos = "/api/presupuestos";

    /// <summary>Carpeta propia de esta instancia con una SPA mínima (solo <c>index.html</c>).</summary>
    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "optica-pruebas-api-sincache", Guid.NewGuid().ToString("N"));

    public SinCacheTests()
    {
        Directory.CreateDirectory(_raiz);
        File.WriteAllText(
            Path.Combine(_raiz, "index.html"),
            "<!doctype html><html><head><title>Óptica</title></head><body><div id=\"raiz\"></div></body></html>");
    }

    public void Dispose()
    {
        if (Directory.Exists(_raiz))
        {
            Directory.Delete(_raiz, recursive: true);
        }
    }

    /// <summary>
    /// El estado del acceso (anónimo), el alta del presupuesto (201) y su consulta (200) responden con
    /// <c>no-store</c>.
    /// </summary>
    [Fact]
    public async Task Api_RespuestasExitosas_NoStore()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();

        var estado = await Http.GetAsync(cliente, "/api/acceso/estado");
        Assert.Equal(HttpStatusCode.OK, estado.StatusCode);
        AfirmarNoStore(estado, "GET /api/acceso/estado");

        // El segmento /api se compara sin distinguir mayúsculas, igual que el ruteo.
        var enMayusculas = await Http.GetAsync(cliente, "/API/acceso/estado");
        Assert.Equal(HttpStatusCode.OK, enMayusculas.StatusCode);
        AfirmarNoStore(enMayusculas, "GET /API/acceso/estado");

        // Nadie pisa el no-store con [ResponseCache] (R-08): el middleware respeta un Cache-Control ya fijado.
        var conResponseCache = typeof(Program).Assembly.GetTypes()
            .Where(tipo => typeof(ControllerBase).IsAssignableFrom(tipo) && !tipo.IsAbstract)
            .SelectMany(tipo => new MemberInfo[] { tipo }.Concat(
                tipo.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)))
            .Where(miembro => miembro.IsDefined(typeof(ResponseCacheAttribute), inherit: true))
            .Select(miembro => miembro is Type tipo ? tipo.Name : $"{miembro.DeclaringType?.Name}.{miembro.Name}")
            .ToList();
        Assert.True(
            conResponseCache.Count == 0, $"[ResponseCache] pisaría el no-store de /api en: {string.Join(", ", conResponseCache)}.");

        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);

        var alta = await Http.PostJsonAsync(cliente, RutaPresupuestos, CuerpoPresupuesto(codigo), cookie);
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);
        AfirmarNoStore(alta, "POST /api/presupuestos");
        var numero = (await Http.JsonAsync(alta)).GetProperty("numero").GetInt64();

        var consulta = await Http.GetAsync(cliente, $"{RutaPresupuestos}/{numero}", cookie);
        Assert.Equal(HttpStatusCode.OK, consulta.StatusCode);
        AfirmarNoStore(consulta, "GET /api/presupuestos/{numero}");

        // FEAT-003c: la modificación y el paso a Final devuelven el presupuesto con los datos del cliente.
        var modificacion = await Http.EnviarAsync(
            cliente,
            HttpMethod.Put,
            $"{RutaPresupuestos}/{numero}",
            new StringContent(CuerpoModificacion(codigo), Encoding.UTF8, "application/json"),
            cookie);
        Assert.Equal(HttpStatusCode.OK, modificacion.StatusCode);
        AfirmarNoStore(modificacion, "PUT /api/presupuestos/{numero}");

        var final = await Http.EnviarAsync(
            cliente,
            HttpMethod.Put,
            $"{RutaPresupuestos}/{numero}/estado",
            new StringContent("{\"estado\":\"final\",\"version\":1}", Encoding.UTF8, "application/json"),
            cookie);
        Assert.Equal(HttpStatusCode.OK, final.StatusCode);
        AfirmarNoStore(final, "PUT /api/presupuestos/{numero}/estado");

        // El 409 también: un Final no se modifica.
        var conflicto = await Http.EnviarAsync(
            cliente,
            HttpMethod.Put,
            $"{RutaPresupuestos}/{numero}",
            new StringContent(CuerpoModificacion(codigo), Encoding.UTF8, "application/json"),
            cookie);
        Assert.Equal(HttpStatusCode.Conflict, conflicto.StatusCode);
        AfirmarNoStore(conflicto, "409 de un presupuesto en Final");
    }

    /// <summary>
    /// Las respuestas de error de <c>/api</c> también llevan <c>no-store</c>, sin cambiar su código: 401 sin sesión, 404
    /// de un presupuesto inexistente, 413 (con <c>Content-Length</c> y en partes, que pasa por el manejador de errores),
    /// 415 y 400 de un cuerpo ilegible.
    /// </summary>
    [Fact]
    public async Task Api_RespuestasDeError_NoStore()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();

        var sinSesion = await Http.GetAsync(cliente, $"{RutaPresupuestos}/1");
        Assert.Equal(HttpStatusCode.Unauthorized, sinSesion.StatusCode);
        AfirmarNoStore(sinSesion, "401 sin sesión");

        var cookie = await Http.DefinirAsync(cliente);

        // 423: cinco ingresos con una contraseña incorrecta bloquean el acceso; el sexto, con la correcta, sigue en 423.
        for (var intento = 1; intento <= 5; intento++)
        {
            await IngresarAsync(cliente, "otra-clave-mala");
            fabrica.Reloj.Advance(TimeSpan.FromSeconds(1));
        }

        var bloqueado = await IngresarAsync(cliente, Http.Contrasena);
        Assert.Equal((HttpStatusCode)StatusCodes.Status423Locked, bloqueado.StatusCode);
        AfirmarNoStore(bloqueado, "423 de acceso bloqueado");

        var codigo = await GrabarArticuloAsync(cliente, cookie);

        var inexistente = await Http.GetAsync(cliente, $"{RutaPresupuestos}/999", cookie);
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
        AfirmarNoStore(inexistente, "404 de presupuesto inexistente");

        var grande = CuerpoPresupuesto(codigo, new string('a', 17 * 1024));
        var conLargo = await Http.PostJsonAsync(cliente, RutaPresupuestos, grande, cookie);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, conLargo.StatusCode);
        AfirmarNoStore(conLargo, "413 con Content-Length");

        var enPartes = await Http.EnviarAsync(
            cliente, HttpMethod.Post, RutaPresupuestos, new Http.ContenidoSinLargo(grande, "application/json"), cookie);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, enPartes.StatusCode);
        AfirmarNoStore(enPartes, "413 en partes");

        var otroTipo = await Http.EnviarAsync(
            cliente,
            HttpMethod.Post,
            RutaPresupuestos,
            new StringContent(CuerpoPresupuesto(codigo), Encoding.UTF8, "text/plain"),
            cookie);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, otroTipo.StatusCode);
        AfirmarNoStore(otroTipo, "415");

        var ilegible = await Http.PostJsonAsync(cliente, RutaPresupuestos, "esto no es json", cookie);
        Assert.Equal(HttpStatusCode.BadRequest, ilegible.StatusCode);
        AfirmarNoStore(ilegible, "400 de cuerpo ilegible");

        // 500: un repositorio que falla en una ruta anónima llega al manejador de errores no controlados.
        using var conFalla = new FabricaApi(servicios =>
        {
            servicios.RemoveAll<ICredencialAccesoRepository>();
            servicios.AddScoped<ICredencialAccesoRepository, RepositorioQueFalla>();
        });
        var error = await Http.GetAsync(conFalla.CrearClienteSinCookies(), "/api/acceso/estado");
        Assert.Equal(HttpStatusCode.InternalServerError, error.StatusCode);
        AfirmarNoStore(error, "500 no controlado");
    }

    /// <summary>
    /// Fuera de <c>/api</c> el middleware no actúa: la raíz y una ruta de la SPA siguen con <c>no-cache</c> y sin
    /// <c>no-store</c>.
    /// </summary>
    [Fact]
    public async Task Spa_ArchivosYRutas_SinNoStore()
    {
        using var fabrica = new FabricaApi();
        using var conWeb = fabrica.WithWebHostBuilder(constructor => constructor.UseSetting(ClaveWeb, _raiz));
        var cliente = conWeb.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
            AllowAutoRedirect = false,
        });

        foreach (var ruta in new[] { "/", "/presupuestos/nuevo", "/presupuestos/1/editar" })
        {
            var respuesta = await Http.GetAsync(cliente, ruta);

            Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
            Assert.True(respuesta.Headers.TryGetValues("Cache-Control", out var valores), $"{ruta} no trae Cache-Control.");
            Assert.Equal("no-cache", Assert.Single(valores));
            Assert.False(respuesta.Headers.CacheControl?.NoStore ?? false, $"{ruta} trae no-store.");
        }

        // /apiX no es el segmento /api: la responde la SPA, sin no-store. /apiX.js es un 404 de la SPA sin Cache-Control:
        // ahí el middleware no tendría un no-cache que respetar, así que es el caso que muestra que no actúa.
        var parecida = await Http.GetAsync(cliente, "/apiX");
        Assert.Equal(HttpStatusCode.OK, parecida.StatusCode);
        Assert.False(parecida.Headers.CacheControl?.NoStore ?? false, "/apiX trae no-store.");

        var archivoParecido = await Http.GetAsync(cliente, "/apiX.js");
        Assert.Equal(HttpStatusCode.NotFound, archivoParecido.StatusCode);
        Assert.False(archivoParecido.Headers.CacheControl?.NoStore ?? false, "/apiX.js trae no-store.");
    }

    /// <summary>
    /// FEAT-006 (ADR-019, R-02): la búsqueda de presupuestos trae Apellido, Nombre y DNI; su 200, su 400 por validación y
    /// su 400 por cuerpo ilegible responden con <c>no-store</c>.
    /// </summary>
    [Fact]
    public async Task PostBusqueda_RespuestaNoStore()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);
        Assert.Equal(
            HttpStatusCode.Created,
            (await Http.PostJsonAsync(cliente, RutaPresupuestos, CuerpoPresupuesto(codigo), cookie)).StatusCode);

        var encontrada = await Http.PostJsonAsync(cliente, RutaBusqueda, "{\"apellido\":\"ficticio\"}", cookie);
        Assert.Equal(HttpStatusCode.OK, encontrada.StatusCode);
        Assert.Equal(1, (await Http.JsonAsync(encontrada)).GetProperty("total").GetInt32());
        AfirmarNoStore(encontrada, "POST /api/presupuestos/busqueda");

        var invalida = await Http.PostJsonAsync(cliente, RutaBusqueda, "{\"desde\":\"2026-02-30\"}", cookie);
        Assert.Equal(HttpStatusCode.BadRequest, invalida.StatusCode);
        AfirmarNoStore(invalida, "400 de la búsqueda");

        var ilegible = await Http.PostJsonAsync(cliente, RutaBusqueda, "{\"pagina\":\"2\"}", cookie);
        Assert.Equal(HttpStatusCode.BadRequest, ilegible.StatusCode);
        AfirmarNoStore(ilegible, "400 de un cuerpo de búsqueda ilegible");
    }

    private const string RutaBusqueda = RutaPresupuestos + "/busqueda";

    /// <summary>
    /// FEAT-006 (R-02): el PDF lleva los datos del cliente; su 200 (<c>application/pdf</c>), su 409 <c>borrador</c> y su
    /// 404 responden con <c>no-store</c>, como el resto de <c>/api</c>.
    /// </summary>
    [Fact]
    public async Task GetPdf_RespuestaNoStore()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);
        var alta = await Http.PostJsonAsync(cliente, RutaPresupuestos, CuerpoPresupuesto(codigo), cookie);
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);
        var numero = (await Http.JsonAsync(alta)).GetProperty("numero").GetInt64();

        var borrador = await Http.GetAsync(cliente, $"{RutaPresupuestos}/{numero}/pdf", cookie);
        Assert.Equal(HttpStatusCode.Conflict, borrador.StatusCode);
        AfirmarNoStore(borrador, "409 del PDF de un Borrador");

        var final = await Http.EnviarAsync(
            cliente,
            HttpMethod.Put,
            $"{RutaPresupuestos}/{numero}/estado",
            new StringContent("{\"estado\":\"final\",\"version\":0}", Encoding.UTF8, "application/json"),
            cookie);
        Assert.Equal(HttpStatusCode.OK, final.StatusCode);

        var pdf = await Http.GetAsync(cliente, $"{RutaPresupuestos}/{numero}/pdf", cookie);
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
        AfirmarNoStore(pdf, "GET /api/presupuestos/{numero}/pdf");

        var inexistente = await Http.GetAsync(cliente, $"{RutaPresupuestos}/999/pdf", cookie);
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
        AfirmarNoStore(inexistente, "404 del PDF");
    }

    /// <summary>
    /// Exige la directiva <c>no-store</c>. No exige que sea la única: una respuesta que pasa por el manejador de
    /// errores ya trae <c>no-cache,no-store</c> de ASP.NET Core, y el middleware no la pisa.
    /// </summary>
    private static void AfirmarNoStore(HttpResponseMessage respuesta, string caso)
    {
        Assert.True(respuesta.Headers.TryGetValues("Cache-Control", out _), $"{caso}: no trae Cache-Control.");
        Assert.True(respuesta.Headers.CacheControl?.NoStore ?? false, $"{caso}: Cache-Control sin no-store.");
    }

    private static string CuerpoPresupuesto(long codigo, string? relleno = null) =>
        JsonSerializer.Serialize(new
        {
            cliente = new { apellido = "Ficticio", nombre = "Ana", dni = "30111222" },
            lineas = new[] { new { codigoArticulo = codigo, cantidad = 1, porcentajeDescuento = 0 } },
            relleno,
        });

    /// <summary>La modificación del presupuesto recién dado de alta: versión 0 y su línea 1, con datos ficticios.</summary>
    private static string CuerpoModificacion(long codigo) =>
        JsonSerializer.Serialize(new
        {
            version = 0,
            cliente = new { apellido = "Ficticio", nombre = "Ana", dni = "99999999" },
            lineas = new[] { new { orden = 1, codigoArticulo = codigo, cantidad = 2, porcentajeDescuento = 0 } },
        });

    private static Task<HttpResponseMessage> IngresarAsync(HttpClient cliente, string contrasena) =>
        Http.PostJsonAsync(cliente, "/api/acceso/ingresar", Http.CuerpoContrasena(contrasena));

    private static async Task<long> GrabarArticuloAsync(HttpClient cliente, string cookie)
    {
        var respuesta = await Http.PostJsonAsync(
            cliente,
            "/api/articulos",
            JsonSerializer.Serialize(
                new { codigoProveedor = "ABC-1", descripcion = "Armazón", precioCosto = 1210m, margenUtilidad = 50m }),
            cookie);
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await Http.JsonAsync(respuesta)).GetProperty("codigo").GetInt64();
    }

    /// <summary>Repositorio de la credencial que falla en todo: fuerza un 500 genérico.</summary>
    private sealed class RepositorioQueFalla : ICredencialAccesoRepository
    {
        private static InvalidOperationException Falla() => new("Falla forzada por la prueba.");

        public Task<bool> ExisteAsync(CancellationToken cancelacion = default) => throw Falla();

        public Task<CredencialAcceso?> ObtenerAsync(CancellationToken cancelacion = default) => throw Falla();

        public void Agregar(CredencialAcceso credencial) => throw Falla();

        public void Reemplazar(CredencialAcceso anterior, CredencialAcceso nueva) => throw Falla();

        public void Eliminar() => throw Falla();
    }
}
