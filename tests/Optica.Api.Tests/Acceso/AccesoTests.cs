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
    private const string NuevaContrasena = "clave-nueva-22";

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
    public async Task Ingresar_RelojAtrasado_423ConContrasenaCorrecta()
    {
        // El FakeTimeProvider no retrocede: la Api ve su hora más un desfase que la prueba ajusta.
        var reloj = new RelojConDesfase();
        using var fabrica = new FabricaApi(servicios =>
        {
            servicios.RemoveAll<TimeProvider>();
            servicios.AddSingleton<TimeProvider>(reloj);
        });
        reloj.Base = fabrica.Reloj;
        var cliente = fabrica.CrearClienteSinCookies();
        await Http.DefinirAsync(cliente);

        for (var intento = 1; intento <= 4; intento++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await IngresarAsync(cliente, "otra-clave-mala")).StatusCode);
            fabrica.Reloj.Advance(TimeSpan.FromSeconds(1));
        }

        await AfirmarBloqueadoAsync(await IngresarAsync(cliente, "otra-clave-mala"));

        // FIX-001: la hora de la PC retrocede 1 segundo (resincronización) y el bloqueo sigue, ni la correcta entra.
        reloj.Desfase = TimeSpan.FromSeconds(-1);
        var conLaCorrecta = await IngresarAsync(cliente, Http.Contrasena);

        await AfirmarBloqueadoAsync(conLaCorrecta);
        Assert.Null(Http.Cookie(conLaCorrecta));
        var eventos = fabrica.Eventos();
        Assert.Equal(5, eventos.Count(e => e is { Tipo: "ingreso", Resultado: "rechazo" }));
        Assert.Equal(("ingreso", "bloqueado"), (eventos[^1].Tipo, eventos[^1].Resultado));
        // El intento se evaluó con la hora atrasada: quedó antes del quinto rechazo (el ancla).
        Assert.True(
            eventos[^1].FechaHora < eventos[^2].FechaHora,
            $"El bloqueado ({eventos[^1].FechaHora:O}) no quedó antes del quinto ({eventos[^2].FechaHora:O}).");
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
        var hasher = new HasherConEncuentro();
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
        var hasher = new HasherConEncuentro();
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
            FabricaApi.LiberarConexiones(directorio);
            Directory.Delete(directorio, recursive: true);
        }
    }

    [Fact]
    public async Task Cambiar_ActualCorrecta_204YIngresarAceptaLaNuevaYRechazaLaAnterior()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);

        var respuesta = await Http.CambiarAsync(cliente, Http.Contrasena, NuevaContrasena, cookie);

        Assert.Equal(HttpStatusCode.NoContent, respuesta.StatusCode);
        Assert.Equal(string.Empty, await respuesta.Content.ReadAsStringAsync());
        Assert.Equal(("cambio_contrasena", "exito"), fabrica.Eventos().Select(e => (e.Tipo, e.Resultado)).Last());
        // La sesión abierta sigue sirviendo (riesgo aceptado en FEAT-001a).
        Assert.Equal(HttpStatusCode.NoContent, (await Http.GetAsync(cliente, "/api/sesion", cookie)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await IngresarAsync(cliente, NuevaContrasena)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await IngresarAsync(cliente, Http.Contrasena)).StatusCode);
    }

    [Fact]
    public async Task Cambiar_ActualIncorrecta_Invalido400ConCampoActual()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);

        var respuesta = await Http.CambiarAsync(cliente, "otra-clave-mala", NuevaContrasena, cookie);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        Assert.Equal(["La contraseña actual no es válida."], await Http.ErroresContrasenaActualAsync(respuesta));
        Assert.Equal(["contrasenaActual"], await Http.CamposConErrorAsync(respuesta));
        Assert.Equal(("cambio_contrasena", "rechazo"), fabrica.Eventos().Select(e => (e.Tipo, e.Resultado)).Last());
        // No se cambió nada.
        Assert.Equal(HttpStatusCode.NoContent, (await IngresarAsync(cliente, Http.Contrasena)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await IngresarAsync(cliente, NuevaContrasena)).StatusCode);
    }

    [Theory]
    [InlineData("{\"contrasenaNueva\":\"clave-nueva-22\"}")]
    [InlineData("{\"contrasenaActual\":null,\"contrasenaNueva\":\"clave-nueva-22\"}")]
    [InlineData("{\"contrasenaActual\":\"1234567\",\"contrasenaNueva\":\"clave-nueva-22\"}")]
    public async Task Cambiar_ActualFaltante_Invalido400(string cuerpo)
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var eventosAntes = fabrica.Eventos().Count;

        var respuesta = await Http.PostJsonAsync(cliente, "/api/acceso/cambiar", cuerpo, cookie);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal([AccionMuyCorta], await Http.ErroresContrasenaActualAsync(respuesta));
        Assert.Equal(["contrasenaActual"], await Http.CamposConErrorAsync(respuesta));
        Assert.Equal(eventosAntes, fabrica.Eventos().Count);
    }

    [Theory]
    [InlineData("1234567", AccionMuyCorta)]
    [InlineData("larga", "Ingresá una contraseña de 128 caracteres o menos")]
    [InlineData("sustituto", "La contraseña tiene caracteres no válidos. Escribila de nuevo.")]
    [InlineData(null, AccionMuyCorta)]
    public async Task Cambiar_NuevaInvalida_Invalido400ConCampoNueva(string? nueva, string accion)
    {
        nueva = nueva switch
        {
            "larga" => new string('x', 129),
            "sustituto" => "abcdefgh\uD800",
            _ => nueva,
        };
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var eventosAntes = fabrica.Eventos().Count;

        var respuesta = await Http.CambiarAsync(cliente, Http.Contrasena, nueva, cookie);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal([accion], await Http.ErroresContrasenaNuevaAsync(respuesta));
        Assert.Equal(["contrasenaNueva"], await Http.CamposConErrorAsync(respuesta));
        Assert.Equal(eventosAntes, fabrica.Eventos().Count);
        Assert.Equal(HttpStatusCode.NoContent, (await IngresarAsync(cliente, Http.Contrasena)).StatusCode);
    }

    [Theory]
    [InlineData("{\"contrasenaActual\":\"clave-de-prueba-1\",\"contrasenaNueva\":12345678}", "contrasenaNueva")]
    [InlineData("{\"contrasenaActual\":\"clave-de-prueba-1\",\"contrasenaNueva\":[\"a\"]}", "contrasenaNueva")]
    [InlineData("{\"contrasenaActual\":12345678,\"contrasenaNueva\":\"clave-nueva-22\"}", "contrasenaActual")]
    [InlineData("{\"contrasenaActual\":\"clave-sin-cerrar", "contrasenaActual")]
    [InlineData("esto no es json", "contrasenaActual")]
    [InlineData("{\"contrasenaActual\":\"clave-de-prueba-1\",\"contrasenaNueva\":[\"a\"", "contrasenaNueva")]
    [InlineData("{\"contrasenaActual\":\"clave-de-prueba-1\",\"contrasenaNueva\":[\"a\",}", "contrasenaNueva")]
    [InlineData("{\"contrasenaActual\":\"clave-de-prueba-1\",\"contrasenaNueva\":{\"x\":", "contrasenaNueva")]
    [InlineData("{\"contrasenaActual\":\"clave-de-prueba-1\",\"contrasenaNueva\":{\"x\":[1,2}}", "contrasenaNueva")]
    [InlineData("{\"contrasenaActual\":[[[\"a\"", "contrasenaActual")]
    public async Task Cambiar_CuerpoIlegible_Invalido400EnElCampoQueCorresponde(string cuerpo, string campo)
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var eventosAntes = fabrica.Eventos().Count;

        var respuesta = await Http.PostJsonAsync(cliente, "/api/acceso/cambiar", cuerpo, cookie);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal([campo], await Http.CamposConErrorAsync(respuesta));
        var errores = campo == "contrasenaNueva"
            ? await Http.ErroresContrasenaNuevaAsync(respuesta)
            : await Http.ErroresContrasenaActualAsync(respuesta);
        Assert.Equal([AccionCuerpoIlegible], errores);
        Assert.DoesNotContain("JSON", await respuesta.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(eventosAntes, fabrica.Eventos().Count);
    }

    [Fact]
    public async Task Cambiar_ActualConSustitutoSueltoEscapado_Invalido400CaracteresNoValidos()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var eventosAntes = fabrica.Eventos().Count;

        // Escrito con escapes: el sustituto suelto viaja como \ud800 y lo tiene que entregar el lector de la Api.
        var respuesta = await Http.PostJsonAsync(
            cliente,
            "/api/acceso/cambiar",
            "{\"contrasenaActual\":\"abcdefgh\\ud800\",\"contrasenaNueva\":\"clave-nueva-22\"}",
            cookie);

        // Es una contraseña con caracteres no válidos (política), no un cuerpo ilegible.
        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal(["contrasenaActual"], await Http.CamposConErrorAsync(respuesta));
        Assert.Equal(
            ["La contraseña tiene caracteres no válidos. Escribila de nuevo."],
            await Http.ErroresContrasenaActualAsync(respuesta));
        Assert.Equal(eventosAntes, fabrica.Eventos().Count);
    }

    [Fact]
    public async Task Cambiar_CuerpoVacio_Invalido400EnLosDosCampos()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var eventosAntes = fabrica.Eventos().Count;

        var respuesta = await Http.PostJsonAsync(cliente, "/api/acceso/cambiar", string.Empty, cookie);

        // Como en definir: un cuerpo vacío es un pedido sin contraseñas, no un cuerpo ilegible. Faltan las dos.
        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        // El orden de las claves de errors no es parte del contrato.
        Assert.Equal(
            ["contrasenaActual", "contrasenaNueva"], (await Http.CamposConErrorAsync(respuesta)).Order(StringComparer.Ordinal));
        Assert.Equal([AccionMuyCorta], await Http.ErroresContrasenaActualAsync(respuesta));
        Assert.Equal([AccionMuyCorta], await Http.ErroresContrasenaNuevaAsync(respuesta));
        Assert.Equal(eventosAntes, fabrica.Eventos().Count);
        Assert.Equal(HttpStatusCode.NoContent, (await IngresarAsync(cliente, Http.Contrasena)).StatusCode);
    }

    [Fact]
    public async Task Cambiar_LimitadorSaturado_Rechazado429()
    {
        var hasher = new HasherBloqueante();
        using var fabrica = new FabricaApi(servicios =>
        {
            servicios.RemoveAll<IHasherContrasena>();
            servicios.AddSingleton<IHasherContrasena>(hasher);
        });
        var cliente = fabrica.CrearClienteSinCookies();
        // Definir solo calcula el hash (no se retiene): deja la sesión abierta para cambiar.
        var cookie = await Http.DefinirAsync(cliente);
        Task<HttpResponseMessage> Cambiar() => Http.CambiarAsync(cliente, Http.Contrasena, NuevaContrasena, cookie);

        try
        {
            // Dos pedidos ocupan los dos lugares del limitador, retenidos dentro de la verificación de la actual.
            var enCurso = new[] { Cambiar(), Cambiar() };
            await hasher.EsperarDentroAsync(2);

            // Once más: diez entran a la cola y uno sobra.
            var siguientes = Enumerable.Range(0, 11).Select(_ => Cambiar()).ToList();
            var primero = await Task.WhenAny(Task.WhenAny(siguientes), Task.Delay(TimeSpan.FromSeconds(20)));
            Assert.True(primero is Task<Task<HttpResponseMessage>>, "Ningún pedido fue rechazado con el limitador lleno.");
            var rechazado = await await (Task<Task<HttpResponseMessage>>)primero;
            Assert.Equal(HttpStatusCode.TooManyRequests, rechazado.StatusCode);

            hasher.Liberar();
            var todas = await Task.WhenAll(enCurso.Concat(siguientes));
            Assert.Equal(1, todas.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests));
            // Los doce que pasaron el limitador llegaron al caso de uso; este hasher siempre rechaza la actual.
            Assert.Equal(12, todas.Count(r => r.StatusCode == HttpStatusCode.BadRequest));
        }
        finally
        {
            hasher.Liberar();
        }
    }

    [Fact]
    public async Task Cambiar_SinSesion_NoAutorizado401()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        await Http.DefinirAsync(cliente);
        var eventosAntes = fabrica.Eventos().Count;

        var respuesta = await Http.CambiarAsync(cliente, Http.Contrasena, NuevaContrasena, cookie: null);

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        Assert.Equal(string.Empty, await respuesta.Content.ReadAsStringAsync());
        Assert.Null(respuesta.Headers.Location);
        Assert.Equal(eventosAntes, fabrica.Eventos().Count);
        Assert.Equal(HttpStatusCode.NoContent, (await IngresarAsync(cliente, Http.Contrasena)).StatusCode);
        // Sin sesión, una ruta inexistente también da 401: el control asegura que el 401 es de la acción protegida.
        ProteccionTests.AfirmarQueElEndpointExiste(fabrica, "/api/acceso/cambiar");
    }

    [Fact]
    public async Task Cambiar_SinCredencial_Conflicto409()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        // Como un restablecimiento con la sesión abierta: la credencial ya no está.
        using (var alcance = fabrica.Services.CreateScope())
        {
            var contexto = alcance.ServiceProvider.GetRequiredService<Optica.Infrastructure.Persistencia.OpticaDbContext>();
            contexto.CredencialesAcceso.Remove(contexto.CredencialesAcceso.Single());
            contexto.SaveChanges();
        }

        var eventosAntes = fabrica.Eventos().Count;

        var respuesta = await Http.CambiarAsync(cliente, Http.Contrasena, NuevaContrasena, cookie);

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        var json = await Http.JsonAsync(respuesta);
        Assert.Equal(Optica.Api.Controllers.AccesoController.AccionSinContrasena, json.GetProperty("detail").GetString());
        Assert.Equal(eventosAntes, fabrica.Eventos().Count);
        var estado = await Http.JsonAsync(await Http.GetAsync(cliente, "/api/acceso/estado"));
        Assert.False(estado.GetProperty("contrasenaDefinida").GetBoolean());
    }

    [Fact]
    public async Task Cambiar_DosCambiosSimultaneos_Conflicto409()
    {
        var hasher = new HasherConEncuentro();
        using var fabrica = new FabricaApi(servicios =>
        {
            servicios.RemoveAll<IHasherContrasena>();
            servicios.AddSingleton<IHasherContrasena>(hasher);
        });
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);

        // Los dos pedidos leen la misma credencial antes de que cualquiera confirme: el hasher retiene cada
        // verificación de la actual hasta que las dos están adentro.
        hasher.EsperarEncuentro(2);
        var nuevas = new[] { "nueva-clave-A1", "nueva-clave-B2" };
        var respuestas = await Task.WhenAll(nuevas.Select(nueva => Http.CambiarAsync(cliente, Http.Contrasena, nueva, cookie)));

        Assert.True(hasher.SeEncontraron, "Los dos pedidos no estuvieron a la vez dentro de la verificación.");
        Assert.Equal(1, respuestas.Count(r => r.StatusCode == HttpStatusCode.NoContent));
        Assert.Equal(1, respuestas.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        var conflicto = respuestas.Single(r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal("application/problem+json", conflicto.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            Optica.Api.Controllers.AccesoController.MensajeCambioEnConflicto,
            (await Http.JsonAsync(conflicto)).GetProperty("detail").GetString());

        // Quedó una sola: la del que respondió 204; ni la del otro ni la anterior entran.
        var ganadora = nuevas[Array.FindIndex(respuestas, r => r.StatusCode == HttpStatusCode.NoContent)];
        var perdedora = nuevas.Single(n => n != ganadora);
        var eventos = fabrica.Eventos().Where(e => e.Tipo == "cambio_contrasena").Select(e => e.Resultado).ToList();
        Assert.Equal(["exito"], eventos);
        Assert.Equal(HttpStatusCode.NoContent, (await IngresarAsync(cliente, ganadora)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await IngresarAsync(cliente, perdedora)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await IngresarAsync(cliente, Http.Contrasena)).StatusCode);

        // El conflicto no deja la excepción en el log por encima de Debug.
        var lineas = fabrica.Registro.Lineas;
        Assert.DoesNotContain(lineas, l => l.Nivel > Microsoft.Extensions.Logging.LogLevel.Debug && l.Excepcion is not null);
        Assert.DoesNotContain(lineas, l => l.TextoCompleto.Contains("ConflictoAlConfirmar", StringComparison.Ordinal));
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
    /// Reloj de la Api que delega en <see cref="Base"/> (el <c>FakeTimeProvider</c> de <see cref="FabricaApi"/>, única
    /// fuente de la hora) y le suma <see cref="Desfase"/>: un desfase negativo atrasa la hora, como una resincronización,
    /// cosa que el <c>FakeTimeProvider</c> no permite. Los temporizadores y las marcas monotónicas no se desfasan.
    /// </summary>
    private sealed class RelojConDesfase : TimeProvider
    {
        public TimeProvider? Base { get; set; }

        public TimeSpan Desfase { get; set; }

        private TimeProvider Origen =>
            Base ?? throw new InvalidOperationException("Falta asignar el reloj base antes de usar la Api.");

        public override DateTimeOffset GetUtcNow() => Origen.GetUtcNow() + Desfase;

        public override TimeZoneInfo LocalTimeZone => Origen.LocalTimeZone;

        public override long TimestampFrequency => Origen.TimestampFrequency;

        public override long GetTimestamp() => Origen.GetTimestamp();

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            Origen.CreateTimer(callback, state, dueTime, period);
    }

    /// <summary>
    /// Hasher que acepta solo la contraseña de la que salió cada hash; cada cálculo da un hash distinto, como una sal
    /// nueva. Después de <see cref="EsperarEncuentro"/>, cada verificación espera a que la cantidad pedida de
    /// verificaciones esté adentro a la vez antes de responder: así dos pedidos leen el mismo estado (los rechazos o la
    /// credencial) antes de que cualquiera confirme lo suyo.
    /// </summary>
    private sealed class HasherConEncuentro : IHasherContrasena
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _calculados = new();
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

        public string Calcular(string contrasena)
        {
            var hash = "PBKDF2-SHA256$600000$" + Guid.NewGuid().ToString("N") + "$AAAA";
            _calculados[hash] = contrasena;
            return hash;
        }

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

            return _calculados.TryGetValue(hash, out var calculada) && calculada == contrasena;
        }
    }
}
