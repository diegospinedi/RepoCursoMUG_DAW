using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Optica.Application.Comun;
using Optica.Domain.Catalogo;
using Optica.Domain.Presupuestos;

namespace Optica.Api.Tests.Presupuestos;

/// <summary>
/// <c>POST /api/presupuestos</c> y <c>GET /api/presupuestos/{numero}</c> (FEAT-003a, Block 4): con sesión, la Api
/// traduce el alta y la consulta del presupuesto. Un cuerpo ilegible da 400 con un texto fijo; los datos inválidos, 400
/// con todos los campos (D-20) y sin repetir lo enviado (D-17); un presupuesto inexistente, 404 sin detalles. Todos los
/// datos del cliente de estas pruebas son ficticios.
/// </summary>
public sealed class PresupuestosApiTests
{
    private const string Ruta = "/api/presupuestos";
    private const string RutaArticulos = "/api/articulos";
    private const string AccionCuerpoIlegible = "No se pudieron leer los valores enviados. Revisalos y grabá de nuevo.";
    private const string TituloNoEncontrado = "No se encontró el presupuesto.";

    private static readonly string[] CamposRespuesta = ["numero", "fecha", "estado", "cliente", "lineas"];
    private static readonly string[] CamposCliente = ["apellido", "nombre", "dni", "domicilio", "email", "telefono"];

    private static readonly string[] CamposLinea =
    [
        "codigoArticulo", "descripcion", "precioUnitario", "cantidad", "porcentajeDescuento", "precioConDescuento",
        "precioFinal",
    ];

    // Errores de lectura y de validación.

    /// <summary>
    /// Un cuerpo ilegible responde 400 con un solo error y la acción fija, en el campo que nombra el lector de JSON (el
    /// DNI enviado como número, un código de artículo que no es un entero, un email que no es texto) o en
    /// <c>apellido</c> si no nombra ninguno; sin los mensajes del lector ni lo enviado, y sin grabar nada.
    /// </summary>
    [Theory]
    [InlineData("esto no es json", "apellido")]
    [InlineData("[1,2,3]", "apellido")]
    [InlineData("{\"cliente\":{\"apellido\":\"Zq7-marcado", "apellido")]
    [InlineData(
        "{\"cliente\":{\"apellido\":\"Perez\",\"nombre\":\"Ana\",\"dni\":30111222},\"lineas\":[{\"codigoArticulo\":1}]}",
        "dni")]
    [InlineData(
        "{\"cliente\":{\"apellido\":\"Perez\",\"nombre\":\"Ana\",\"dni\":\"30111222\",\"email\":[\"Zq7-marcado\"]},"
        + "\"lineas\":[{\"codigoArticulo\":1}]}",
        "email")]
    [InlineData(
        "{\"cliente\":{\"apellido\":\"Perez\",\"nombre\":\"Ana\",\"dni\":\"30111222\"},"
        + "\"lineas\":[{\"codigoArticulo\":1},{\"codigoArticulo\":\"Zq7-marcado\"}]}",
        "lineas[1]")]
    [InlineData(
        "{\"cliente\":{\"apellido\":\"Perez\",\"nombre\":\"Ana\",\"dni\":\"30111222\"},"
        + "\"lineas\":[{\"codigoArticulo\":1.5}]}",
        "lineas[0]")]
    [InlineData(
        "{\"cliente\":{\"apellido\":\"Perez\",\"nombre\":\"Ana\",\"dni\":\"30111222\"},\"lineas\":\"Zq7-marcado\"}",
        "lineas")]
    public async Task Post_CuerpoIlegible_400(string cuerpo, string campo)
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        await GrabarArticuloAsync(cliente, cookie);

        var respuesta = await Http.PostJsonAsync(cliente, Ruta, cuerpo, cookie);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        Assert.Equal(new Dictionary<string, string[]> { [campo] = [AccionCuerpoIlegible] }, await ErroresAsync(respuesta));
        var texto = await respuesta.Content.ReadAsStringAsync();
        Assert.DoesNotContain("JSON", texto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LineNumber", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("System.", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("Zq7-marcado", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("30111222", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("$.", texto, StringComparison.Ordinal);
        await AfirmarSinPresupuestoAsync(cliente, cookie, 1);
    }

