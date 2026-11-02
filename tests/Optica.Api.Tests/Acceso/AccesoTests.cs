using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Optica.Application.Acceso;

namespace Optica.Api.Tests.Acceso;

public sealed class AccesoTests
{
    private const string AccionMuyCorta = "Ingresá una contraseña de 8 caracteres o más";
    private const string MensajeIncorrecta = "La contraseña no es correcta. Volvé a ingresarla.";
    private const string AccionCuerpoIlegible = "No se pudo leer la contraseña enviada. Escribila de nuevo.";

    [Fact]
    public async Task Estado_SinContrasena_DevuelveNoDefinida()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();

        var respuesta = await Http.GetAsync(cliente, "/api/acceso/estado");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var json = await Http.JsonAsync(respuesta);
        Assert.False(json.GetProperty("contrasenaDefinida").GetBoolean());

        await Http.DefinirAsync(cliente);
        var despues = await Http.JsonAsync(await Http.GetAsync(cliente, "/api/acceso/estado"));
        Assert.True(despues.GetProperty("contrasenaDefinida").GetBoolean());
    }

    [Fact]
    public async Task Definir_De7Caracteres_Invalido400ConCampoYAccion()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();

        var respuesta = await Http.PostJsonAsync(cliente, "/api/acceso/definir", Http.CuerpoContrasena("1234567"));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal([AccionMuyCorta], await Http.ErroresContrasenaAsync(respuesta));
        Assert.Null(Http.SetCookie(respuesta));
        var estado = await Http.JsonAsync(await Http.GetAsync(cliente, "/api/acceso/estado"));
        Assert.False(estado.GetProperty("contrasenaDefinida").GetBoolean());
    }

    [Fact]
    public async Task Definir_CaracteresInvalidos_Invalido400ConAccion()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();

        // Un sustituto UTF-16 suelto, escrito como escape JSON.
        var respuesta = await Http.PostJsonAsync(cliente, "/api/acceso/definir", "{\"contrasena\":\"abcdefgh\\ud800\"}");

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal(
            ["La contraseña tiene caracteres no válidos. Escribila de nuevo."],
            await Http.ErroresContrasenaAsync(respuesta));
        Assert.Empty(fabrica.Eventos());
    }

    [Fact]
    public async Task Definir_YaDefinida_Conflicto409()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        await Http.DefinirAsync(cliente, "primera-clave");

        var respuesta = await Http.PostJsonAsync(cliente, "/api/acceso/definir", Http.CuerpoContrasena("segunda-clave"));

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        Assert.Null(Http.SetCookie(respuesta));
        // La contraseña sigue siendo la primera.
        var ingresoConPrimera = await Http.PostJsonAsync(cliente, "/api/acceso/ingresar", Http.CuerpoContrasena("primera-clave"));
        Assert.Equal(HttpStatusCode.NoContent, ingresoConPrimera.StatusCode);
        var ingresoConSegunda = await Http.PostJsonAsync(cliente, "/api/acceso/ingresar", Http.CuerpoContrasena("segunda-clave"));
        Assert.Equal(HttpStatusCode.Unauthorized, ingresoConSegunda.StatusCode);
    }

    [Fact]
    public async Task Ingresar_ContrasenaIncorrecta_Rechazado401YEventoRegistrado()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        await Http.DefinirAsync(cliente);

        var respuesta = await Http.PostJsonAsync(cliente, "/api/acceso/ingresar", Http.CuerpoContrasena("otra-clave-mala"));

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        Assert.Contains(MensajeIncorrecta, await respuesta.Content.ReadAsStringAsync());
        Assert.Null(Http.SetCookie(respuesta));
        var eventos = fabrica.Eventos().Select(e => (e.Tipo, e.Resultado)).ToList();
        Assert.Equal([("definicion", "exito"), ("ingreso", "rechazo")], eventos);
    }

    [Fact]
    public async Task Ingresar_SinContrasenaDefinida_Conflicto409()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();

        var respuesta = await Http.PostJsonAsync(cliente, "/api/acceso/ingresar", Http.CuerpoContrasena(Http.Contrasena));

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        Assert.Contains("Definí la contraseña primero", await respuesta.Content.ReadAsStringAsync());
        Assert.Null(Http.SetCookie(respuesta));
        Assert.Empty(fabrica.Eventos());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"contrasena\":null}")]
    [InlineData("")]
    public async Task Ingresar_ContrasenaFaltante_Invalido400(string cuerpo)
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        await Http.DefinirAsync(cliente);

        var respuesta = await Http.PostJsonAsync(cliente, "/api/acceso/ingresar", cuerpo);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal([AccionMuyCorta], await Http.ErroresContrasenaAsync(respuesta));
        Assert.Null(Http.SetCookie(respuesta));
    }

    [Theory]
    [InlineData("{\"contrasena\":\"1234567\"}", AccionMuyCorta)]
    [InlineData("{\"contrasena\":null}", AccionMuyCorta)]
    [InlineData("larga", "Ingresá una contraseña de 128 caracteres o menos")]
    [InlineData("{\"contrasena\":\"abcdefgh\\ud800\"}", "La contraseña tiene caracteres no válidos. Escribila de nuevo.")]
    public async Task Ingresar_ContrasenaFueraDePolitica_Invalido400SinEvento(string cuerpo, string accion)
    {
        if (cuerpo == "larga")
        {
            cuerpo = Http.CuerpoContrasena(new string('x', 129));
        }

        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        await Http.DefinirAsync(cliente);
        var eventosAntes = fabrica.Eventos().Count;

        var respuesta = await Http.PostJsonAsync(cliente, "/api/acceso/ingresar", cuerpo);

        // Decisión del usuario (2026-10-31, ADR-005): 400 con campo y acción y ninguna fila nueva en EventoAcceso.
        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal([accion], await Http.ErroresContrasenaAsync(respuesta));
        Assert.Null(Http.SetCookie(respuesta));
        Assert.Equal(eventosAntes, fabrica.Eventos().Count);
    }

    [Theory]
    [InlineData("/api/acceso/definir", "{\"contrasena\":12345678}")]
    [InlineData("/api/acceso/ingresar", "{\"contrasena\":12345678}")]
    [InlineData("/api/acceso/definir", "{\"contrasena\":\"clave-sin-cerrar")]
    [InlineData("/api/acceso/ingresar", "{\"contrasena\":\"clave-sin-cerrar")]
    [InlineData("/api/acceso/ingresar", "esto no es json")]
    [InlineData("/api/acceso/ingresar", "{\"contrasena\":[\"a\"]}")]
    public async Task Post_JsonRotoOContrasenaNoTexto_Invalido400CuerpoIlegible(string ruta, string cuerpo)
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        if (ruta.EndsWith("ingresar", StringComparison.Ordinal))
        {
            await Http.DefinirAsync(cliente);
        }

        var eventosAntes = fabrica.Eventos().Count;

        var respuesta = await Http.PostJsonAsync(cliente, ruta, cuerpo);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal([AccionCuerpoIlegible], await Http.ErroresContrasenaAsync(respuesta));
        Assert.Null(Http.SetCookie(respuesta));
        Assert.Equal(eventosAntes, fabrica.Eventos().Count);
        // Nunca se devuelven los mensajes internos del lector de JSON.
        Assert.DoesNotContain("JSON", await respuesta.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/api/acceso/definir")]
    [InlineData("/api/acceso/ingresar")]
    public async Task Post_ContrasenaConBytesUtf8MalFormados_Invalido400CuerpoIlegible(string ruta)
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        if (ruta.EndsWith("ingresar", StringComparison.Ordinal))
        {
            await Http.DefinirAsync(cliente);
        }

        var eventosAntes = fabrica.Eventos().Count;
        byte[] cuerpo = [.. "{\"contrasena\":\"abcdefgh"u8, 0xFF, .. "\"}"u8];
        var contenido = new ByteArrayContent(cuerpo);
        contenido.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        var respuesta = await Http.EnviarAsync(cliente, HttpMethod.Post, ruta, contenido);

        // Es un texto que no se pudo leer, no una contraseña con caracteres no válidos: no llega al caso de uso.
        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal([AccionCuerpoIlegible], await Http.ErroresContrasenaAsync(respuesta));
        Assert.Null(Http.SetCookie(respuesta));
        Assert.Equal(eventosAntes, fabrica.Eventos().Count);
    }

    [Fact]
    public async Task Definir_SustitutoSueltoEscapado_Invalido400CaracteresNoValidos()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();

        var respuesta = await Http.PostJsonAsync(cliente, "/api/acceso/definir", "{\"contrasena\":\"abcdefgh\\udc00\"}");

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal(
            ["La contraseña tiene caracteres no válidos. Escribila de nuevo."], await Http.ErroresContrasenaAsync(respuesta));
        Assert.Null(Http.SetCookie(respuesta));
        Assert.Empty(fabrica.Eventos());
    }

    [Fact]
    public async Task Salir_SinCuerpoConContentTypeJson_NoContent204()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);

        var salida = await Http.PostJsonAsync(cliente, "/api/acceso/salir", string.Empty, cookie);

        Assert.Equal(HttpStatusCode.NoContent, salida.StatusCode);
        Assert.Contains(fabrica.Eventos(), e => e.Tipo == "salida");
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    public async Task Definir_ContrasenaFaltante_Invalido400(string cuerpo)
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();

        var respuesta = await Http.PostJsonAsync(cliente, "/api/acceso/definir", cuerpo);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal([AccionMuyCorta], await Http.ErroresContrasenaAsync(respuesta));
        Assert.Null(Http.SetCookie(respuesta));
        Assert.Empty(fabrica.Eventos());
    }

    [Fact]
    public async Task Ingresar_Correcta_EmiteCookieYSalirLaBorra()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        await Http.DefinirAsync(cliente);

        var ingreso = await Http.PostJsonAsync(cliente, "/api/acceso/ingresar", Http.CuerpoContrasena(Http.Contrasena));
        Assert.Equal(HttpStatusCode.NoContent, ingreso.StatusCode);
        var cookie = Http.Cookie(ingreso);
        Assert.NotNull(cookie);
        Assert.Equal(HttpStatusCode.NoContent, (await Http.GetAsync(cliente, "/api/sesion", cookie)).StatusCode);

        var salida = await Http.PostJsonAsync(cliente, "/api/acceso/salir", "{}", cookie);

        Assert.Equal(HttpStatusCode.NoContent, salida.StatusCode);
        Assert.Contains("expires=thu, 01 jan 1970", Http.SetCookie(salida)?.ToLowerInvariant() ?? string.Empty);
        var eventos = fabrica.Eventos().Select(e => (e.Tipo, e.Resultado)).ToList();
        Assert.Equal([("definicion", "exito"), ("ingreso", "exito"), ("salida", "exito")], eventos);
    }

    [Fact]
    public async Task Ingresar_LimitadorSaturado_Rechazado429()
    {
        var hasher = new HasherBloqueante();
        using var fabrica = new FabricaApi(servicios =>
        {
            servicios.RemoveAll<IHasherContrasena>();
            servicios.AddSingleton<IHasherContrasena>(hasher);
        });
        var cliente = fabrica.CrearClienteSinCookies();
        await Http.DefinirAsync(cliente);
        Task<HttpResponseMessage> Ingresar() =>
            Http.PostJsonAsync(cliente, "/api/acceso/ingresar", Http.CuerpoContrasena(Http.Contrasena));

        try
        {
            // Dos pedidos ocupan los dos lugares del limitador, retenidos dentro de la verificación.
            var enCurso = new[] { Ingresar(), Ingresar() };
            await hasher.EsperarDentroAsync(2);

            // Once más: diez entran a la cola y uno sobra.
            var siguientes = Enumerable.Range(0, 11).Select(_ => Ingresar()).ToList();
            var primero = await Task.WhenAny(Task.WhenAny(siguientes), Task.Delay(TimeSpan.FromSeconds(20)));
            Assert.True(primero is Task<Task<HttpResponseMessage>>, "Ningún pedido fue rechazado con el limitador lleno.");
            var rechazado = await await (Task<Task<HttpResponseMessage>>)primero;
            Assert.Equal(HttpStatusCode.TooManyRequests, rechazado.StatusCode);

            hasher.Liberar();
            var todas = await Task.WhenAll(enCurso.Concat(siguientes));
            Assert.Equal(1, todas.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests));
            Assert.Equal(12, todas.Count(r => r.StatusCode == HttpStatusCode.Unauthorized));
        }
        finally
        {
            hasher.Liberar();
        }
    }

    [Fact]
    public async Task Definir_LimitadorSaturado_Rechazado429()
    {
        var hasher = new HasherBloqueante(alCalcular: true);
        using var fabrica = new FabricaApi(servicios =>
        {
            servicios.RemoveAll<IHasherContrasena>();
            servicios.AddSingleton<IHasherContrasena>(hasher);
        });
        var cliente = fabrica.CrearClienteSinCookies();
        Task<HttpResponseMessage> Definir() =>
            Http.PostJsonAsync(cliente, "/api/acceso/definir", Http.CuerpoContrasena(Http.Contrasena));

        try
        {
            // Dos pedidos ocupan los dos lugares del limitador, retenidos dentro del cálculo del hash.
            var enCurso = new[] { Definir(), Definir() };
            await hasher.EsperarDentroAsync(2);

            // Once más: diez entran a la cola y uno sobra.
            var siguientes = Enumerable.Range(0, 11).Select(_ => Definir()).ToList();
            var primero = await Task.WhenAny(Task.WhenAny(siguientes), Task.Delay(TimeSpan.FromSeconds(20)));
            Assert.True(primero is Task<Task<HttpResponseMessage>>, "Ningún pedido fue rechazado con el limitador lleno.");
            var rechazado = await await (Task<Task<HttpResponseMessage>>)primero;
            Assert.Equal(HttpStatusCode.TooManyRequests, rechazado.StatusCode);

            hasher.Liberar();
            var todas = await Task.WhenAll(enCurso.Concat(siguientes));
            Assert.Equal(1, todas.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests));
            // Una definición gana; las demás encuentran la contraseña ya definida.
            Assert.Equal(1, todas.Count(r => r.StatusCode == HttpStatusCode.Created));
            Assert.Equal(11, todas.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        }
        finally
        {
            hasher.Liberar();
        }
    }

    /// <summary>
    /// Hasher que retiene cada verificación (o, con <c>alCalcular</c>, cada cálculo) hasta que la prueba lo libera;
    /// siempre rechaza al verificar.
    /// </summary>
    private sealed class HasherBloqueante(bool alCalcular = false) : IHasherContrasena
    {
        private readonly ManualResetEventSlim _liberado = new(false);
        private int _dentro;

        public string Calcular(string contrasena)
        {
            if (alCalcular)
            {
                Retener();
            }

            return "PBKDF2-SHA256$600000$AAAA$AAAA";
        }

        public bool Verificar(string contrasena, string hash)
        {
            Retener();
            return false;
        }

        private void Retener()
        {
            Interlocked.Increment(ref _dentro);
            _liberado.Wait(TimeSpan.FromSeconds(30));
        }

        public async Task EsperarDentroAsync(int cantidad)
        {
            var limite = DateTime.UtcNow.AddSeconds(20);
            while (Volatile.Read(ref _dentro) < cantidad)
            {
                Assert.True(DateTime.UtcNow < limite, $"No llegaron {cantidad} verificaciones en curso.");
                await Task.Delay(20);
            }
        }

        public void Liberar() => _liberado.Set();
    }
}
