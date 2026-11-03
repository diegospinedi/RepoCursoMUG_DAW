using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Optica.Application.Acceso;

namespace Optica.Api.Tests.Acceso;

public sealed class AccesoTests
{
    private const string AccionMuyCorta = "Ingresá una contraseña de 8 caracteres o más";
    private const string MensajeIncorrecta = "La contraseña no es correcta. Volvé a ingresarla.";
    private const string AccionCuerpoIlegible = "No se pudo leer la contraseña enviada. Escribila de nuevo.";
    private const string MensajeBloqueado = "El acceso está bloqueado temporalmente. Volvé a intentar en unos minutos.";

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
            // Los doce que pasaron el limitador llegaron al caso de uso: 401 hasta el bloqueo y 423 desde el quinto
            // rechazo (FEAT-001b). Después de confirmar el suyo, cada pedido bloquea si lo releído (con la hora tomada
            // después de releer) o lo leído más su propio rechazo llegan al quinto: el que deja el quinto rechazo o uno
            // posterior siempre responde 423, en cualquier orden de confirmación, y nunca hay más de cuatro 401. Con
            // dos lugares en el limitador, el que deja el cuarto también puede responder 423 si el otro pedido en curso
            // confirmó antes de su relectura, así que hay al menos tres 401.
            var noAutorizados = todas.Count(r => r.StatusCode == HttpStatusCode.Unauthorized);
            var bloqueados = todas.Count(r => r.StatusCode == (HttpStatusCode)StatusCodes.Status423Locked);
            Assert.Equal(12, noAutorizados + bloqueados);
            Assert.InRange(noAutorizados, 3, 4);
        }
        finally
        {
            hasher.Liberar();
        }
    }

    [Fact]
    public async Task Ingresar_CincoRechazos_Bloqueado423ConLaCorrecta()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        await Http.DefinirAsync(cliente);

        for (var intento = 1; intento <= 4; intento++)
        {
            var rechazo = await IngresarAsync(cliente, "otra-clave-mala");
            Assert.Equal(HttpStatusCode.Unauthorized, rechazo.StatusCode);
            fabrica.Reloj.Advance(TimeSpan.FromSeconds(1));
        }

        // El quinto rechazo responde 423 en ese mismo intento.
        var quinto = await IngresarAsync(cliente, "otra-clave-mala");
        await AfirmarBloqueadoAsync(quinto);

        // Durante el bloqueo, ni la correcta entra.
        var conLaCorrecta = await IngresarAsync(cliente, Http.Contrasena);
        await AfirmarBloqueadoAsync(conLaCorrecta);

        var eventos = fabrica.Eventos().Select(e => (e.Tipo, e.Resultado)).ToList();
        Assert.Equal(
            [
                ("definicion", "exito"),
                ("ingreso", "rechazo"), ("ingreso", "rechazo"), ("ingreso", "rechazo"), ("ingreso", "rechazo"),
                ("ingreso", "rechazo"),
                ("ingreso", "bloqueado"),
            ],
            eventos);
    }

    [Fact]
    public async Task Ingresar_Pasados5Minutos_AceptaLaCorrecta()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        await Http.DefinirAsync(cliente);
        for (var intento = 1; intento <= 5; intento++)
        {
            await IngresarAsync(cliente, "otra-clave-mala");
        }

        await AfirmarBloqueadoAsync(await IngresarAsync(cliente, Http.Contrasena));

        // Un instante antes de los 5 minutos sigue bloqueado; a los 5 minutos del quinto rechazo se libera solo.
        fabrica.Reloj.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(1));
        await AfirmarBloqueadoAsync(await IngresarAsync(cliente, Http.Contrasena));
        fabrica.Reloj.Advance(TimeSpan.FromSeconds(1));
        var ingreso = await IngresarAsync(cliente, Http.Contrasena);

        Assert.Equal(HttpStatusCode.NoContent, ingreso.StatusCode);
        Assert.NotNull(Http.Cookie(ingreso));
        Assert.Equal(("ingreso", "exito"), fabrica.Eventos().Select(e => (e.Tipo, e.Resultado)).Last());
    }

    [Fact]
    public async Task Ingresar_DosPedidosSimultaneosEnElCuarto_Bloqueado423()
    {
        var hasher = new HasherConEncuentro(Http.Contrasena);
        using var fabrica = new FabricaApi(servicios =>
        {
            servicios.RemoveAll<IHasherContrasena>();
            servicios.AddSingleton<IHasherContrasena>(hasher);
        });
        var cliente = fabrica.CrearClienteSinCookies();
        await Http.DefinirAsync(cliente);
        for (var intento = 1; intento <= 4; intento++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await IngresarAsync(cliente, "otra-clave-mala")).StatusCode);
        }

        // Los dos pedidos leen los cuatro rechazos confirmados antes de que cualquiera registre el suyo: el hasher
        // retiene cada verificación hasta que las dos están adentro.
        hasher.EsperarEncuentro(2);
        var simultaneos = await Task.WhenAll(
            IngresarAsync(cliente, "otra-clave-mala"), IngresarAsync(cliente, "otra-clave-mala"));

        Assert.True(hasher.SeEncontraron, "Los dos pedidos no estuvieron a la vez dentro de la verificación.");
        foreach (var respuesta in simultaneos)
        {
            await AfirmarBloqueadoAsync(respuesta);
        }

        // Seis rechazos en la ventana: el bloqueo sigue anclado en el quinto y no se pierde.
        await AfirmarBloqueadoAsync(await IngresarAsync(cliente, Http.Contrasena));
        var eventos = fabrica.Eventos();
        Assert.Equal(6, eventos.Count(e => e is { Tipo: "ingreso", Resultado: "rechazo" }));
        Assert.Equal(("ingreso", "bloqueado"), (eventos[^1].Tipo, eventos[^1].Resultado));
        Assert.Equal(6, hasher.Verificaciones);
    }

    [Fact]
    public async Task Ingresar_DosPedidosSimultaneosEnElTercero_ElQuintoRecibe423()
    {
        var hasher = new HasherConEncuentro(Http.Contrasena);
        using var fabrica = new FabricaApi(servicios =>
        {
            servicios.RemoveAll<IHasherContrasena>();
            servicios.AddSingleton<IHasherContrasena>(hasher);
        });
        var cliente = fabrica.CrearClienteSinCookies();
        await Http.DefinirAsync(cliente);
        for (var intento = 1; intento <= 3; intento++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await IngresarAsync(cliente, "otra-clave-mala")).StatusCode);
        }

        // Los dos pedidos leen los tres rechazos confirmados antes de que cualquiera registre el suyo: el que confirma
        // segundo deja el quinto rechazo y tiene que recibir 423; el primero puede ver el cuarto o, si el otro ya
        // confirmó, el quinto.
        hasher.EsperarEncuentro(2);
        var simultaneos = await Task.WhenAll(
            IngresarAsync(cliente, "otra-clave-mala"), IngresarAsync(cliente, "otra-clave-mala"));

        Assert.True(hasher.SeEncontraron, "Los dos pedidos no estuvieron a la vez dentro de la verificación.");
        var bloqueados = simultaneos.Where(r => r.StatusCode == (HttpStatusCode)StatusCodes.Status423Locked).ToList();
        Assert.All(simultaneos, r => Assert.Contains(
            r.StatusCode, new[] { HttpStatusCode.Unauthorized, (HttpStatusCode)StatusCodes.Status423Locked }));
        Assert.NotEmpty(bloqueados);
        foreach (var respuesta in bloqueados)
        {
            await AfirmarBloqueadoAsync(respuesta);
        }

        Assert.Equal(5, fabrica.Eventos().Count(e => e is { Tipo: "ingreso", Resultado: "rechazo" }));
        await AfirmarBloqueadoAsync(await IngresarAsync(cliente, Http.Contrasena));
    }

    [Fact]
    public async Task Ingresar_BloqueoSobreviveAlReinicio_Bloqueado423()
    {
        var directorio = Path.Combine(Path.GetTempPath(), "optica-pruebas-api", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directorio);
        try
        {
            using (var primera = new FabricaApi(directorioDatos: directorio))
            {
                var cliente = primera.CrearClienteSinCookies();
                await Http.DefinirAsync(cliente);
                for (var intento = 1; intento <= 5; intento++)
                {
                    await IngresarAsync(cliente, "otra-clave-mala");
                }
            }

            // Se descartó la primera Api antes de crear la segunda sobre el mismo directorio de datos.
            using var segunda = new FabricaApi(directorioDatos: directorio);
            var respuesta = await IngresarAsync(segunda.CrearClienteSinCookies(), Http.Contrasena);

            await AfirmarBloqueadoAsync(respuesta);
            Assert.Equal(5, segunda.Eventos().Count(e => e is { Tipo: "ingreso", Resultado: "rechazo" }));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directorio, recursive: true);
        }
    }

    private static Task<HttpResponseMessage> IngresarAsync(HttpClient cliente, string contrasena) =>
        Http.PostJsonAsync(cliente, "/api/acceso/ingresar", Http.CuerpoContrasena(contrasena));

    /// <summary>423 con ProblemDetails: el mensaje de bloqueo en <c>detail</c>, sin <c>errors</c> y sin cookie.</summary>
    private static async Task AfirmarBloqueadoAsync(HttpResponseMessage respuesta)
    {
        Assert.Equal((HttpStatusCode)StatusCodes.Status423Locked, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        var json = await Http.JsonAsync(respuesta);
        Assert.Equal(MensajeBloqueado, json.GetProperty("detail").GetString());
        Assert.Equal(StatusCodes.Status423Locked, json.GetProperty("status").GetInt32());
        Assert.False(json.TryGetProperty("errors", out _));
        Assert.Null(Http.SetCookie(respuesta));
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

    /// <summary>
    /// Hasher que acepta solo <paramref name="correcta"/>. Después de <see cref="EsperarEncuentro"/>, cada verificación
    /// espera a que la cantidad pedida de verificaciones esté adentro a la vez antes de responder: así dos pedidos leen
    /// el mismo estado del registro antes de que cualquiera confirme su rechazo.
    /// </summary>
    private sealed class HasherConEncuentro(string correcta) : IHasherContrasena
    {
        private const string HashFijo = "PBKDF2-SHA256$600000$AAAA$AAAA";
        private readonly object _candado = new();
        private int _esperadas;
        private int _dentro;
        private int _verificaciones;

        public bool SeEncontraron { get; private set; }

        public int Verificaciones => Volatile.Read(ref _verificaciones);

        public void EsperarEncuentro(int cantidad)
        {
            lock (_candado)
            {
                _esperadas = cantidad;
                _dentro = 0;
            }
        }

        public string Calcular(string contrasena) => HashFijo;

        public bool Verificar(string contrasena, string hash)
        {
            Interlocked.Increment(ref _verificaciones);
            lock (_candado)
            {
                if (_esperadas > 0)
                {
                    _dentro++;
                    Monitor.PulseAll(_candado);
                    var limite = DateTime.UtcNow.AddSeconds(20);
                    while (_dentro < _esperadas && DateTime.UtcNow < limite)
                    {
                        Monitor.Wait(_candado, TimeSpan.FromMilliseconds(100));
                    }

                    if (_dentro >= _esperadas)
                    {
                        SeEncontraron = true;
                    }
                }
            }

            return hash == HashFijo && contrasena == correcta;
        }
    }
}
