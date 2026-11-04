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
        var endpoints = fabrica.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(endpoint =>
            {
                var ruta = "/" + (endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/');
                var metodos = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods
                    ?? [HttpMethods.Get, HttpMethods.Post];
                return metodos.Select(metodo => (Metodo: metodo, Ruta: ruta));
            })
            .Distinct()
            .ToList();

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
    /// Hay una acción POST registrada en la ruta. Lo necesitan las pruebas cuya respuesta (415, 401) también se daría en
    /// una ruta inexistente.
    /// </summary>
    internal static void AfirmarQueElEndpointExiste(FabricaApi fabrica, string ruta)
    {
        var existe = fabrica.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Any(endpoint =>
                "/" + (endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/') == ruta
                && endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains(HttpMethods.Post) == true);
        Assert.True(existe, $"No hay un endpoint POST {ruta}.");
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