    /// <summary>AC-02: con Apellido y Nombre y sin DNI, 400 en <c>dni</c> con el texto del PRD, sin grabar.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Post_SinDni_400ConIngresaElDni(string? dni)
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);

        var respuesta = await PostAsync(cliente, cookie, Cuerpo("Pérez", "Ana", dni, null, null, null, codigo));

        await AfirmarRechazoAsync(respuesta, new() { ["dni"] = ["Ingresá el DNI del cliente"] });
        await AfirmarSinPresupuestoAsync(cliente, cookie, 1);
    }

    /// <summary>AC-03: sin Apellido o sin Nombre, 400 junto al campo faltante, sin grabar.</summary>
    [Theory]
    [InlineData(null, "Ana", "apellido", "Ingresá el apellido del cliente")]
    [InlineData("Pérez", null, "nombre", "Ingresá el nombre del cliente")]
    [InlineData("  ", "Ana", "apellido", "Ingresá el apellido del cliente")]
    public async Task Post_SinApellidoOSinNombre_400EnElCampo(
        string? apellido, string? nombre, string campo, string accion)
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);

        var respuesta = await PostAsync(cliente, cookie, Cuerpo(apellido, nombre, "30111222", null, null, null, codigo));

        await AfirmarRechazoAsync(respuesta, new() { [campo] = [accion] });
        await AfirmarSinPresupuestoAsync(cliente, cookie, 1);
    }

    /// <summary>AC-06: un DNI que no tiene 7 u 8 dígitos, 400 en <c>dni</c> sin repetir el valor, sin grabar.</summary>
    [Theory]
    [InlineData("123456")]
    [InlineData("123456789")]
    [InlineData("23A56789")]
    public async Task Post_Dni123456_123456789_23A56789_400(string dni)
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);

        var respuesta = await PostAsync(cliente, cookie, Cuerpo("Pérez", "Ana", dni, null, null, null, codigo));

        await AfirmarRechazoAsync(respuesta, new() { ["dni"] = ["El DNI debe tener 7 u 8 dígitos"] });
        Assert.DoesNotContain(dni, await respuesta.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await AfirmarSinPresupuestoAsync(cliente, cookie, 1);
    }

    /// <summary>AC-07: "ana@" y "ana@mail", 400 en <c>email</c> con el formato esperado, sin grabar.</summary>
    [Theory]
    [InlineData("ana@")]
    [InlineData("ana@mail")]
    public async Task Post_EmailAnaArrobaYAnaArrobaMail_400(string email)
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);

        var respuesta = await PostAsync(cliente, cookie, Cuerpo("Pérez", "Ana", "30111222", null, email, null, codigo));

        await AfirmarRechazoAsync(
            respuesta, new() { ["email"] = ["El email debe tener el formato usuario@dominio.extensión"] });
        await AfirmarSinPresupuestoAsync(cliente, cookie, 1);
    }

    /// <summary>AC-11: sin líneas (lista vacía o ausente), 400 en <c>lineas</c>, sin grabar.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Post_SinLineas_400EnLineas(bool listaVacia)
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var cuerpo = listaVacia
            ? Cuerpo("Pérez", "Ana", "30111222", null, null, null)
            : "{\"cliente\":{\"apellido\":\"Perez\",\"nombre\":\"Ana\",\"dni\":\"30111222\"}}";

        var respuesta = await PostAsync(cliente, cookie, cuerpo);

        await AfirmarRechazoAsync(respuesta, new() { ["lineas"] = ["Agregá al menos una línea al presupuesto"] });
        await AfirmarSinPresupuestoAsync(cliente, cookie, 1);
    }

    /// <summary>
    /// AC-16: una línea con un artículo de precio de venta -150 (costo -100, margen 50), 400 en esa línea con el texto
    /// del PRD; no se graba nada del presupuesto, tampoco la línea válida (D-14).
    /// </summary>
    [Fact]
    public async Task Post_PrecioMenos150_400EnLaLinea()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var valido = await GrabarArticuloAsync(cliente, cookie);
        var negativo = await GrabarArticuloAsync(cliente, cookie, "Estuche", "NEG-1", -100m, 50m);

        var respuesta = await PostAsync(
            cliente, cookie, Cuerpo("Pérez", "Ana", "30111222", null, null, null, valido, negativo));

        await AfirmarRechazoAsync(respuesta, new() { ["lineas[1]"] = ["El precio unitario no puede ser negativo"] });
        await AfirmarSinPresupuestoAsync(cliente, cookie, 1);
    }

    /// <summary>D-17: los errores no repiten el DNI ni el email enviados, ni ningún otro dato del cliente.</summary>
    [Fact]
    public async Task Post_Errores400_NoRepitenElValor()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);

        var respuesta = await PostAsync(
            cliente,
            cookie,
            Cuerpo("Zq7Apellido", "Zq7Nombre", "99887766554", "Zq7 Calle Ficticia 123", "zq7marcado@", "011-5555-0199",
                codigo));

        await AfirmarRechazoAsync(
            respuesta,
            new()
            {
                ["dni"] = ["El DNI debe tener 7 u 8 dígitos"],
                ["email"] = ["El email debe tener el formato usuario@dominio.extensión"],
            });
        var texto = await respuesta.Content.ReadAsStringAsync();
        Assert.DoesNotContain("99887766554", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("zq7", texto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("5555", texto, StringComparison.Ordinal);
        await AfirmarSinPresupuestoAsync(cliente, cookie, 1);
    }

    /// <summary>
    /// Un número sin presupuesto: 404 con ProblemDetails de la acción, <c>title</c> fijo y sin detalles ni datos de
    /// otro presupuesto. Un número que no es un entero de 64 bits no llega a la acción.
    /// </summary>
    [Fact]
    public async Task Get_Inexistente_404()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);
        var alta = await PostAsync(
            cliente, cookie, Cuerpo("Zq7Apellido", "Ana", "30111222", null, null, null, codigo));
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);

        var respuesta = await Http.GetAsync(cliente, $"{Ruta}/2", cookie);

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        var json = await Http.JsonAsync(respuesta);
        Assert.Equal(404, json.GetProperty("status").GetInt32());
        Assert.Equal(TituloNoEncontrado, json.GetProperty("title").GetString());
        Assert.False(json.TryGetProperty("detail", out _));
        Assert.False(json.TryGetProperty("errors", out _));
        var texto = json.GetRawText();
        Assert.DoesNotContain("Zq7", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("30111222", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", texto, StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.NotFound, (await Http.GetAsync(cliente, $"{Ruta}/abc", cookie)).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound, (await Http.GetAsync(cliente, $"{Ruta}/99999999999999999999", cookie)).StatusCode);
    }

    // Altas grabadas.

    /// <summary>
    /// AC-01 y AC-17: con los seis datos y una línea, 201 con el presupuesto grabado y <c>Location</c> hacia su GET; el
    /// GET devuelve lo mismo, con los seis datos y las siete columnas de la línea.
    /// </summary>
    [Fact]
    public async Task Post_SeisDatos_201YGetLosMuestra()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);

        var respuesta = await PostAsync(
            cliente,
            cookie,
            Cuerpo("Pérez", "Ana", "30111222", "Av. Ficticia 742", "ana@mail.com", "011-4444-0000", codigo));

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        Assert.Equal("application/json", respuesta.Content.Headers.ContentType?.MediaType);
        var json = await Http.JsonAsync(respuesta);
        Assert.Equal(CamposRespuesta, Nombres(json));
        Assert.Equal(1, json.GetProperty("numero").GetInt64());
        AfirmarCliente(json, "Pérez", "Ana", "30111222", "Av. Ficticia 742", "ana@mail.com", "011-4444-0000");
        var linea = Assert.Single(json.GetProperty("lineas").EnumerateArray());
        AfirmarLinea(linea, codigo, "Armazón", 1815m);
        var camino = Camino(respuesta);
        Assert.Equal($"{Ruta}/1", camino);

        var lectura = await Http.GetAsync(cliente, camino, cookie);
        Assert.Equal(HttpStatusCode.OK, lectura.StatusCode);
        Assert.Equal(json.GetRawText(), (await Http.JsonAsync(lectura)).GetRawText());
    }

    /// <summary>AC-04: sin Domicilio, Email ni Teléfono (ausentes o en blanco), 201 con esos tres en null (D-11).</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task Post_SinOpcionales_201(string? opcional)
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);

        var respuesta = await PostAsync(
            cliente, cookie, Cuerpo("Pérez", "Ana", "30111222", opcional, opcional, opcional, codigo));

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        AfirmarCliente(await Http.JsonAsync(respuesta), "Pérez", "Ana", "30111222", null, null, null);
        AfirmarCliente(await LeerAsync(cliente, cookie, 1), "Pérez", "Ana", "30111222", null, null, null);
    }

    /// <summary>AC-05: el DNI "23.456.789" se graba y se lee como 23456789.</summary>
    [Fact]
    public async Task Post_Dni23Punto456Punto789_GetDevuelve23456789()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);

        var respuesta = await PostAsync(cliente, cookie, Cuerpo("Pérez", "Ana", "23.456.789", null, null, null, codigo));

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        Assert.Equal("23456789", (await Http.JsonAsync(respuesta)).GetProperty("cliente").GetProperty("dni").GetString());
        var leido = await LeerAsync(cliente, cookie, 1);
        Assert.Equal("23456789", leido.GetProperty("cliente").GetProperty("dni").GetString());
    }

    /// <summary>AC-08: el email "ana@mail.com" se graba tal cual.</summary>
    [Fact]
    public async Task Post_EmailAnaMailCom_201()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);

        var respuesta = await PostAsync(
            cliente, cookie, Cuerpo("Pérez", "Ana", "30111222", null, "ana@mail.com", null, codigo));

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var leido = await LeerAsync(cliente, cookie, 1);
        Assert.Equal("ana@mail.com", leido.GetProperty("cliente").GetProperty("email").GetString());
    }

    /// <summary>
    /// AC-09: con el presupuesto 154 como último grabado, el alta siguiente es el 155. El 154 se graba en Borrador por el
    /// repositorio, como lo dejaría la aplicación.
    /// </summary>
    [Fact]
    public async Task Post_Ultimo154_Numero155()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);
        await GrabarPresupuestoDirectoAsync(fabrica, 154, codigo);

        var respuesta = await PostAsync(cliente, cookie, Cuerpo("Pérez", "Ana", "30111222", null, null, null, codigo));

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        Assert.Equal(155, (await Http.JsonAsync(respuesta)).GetProperty("numero").GetInt64());
        Assert.Equal($"{Ruta}/155", Camino(respuesta));
        Assert.Equal(155, (await LeerAsync(cliente, cookie, 155)).GetProperty("numero").GetInt64());
    }

    /// <summary>AC-10: sin presupuestos grabados, el primero es el 1; el siguiente, el 2.</summary>
    [Fact]
    public async Task Post_BaseVacia_Numero1()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);
        var cuerpo = Cuerpo("Pérez", "Ana", "30111222", null, null, null, codigo);

        var primero = await PostAsync(cliente, cookie, cuerpo);
        var segundo = await PostAsync(cliente, cookie, cuerpo);

        Assert.Equal(HttpStatusCode.Created, primero.StatusCode);
        Assert.Equal(1, (await Http.JsonAsync(primero)).GetProperty("numero").GetInt64());
        Assert.Equal(2, (await Http.JsonAsync(segundo)).GetProperty("numero").GetInt64());
    }

    /// <summary>AC-12: el presupuesto grabado queda en Borrador, en la respuesta del alta y al volver a abrirlo.</summary>
    [Fact]
    public async Task Post_EstadoBorrador()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);

        var respuesta = await PostAsync(cliente, cookie, Cuerpo("Pérez", "Ana", "30111222", null, null, null, codigo));

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        Assert.Equal("borrador", (await Http.JsonAsync(respuesta)).GetProperty("estado").GetString());
        Assert.Equal("borrador", (await LeerAsync(cliente, cookie, 1)).GetProperty("estado").GetString());
    }

    /// <summary>
    /// AC-15 y AC-18: un artículo de precio de venta 1850 (costo 1850, margen 0) da una línea con su código, su
    /// descripción, Precio Unitario 1850, Cantidad 1, Descuento 0 y los dos precios en 1850. Un precio, una descripción,
    /// una cantidad o un descuento enviados en la línea se ignoran: el servidor copia los del catálogo (D-12, FR-11).
    /// </summary>
    [Fact]
    public async Task Post_Articulo1850_Linea1850Cantidad1Descuento0()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie, "Armazón acetato", "ACE-1", 1850m, 0m);
        var cuerpo =
            "{\"cliente\":{\"apellido\":\"Perez\",\"nombre\":\"Ana\",\"dni\":\"30111222\"},\"lineas\":[{\"codigoArticulo\":"
            + codigo + ",\"precioUnitario\":1,\"descripcion\":\"Otra\",\"cantidad\":5,\"porcentajeDescuento\":50}]}";

        var respuesta = await PostAsync(cliente, cookie, cuerpo);

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        AfirmarLinea(
            Assert.Single((await Http.JsonAsync(respuesta)).GetProperty("lineas").EnumerateArray()),
            codigo,
            "Armazón acetato",
            1850m);
        AfirmarLinea(
            Assert.Single((await LeerAsync(cliente, cookie, 1)).GetProperty("lineas").EnumerateArray()),
            codigo,
            "Armazón acetato",
            1850m);
    }

    /// <summary>
    /// AC-19 (D-06, D-22): con la PC en hora de Argentina el 10/03/2026, el presupuesto guarda 10/03/2026 y el GET la
    /// devuelve, también a las 23:30 hora local (02:30 UTC del 11/03); a la medianoche local ya es 11/03. El reloj de
    /// <see cref="FabricaApi"/> arranca el 31/10/2026 y un <see cref="FakeTimeProvider"/> no retrocede: la prueba lo
    /// reemplaza por uno propio que arranca el 10/03/2026 a las 23:20 hora local.
    /// </summary>
    [Fact]
    public async Task Post_Reloj10_03_2026_GetDevuelveEsaFecha()
    {
        // Los tres pedidos entran en los 60 minutos de la sesión emitida al empezar (la prueba no renueva la cookie).
        var reloj = new FakeTimeProvider(new DateTimeOffset(2026, 3, 11, 2, 20, 0, TimeSpan.Zero));
        reloj.SetLocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById("America/Argentina/Buenos_Aires"));
        using var fabrica = new FabricaApi(servicios =>
        {
            servicios.RemoveAll<TimeProvider>();
            servicios.AddSingleton<TimeProvider>(reloj);
        });
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);
        var cuerpo = Cuerpo("Pérez", "Ana", "30111222", null, null, null, codigo);

        // 23:20 hora local del 10/03 (02:20 UTC del 11/03).
        var primero = await PostAsync(cliente, cookie, cuerpo);
        reloj.Advance(TimeSpan.FromMinutes(10));
        // 23:30 hora local del 10/03 (02:30 UTC del 11/03).
        var segundo = await PostAsync(cliente, cookie, cuerpo);
        reloj.Advance(TimeSpan.FromMinutes(30));
        // 00:00 hora local del 11/03: control de que la fecha sigue al reloj local.
        var tercero = await PostAsync(cliente, cookie, cuerpo);

        Assert.Equal(HttpStatusCode.Created, primero.StatusCode);
        Assert.Equal("2026-03-10", (await Http.JsonAsync(primero)).GetProperty("fecha").GetString());
        Assert.Equal("2026-03-10", (await Http.JsonAsync(segundo)).GetProperty("fecha").GetString());
        Assert.Equal("2026-03-11", (await Http.JsonAsync(tercero)).GetProperty("fecha").GetString());
        Assert.Equal("2026-03-10", (await LeerAsync(cliente, cookie, 1)).GetProperty("fecha").GetString());
        Assert.Equal("2026-03-10", (await LeerAsync(cliente, cookie, 2)).GetProperty("fecha").GetString());
        Assert.Equal("2026-03-11", (await LeerAsync(cliente, cookie, 3)).GetProperty("fecha").GetString());
    }

    /// <summary>
    /// D-17 (Ley 25.326): con el registro en Trace (donde MVC registra los argumentos de la acción) y en todas las
    /// categorías, ningún mensaje de un alta grabada, de un alta rechazada ni de la consulta contiene el apellido, el
    /// nombre, el DNI, el domicilio, el email ni el teléfono del cliente. Datos ficticios.
    /// </summary>
    [Fact]
    public async Task Registro_AltaPresupuesto_SinDatosPersonales()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);

        var alta = await PostAsync(
            cliente,
            cookie,
            Cuerpo("Zq7Apellido", "Zq7Nombre", "30.111.222", "Zq7 Calle Ficticia 123", "zq7@correo-ficticio.com",
                "011-5555-0199", codigo));
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);
        var rechazo = await PostAsync(
            cliente,
            cookie,
            Cuerpo("Zq7Apellido", "Zq7Nombre", "30.111.222", "Zq7 Calle Ficticia 123", "zq7otro@", "011-5555-0199",
                codigo));
        Assert.Equal(HttpStatusCode.BadRequest, rechazo.StatusCode);
        // Un cuerpo ilegible: el DNI con un marcador ficticio y el JSON roto después. El lector de JSON y MVC registran
        // el error de lectura; el marcador no puede llegar con él.
        var ilegible = await PostAsync(
            cliente,
            cookie,
            "{\"cliente\":{\"apellido\":\"Zq7Apellido\",\"nombre\":\"Zq7Nombre\",\"dni\":\"ZZMARCA\"},\"lineas\":[{\"cod");
        Assert.Equal(HttpStatusCode.BadRequest, ilegible.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Http.GetAsync(cliente, $"{Ruta}/1", cookie)).StatusCode);

        var lineas = fabrica.Registro.Lineas;
        // Control: la captura ve Trace, y MVC registró la ejecución de la acción del alta.
        Assert.Contains(lineas, l => l.Nivel == LogLevel.Trace);
        Assert.Contains(lineas, l => l.Mensaje.Contains("PresupuestosController", StringComparison.Ordinal));
        // Control: el error de lectura del cuerpo ilegible sí se registró (en Debug), con su texto.
        Assert.Contains(lineas, l => l.Nivel <= LogLevel.Debug
            && l.Categoria.StartsWith("Microsoft.AspNetCore.Mvc", StringComparison.Ordinal)
            && l.Mensaje.Contains("JSON", StringComparison.OrdinalIgnoreCase));
        foreach (var secreto in new[] { "Zq7", "zq7", "30111222", "30.111.222", "5555-0199", "correo-ficticio", "ZZMARCA" })
        {
            var conElDato = lineas.Where(l => l.TextoCompleto.Contains(secreto, StringComparison.Ordinal))
                .Select(l => $"{l.Categoria} ({l.Nivel})")
                .ToList();
            Assert.True(conElDato.Count == 0, $"\"{secreto}\" llegó al log en: {string.Join(", ", conElDato)}");
        }
    }

    // Ayudas.

    private static string Cuerpo(
        string? apellido, string? nombre, string? dni, string? domicilio, string? email, string? telefono,
        params long[] codigos) =>
        JsonSerializer.Serialize(new
        {
            cliente = new { apellido, nombre, dni, domicilio, email, telefono },
            lineas = codigos.Select(codigo => new { codigoArticulo = codigo }).ToArray(),
        });

    private static Task<HttpResponseMessage> PostAsync(HttpClient cliente, string cookie, string cuerpo) =>
        Http.PostJsonAsync(cliente, Ruta, cuerpo, cookie);

    /// <summary>Graba un artículo activo; por defecto costo 1210 y margen 50 (precio de venta 1815).</summary>
    private static async Task<long> GrabarArticuloAsync(
        HttpClient cliente,
        string cookie,
        string descripcion = "Armazón",
        string codigoProveedor = "ABC-1",
        decimal precioCosto = 1210m,
        decimal margenUtilidad = 50m)
    {
        var respuesta = await Http.PostJsonAsync(
            cliente,
            RutaArticulos,
            JsonSerializer.Serialize(new { codigoProveedor, descripcion, precioCosto, margenUtilidad }),
            cookie);
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await Http.JsonAsync(respuesta)).GetProperty("codigo").GetInt64();
    }

    /// <summary>Graba en Borrador, por el repositorio, el presupuesto <paramref name="numero"/> con una línea.</summary>
    private static async Task GrabarPresupuestoDirectoAsync(FabricaApi fabrica, long numero, long codigoArticulo)
    {
        using var alcance = fabrica.Services.CreateScope();
        var servicios = alcance.ServiceProvider;
        var articulo = await servicios.GetRequiredService<IArticuloRepository>().ObtenerAsync(codigoArticulo)
            ?? throw new InvalidOperationException("No se encontró el artículo de la prueba.");
        var linea = LineaPresupuesto.DesdeArticulo(articulo, cantidad: 1, porcentajeDescuento: 0m).Linea
            ?? throw new InvalidOperationException("El artículo de la prueba no da una línea válida.");
        servicios.GetRequiredService<IPresupuestoRepository>().Agregar(Presupuesto.Nuevo(
            numero,
            new DateOnly(2026, 10, 30),
            new ClientePresupuesto("Gómez", "Luis", "20333444", null, null, null),
            [linea]));
        await servicios.GetRequiredService<IUnidadDeTrabajo>().ConfirmarAsync();
    }

    private static async Task<JsonElement> LeerAsync(HttpClient cliente, string cookie, long numero)
    {
        var respuesta = await Http.GetAsync(cliente, $"{Ruta}/{numero}", cookie);
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        return await Http.JsonAsync(respuesta);
    }

    /// <summary>
    /// No hay presupuesto con ese número: el pedido rechazado no grabó nada. Exige el 404 de la acción (con
    /// ProblemDetails), no el de una ruta inexistente.
    /// </summary>
    private static async Task AfirmarSinPresupuestoAsync(HttpClient cliente, string cookie, long numero)
    {
        var respuesta = await Http.GetAsync(cliente, $"{Ruta}/{numero}", cookie);
        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
    }

    private static async Task AfirmarRechazoAsync(HttpResponseMessage respuesta, Dictionary<string, string[]> errores)
    {
        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        Assert.Equal(errores, await ErroresAsync(respuesta));
    }

    private static void AfirmarCliente(
        JsonElement json, string apellido, string nombre, string dni, string? domicilio, string? email, string? telefono)
    {
        var datos = json.GetProperty("cliente");
        Assert.Equal(CamposCliente, Nombres(datos));
        Assert.Equal(apellido, datos.GetProperty("apellido").GetString());
        Assert.Equal(nombre, datos.GetProperty("nombre").GetString());
        Assert.Equal(dni, datos.GetProperty("dni").GetString());
        Assert.Equal(domicilio, Texto(datos.GetProperty("domicilio")));
        Assert.Equal(email, Texto(datos.GetProperty("email")));
        Assert.Equal(telefono, Texto(datos.GetProperty("telefono")));
    }

    /// <summary>Una línea con Cantidad 1, Descuento 0 y los tres precios iguales al de venta del artículo (D-03).</summary>
    private static void AfirmarLinea(JsonElement linea, long codigo, string descripcion, decimal precio)
    {
        Assert.Equal(CamposLinea, Nombres(linea));
        Assert.Equal(codigo, linea.GetProperty("codigoArticulo").GetInt64());
        Assert.Equal(descripcion, linea.GetProperty("descripcion").GetString());
        Assert.Equal(precio, linea.GetProperty("precioUnitario").GetDecimal());
        Assert.Equal(1, linea.GetProperty("cantidad").GetInt32());
        Assert.Equal(0m, linea.GetProperty("porcentajeDescuento").GetDecimal());
        Assert.Equal(precio, linea.GetProperty("precioConDescuento").GetDecimal());
        Assert.Equal(precio, linea.GetProperty("precioFinal").GetDecimal());
    }

    private static string? Texto(JsonElement valor) =>
        valor.ValueKind == JsonValueKind.Null ? null : valor.GetString();

    private static string[] Nombres(JsonElement json) =>
        json.EnumerateObject().Select(propiedad => propiedad.Name).ToArray();

    private static string Camino(HttpResponseMessage respuesta)
    {
        var ubicacion = respuesta.Headers.Location;
        Assert.NotNull(ubicacion);
        return ubicacion.IsAbsoluteUri ? ubicacion.AbsolutePath : ubicacion.OriginalString;
    }

    private static async Task<Dictionary<string, string[]>> ErroresAsync(HttpResponseMessage respuesta)
    {
        var json = await Http.JsonAsync(respuesta);
        return json.GetProperty("errors").EnumerateObject().ToDictionary(
            campo => campo.Name,
            campo => campo.Value.EnumerateArray().Select(valor => valor.GetString()!).ToArray());
    }
}
