using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;

namespace Optica.Api.Tests.Web;

/// <summary>
/// La Api sirve la SPA compilada desde <c>Optica:Web:Directorio</c>: archivos estáticos y, para toda ruta que no es de
/// <c>/api</c>, <c>index.html</c>. Las respuestas de la SPA llevan la política de contenido de <see cref="Csp"/> (R-11)
/// y <c>X-Content-Type-Options: nosniff</c>; <c>index.html</c> se revalida siempre.
/// </summary>
public sealed class SpaTests : IDisposable
{
    private const string ClaveWeb = "Optica:Web:Directorio";

    private const string Csp =
        "default-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'; object-src 'none'";

    private const string IndiceHtml = "<!doctype html><html><head><title>Óptica</title></head><body><div id=\"raiz\"></div></body></html>";

    /// <summary>Contenido de los archivos que nunca se sirven: si aparece en una respuesta, se filtró.</summary>
    private const string Secreto = "SECRETO-QUE-NO-SE-SIRVE";

    /// <summary>Carpeta propia de esta instancia; la SPA está en <see cref="DirectorioWeb"/>, adentro.</summary>
    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "optica-pruebas-api-web", Guid.NewGuid().ToString("N"));

    public SpaTests()
    {
        DirectorioWeb = Path.Combine(_raiz, "web");
        Directory.CreateDirectory(Path.Combine(DirectorioWeb, "assets"));
        File.WriteAllText(Path.Combine(DirectorioWeb, "index.html"), IndiceHtml);
        File.WriteAllText(Path.Combine(DirectorioWeb, "assets", "app-123.js"), "console.log('optica');");

        // Archivos que no se sirven: ocultos dentro de la SPA y la configuración, fuera de ella.
        File.WriteAllText(Path.Combine(DirectorioWeb, ".env"), Secreto);
        File.WriteAllText(Path.Combine(DirectorioWeb, ".oculto.txt"), Secreto);
        File.WriteAllText(Path.Combine(_raiz, "appsettings.json"), $"{{\"Marcador\":\"{Secreto}\"}}");
    }

    internal string DirectorioWeb { get; }

    public void Dispose()
    {
        if (Directory.Exists(_raiz))
        {
            Directory.Delete(_raiz, recursive: true);
        }
    }

    [Fact]
    public void Dispose_BorraSoloSuCarpeta()
    {
        var otra = new SpaTests();
        try
        {
            using (new SpaTests())
            {
            }

            Assert.True(Directory.Exists(otra.DirectorioWeb), "Una instancia borró la carpeta de otra.");
        }
        finally
        {
            otra.Dispose();
        }
    }

    [Fact]
    public async Task Spa_RespuestaConCsp()
    {
        using var fabrica = new FabricaApi();
        using var conWeb = ConDirectorioWeb(fabrica, DirectorioWeb);
        var cliente = Cliente(conWeb);

        // La raíz, una ruta de la SPA (enlace directo o recarga) y un archivo estático.
        foreach (var ruta in new[] { "/", "/ingreso", "/definir-contrasena" })
        {
            var respuesta = await Http.GetAsync(cliente, ruta);

            Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
            Assert.Equal("text/html", respuesta.Content.Headers.ContentType?.MediaType);
            Assert.Equal(IndiceHtml, await respuesta.Content.ReadAsStringAsync());
            AfirmarEncabezadosDeLaSpa(respuesta, ruta);
        }

        var script = await Http.GetAsync(cliente, "/assets/app-123.js");
        Assert.Equal(HttpStatusCode.OK, script.StatusCode);
        Assert.Equal("text/javascript", script.Content.Headers.ContentType?.MediaType);
        AfirmarEncabezadosDeLaSpa(script, "/assets/app-123.js");

        // Sin sesión: los archivos de la SPA son anónimos y no abren sesión.
        Assert.Null(Http.SetCookie(script));
    }

    [Fact]
    public async Task Spa_IndiceSiempreSeRevalida()
    {
        using var fabrica = new FabricaApi();
        using var conWeb = ConDirectorioWeb(fabrica, DirectorioWeb);
        var cliente = Cliente(conWeb);

        // Por el fallback (rutas de la SPA) y pedido directo, que lo sirven los archivos estáticos.
        foreach (var ruta in new[] { "/", "/ingreso", "/index.html" })
        {
            var respuesta = await Http.GetAsync(cliente, ruta);

            Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
            Assert.Equal(IndiceHtml, await respuesta.Content.ReadAsStringAsync());
            Assert.True(respuesta.Headers.CacheControl?.NoCache == true, $"{ruta} no trae Cache-Control: no-cache.");
            AfirmarEncabezadosDeLaSpa(respuesta, ruta);
        }
    }

    [Fact]
    public async Task Spa_Head_RespondeComoGetSinCuerpo()
    {
        using var fabrica = new FabricaApi();
        using var conWeb = ConDirectorioWeb(fabrica, DirectorioWeb);
        var cliente = Cliente(conWeb);

        var respuesta = await Http.EnviarAsync(cliente, HttpMethod.Head, "/", null);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("text/html", respuesta.Content.Headers.ContentType?.MediaType);
        Assert.True(respuesta.Headers.CacheControl?.NoCache == true, "HEAD / no trae Cache-Control: no-cache.");
        Assert.Empty(await respuesta.Content.ReadAsByteArrayAsync());
        AfirmarEncabezadosDeLaSpa(respuesta, "HEAD /");
    }

    [Fact]
    public async Task Spa_ArchivoInexistenteConExtension_404()
    {
        using var fabrica = new FabricaApi();
        using var conWeb = ConDirectorioWeb(fabrica, DirectorioWeb);
        var cliente = Cliente(conWeb);

        // Un archivo de una versión anterior no recibe index.html: sería HTML servido como script.
        var respuesta = await Http.GetAsync(cliente, "/assets/viejo.js");

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        Assert.DoesNotContain("<!doctype", await respuesta.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Spa_RutasConPuntosYArchivosOcultos_NuncaSirvenElArchivo()
    {
        using var fabrica = new FabricaApi();
        using var conWeb = ConDirectorioWeb(fabrica, DirectorioWeb);
        // Fuerza el arranque; los pedidos van directo al servidor de pruebas, con la ruta tal cual (un cliente HTTP
        // normaliza los puntos antes de mandarla).
        _ = conWeb.Server;

        var rutas = new[]
        {
            "/../appsettings.json", "/..%2fappsettings.json", "/..%2Fappsettings.json", "/%2e%2e/appsettings.json",
            "/%2e%2e/", "/assets/../../appsettings.json", "/assets/..%2f..%2fappsettings.json", "/.env", "/.oculto.txt",
            "/assets/../.env",
        };
        foreach (var ruta in rutas)
        {
            var contexto = await conWeb.Server.SendAsync(c =>
            {
                c.Request.Method = HttpMethods.Get;
                c.Request.Scheme = "https";
                c.Request.Host = new HostString("localhost");
                c.Request.Path = new PathString(ruta);
            });
            var cuerpo = await new StreamReader(contexto.Response.Body).ReadToEndAsync();

            Assert.False(cuerpo.Contains(Secreto, StringComparison.Ordinal), $"{ruta} sirvió un archivo que no es de la SPA.");
            Assert.True(
                contexto.Response.StatusCode == StatusCodes.Status404NotFound
                    || (contexto.Response.StatusCode == StatusCodes.Status200OK && cuerpo == IndiceHtml),
                $"{ruta} respondió {contexto.Response.StatusCode}: tiene que ser 404 o index.html.");
        }
    }

    [Fact]
    public async Task Spa_DirectorioWebFaltante_ApiArrancaY404()
    {
        using var fabrica = new FabricaApi();
        // Bajo la carpeta del usuario, que lleva su nombre: la advertencia no puede mostrarla tal cual.
        var perfil = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.False(string.IsNullOrEmpty(perfil), "La prueba necesita la carpeta del usuario.");
        var inexistente = Path.Combine(perfil, ".optica-pruebas-web-" + Guid.NewGuid().ToString("N"));
        using var conWeb = ConDirectorioWeb(fabrica, inexistente);
        var cliente = Cliente(conWeb);

        // La Api sigue respondiendo…
        var estado = await Http.GetAsync(cliente, "/api/acceso/estado");
        Assert.Equal(HttpStatusCode.OK, estado.StatusCode);

        // …y las rutas de la SPA responden 404.
        foreach (var ruta in new[] { "/", "/ingreso", "/assets/app-123.js" })
        {
            var respuesta = await Http.GetAsync(cliente, ruta);
            Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        }

        // Una advertencia nombra la clave a corregir, con la carpeta del usuario reemplazada (RutasPrivadas).
        var advertencia = Assert.Single(
            fabrica.Registro.Lineas,
            l => l.Nivel == LogLevel.Warning && l.Mensaje.Contains(ClaveWeb, StringComparison.Ordinal));
        Assert.False(
            advertencia.TextoCompleto.Contains(perfil, StringComparison.OrdinalIgnoreCase),
            "La advertencia del directorio faltante lleva la carpeta del usuario tal cual.");
        Assert.Contains(Path.GetFileName(inexistente), advertencia.Mensaje, StringComparison.Ordinal);
        Assert.DoesNotContain(fabrica.Registro.Lineas, l => l.Nivel >= LogLevel.Warning && l.Excepcion is not null);
    }

    [Fact]
    public async Task Spa_DirectorioIgualALaRaizDelContenido_NoSirveNada()
    {
        // La raíz del contenido es la carpeta de la SPA: ahí estarían la configuración y los ensamblados.
        File.WriteAllText(Path.Combine(DirectorioWeb, "Optica.Falsa.dll"), Secreto);
        File.WriteAllText(Path.Combine(DirectorioWeb, "appsettings.Local.json"), $"{{\"Marcador\":\"{Secreto}\"}}");
        using var fabrica = new FabricaApi();
        using var conWeb = fabrica.WithWebHostBuilder(constructor =>
        {
            constructor.UseContentRoot(DirectorioWeb);
            constructor.UseSetting(ClaveWeb, DirectorioWeb);
        });

        await AfirmarQueNoSirveNadaAsync(fabrica, Cliente(conWeb), "/Optica.Falsa.dll", "/appsettings.Local.json");
    }

    [Fact]
    public async Task Spa_DirectorioQueContieneLaRaizDelContenido_NoSirveNada()
    {
        File.WriteAllText(Path.Combine(_raiz, "index.html"), IndiceHtml);
        using var fabrica = new FabricaApi();
        using var conWeb = fabrica.WithWebHostBuilder(constructor =>
        {
            constructor.UseContentRoot(DirectorioWeb);
            constructor.UseSetting(ClaveWeb, _raiz);
        });

        await AfirmarQueNoSirveNadaAsync(fabrica, Cliente(conWeb), "/appsettings.json", "/web/index.html");
    }

    [Fact]
    public async Task Spa_DirectorioIgualALaCarpetaDeLaAplicacion_NoSirveNada()
    {
        // La carpeta de la aplicación (en la prueba, la salida del proyecto de pruebas) con un index.html de más.
        var aplicacion = AppContext.BaseDirectory;
        var indice = Path.Combine(aplicacion, "index.html");
        var habiaIndice = File.Exists(indice);
        if (!habiaIndice)
        {
            File.WriteAllText(indice, IndiceHtml);
        }

        try
        {
            using var fabrica = new FabricaApi();
            using var conWeb = ConDirectorioWeb(fabrica, aplicacion);

            await AfirmarQueNoSirveNadaAsync(fabrica, Cliente(conWeb), "/Optica.Api.dll", "/Optica.Api.Tests.dll");
        }
        finally
        {
            if (!habiaIndice)
            {
                File.Delete(indice);
            }
        }
    }

    [Fact]
    public async Task Spa_DirectorioIgualALaCarpetaDeDatos_NoSirveNada()
    {
        // La carpeta de datos tiene la base y, en claves/, las claves de Data Protection que cifran la cookie.
        using var fabrica = new FabricaApi();
        File.WriteAllText(Path.Combine(fabrica.DirectorioDatos, "index.html"), IndiceHtml);
        File.WriteAllText(Path.Combine(fabrica.DirectorioDatos, "secreto.txt"), Secreto);
        using var conWeb = ConDirectorioWeb(fabrica, fabrica.DirectorioDatos);

        await AfirmarQueNoSirveNadaEnAsync(
            fabrica, Cliente(conWeb), "carpeta de datos", "/secreto.txt", "/optica.db");
    }

    [Fact]
    public async Task Spa_DirectorioQueContieneLaCarpetaDeDatos_NoSirveNada()
    {
        // La carpeta de datos está dentro de la configurada para la SPA, que además tiene un index.html.
        var datos = Path.Combine(_raiz, "datos");
        Directory.CreateDirectory(datos);
        File.WriteAllText(Path.Combine(_raiz, "index.html"), IndiceHtml);
        File.WriteAllText(Path.Combine(datos, "secreto.txt"), Secreto);
        try
        {
            using var fabrica = new FabricaApi(directorioDatos: datos);
            using var conWeb = ConDirectorioWeb(fabrica, _raiz);

            await AfirmarQueNoSirveNadaEnAsync(
                fabrica, Cliente(conWeb), "carpeta de datos", "/datos/secreto.txt", "/datos/optica.db", "/appsettings.json");
        }
        finally
        {
            // La base queda en la carpeta de la prueba: se liberan las conexiones para poder borrarla.
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        }
    }

    [Fact]
    public async Task Spa_IndiceBorradoConLaApiEnMarcha_Responde404()
    {
        using var fabrica = new FabricaApi();
        using var conWeb = ConDirectorioWeb(fabrica, DirectorioWeb);
        var cliente = Cliente(conWeb);
        var antes = await Http.GetAsync(cliente, "/");
        Assert.Equal(HttpStatusCode.OK, antes.StatusCode);

        // Se borra index.html con la Api en marcha (por ejemplo, mientras se reemplaza wwwroot).
        File.Delete(Path.Combine(DirectorioWeb, "index.html"));

        foreach (var ruta in new[] { "/", "/ingreso" })
        {
            var get = await Http.GetAsync(cliente, ruta);
            Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
            Assert.NotEqual("text/html", get.Content.Headers.ContentType?.MediaType);

            var head = await Http.EnviarAsync(cliente, HttpMethod.Head, ruta, null);
            Assert.Equal(HttpStatusCode.NotFound, head.StatusCode);
        }

        // La Api sigue respondiendo.
        var estado = await Http.GetAsync(cliente, "/api/acceso/estado");
        Assert.Equal(HttpStatusCode.OK, estado.StatusCode);
    }

    [Fact]
    public async Task Spa_RutaApiInexistente_NoDevuelveIndexError404()
    {
        using var fabrica = new FabricaApi();
        using var conWeb = ConDirectorioWeb(fabrica, DirectorioWeb);
        var cliente = Cliente(conWeb);

        // Sin sesión: 401 vacío (Block 3), nunca index.html.
        foreach (var ruta in new[] { "/api/no-existe", "/api", "/API/no-existe", "/api/acceso/no-existe" })
        {
            var sinSesion = await Http.GetAsync(cliente, ruta);
            Assert.Equal(HttpStatusCode.Unauthorized, sinSesion.StatusCode);
            Assert.Equal(string.Empty, await sinSesion.Content.ReadAsStringAsync());
        }

        // Con sesión: 404, sin index.html.
        var cookie = await Http.DefinirAsync(cliente);
        foreach (var ruta in new[] { "/api/no-existe", "/api", "/API/no-existe", "/api/acceso/no-existe" })
        {
            var conSesion = await Http.GetAsync(cliente, ruta, cookie);
            Assert.Equal(HttpStatusCode.NotFound, conSesion.StatusCode);
            Assert.NotEqual("text/html", conSesion.Content.Headers.ContentType?.MediaType);
            Assert.DoesNotContain("<!doctype", await conSesion.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        }

        // Una ruta que solo empieza con las letras "api" es de la SPA.
        var spa = await Http.GetAsync(cliente, "/apiario");
        Assert.Equal(HttpStatusCode.OK, spa.StatusCode);
        Assert.Equal(IndiceHtml, await spa.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Spa_MetodoDistintoDeGet_NoDevuelveIndex()
    {
        using var fabrica = new FabricaApi();
        using var conWeb = ConDirectorioWeb(fabrica, DirectorioWeb);
        var cliente = Cliente(conWeb);

        var respuesta = await Http.PostJsonAsync(cliente, "/ingreso", "{}");

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        Assert.DoesNotContain("<!doctype", await respuesta.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Con <c>Optica:Web:Directorio</c> en la carpeta de la aplicación (o una que la contiene) no se sirve ningún
    /// archivo, la Api sigue respondiendo y una advertencia nombra la clave a corregir.
    /// </summary>
    private static Task AfirmarQueNoSirveNadaAsync(FabricaApi fabrica, HttpClient cliente, params string[] archivos) =>
        AfirmarQueNoSirveNadaEnAsync(fabrica, cliente, "carpeta de la aplicación", archivos);

    /// <summary>
    /// Con <c>Optica:Web:Directorio</c> en una carpeta prohibida (o una que la contiene) no se sirve ningún archivo, la
    /// Api sigue respondiendo y una advertencia nombra la clave a corregir y la carpeta (<paramref name="carpeta"/>),
    /// sin la ruta de la carpeta de datos.
    /// </summary>
    private static async Task AfirmarQueNoSirveNadaEnAsync(
        FabricaApi fabrica, HttpClient cliente, string carpeta, params string[] archivos)
    {
        var estado = await Http.GetAsync(cliente, "/api/acceso/estado");
        Assert.Equal(HttpStatusCode.OK, estado.StatusCode);

        foreach (var ruta in new[] { "/", "/ingreso", "/index.html" }.Concat(archivos))
        {
            var respuesta = await Http.GetAsync(cliente, ruta);
            Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
            Assert.Empty(await respuesta.Content.ReadAsByteArrayAsync());
        }

        var advertencia = Assert.Single(
            fabrica.Registro.Lineas,
            l => l.Nivel == LogLevel.Warning && l.Mensaje.Contains(ClaveWeb, StringComparison.Ordinal));
        Assert.Contains(carpeta, advertencia.Mensaje, StringComparison.Ordinal);
        Assert.DoesNotContain(fabrica.DirectorioDatos, advertencia.TextoCompleto, StringComparison.OrdinalIgnoreCase);
    }

    private static void AfirmarEncabezadosDeLaSpa(HttpResponseMessage respuesta, string ruta)
    {
        Assert.True(
            respuesta.Headers.TryGetValues("Content-Security-Policy", out var politicas),
            $"{ruta} no trae Content-Security-Policy.");
        Assert.Equal(Csp, Assert.Single(politicas));
        Assert.True(
            respuesta.Headers.TryGetValues("X-Content-Type-Options", out var nosniff),
            $"{ruta} no trae X-Content-Type-Options.");
        Assert.Equal("nosniff", Assert.Single(nosniff));
    }

    private static WebApplicationFactory<Program> ConDirectorioWeb(FabricaApi fabrica, string directorio) =>
        fabrica.WithWebHostBuilder(constructor => constructor.UseSetting(ClaveWeb, directorio));

    private static HttpClient Cliente(WebApplicationFactory<Program> fabrica) =>
        fabrica.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
            AllowAutoRedirect = false,
        });
}
