using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Optica.Application.Comun;
using Optica.Domain.Catalogo;
using Optica.Domain.Presupuestos;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Graphics.Colors;

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
    private const string AccionCuerpoIlegibleBusqueda = "No se pudieron leer los filtros enviados. Revisalos y buscá de nuevo.";
    private const string TituloNoEncontrado = "No se encontró el presupuesto.";

    private const string TextoCantidadInvalida = "La cantidad debe ser un número entero mayor a 0";
    private const string TextoDescuentoFueraDeRango = "El descuento debe estar entre 0 y 100";

    private const string TituloConflicto = "No se pudo grabar el presupuesto.";
    private const string TextoCambiado = "El presupuesto cambió mientras lo editabas. Volvé a abrirlo.";
    private const string TextoFinal = "El presupuesto está en Final y no se puede modificar.";
    private const string TextoVersion = "Volvé a abrir el presupuesto para editarlo.";
    private const string TextoLineaAjena = "La línea no corresponde a este presupuesto.";
    private const string TextoEstado = "El presupuesto solo puede pasar a Final.";
    private const string TextoArticuloNoDisponible = "Ese artículo ya no está disponible. Quitá la línea y buscalo de nuevo";

    private static readonly string[] CamposRespuesta =
        ["numero", "fecha", "estado", "cliente", "lineas", "total", "version"];

    private static readonly string[] CamposCliente = ["apellido", "nombre", "dni", "domicilio", "email", "telefono"];

    private static readonly string[] CamposLinea =
    [
        "codigoArticulo", "descripcion", "precioUnitario", "cantidad", "porcentajeDescuento", "precioConDescuento",
        "precioFinal", "orden",
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

        // R-04 (FEAT-003b): tampoco repiten la cantidad ni el descuento de la línea.
        var lineas = await PostAsync(cliente, cookie, CuerpoConLineas(
            "{\"codigoArticulo\":" + codigo + ",\"cantidad\":98765,\"porcentajeDescuento\":12.345}",
            "{\"codigoArticulo\":" + codigo + ",\"cantidad\":-4321,\"porcentajeDescuento\":543.21}"));

        await AfirmarRechazoAsync(
            lineas,
            new()
            {
                ["lineas[0].cantidad"] = ["Usá hasta 9.999 unidades."],
                ["lineas[0].porcentajeDescuento"] = ["Ingresá hasta 2 decimales."],
                ["lineas[1].cantidad"] = [TextoCantidadInvalida],
                ["lineas[1].porcentajeDescuento"] = [TextoDescuentoFueraDeRango],
            });
        var textoLineas = await lineas.Content.ReadAsStringAsync();
        foreach (var valor in new[] { "98765", "12.345", "4321", "543.21" })
        {
            Assert.DoesNotContain(valor, textoLineas, StringComparison.Ordinal);
        }

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
    /// AC-15 de FEAT-003a, invertida por FEAT-003b: la Cantidad y el Descuento enviados en la línea se usan (FR-01); el
    /// precio unitario y la descripción enviados se siguen ignorando: el servidor copia los del catálogo (D-12, FR-11).
    /// Precio de venta 1850, Cantidad 5 y 50 % dan 925,00 y 4.625,00.
    /// </summary>
    [Fact]
    public async Task Post_Articulo1850_UsaCantidadYDescuentoDelPedido()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie, "Armazón acetato", "ACE-1", 1850m, 0m);
        var cuerpo = CuerpoConLineas(
            "{\"codigoArticulo\":" + codigo
            + ",\"precioUnitario\":1,\"descripcion\":\"Otra\",\"cantidad\":5,\"porcentajeDescuento\":50}");

        var respuesta = await PostAsync(cliente, cookie, cuerpo);

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        AfirmarLinea(
            Assert.Single((await Http.JsonAsync(respuesta)).GetProperty("lineas").EnumerateArray()),
            codigo, "Armazón acetato", 1850m, 5, 50m, 925m, 4625m);
        AfirmarLinea(
            Assert.Single((await LeerAsync(cliente, cookie, 1)).GetProperty("lineas").EnumerateArray()),
            codigo, "Armazón acetato", 1850m, 5, 50m, 925m, 4625m);
    }

    /// <summary>
    /// AC-01 y AC-07 (D-05, D-14): 1.000,00 × 3 con 10 % da 900,00 y 2.700,00; con una línea de 1.850,00 el total es
    /// 4.550,00. El alta y la consulta devuelven <c>total</c> después de <c>lineas</c>, con el mismo cuerpo.
    /// </summary>
    [Fact]
    public async Task Get_DevuelveTotal()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var lente = await GrabarArticuloAsync(cliente, cookie, "Lente bifocal", "LEN-1", 1000m, 0m);
        var armazon = await GrabarArticuloAsync(cliente, cookie, "Armazón acetato", "ACE-1", 1850m, 0m);

        var respuesta = await PostAsync(cliente, cookie, CuerpoConLineas(
            "{\"codigoArticulo\":" + lente + ",\"cantidad\":3,\"porcentajeDescuento\":10}",
            "{\"codigoArticulo\":" + armazon + ",\"cantidad\":1,\"porcentajeDescuento\":0}"));

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var alta = await Http.JsonAsync(respuesta);
        Assert.Equal(CamposRespuesta, Nombres(alta));
        Assert.Equal(4550m, alta.GetProperty("total").GetDecimal());
        AfirmarLinea(alta.GetProperty("lineas")[0], lente, "Lente bifocal", 1000m, 3, 10m, 900m, 2700m);
        AfirmarLinea(alta.GetProperty("lineas")[1], armazon, "Armazón acetato", 1850m);

        var leido = await LeerAsync(cliente, cookie, 1);
        Assert.Equal(CamposRespuesta, Nombres(leido));
        Assert.Equal(4550m, leido.GetProperty("total").GetDecimal());
        Assert.Equal(alta.GetRawText(), leido.GetRawText());
    }

    /// <summary>
    /// R-01: el pedido no fija precios. Un <c>precioConDescuento</c>, un <c>precioFinal</c> o un <c>total</c> enviados se
    /// ignoran y se graba lo que calcula el servidor.
    /// </summary>
    [Fact]
    public async Task Post_PreciosEnElPedido_SeIgnoran()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie, "Lente bifocal", "LEN-1", 1000m, 0m);
        var cuerpo =
            "{\"cliente\":{\"apellido\":\"Ficticiez\",\"nombre\":\"Prueboncia\",\"dni\":\"00718294\"},\"total\":1,"
            + "\"lineas\":[{\"codigoArticulo\":" + codigo + ",\"cantidad\":3,\"porcentajeDescuento\":10,"
            + "\"precioConDescuento\":1,\"precioFinal\":1}]}";

        var respuesta = await PostAsync(cliente, cookie, cuerpo);

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var leido = await LeerAsync(cliente, cookie, 1);
        AfirmarLinea(leido.GetProperty("lineas")[0], codigo, "Lente bifocal", 1000m, 3, 10m, 900m, 2700m);
        Assert.Equal(2700m, leido.GetProperty("total").GetDecimal());
    }

    /// <summary>AC-02: Cantidad 0, -1 o ausente (o null), 400 en <c>lineas[i].cantidad</c> con el texto del PRD.</summary>
    [Theory]
    [InlineData(",\"cantidad\":0")]
    [InlineData(",\"cantidad\":-1")]
    [InlineData(",\"cantidad\":null")]
    [InlineData("")]
    public async Task Post_CantidadInvalida_400(string cantidad)
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);

        var respuesta = await PostAsync(cliente, cookie, CuerpoConLineas(
            "{\"codigoArticulo\":" + codigo + ",\"cantidad\":1,\"porcentajeDescuento\":0}",
            "{\"codigoArticulo\":" + codigo + cantidad + ",\"porcentajeDescuento\":0}"));

        await AfirmarRechazoAsync(respuesta, new() { ["lineas[1].cantidad"] = [TextoCantidadInvalida] });
        await AfirmarSinPresupuestoAsync(cliente, cookie, 1);
    }

    /// <summary>
    /// D-07 y D-09: una cantidad que no es un entero JSON escrito con dígitos (2.5, 1e3 —el lector no la toma como
    /// 1000—, un texto) es un cuerpo ilegible en <c>lineas[i].cantidad</c>, con el texto de AC-02 y sin el valor ni el
    /// mensaje del lector.
    /// </summary>
    [Theory]
    [InlineData("2.5")]
    [InlineData("1e3")]
    [InlineData("\"3\"")]
    [InlineData("\"Zq7-marcado\"")]
    public async Task Post_CantidadDecimal_400(string cantidad)
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);

        var respuesta = await PostAsync(cliente, cookie, CuerpoConLineas(
            "{\"codigoArticulo\":" + codigo + ",\"cantidad\":1,\"porcentajeDescuento\":0}",
            "{\"codigoArticulo\":" + codigo + ",\"cantidad\":" + cantidad + ",\"porcentajeDescuento\":0}"));

        await AfirmarRechazoAsync(respuesta, new() { ["lineas[1].cantidad"] = [TextoCantidadInvalida] });
        AfirmarSinDetallesDelLector(await respuesta.Content.ReadAsStringAsync(), "2.5", "Zq7");
        await AfirmarSinPresupuestoAsync(cliente, cookie, 1);
    }

    /// <summary>
    /// D-07 y D-09: un descuento que no es un número JSON (<c>"10"</c>, un texto, un objeto) es un cuerpo ilegible en
    /// <c>lineas[i].porcentajeDescuento</c>, con el texto de AC-03 y sin el valor ni el mensaje del lector.
    /// </summary>
    [Theory]
    [InlineData("\"10\"")]
    [InlineData("\"Zq7-marcado\"")]
    [InlineData("{\"valor\":10}")]
    public async Task Post_DescuentoTexto_400(string descuento)
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);

        var respuesta = await PostAsync(cliente, cookie, CuerpoConLineas(
            "{\"codigoArticulo\":" + codigo + ",\"cantidad\":1,\"porcentajeDescuento\":" + descuento + "}"));

        await AfirmarRechazoAsync(respuesta, new() { ["lineas[0].porcentajeDescuento"] = [TextoDescuentoFueraDeRango] });
        AfirmarSinDetallesDelLector(await respuesta.Content.ReadAsStringAsync(), "\"10\"", "Zq7");
        await AfirmarSinPresupuestoAsync(cliente, cookie, 1);
    }

    /// <summary>
    /// D-01: un entero JSON positivo que no entra en un <c>int</c> también es "más de 9.999" y lleva ese texto, no el de
    /// un cuerpo ilegible; un entero negativo enorme sigue con el de AC-02. Ninguno repite el valor.
    /// </summary>
    [Theory]
    [InlineData("2147483648", "Usá hasta 9.999 unidades.")]
    [InlineData("10000000000", "Usá hasta 9.999 unidades.")]
    [InlineData("123456789012345678901234567890123456789", "Usá hasta 9.999 unidades.")]
    [InlineData("-2147483649", TextoCantidadInvalida)]
    [InlineData("-10000000000", TextoCantidadInvalida)]
    public async Task Post_CantidadFueraDeInt_400(string cantidad, string accion)
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);

        var respuesta = await PostAsync(cliente, cookie, CuerpoConLineas(
            "{\"codigoArticulo\":" + codigo + ",\"cantidad\":" + cantidad + ",\"porcentajeDescuento\":0}"));

        await AfirmarRechazoAsync(respuesta, new() { ["lineas[0].cantidad"] = [accion] });
        AfirmarSinDetallesDelLector(await respuesta.Content.ReadAsStringAsync(), cantidad.TrimStart('-'));
        await AfirmarSinPresupuestoAsync(cliente, cookie, 1);
    }

    /// <summary>AC-09 (D-01): Cantidad 10.000, 400 en <c>lineas[i].cantidad</c> con "Usá hasta 9.999 unidades.".</summary>
    [Fact]
    public async Task Post_CantidadMayorAlMaximo_400()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);

        var respuesta = await PostAsync(cliente, cookie, CuerpoConLineas(
            "{\"codigoArticulo\":" + codigo + ",\"cantidad\":10000,\"porcentajeDescuento\":0}"));

        await AfirmarRechazoAsync(respuesta, new() { ["lineas[0].cantidad"] = ["Usá hasta 9.999 unidades."] });
        await AfirmarSinPresupuestoAsync(cliente, cookie, 1);
    }

    /// <summary>AC-03: descuento -1, 100.01, ausente o null, 400 en <c>lineas[i].porcentajeDescuento</c>.</summary>
    [Theory]
    [InlineData(",\"porcentajeDescuento\":-1")]
    [InlineData(",\"porcentajeDescuento\":100.01")]
    [InlineData(",\"porcentajeDescuento\":null")]
    [InlineData("")]
    public async Task Post_DescuentoFueraDeRango_400(string descuento)
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);

        var respuesta = await PostAsync(cliente, cookie, CuerpoConLineas(
            "{\"codigoArticulo\":" + codigo + ",\"cantidad\":1" + descuento + "}"));

        await AfirmarRechazoAsync(respuesta, new() { ["lineas[0].porcentajeDescuento"] = [TextoDescuentoFueraDeRango] });
        await AfirmarSinPresupuestoAsync(cliente, cookie, 1);
    }

    /// <summary>D-02: descuento 10.555, 400 en <c>lineas[i].porcentajeDescuento</c> con "Ingresá hasta 2 decimales.".</summary>
    [Fact]
    public async Task Post_DescuentoConTresDecimales_400()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);

        var respuesta = await PostAsync(cliente, cookie, CuerpoConLineas(
            "{\"codigoArticulo\":" + codigo + ",\"cantidad\":1,\"porcentajeDescuento\":10.555}"));

        await AfirmarRechazoAsync(respuesta, new() { ["lineas[0].porcentajeDescuento"] = ["Ingresá hasta 2 decimales."] });
        await AfirmarSinPresupuestoAsync(cliente, cookie, 1);
    }

    /// <summary>
    /// D-04 por la Api: un Precio Final por encima del tope del contrato responde 400 en <c>lineas[i].cantidad</c>, no
    /// 500, y no graba nada. El artículo de 10.000.000.000 entra en el catálogo; por 9.999 ya no entra en la web.
    /// </summary>
    [Fact]
    public async Task Post_ImporteExcedido_400SinGrabar()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie, "Artículo caro", "CAR-1", 10_000_000_000m, 0m);

        var linea = await PostAsync(cliente, cookie, CuerpoConLineas(
            "{\"codigoArticulo\":" + codigo + ",\"cantidad\":9999,\"porcentajeDescuento\":0}"));
        var total = await PostAsync(cliente, cookie, CuerpoConLineas(
            Enumerable.Repeat("{\"codigoArticulo\":" + codigo + ",\"cantidad\":1000,\"porcentajeDescuento\":0}", 10)
                .ToArray()));

        await AfirmarRechazoAsync(linea, new() { ["lineas[0].cantidad"] = ["El precio final de la línea es demasiado alto."] });
        await AfirmarRechazoAsync(total, new() { ["lineas"] = ["El total del presupuesto es demasiado alto."] });
        await AfirmarSinPresupuestoAsync(cliente, cookie, 1);
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

    // FEAT-003c: modificación en Borrador y paso a Final.

    /// <summary>
    /// AC-01 (D-06): con el reloj en el 10/03/2026 se da de alta; dos días después (12/03/2026) se modifica y la fecha
    /// sigue siendo el 10/03/2026, en la respuesta y en el GET. La sesión vence a los 60 minutos: se vuelve a ingresar.
    /// </summary>
    [Fact]
    public async Task Modificar_ConRelojPosterior_ConservaFecha()
    {
        var reloj = new FakeTimeProvider(new DateTimeOffset(2026, 3, 10, 15, 0, 0, TimeSpan.Zero));
        reloj.SetLocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById("America/Argentina/Buenos_Aires"));
        using var fabrica = new FabricaApi(servicios =>
        {
            servicios.RemoveAll<TimeProvider>();
            servicios.AddSingleton<TimeProvider>(reloj);
        });
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);
        var numero = await AltaAsync(cliente, cookie, codigo);
        Assert.Equal("2026-03-10", (await LeerAsync(cliente, cookie, numero)).GetProperty("fecha").GetString());

        reloj.Advance(TimeSpan.FromDays(2));
        var ingreso = await Http.PostJsonAsync(cliente, "/api/acceso/ingresar", Http.CuerpoContrasena(Http.Contrasena));
        var cookieNueva = Http.Cookie(ingreso);
        Assert.NotNull(cookieNueva);
        var respuesta = await PutAsync(
            cliente, cookieNueva, numero, CuerpoModificacion(0, ClienteJson("Calle Ficticia 123"), LineaJson(codigo, 1)));

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("2026-03-10", (await Http.JsonAsync(respuesta)).GetProperty("fecha").GetString());
        Assert.Equal("2026-03-10", (await LeerAsync(cliente, cookieNueva, numero)).GetProperty("fecha").GetString());
    }

    /// <summary>
    /// AC-02: se agrega el Domicilio a un Borrador; 200 con el presupuesto releído (estado borrador, versión 1, mismo
    /// número) y el GET devuelve lo mismo.
    /// </summary>
    [Fact]
    public async Task Put_ModificarDomicilio_200YGetLoMuestra()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);
        var numero = await AltaAsync(cliente, cookie, codigo);

        var respuesta = await PutAsync(
            cliente, cookie, numero, CuerpoModificacion(0, ClienteJson("Calle Ficticia 123"), LineaJson(codigo, 1)));

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("application/json", respuesta.Content.Headers.ContentType?.MediaType);
        var json = await Http.JsonAsync(respuesta);
        Assert.Equal(CamposRespuesta, Nombres(json));
        Assert.Equal(numero, json.GetProperty("numero").GetInt64());
        Assert.Equal("borrador", json.GetProperty("estado").GetString());
        Assert.Equal(1, json.GetProperty("version").GetInt64());
        AfirmarCliente(json, "Ficticiez", "Prueboncia", "00718294", "Calle Ficticia 123", "prueba@example.com", null);
        AfirmarLinea(Assert.Single(json.GetProperty("lineas").EnumerateArray()), 1, codigo, "Armazón", 1815m);
        Assert.Equal(json.GetRawText(), (await LeerAsync(cliente, cookie, numero)).GetRawText());
    }

    /// <summary>AC-03: se quita una línea y se agrega otra; las líneas quedan renumeradas 1..n y el total recalculado.</summary>
    [Fact]
    public async Task Put_AgregarYQuitarLinea_200()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var armazon = await GrabarArticuloAsync(cliente, cookie);
        var lente = await GrabarArticuloAsync(cliente, cookie, "Lente bifocal", "LEN-1", 1000m, 0m);
        var estuche = await GrabarArticuloAsync(cliente, cookie, "Estuche", "EST-1", 500m, 0m);
        var numero = await AltaAsync(cliente, cookie, armazon, lente);

        var respuesta = await PutAsync(
            cliente, cookie, numero, CuerpoModificacion(0, ClienteJson(), LineaJson(lente, 2), LineaJson(estuche, null, 2)));

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var json = await Http.JsonAsync(respuesta);
        var lineas = json.GetProperty("lineas");
        Assert.Equal(2, lineas.GetArrayLength());
        AfirmarLinea(lineas[0], 1, lente, "Lente bifocal", 1000m);
        AfirmarLinea(lineas[1], 2, estuche, "Estuche", 500m, 2, 0m, 500m, 1000m);
        Assert.Equal(2000m, json.GetProperty("total").GetDecimal());
        Assert.Equal(json.GetRawText(), (await LeerAsync(cliente, cookie, numero)).GetRawText());
    }

    /// <summary>AC-04: un Borrador pasa a Final con la versión leída; 200 con estado final y versión 1, y el GET lo muestra.</summary>
    [Fact]
    public async Task PutEstado_Final_200()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);
        var numero = await AltaAsync(cliente, cookie, codigo);
        var antes = await LeerAsync(cliente, cookie, numero);

        var respuesta = await PutEstadoAsync(cliente, cookie, numero, CuerpoEstado("final", 0));

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var json = await Http.JsonAsync(respuesta);
        Assert.Equal(CamposRespuesta, Nombres(json));
        Assert.Equal("final", json.GetProperty("estado").GetString());
        Assert.Equal(1, json.GetProperty("version").GetInt64());
        Assert.Equal(antes.GetProperty("fecha").GetString(), json.GetProperty("fecha").GetString());
        Assert.Equal(antes.GetProperty("lineas").GetRawText(), json.GetProperty("lineas").GetRawText());
        Assert.Equal(json.GetRawText(), (await LeerAsync(cliente, cookie, numero)).GetRawText());
    }

    /// <summary>AC-07: modificar un Final responde 409 <c>final</c> y no cambia nada, con la versión actual o una vieja.</summary>
    [Fact]
    public async Task Put_PresupuestoFinal_409Final()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);
        var numero = await AltaAsync(cliente, cookie, codigo);
        Assert.Equal(HttpStatusCode.OK, (await PutEstadoAsync(cliente, cookie, numero, CuerpoEstado("final", 0))).StatusCode);
        var final = await LeerAsync(cliente, cookie, numero);

        foreach (var version in new long[] { 1, 0 })
        {
            var respuesta = await PutAsync(
                cliente, cookie, numero, CuerpoModificacion(version, ClienteJson("Calle Ficticia 123"), LineaJson(codigo, 1)));

            await AfirmarConflictoAsync(respuesta, "final", TextoFinal);
        }

        Assert.Equal(final.GetRawText(), (await LeerAsync(cliente, cookie, numero)).GetRawText());
    }

    /// <summary>AC-08: un Final no vuelve a Borrador: 409 <c>final</c>; tampoco pasa de nuevo a Final.</summary>
    [Fact]
    public async Task PutEstado_BorradorSobreFinal_409Final()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);
        var numero = await AltaAsync(cliente, cookie, codigo);
        Assert.Equal(HttpStatusCode.OK, (await PutEstadoAsync(cliente, cookie, numero, CuerpoEstado("final", 0))).StatusCode);
        var final = await LeerAsync(cliente, cookie, numero);

        await AfirmarConflictoAsync(
            await PutEstadoAsync(cliente, cookie, numero, CuerpoEstado("borrador", 1)), "final", TextoFinal);
        await AfirmarConflictoAsync(
            await PutEstadoAsync(cliente, cookie, numero, CuerpoEstado("final", 1)), "final", TextoFinal);

        var leido = await LeerAsync(cliente, cookie, numero);
        Assert.Equal("final", leido.GetProperty("estado").GetString());
        Assert.Equal(final.GetRawText(), leido.GetRawText());
    }

    /// <summary>
    /// AC-09 (D-02): cambia el precio del artículo en el catálogo; la línea existente conserva el Precio Unitario grabado
    /// y recalcula con la Cantidad y el Descuento nuevos.
    /// </summary>
    [Fact]
    public async Task Put_LineaExistente_ConservaPrecioUnitarioGrabado()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);
        var numero = await AltaAsync(cliente, cookie, codigo);
        var cambio = await Http.EnviarAsync(
            cliente,
            HttpMethod.Put,
            $"{RutaArticulos}/{codigo}",
            Json(JsonSerializer.Serialize(new
            {
                codigoProveedor = "ABC-1",
                descripcion = "Armazón nuevo",
                precioCosto = 2000m,
                margenUtilidad = 50m,
                version = 0,
            })),
            cookie);
        Assert.Equal(HttpStatusCode.OK, cambio.StatusCode);
        Assert.NotEqual(1815m, (await Http.JsonAsync(cambio)).GetProperty("precioVenta").GetDecimal());

        var respuesta = await PutAsync(
            cliente, cookie, numero, CuerpoModificacion(0, ClienteJson(), LineaJson(codigo, 1, 2, 10m)));

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var linea = (await Http.JsonAsync(respuesta)).GetProperty("lineas")[0];
        AfirmarLinea(linea, 1, codigo, "Armazón", 1815m, 2, 10m, 1633.50m, 3267m);
    }

    /// <summary>AC-10 (D-02): con el artículo inactivo, la línea existente se conserva y se puede cambiar su Cantidad.</summary>
    [Fact]
    public async Task Put_LineaConArticuloInactivo_ConservaCopia()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);
        var numero = await AltaAsync(cliente, cookie, codigo);
        await DesactivarArticuloAsync(cliente, cookie, codigo);

        var respuesta = await PutAsync(
            cliente, cookie, numero, CuerpoModificacion(0, ClienteJson(), LineaJson(codigo, 1, 3)));

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var linea = (await Http.JsonAsync(respuesta)).GetProperty("lineas")[0];
        AfirmarLinea(linea, 1, codigo, "Armazón", 1815m, 3, 0m, 1815m, 5445m);
    }

    /// <summary>D-01: con una versión que ya cambió (otra sesión grabó), 409 <c>cambiado</c> sin escribir.</summary>
    [Fact]
    public async Task Put_VersionVieja_409Cambiado()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);
        var numero = await AltaAsync(cliente, cookie, codigo);
        var primera = await PutAsync(
            cliente, cookie, numero, CuerpoModificacion(0, ClienteJson("Calle Ficticia 123"), LineaJson(codigo, 1)));
        Assert.Equal(HttpStatusCode.OK, primera.StatusCode);
        var grabado = await Http.JsonAsync(primera);

        var respuesta = await PutAsync(
            cliente, cookie, numero, CuerpoModificacion(0, ClienteJson("Otra Calle 456"), LineaJson(codigo, 1, 5)));

        await AfirmarConflictoAsync(respuesta, "cambiado", TextoCambiado);
        Assert.Equal(grabado.GetRawText(), (await LeerAsync(cliente, cookie, numero)).GetRawText());
    }

    /// <summary>D-01: pasar a Final con una versión que ya cambió, 409 <c>cambiado</c>; sigue en Borrador.</summary>
    [Fact]
    public async Task PutEstado_VersionVieja_409Cambiado()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);
        var numero = await AltaAsync(cliente, cookie, codigo);
        Assert.Equal(
            HttpStatusCode.OK,
            (await PutAsync(cliente, cookie, numero, CuerpoModificacion(0, ClienteJson(), LineaJson(codigo, 1, 2)))).StatusCode);
        var grabado = await LeerAsync(cliente, cookie, numero);

        await AfirmarConflictoAsync(
            await PutEstadoAsync(cliente, cookie, numero, CuerpoEstado("final", 0)), "cambiado", TextoCambiado);

        Assert.Equal(grabado.GetRawText(), (await LeerAsync(cliente, cookie, numero)).GetRawText());
    }

    /// <summary>
    /// Una versión faltante o fuera de 0..2^53−1, 400 en <c>version</c> con el texto fijo; una que no es un entero, el
    /// cuerpo ilegible en <c>version</c>. Sin escribir, en los dos PUT.
    /// </summary>
    [Fact]
    public async Task Put_VersionFaltante_400()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);
        var numero = await AltaAsync(cliente, cookie, codigo);
        var antes = await LeerAsync(cliente, cookie, numero);

        foreach (var version in new long?[] { null, -1, 9_007_199_254_740_992 })
        {
            await AfirmarRechazoAsync(
                await PutAsync(cliente, cookie, numero, CuerpoModificacion(version, ClienteJson(), LineaJson(codigo, 1))),
                new() { ["version"] = [TextoVersion] });
            await AfirmarRechazoAsync(
                await PutEstadoAsync(cliente, cookie, numero, CuerpoEstado("final", version)),
                new() { ["version"] = [TextoVersion] });
        }

        var ilegible = await PutAsync(
            cliente, cookie, numero,
            "{\"version\":\"Zq7-marcado\",\"cliente\":" + ClienteJson() + ",\"lineas\":[" + LineaJson(codigo, 1) + "]}");
        await AfirmarRechazoAsync(ilegible, new() { ["version"] = [AccionCuerpoIlegible] });
        AfirmarSinDetallesDelLector(await ilegible.Content.ReadAsStringAsync(), "Zq7-marcado");
        var estadoIlegible = await PutEstadoAsync(cliente, cookie, numero, "{\"estado\":\"final\",\"version\":1.5}");
        await AfirmarRechazoAsync(estadoIlegible, new() { ["version"] = [AccionCuerpoIlegible] });

        Assert.Equal(antes.GetRawText(), (await LeerAsync(cliente, cookie, numero)).GetRawText());
    }

    /// <summary>Un <c>estado</c> que no es "final" (ni "borrador"), o ausente, 400 en <c>estado</c>; sin escribir.</summary>
    [Fact]
    public async Task PutEstado_EstadoInvalido_400()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);
        var numero = await AltaAsync(cliente, cookie, codigo);
        var antes = await LeerAsync(cliente, cookie, numero);

        foreach (var estado in new[] { null, "Zq7-anulado", "FINAL", "" })
        {
            var respuesta = await PutEstadoAsync(cliente, cookie, numero, CuerpoEstado(estado, 0));

            await AfirmarRechazoAsync(respuesta, new() { ["estado"] = [TextoEstado] });
            Assert.DoesNotContain("Zq7", await respuesta.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        await AfirmarRechazoAsync(
            await PutEstadoAsync(cliente, cookie, numero, "{\"estado\":1,\"version\":0}"),
            new() { ["estado"] = [AccionCuerpoIlegible] });
        await AfirmarRechazoAsync(
            await PutEstadoAsync(cliente, cookie, numero, "esto no es json"),
            new() { ["estado"] = [AccionCuerpoIlegible] });
        Assert.Equal(antes.GetRawText(), (await LeerAsync(cliente, cookie, numero)).GetRawText());
    }

    /// <summary>
    /// D-03: un <c>orden</c> repetido, menor que 1, inexistente o de una línea con otro código, 400 en <c>lineas[i]</c>
    /// con "La línea no corresponde a este presupuesto."; un <c>orden</c> ilegible, en <c>lineas[i].orden</c>. Sin escribir.
    /// </summary>
    [Fact]
    public async Task Put_LineaAjena_400()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var armazon = await GrabarArticuloAsync(cliente, cookie);
        var lente = await GrabarArticuloAsync(cliente, cookie, "Lente bifocal", "LEN-1", 1000m, 0m);
        var numero = await AltaAsync(cliente, cookie, armazon, lente);
        var antes = await LeerAsync(cliente, cookie, numero);

        // Repetido y menor que 1: antes de leer la base.
        await AfirmarRechazoAsync(
            await PutAsync(cliente, cookie, numero, CuerpoModificacion(
                0, ClienteJson(), LineaJson(armazon, 1), LineaJson(armazon, 1), LineaJson(lente, 0))),
            new() { ["lineas[1]"] = [TextoLineaAjena], ["lineas[2]"] = [TextoLineaAjena] });
        // Inexistente y con otro código: después de comparar la versión.
        await AfirmarRechazoAsync(
            await PutAsync(cliente, cookie, numero, CuerpoModificacion(
                0, ClienteJson(), LineaJson(armazon, 1), LineaJson(armazon, 2), LineaJson(lente, 3))),
            new() { ["lineas[1]"] = [TextoLineaAjena], ["lineas[2]"] = [TextoLineaAjena] });
        await AfirmarRechazoAsync(
            await PutAsync(cliente, cookie, numero,
                "{\"version\":0,\"cliente\":" + ClienteJson()
                + ",\"lineas\":[{\"orden\":\"x\",\"codigoArticulo\":" + armazon + ",\"cantidad\":1,\"porcentajeDescuento\":0}]}"),
            new() { ["lineas[0].orden"] = [AccionCuerpoIlegible] });

        Assert.Equal(antes.GetRawText(), (await LeerAsync(cliente, cookie, numero)).GetRawText());
    }

    /// <summary>D-10: la versión se compara antes que el <c>orden</c>: con una versión vieja y un <c>orden</c> inexistente, 409.</summary>
    [Fact]
    public async Task Put_OrdenDeVersionVieja_409Cambiado()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);
        var numero = await AltaAsync(cliente, cookie, codigo);
        Assert.Equal(
            HttpStatusCode.OK,
            (await PutAsync(cliente, cookie, numero, CuerpoModificacion(0, ClienteJson(), LineaJson(codigo, 1, 2)))).StatusCode);

        var respuesta = await PutAsync(
            cliente, cookie, numero, CuerpoModificacion(0, ClienteJson(), LineaJson(codigo, 5)));

        await AfirmarConflictoAsync(respuesta, "cambiado", TextoCambiado);
    }

    /// <summary>D-10: <c>estado</c> "borrador" sobre un Borrador es inválido (400 en <c>estado</c>), no un conflicto.</summary>
    [Fact]
    public async Task PutEstado_BorradorSobreBorrador_400()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);
        var numero = await AltaAsync(cliente, cookie, codigo);
        var antes = await LeerAsync(cliente, cookie, numero);

        await AfirmarRechazoAsync(
            await PutEstadoAsync(cliente, cookie, numero, CuerpoEstado("borrador", 0)),
            new() { ["estado"] = [TextoEstado] });

        Assert.Equal(antes.GetRawText(), (await LeerAsync(cliente, cookie, numero)).GetRawText());
    }

    /// <summary>D-10: cada línea del alta y de la consulta trae su <c>orden</c> 1..n, al final de sus columnas.</summary>
    [Fact]
    public async Task Get_LineasTraenOrden()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var armazon = await GrabarArticuloAsync(cliente, cookie);
        var lente = await GrabarArticuloAsync(cliente, cookie, "Lente bifocal", "LEN-1", 1000m, 0m);

        var alta = await PostAsync(
            cliente, cookie, Cuerpo("Ficticiez", "Prueboncia", "00718294", null, null, null, lente, armazon, lente));

        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);
        foreach (var json in new[] { await Http.JsonAsync(alta), await LeerAsync(cliente, cookie, 1) })
        {
            Assert.Equal(0, json.GetProperty("version").GetInt64());
            var lineas = json.GetProperty("lineas").EnumerateArray().ToList();
            Assert.Equal([1, 2, 3], lineas.Select(linea => linea.GetProperty("orden").GetInt32()));
            Assert.Equal([lente, armazon, lente], lineas.Select(linea => linea.GetProperty("codigoArticulo").GetInt64()));
            Assert.All(lineas, linea => Assert.Equal(CamposLinea, Nombres(linea)));
        }
    }

    /// <summary>Una línea nueva con un artículo inactivo, 400 en esa línea como en el alta; sin escribir.</summary>
    [Fact]
    public async Task Put_ArticuloNuevoInactivo_400()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var armazon = await GrabarArticuloAsync(cliente, cookie);
        var lente = await GrabarArticuloAsync(cliente, cookie, "Lente bifocal", "LEN-1", 1000m, 0m);
        var numero = await AltaAsync(cliente, cookie, armazon);
        await DesactivarArticuloAsync(cliente, cookie, lente);
        var antes = await LeerAsync(cliente, cookie, numero);

        var respuesta = await PutAsync(
            cliente, cookie, numero, CuerpoModificacion(0, ClienteJson(), LineaJson(armazon, 1), LineaJson(lente)));

        await AfirmarRechazoAsync(respuesta, new() { ["lineas[1]"] = [TextoArticuloNoDisponible] });
        Assert.Equal(antes.GetRawText(), (await LeerAsync(cliente, cookie, numero)).GetRawText());
    }

    /// <summary>Datos inválidos del cliente y de las líneas: 400 con todos los campos y los textos del alta; sin escribir.</summary>
    [Fact]
    public async Task Put_DatosInvalidos_400PorCampo()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);
        var numero = await AltaAsync(cliente, cookie, codigo);
        var antes = await LeerAsync(cliente, cookie, numero);
        var clienteInvalido = JsonSerializer.Serialize(new
        {
            apellido = "Ficticiez",
            nombre = (string?)null,
            dni = "123",
            email = "zq7@",
            domicilio = (string?)null,
        });

        var respuesta = await PutAsync(
            cliente, cookie, numero, CuerpoModificacion(0, clienteInvalido, LineaJson(codigo, 1, 0, 101m)));

        await AfirmarRechazoAsync(
            respuesta,
            new()
            {
                ["nombre"] = ["Ingresá el nombre del cliente"],
                ["dni"] = ["El DNI debe tener 7 u 8 dígitos"],
                ["email"] = ["El email debe tener el formato usuario@dominio.extensión"],
                ["lineas[0].cantidad"] = [TextoCantidadInvalida],
                ["lineas[0].porcentajeDescuento"] = [TextoDescuentoFueraDeRango],
            });
        Assert.DoesNotContain("zq7", await respuesta.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await AfirmarRechazoAsync(
            await PutAsync(cliente, cookie, numero, CuerpoModificacion(0, ClienteJson())),
            new() { ["lineas"] = ["Agregá al menos una línea al presupuesto"] });
        Assert.Equal(antes.GetRawText(), (await LeerAsync(cliente, cookie, numero)).GetRawText());
    }

    /// <summary>Un presupuesto inexistente: 404 con el título fijo y sin detalles, en los dos PUT.</summary>
    [Fact]
    public async Task Put_Inexistente_404()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);

        foreach (var respuesta in new[]
        {
            await PutAsync(cliente, cookie, 99, CuerpoModificacion(0, ClienteJson(), LineaJson(codigo))),
            await PutEstadoAsync(cliente, cookie, 99, CuerpoEstado("final", 0)),
        })
        {
            Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
            Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
            var json = await Http.JsonAsync(respuesta);
            Assert.Equal(TituloNoEncontrado, json.GetProperty("title").GetString());
            Assert.False(json.TryGetProperty("detail", out _));
            Assert.False(json.TryGetProperty("errors", out _));
        }

        await AfirmarSinPresupuestoAsync(cliente, cookie, 99);
    }

    /// <summary>Sin sesión, los dos PUT responden 401 sin cuerpo y no cambian nada.</summary>
    [Fact]
    public async Task Put_SinSesion_401()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);
        var numero = await AltaAsync(cliente, cookie, codigo);
        var antes = await LeerAsync(cliente, cookie, numero);

        foreach (var respuesta in new[]
        {
            await Http.EnviarAsync(
                cliente, HttpMethod.Put, $"{Ruta}/{numero}",
                Json(CuerpoModificacion(0, ClienteJson("Calle Ficticia 123"), LineaJson(codigo, 1)))),
            await Http.EnviarAsync(cliente, HttpMethod.Put, $"{Ruta}/{numero}/estado", Json(CuerpoEstado("final", 0))),
        })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
            Assert.Equal(string.Empty, await respuesta.Content.ReadAsStringAsync());
        }

        Assert.Equal(antes.GetRawText(), (await LeerAsync(cliente, cookie, numero)).GetRawText());
        // Control: el 401 lo dio la sesión, no una ruta inexistente; con sesión, los mismos pedidos se atienden.
        Assert.Equal(
            HttpStatusCode.OK,
            (await PutAsync(cliente, cookie, numero, CuerpoModificacion(0, ClienteJson(), LineaJson(codigo, 1)))).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK, (await PutEstadoAsync(cliente, cookie, numero, CuerpoEstado("final", 1))).StatusCode);
    }

    /// <summary>
    /// Ley 25.326: los 409 traen solo el título, el texto fijo y el <c>motivo</c>; no repiten el cliente, las líneas ni la
    /// versión enviados. Datos ficticios con marcadores.
    /// </summary>
    [Fact]
    public async Task Put_Errores409_NoRepitenDatos()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);
        var numero = await AltaAsync(cliente, cookie, codigo);
        var marcado = JsonSerializer.Serialize(new
        {
            apellido = "Zq7Apellido",
            nombre = "Zq7Nombre",
            dni = "00.718.294",
            domicilio = "Zq7 Calle Ficticia 123",
            email = "zq7@example.com",
            telefono = "011-5555-0199",
        });
        Assert.Equal(
            HttpStatusCode.OK,
            (await PutAsync(cliente, cookie, numero, CuerpoModificacion(0, ClienteJson(), LineaJson(codigo, 1)))).StatusCode);

        var cambiado = await PutAsync(cliente, cookie, numero, CuerpoModificacion(0, marcado, LineaJson(codigo, 1, 7531, 12.34m)));
        await AfirmarConflictoAsync(cambiado, "cambiado", TextoCambiado);
        Assert.Equal(HttpStatusCode.OK, (await PutEstadoAsync(cliente, cookie, numero, CuerpoEstado("final", 1))).StatusCode);
        var final = await PutAsync(cliente, cookie, numero, CuerpoModificacion(2, marcado, LineaJson(codigo, 1, 7531, 12.34m)));
        await AfirmarConflictoAsync(final, "final", TextoFinal);

        foreach (var respuesta in new[] { cambiado, final })
        {
            var json = await Http.JsonAsync(respuesta);
            Assert.All(
                Nombres(json),
                nombre => Assert.Contains(nombre, new[] { "type", "title", "status", "detail", "motivo", "traceId" }));
            var texto = json.GetRawText();
            foreach (var dato in new[] { "Zq7", "zq7", "00718294", "00.718.294", "5555", "example.com", "7531", "12.34" })
            {
                Assert.DoesNotContain(dato, texto, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>
    /// Ley 25.326: con el registro en Trace, ninguna línea de una modificación grabada, rechazada (400 y 409) ni de un paso
    /// a Final contiene los datos del cliente enviados. Datos ficticios con marcadores.
    /// </summary>
    [Fact]
    public async Task Registro_ModificacionYPasoAFinal_SinDatosPersonales()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);
        var numero = await AltaAsync(cliente, cookie, codigo);
        var marcado = JsonSerializer.Serialize(new
        {
            apellido = "Zq7Apellido",
            nombre = "Zq7Nombre",
            dni = "00.718.294",
            domicilio = "Zq7 Calle Ficticia 123",
            email = "zq7@example.com",
            telefono = "011-5555-0199",
        });
        var invalido = marcado.Replace("00.718.294", "ZZMARCA", StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.OK, (await PutAsync(cliente, cookie, numero, CuerpoModificacion(0, marcado, LineaJson(codigo, 1)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PutAsync(cliente, cookie, numero, CuerpoModificacion(1, invalido, LineaJson(codigo, 1)))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await PutAsync(cliente, cookie, numero, CuerpoModificacion(0, marcado, LineaJson(codigo, 1)))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PutEstadoAsync(cliente, cookie, numero, CuerpoEstado("final", 1))).StatusCode);

        var lineas = fabrica.Registro.Lineas;
        Assert.Contains(lineas, l => l.Nivel == LogLevel.Trace);
        Assert.Contains(lineas, l => l.Mensaje.Contains("PresupuestosController", StringComparison.Ordinal));
        foreach (var secreto in new[] { "Zq7", "zq7", "00718294", "00.718.294", "5555-0199", "example.com", "ZZMARCA" })
        {
            var conElDato = lineas.Where(l => l.TextoCompleto.Contains(secreto, StringComparison.Ordinal))
                .Select(l => $"{l.Categoria} ({l.Nivel})")
                .ToList();
            Assert.True(conElDato.Count == 0, $"\"{secreto}\" llegó al log en: {string.Join(", ", conElDato)}");
        }
    }

    /// <summary>
    /// R-02: con sesión, el PUT de modificación sin <c>Content-Type: application/json</c> (texto plano o ninguno) responde
    /// 415 sin cuerpo y no cambia nada.
    /// </summary>
    [Fact]
    public async Task Put_SinJson_415()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);
        var numero = await AltaAsync(cliente, cookie, codigo);
        var antes = await LeerAsync(cliente, cookie, numero);
        var cuerpo = CuerpoModificacion(0, ClienteJson("Calle Ficticia 123"), LineaJson(codigo, 1));

        await AfirmarSinJsonAsync(cliente, cookie, $"{Ruta}/{numero}", cuerpo);

        Assert.Equal(antes.GetRawText(), (await LeerAsync(cliente, cookie, numero)).GetRawText());
    }

    /// <summary>R-02: con sesión, el PUT de estado sin JSON responde 415 sin cuerpo y el presupuesto sigue en Borrador.</summary>
    [Fact]
    public async Task PutEstado_SinJson_415()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);
        var numero = await AltaAsync(cliente, cookie, codigo);
        var antes = await LeerAsync(cliente, cookie, numero);

        await AfirmarSinJsonAsync(cliente, cookie, $"{Ruta}/{numero}/estado", CuerpoEstado("final", 0));

        Assert.Equal(antes.GetRawText(), (await LeerAsync(cliente, cookie, numero)).GetRawText());
    }

    /// <summary>
    /// D-16: con sesión, un PUT de modificación de más de 16 KB responde 413, con <c>Content-Length</c> y en partes, sin
    /// cambiar nada; uno de 16 KB justos se atiende.
    /// </summary>
    [Fact]
    public async Task Put_DemasiadoGrande_413()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);
        var numero = await AltaAsync(cliente, cookie, codigo);
        var antes = await LeerAsync(cliente, cookie, numero);
        string Cuerpo(int bytes) => ConRelleno(CuerpoModificacion(0, ClienteJson(), LineaJson(codigo, 1, 2)), bytes);

        await AfirmarDemasiadoGrandeAsync(cliente, cookie, $"{Ruta}/{numero}", Cuerpo(16 * 1024 + 1));
        Assert.Equal(antes.GetRawText(), (await LeerAsync(cliente, cookie, numero)).GetRawText());

        var justo = await PutAsync(cliente, cookie, numero, Cuerpo(16 * 1024));
        Assert.Equal(HttpStatusCode.OK, justo.StatusCode);
    }

    /// <summary>D-16: con sesión, un PUT de estado de más de 16 KB responde 413 y el presupuesto sigue en Borrador.</summary>
    [Fact]
    public async Task PutEstado_DemasiadoGrande_413()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);
        var numero = await AltaAsync(cliente, cookie, codigo);
        var antes = await LeerAsync(cliente, cookie, numero);

        await AfirmarDemasiadoGrandeAsync(
            cliente, cookie, $"{Ruta}/{numero}/estado", ConRelleno(CuerpoEstado("final", 0), 16 * 1024 + 1));

        Assert.Equal(antes.GetRawText(), (await LeerAsync(cliente, cookie, numero)).GetRawText());
        var justo = await PutEstadoAsync(cliente, cookie, numero, ConRelleno(CuerpoEstado("final", 0), 16 * 1024));
        Assert.Equal(HttpStatusCode.OK, justo.StatusCode);
    }

    /// <summary>
    /// AC-09 con el recálculo real del catálogo (FEAT-002c): el múltiplo de redondeo pasa a 50 y el artículo de 1.815,00
    /// queda en 1.850,00 exactos; el presupuesto ya grabado sigue con 1.815,00, por GET, sin modificarlo y después de
    /// modificar su Cantidad.
    /// </summary>
    [Fact]
    public async Task Get_TrasRecalculoDelCatalogo_ConservaPrecioUnitario()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);
        var numero = await AltaAsync(cliente, cookie, codigo);

        // Monotributo: el precio de venta es costo × (100 + margen) / 100 = 1.815,00, redondeado hacia arriba a 50.
        var configuracion =
            "{\"alicuotaIva\":21,\"condicionFiscal\":\"monotributo\",\"multiploRedondeo\":50,\"topeIdentificacion\":1}";
        var grabacion = await Http.PostJsonAsync(cliente, "/api/configuracion", configuracion, cookie);
        Assert.Equal(HttpStatusCode.OK, grabacion.StatusCode);
        Assert.Equal(1, (await Http.JsonAsync(grabacion)).GetProperty("preciosCambiados").GetInt32());
        var articulo = await Http.JsonAsync(await Http.GetAsync(cliente, $"{RutaArticulos}/{codigo}", cookie));
        Assert.Equal(1850.00m, articulo.GetProperty("precioVenta").GetDecimal());

        AfirmarLinea((await LeerAsync(cliente, cookie, numero)).GetProperty("lineas")[0], 1, codigo, "Armazón", 1815m);
        var modificado = await PutAsync(
            cliente, cookie, numero, CuerpoModificacion(0, ClienteJson(), LineaJson(codigo, 1, 2)));
        Assert.Equal(HttpStatusCode.OK, modificado.StatusCode);
        AfirmarLinea(
            (await LeerAsync(cliente, cookie, numero)).GetProperty("lineas")[0],
            1, codigo, "Armazón", 1815m, 2, 0m, 1815m, 3630m);
    }

    /// <summary>
    /// AC-10: desactivar el artículo no cambia un Borrador ya grabado: el GET, sin modificarlo, devuelve el mismo
    /// presupuesto, con el mismo Código, Descripción y Precio Unitario.
    /// </summary>
    [Fact]
    public async Task Get_TrasDesactivarArticulo_LineaSinCambios()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);
        var numero = await AltaAsync(cliente, cookie, codigo);
        var antes = await LeerAsync(cliente, cookie, numero);

        await DesactivarArticuloAsync(cliente, cookie, codigo);

        var despues = await LeerAsync(cliente, cookie, numero);
        AfirmarLinea(despues.GetProperty("lineas")[0], 1, codigo, "Armazón", 1815m);
        Assert.Equal(antes.GetRawText(), despues.GetRawText());
    }

    // FEAT-006: búsqueda de presupuestos (POST /api/presupuestos/busqueda, ADR-019).

    /// <summary>
    /// AC-01 y AC-09 por la Api: con presupuestos de "González" (en Final) y "Gómez", la búsqueda por Apellido "ONZALEZ"
    /// responde 200 solo con el de "González", con sus siete datos, la página, el tamaño 50 y el total.
    /// </summary>
    [Fact]
    public async Task PostBusqueda_GonzalezYGomez_SoloGonzalez()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);
        var gonzalez = await AltaDeAsync(cliente, cookie, "González", "María José", "99.999.998", codigo);
        await AltaDeAsync(cliente, cookie, "Gómez", "Ana", "99999997", codigo);
        // Un Final se graba en Borrador y después pasa a Final, como en la app.
        using (var final = await PutEstadoAsync(cliente, cookie, gonzalez, CuerpoEstado("final", 0)))
        {
            Assert.Equal(HttpStatusCode.OK, final.StatusCode);
        }

        using var respuesta = await PostBusquedaAsync(cliente, cookie, "{\"apellido\":\"ONZALEZ\"}");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("application/json", respuesta.Content.Headers.ContentType?.MediaType);
        var json = await Http.JsonAsync(respuesta);
        Assert.Equal(CamposBusqueda, Nombres(json));
        Assert.Equal(1, json.GetProperty("pagina").GetInt32());
        Assert.Equal(50, json.GetProperty("tamanoPagina").GetInt32());
        Assert.Equal(1, json.GetProperty("total").GetInt32());
        var encontrado = Assert.Single(json.GetProperty("presupuestos").EnumerateArray());
        Assert.Equal(CamposListado, Nombres(encontrado));
        Assert.Equal(gonzalez, encontrado.GetProperty("numero").GetInt64());
        Assert.Equal(
            DateOnly.FromDateTime(fabrica.Reloj.GetLocalNow().DateTime).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            encontrado.GetProperty("fecha").GetString());
        Assert.Equal("González", encontrado.GetProperty("apellido").GetString());
        Assert.Equal("María José", encontrado.GetProperty("nombre").GetString());
        Assert.Equal("99999998", encontrado.GetProperty("dni").GetString());
        Assert.Equal("final", encontrado.GetProperty("estado").GetString());
        Assert.Equal(1815m, encontrado.GetProperty("total").GetDecimal());
    }

    /// <summary>
    /// FR-07, FR-08: sin filtros (cuerpo vacío o con nulos) trae todos, por número de mayor a menor; una página después
    /// de la última trae la lista vacía y el total.
    /// </summary>
    [Fact]
    public async Task PostBusqueda_SinFiltrosYPaginaPosterior_ListaVaciaConTotal()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);
        var primero = await AltaAsync(cliente, cookie, codigo);
        var segundo = await AltaAsync(cliente, cookie, codigo);

        foreach (var cuerpo in new[] { "{}", "{\"apellido\":null,\"desde\":null,\"pagina\":null}", "null" })
        {
            using var todos = await PostBusquedaAsync(cliente, cookie, cuerpo);
            Assert.Equal(HttpStatusCode.OK, todos.StatusCode);
            var json = await Http.JsonAsync(todos);
            Assert.Equal(
                new[] { segundo, primero },
                json.GetProperty("presupuestos").EnumerateArray().Select(p => p.GetProperty("numero").GetInt64()));
            Assert.Equal(2, json.GetProperty("total").GetInt32());
        }

        using var posterior = await PostBusquedaAsync(cliente, cookie, "{\"pagina\":2}");
        Assert.Equal(HttpStatusCode.OK, posterior.StatusCode);
        var vacia = await Http.JsonAsync(posterior);
        Assert.Empty(vacia.GetProperty("presupuestos").EnumerateArray());
        Assert.Equal(2, vacia.GetProperty("pagina").GetInt32());
        Assert.Equal(2, vacia.GetProperty("total").GetInt32());
    }

    /// <summary>
    /// Sad path: un cuerpo ilegible (JSON roto, un arreglo, la página como texto, decimal o fuera de un entero, un DNI
    /// como número, una fecha como número) responde 400 con un solo error y la acción fija, en el campo que nombra el
    /// lector o en <c>apellido</c>, sin los mensajes del lector ni lo enviado.
    /// </summary>
    [Theory]
    [InlineData("esto no es json", "apellido")]
    [InlineData("[1,2,3]", "apellido")]
    [InlineData("{\"apellido\":\"Zq7-marcado", "apellido")]
    [InlineData("{\"apellido\":\"Zq7-marcado\",\"pagina\":\"2\"}", "pagina")]
    [InlineData("{\"apellido\":\"Zq7-marcado\",\"pagina\":1.5}", "pagina")]
    [InlineData("{\"apellido\":\"Zq7-marcado\",\"pagina\":99999999999}", "pagina")]
    [InlineData("{\"dni\":30111222}", "dni")]
    [InlineData("{\"nombre\":[\"Zq7-marcado\"]}", "nombre")]
    [InlineData("{\"desde\":20260310}", "desde")]
    [InlineData("{\"hasta\":{\"dia\":10}}", "hasta")]
    public async Task PostBusqueda_CuerpoIlegible_400(string cuerpo, string campo)
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);

        using var respuesta = await PostBusquedaAsync(cliente, cookie, cuerpo);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            new Dictionary<string, string[]> { [campo] = [AccionCuerpoIlegibleBusqueda] }, await ErroresAsync(respuesta));
        AfirmarSinDetallesDelLector(
            await respuesta.Content.ReadAsStringAsync(), "Zq7-marcado", "30111222", "20260310", "99999999999");
    }

    /// <summary>
    /// Sad path: un Apellido con un sustituto UTF-16 suelto escapado en el JSON (<c>\ud800</c>). El lector de JSON de
    /// ASP.NET Core no lo convierte en texto: responde 400 de cuerpo ilegible en <c>apellido</c>, con la acción fija de la
    /// búsqueda y sin el valor; el caso de uso (que rechaza UTF-16 inválido con su propio texto) no llega a ejecutarse.
    /// </summary>
    [Fact]
    public async Task PostBusqueda_ApellidoConSustitutoSuelto_400SinElValor()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);

        using var respuesta = await PostBusquedaAsync(cliente, cookie, "{\"apellido\":\"Zq7\\ud800\"}");

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal(
            new Dictionary<string, string[]> { ["apellido"] = [AccionCuerpoIlegibleBusqueda] },
            await ErroresAsync(respuesta));
        var texto = await respuesta.Content.ReadAsStringAsync();
        AfirmarSinDetallesDelLector(texto, "Zq7");
        Assert.DoesNotContain("ud800", texto, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Un cuerpo vacío con <c>Content-Type: application/json</c> responde distinto según cómo llega, y esta prueba fija
    /// las dos respuestas tal como son hoy:
    /// <list type="bullet">
    /// <item>con <c>Content-Length: 0</c>, MVC lo reconoce como vacío y, como la solicitud es nullable, la enlaza como
    /// null: 200 con todos los presupuestos, como <c>{}</c>;</item>
    /// <item>en partes (sin <c>Content-Length</c>), MVC no sabe de antemano que está vacío, el lector de JSON no encuentra
    /// ningún valor y responde 400 de cuerpo ilegible en <c>apellido</c>, con la acción fija de la búsqueda y sin
    /// detalles del lector.</item>
    /// </list>
    /// La pantalla siempre envía un objeto JSON; la búsqueda sin filtros se pide con <c>{}</c>.
    /// </summary>
    [Fact]
    public async Task PostBusqueda_CuerpoVacioConJson_SegunLargo()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);
        var numero = await AltaAsync(cliente, cookie, codigo);

        using var conLargoCero = await Http.EnviarAsync(cliente, HttpMethod.Post, RutaBusqueda, Json(string.Empty), cookie);
        Assert.Equal(0, conLargoCero.RequestMessage?.Content?.Headers.ContentLength);
        Assert.Equal(HttpStatusCode.OK, conLargoCero.StatusCode);
        var json = await Http.JsonAsync(conLargoCero);
        Assert.Equal(1, json.GetProperty("total").GetInt32());
        Assert.Equal(
            numero, Assert.Single(json.GetProperty("presupuestos").EnumerateArray()).GetProperty("numero").GetInt64());

        using var enPartes = await Http.EnviarAsync(
            cliente, HttpMethod.Post, RutaBusqueda, new Http.ContenidoSinLargo(string.Empty, "application/json"), cookie);
        Assert.Equal(HttpStatusCode.BadRequest, enPartes.StatusCode);
        Assert.Equal("application/problem+json", enPartes.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            new Dictionary<string, string[]> { ["apellido"] = [AccionCuerpoIlegibleBusqueda] }, await ErroresAsync(enPartes));
        AfirmarSinDetallesDelLector(await enPartes.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Sad path (AC-07, D-07, D-17): fechas inválidas, Desde posterior a Hasta, textos largos y página fuera de rango
    /// responden 400 con todos los errores por campo y textos fijos, sin repetir lo enviado.
    /// </summary>
    [Fact]
    public async Task PostBusqueda_DatosInvalidos_400PorCampo()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);

        using var todos = await PostBusquedaAsync(
            cliente,
            cookie,
            JsonSerializer.Serialize(new
            {
                apellido = "Zq7" + new string('a', 98),
                nombre = "Zq7" + new string('b', 98),
                dni = "Zq7" + new string('1', 18),
                desde = "2026-02-30",
                hasta = "Zq7-fecha",
                pagina = 1_000_001,
            }));

        await AfirmarRechazoAsync(todos, new Dictionary<string, string[]>
        {
            ["apellido"] = ["Usá hasta 100 caracteres."],
            ["nombre"] = ["Usá hasta 100 caracteres."],
            ["dni"] = ["Usá hasta 20 caracteres."],
            ["desde"] = ["La fecha no es válida."],
            ["hasta"] = ["La fecha no es válida."],
            ["pagina"] = ["Pedí una página entre 1 y 1.000.000."],
        });
        var texto = await todos.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Zq7", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("2026-02-30", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("1000001", texto, StringComparison.Ordinal);

        using var rango = await PostBusquedaAsync(
            cliente, cookie, "{\"desde\":\"2026-03-20\",\"hasta\":\"2026-03-10\",\"pagina\":0}");
        await AfirmarRechazoAsync(rango, new Dictionary<string, string[]>
        {
            ["desde"] = ["La fecha Desde no puede ser posterior a Hasta."],
            ["pagina"] = ["Pedí una página entre 1 y 1.000.000."],
        });
    }

    /// <summary>
    /// ADR-019 y R-02 (Ley 25.326): con la captura del log en Trace, ni una búsqueda válida ni una rechazada ni un
    /// cuerpo ilegible dejan Apellido, Nombre o DNI en ningún mensaje. Datos ficticios.
    /// </summary>
    [Fact]
    public async Task PostBusqueda_Registro_SinDatosPersonales()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);

        using var valida = await PostBusquedaAsync(
            cliente, cookie, "{\"apellido\":\"Zq7Apellido\",\"nombre\":\"Zq7Nombre\",\"dni\":\"30.111.222\"}");
        Assert.Equal(HttpStatusCode.OK, valida.StatusCode);
        using var invalida = await PostBusquedaAsync(
            cliente, cookie, "{\"apellido\":\"Zq7Apellido\",\"dni\":\"30.111.222\",\"desde\":\"2026-02-30\"}");
        Assert.Equal(HttpStatusCode.BadRequest, invalida.StatusCode);
        using var ilegible = await PostBusquedaAsync(cliente, cookie, "{\"apellido\":\"Zq7Apellido\",\"dni\":\"ZZMARCA");
        Assert.Equal(HttpStatusCode.BadRequest, ilegible.StatusCode);

        var lineas = fabrica.Registro.Lineas;
        // Control: la captura ve Trace, y MVC registró la ejecución de la acción de la búsqueda.
        // Atención: este control depende del texto que MVC escribe en Trace al ejecutar la acción (hoy incluye
        // "Optica.Api.Controllers.PresupuestosController.Buscar"). Si una versión de ASP.NET Core cambia ese mensaje, falla
        // el control y no la protección de los datos: hay que actualizar el texto buscado, no quitar el control.
        Assert.Contains(lineas, l => l.Nivel == LogLevel.Trace);
        Assert.Contains(lineas, l => l.Mensaje.Contains("PresupuestosController.Buscar", StringComparison.Ordinal));
        foreach (var secreto in new[] { "Zq7", "zq7", "30111222", "30.111.222", "ZZMARCA" })
        {
            var conElDato = lineas.Where(l => l.TextoCompleto.Contains(secreto, StringComparison.Ordinal))
                .Select(l => $"{l.Categoria} ({l.Nivel})")
                .ToList();
            Assert.True(conElDato.Count == 0, $"\"{secreto}\" llegó al log en: {string.Join(", ", conElDato)}");
        }
    }

    // FEAT-006, Block 5: el PDF del presupuesto (FR-11, FR-12).

    private const string TituloConflictoPdf = "No se pudo generar el PDF del presupuesto.";
    private const string TextoBorrador = "El presupuesto está en Borrador.";

    /// <summary>
    /// AC-11: el PDF de un Final responde 200 <c>application/pdf</c> como adjunto, con el nombre
    /// <c>presupuesto-{numero}.pdf</c> y sin datos del cliente en el encabezado (R-02); con la marca instalada, el número
    /// está en el color primario #0903A0 sobre el fondo #FFFFFF. Ninguna línea del log (Trace incluido) lleva los datos del
    /// cliente. Datos ficticios.
    /// </summary>
    [Fact]
    public async Task Pdf_PresupuestoFinal_DevuelvePdf()
    {
        var carpeta = InstalarMarca(
            "{ \"logo\": \"logo.svg\", \"colores\": { \"primario\": \"#0903A0\", \"fondo\": \"#FFFFFF\" } }",
            ("logo.svg", File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "marca-ejemplo", "logo.svg"))));
        try
        {
            using var fabrica = new FabricaApi();
            using var conMarca = ConMarca(fabrica, carpeta);
            var cliente = CrearCliente(conMarca);
            var cookie = await Http.DefinirAsync(cliente);
            var codigo = await GrabarArticuloAsync(cliente, cookie);
            var numero = await AltaDeAsync(cliente, cookie, "Zq7Apellido", "Zq7Nombre", "30.111.222", codigo);
            await PasarAFinalAsync(cliente, cookie, numero, version: 0);

            using var respuesta = await GetPdfAsync(cliente, cookie, numero);

            Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
            Assert.Equal("application/pdf", respuesta.Content.Headers.ContentType?.MediaType);
            var disposicion = respuesta.Content.Headers.ContentDisposition;
            Assert.NotNull(disposicion);
            Assert.Equal("attachment", disposicion.DispositionType);
            var nombre = $"presupuesto-{numero}.pdf";
            Assert.Equal(nombre, disposicion.FileName?.Trim('"'));
            // ASP.NET Core escribe también filename* (RFC 6266) con el mismo nombre, que HttpClient devuelve decodificado.
            Assert.Equal(nombre, disposicion.FileNameStar);
            var encabezados = respuesta.Content.Headers.ToString() + respuesta.Headers;
            Assert.DoesNotContain("Zq7", encabezados, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("30111222", encabezados, StringComparison.Ordinal);

            var pdf = await respuesta.Content.ReadAsByteArrayAsync();
            Assert.Equal("%PDF-"u8.ToArray(), pdf[..5]);
            using var documento = PdfDocument.Open(pdf);
            var pagina = documento.GetPage(1);
            var titulo = pagina.GetWords().FirstOrDefault(palabra => palabra.Text == "Presupuesto");
            Assert.NotNull(titulo);
            Assert.All(titulo.Letters, letra => Assert.True(EsColor(letra.Color, "#0903A0"), "La palabra «Presupuesto» no está en #0903A0."));
            Assert.Contains(
                pagina.Paths,
                camino => camino.IsFilled
                    && camino.FillColor is not null
                    && EsColor(camino.FillColor, "#FFFFFF")
                    && camino.GetBoundingRectangle() is { } rectangulo
                    && rectangulo.Width >= pagina.Width - 1
                    && rectangulo.Height >= pagina.Height - 1);

            var lineas = fabrica.Registro.Lineas;
            Assert.Contains(lineas, l => l.Mensaje.Contains("PresupuestosController.DescargarPdf", StringComparison.Ordinal));
            foreach (var secreto in new[] { "Zq7", "zq7", "30111222", "30.111.222" })
            {
                var conElDato = lineas.Where(l => l.TextoCompleto.Contains(secreto, StringComparison.Ordinal))
                    .Select(l => $"{l.Categoria} ({l.Nivel})")
                    .ToList();
                Assert.True(conElDato.Count == 0, $"\"{secreto}\" llegó al log en: {string.Join(", ", conElDato)}");
            }
        }
        finally
        {
            BorrarMarca(carpeta);
        }
    }

    /// <summary>
    /// AC-13, AC-14 y AC-15 por la Api, leídos con PdfPig: el presupuesto 155 del 10/03/2026, con Apellido, Nombre, DNI y
    /// Teléfono y sin Domicilio ni Email; una línea de 6,67 × 3 con 50 % (3,34 y 10,02) y otra de 2.700,00; total
    /// 2.710,02; la leyenda "Precios finales, IVA incluido" y ningún importe de IVA. El 154 se graba en Borrador por el
    /// repositorio y el 155 se da de alta y pasa a Final como en la app. Datos ficticios.
    /// </summary>
    [Fact]
    public async Task GetPdf_Final_ContenidoDeAC13AC14AC15()
    {
        var reloj = new FakeTimeProvider(new DateTimeOffset(2026, 3, 10, 15, 0, 0, TimeSpan.Zero));
        reloj.SetLocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById("America/Argentina/Buenos_Aires"));
        using var fabrica = new FabricaApi(servicios =>
        {
            servicios.RemoveAll<TimeProvider>();
            servicios.AddSingleton<TimeProvider>(reloj);
        });
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var lente = await GrabarArticuloAsync(cliente, cookie, "Lente orgánico", "LEN-1", 6.67m, 0m);
        var armazon = await GrabarArticuloAsync(cliente, cookie, "Armazón metálico", "ARM-1", 2700m, 0m);
        await GrabarPresupuestoDirectoAsync(fabrica, 154, lente);
        var cuerpo = JsonSerializer.Serialize(new
        {
            cliente = new
            {
                apellido = "Ficticio",
                nombre = "Prueba",
                dni = "30123456",
                domicilio = (string?)null,
                email = (string?)null,
                telefono = "011 5555-0000",
            },
            lineas = new[]
            {
                new { codigoArticulo = lente, cantidad = 3, porcentajeDescuento = 50m },
                new { codigoArticulo = armazon, cantidad = 1, porcentajeDescuento = 0m },
            },
        });
        using var alta = await PostAsync(cliente, cookie, cuerpo);
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);
        Assert.Equal(155, (await Http.JsonAsync(alta)).GetProperty("numero").GetInt64());
        await PasarAFinalAsync(cliente, cookie, 155, version: 0);

        using var respuesta = await GetPdfAsync(cliente, cookie, 155);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        using var documento = PdfDocument.Open(await respuesta.Content.ReadAsByteArrayAsync());
        var pagina = documento.GetPage(1);
        var palabras = pagina.GetWords().Select(palabra => palabra.Text).ToList();
        var texto = string.Join(' ', palabras);

        // AC-13: número, fecha y los cuatro datos cargados, sin Domicilio ni Email.
        Assert.Contains("Presupuesto N.º 155", texto, StringComparison.Ordinal);
        Assert.Contains("Fecha: 10/03/2026", texto, StringComparison.Ordinal);
        Assert.Contains("Apellido: Ficticio", texto, StringComparison.Ordinal);
        Assert.Contains("Nombre: Prueba", texto, StringComparison.Ordinal);
        Assert.Contains("DNI: 30123456", texto, StringComparison.Ordinal);
        Assert.Contains("Nro. de Teléfono: 011 5555-0000", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("Domicilio", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("Email", texto, StringComparison.Ordinal);

        // AC-14: las dos líneas con sus siete campos, en su orden, y el total.
        AfirmarSecuencia(palabras, lente.ToString(CultureInfo.InvariantCulture), "Lente", "orgánico", "6,67", "3", "50", "3,34", "10,02");
        AfirmarSecuencia(
            palabras, armazon.ToString(CultureInfo.InvariantCulture), "Armazón", "metálico", "2.700,00", "1", "0", "2.700,00", "2.700,00");
        Assert.True(
            texto.IndexOf("Lente orgánico", StringComparison.Ordinal) < texto.IndexOf("Armazón metálico", StringComparison.Ordinal),
            "Las líneas no están en su orden.");
        Assert.Contains("Total: $ 2.710,02", texto, StringComparison.Ordinal);

        // AC-15: la leyenda y la palabra IVA solo en ella, sin alícuotas ni importes de IVA.
        Assert.Contains("Precios finales, IVA incluido", texto, StringComparison.Ordinal);
        Assert.Equal(1, texto.Split("IVA", StringSplitOptions.None).Length - 1);
        foreach (var prohibido in new[] { "alícuota", "Alícuota", "21 %", "21%", "10,5 %", "Neto", "Subtotal" })
        {
            Assert.DoesNotContain(prohibido, texto, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// AC-12 (FR-12, R-07): pedido directo a la Api, el PDF de un Borrador responde 409 <c>borrador</c> con su título y
    /// su texto fijos, sin cuerpo PDF; el presupuesto sigue en Borrador y con la misma versión.
    /// </summary>
    [Fact]
    public async Task GetPdf_Borrador_409ConflictoBorrador()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);
        var numero = await AltaDeAsync(cliente, cookie, "Zq7Apellido", "Zq7Nombre", "30.111.222", codigo);

        using var respuesta = await GetPdfAsync(cliente, cookie, numero);

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        Assert.Null(respuesta.Content.Headers.ContentDisposition);
        var texto = await respuesta.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Zq7", texto, StringComparison.OrdinalIgnoreCase);
        var json = await Http.JsonAsync(respuesta);
        Assert.Equal(409, json.GetProperty("status").GetInt32());
        Assert.Equal(TituloConflictoPdf, json.GetProperty("title").GetString());
        Assert.Equal(TextoBorrador, json.GetProperty("detail").GetString());
        Assert.Equal("borrador", json.GetProperty("motivo").GetString());
        Assert.False(json.TryGetProperty("errors", out _));

        var leido = await LeerAsync(cliente, cookie, numero);
        Assert.Equal("borrador", leido.GetProperty("estado").GetString());
        Assert.Equal(0, leido.GetProperty("version").GetInt64());
    }

    /// <summary>
    /// Sad path: un número que no existe responde 404 con el título fijo de FEAT-003a y sin detalles; uno que no es un
    /// entero <c>long</c> no coincide con la ruta (404 del ruteo, sin ProblemDetails de la acción).
    /// </summary>
    [Fact]
    public async Task Pdf_PresupuestoInexistente_NoEncontrado404()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);

        foreach (var numero in new[] { "999", "0", "-1", long.MaxValue.ToString(CultureInfo.InvariantCulture) })
        {
            using var respuesta = await Http.GetAsync(cliente, $"{Ruta}/{numero}/pdf", cookie);

            Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
            Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
            var json = await Http.JsonAsync(respuesta);
            Assert.Equal(TituloNoEncontrado, json.GetProperty("title").GetString());
            Assert.False(json.TryGetProperty("detail", out _));
            Assert.False(json.TryGetProperty("motivo", out _));
        }

        foreach (var ruta in new[] { $"{Ruta}/abc/pdf", $"{Ruta}/1.5/pdf", $"{Ruta}/99999999999999999999/pdf" })
        {
            using var respuesta = await Http.GetAsync(cliente, ruta, cookie);

            Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
            Assert.NotEqual("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        }
    }

    /// <summary>
    /// Sad path (R-03): sin sesión, el PDF responde 401 sin cuerpo, también de un Final que existe; la ruta no es
    /// anónima.
    /// </summary>
    [Fact]
    public async Task GetPdf_SinSesion_401NoAutorizado()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie);
        var numero = await AltaAsync(cliente, cookie, codigo);
        await PasarAFinalAsync(cliente, cookie, numero, version: 0);
        // La ruta existe (un 401 de una ruta inexistente no probaría nada) y no es anónima.
        var patron = $"{Ruta}/{{numero:long}}/pdf";
        var registradas = fabrica.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains("GET") ?? false)
            .Select(endpoint => "/" + (endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/'));
        Assert.Contains(patron, registradas);
        Assert.False(Optica.Api.Acceso.RutasAnonimas.Contiene("GET", patron));

        foreach (var cookieEnviada in new string?[] { null, "OpticaSesion=no-es-una-cookie-valida" })
        {
            using var respuesta = await Http.GetAsync(cliente, $"{Ruta}/{numero}/pdf", cookieEnviada);

            Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
            Assert.Null(respuesta.Content.Headers.ContentDisposition);
            Assert.Empty(await respuesta.Content.ReadAsByteArrayAsync());
        }
    }

    /// <summary>
    /// R-10 (Ley 25.326): con el generador real y un logo PNG corrupto en la marca instalada (firma válida, contenido
    /// roto), el PDF de un Final responde el 500 genérico, y ni la respuesta ni ninguna línea del log (Trace incluido)
    /// llevan el Apellido, el Nombre ni el DNI del cliente. Datos ficticios e identificables.
    /// </summary>
    [Fact]
    public async Task GetPdf_GeneradorConError_500Generico_LogSinDatosDelCliente()
    {
        byte[] corrupto = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02, 0x03, 0x04, 0x05];
        var carpeta = InstalarMarca(
            "{ \"logo\": \"logo.png\", \"colores\": { \"primario\": \"#0903A0\", \"fondo\": \"#FFFFFF\" } }",
            ("logo.png", corrupto));
        try
        {
            using var fabrica = new FabricaApi();
            using var conMarca = ConMarca(fabrica, carpeta);
            var cliente = CrearCliente(conMarca);
            var cookie = await Http.DefinirAsync(cliente);

            // Control: la marca vigente es la instalada (con el logo corrupto), no la de ejemplo.
            var marca = await Http.JsonAsync(await Http.GetAsync(cliente, "/api/marca", cookie));
            Assert.False(marca.GetProperty("esEjemplo").GetBoolean());

            var codigo = await GrabarArticuloAsync(cliente, cookie);
            var numero = await AltaDeAsync(cliente, cookie, "Zq7Corrompido", "Zq7Nombre", "30.111.222", codigo);
            await PasarAFinalAsync(cliente, cookie, numero, version: 0);

            using var respuesta = await GetPdfAsync(cliente, cookie, numero);

            Assert.Equal(HttpStatusCode.InternalServerError, respuesta.StatusCode);
            Assert.Null(respuesta.Content.Headers.ContentDisposition);
            var texto = await respuesta.Content.ReadAsStringAsync();
            var json = JsonDocument.Parse(texto).RootElement;
            Assert.Equal("No se pudo completar el pedido.", json.GetProperty("title").GetString());
            foreach (var secreto in new[] { "Zq7", "zq7", "30111222", "30.111.222", "logo.png" })
            {
                Assert.DoesNotContain(secreto, texto, StringComparison.Ordinal);
            }

            var lineas = fabrica.Registro.Lineas;
            // Control: la captura ve Trace, y el manejador registró el error no controlado del pedido del PDF.
            Assert.Contains(lineas, l => l.Nivel == LogLevel.Trace);
            Assert.Contains(lineas, l => l.Nivel == LogLevel.Error
                && l.TextoCompleto.Contains("InvalidOperationException", StringComparison.Ordinal)
                && l.TextoCompleto.Contains($"{Ruta}/{numero}/pdf", StringComparison.Ordinal));
            foreach (var secreto in new[] { "Zq7", "zq7", "30111222", "30.111.222" })
            {
                var conElDato = lineas.Where(l => l.TextoCompleto.Contains(secreto, StringComparison.Ordinal))
                    .Select(l => $"{l.Categoria} ({l.Nivel})")
                    .ToList();
                Assert.True(conElDato.Count == 0, $"\"{secreto}\" llegó al log en: {string.Join(", ", conElDato)}");
            }
        }
        finally
        {
            BorrarMarca(carpeta);
        }
    }

    private static Task<HttpResponseMessage> GetPdfAsync(HttpClient cliente, string cookie, long numero) =>
        Http.GetAsync(cliente, $"{Ruta}/{numero}/pdf", cookie);

    /// <summary>Pasa a Final, como la pantalla, un presupuesto en Borrador con la versión leída.</summary>
    private static async Task PasarAFinalAsync(HttpClient cliente, string cookie, long numero, long version)
    {
        using var respuesta = await PutEstadoAsync(cliente, cookie, numero, CuerpoEstado("final", version));
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
    }

    /// <summary>
    /// Una carpeta de marca propia (<c>&lt;raíz&gt;/Marca</c>) con ese <c>branding.json</c> y esos archivos. La raíz la
    /// crea <see cref="FabricaApi.NuevoDirectorioTemporal"/> (nunca bajo <c>/mnt/</c>); la prueba la borra al terminar con
    /// <see cref="BorrarMarca"/>.
    /// </summary>
    private static string InstalarMarca(string branding, params (string Nombre, byte[] Contenido)[] archivos)
    {
        var carpeta = Path.Combine(FabricaApi.NuevoDirectorioTemporal(), "Marca");
        Directory.CreateDirectory(carpeta);
        File.WriteAllText(Path.Combine(carpeta, "branding.json"), branding);
        foreach (var (nombre, contenido) in archivos)
        {
            File.WriteAllBytes(Path.Combine(carpeta, nombre), contenido);
        }

        return carpeta;
    }

    /// <summary>
    /// Borra la raíz de la marca de <see cref="InstalarMarca"/>. Tolerante: si no se puede borrar, lo informa y sigue, así
    /// una falla de borrado no tapa la de una aserción.
    /// </summary>
    private static void BorrarMarca(string carpeta) =>
        FabricaApi.BorrarDirectorioDePrueba(Path.GetDirectoryName(carpeta)!);

    /// <summary>La misma Api con <c>Optica:Marca:Directorio</c> en la carpeta de la prueba; el log sigue en la fábrica.</summary>
    private static WebApplicationFactory<Program> ConMarca(FabricaApi fabrica, string carpeta) =>
        fabrica.WithWebHostBuilder(constructor => constructor.UseSetting("Optica:Marca:Directorio", carpeta));

    private static HttpClient CrearCliente(WebApplicationFactory<Program> fabrica) =>
        fabrica.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
            AllowAutoRedirect = false,
        });

    private static void AfirmarSecuencia(IReadOnlyList<string> palabras, params string[] secuencia)
    {
        for (var inicio = 0; inicio + secuencia.Length <= palabras.Count; inicio++)
        {
            if (secuencia.Select((s, i) => palabras[inicio + i] == s).All(coincide => coincide))
            {
                return;
            }
        }

        Assert.Fail($"No aparece la secuencia: {string.Join(" | ", secuencia)}");
    }

    private static bool EsColor(IColor color, string hex)
    {
        var (r, g, b) = color.ToRGBValues();
        var esperado = Convert.FromHexString(hex.AsSpan(1));
        return Cerca(r, esperado[0]) && Cerca(g, esperado[1]) && Cerca(b, esperado[2]);

        static bool Cerca(double valor, byte componente) => Math.Abs((valor * 255) - componente) < 1.5;
    }

    // Ayudas.

    private const string RutaBusqueda = Ruta + "/busqueda";

    private static readonly string[] CamposBusqueda = ["presupuestos", "pagina", "tamanoPagina", "total"];

    private static readonly string[] CamposListado = ["numero", "fecha", "apellido", "nombre", "dni", "estado", "total"];

    private static Task<HttpResponseMessage> PostBusquedaAsync(HttpClient cliente, string cookie, string cuerpo) =>
        Http.PostJsonAsync(cliente, RutaBusqueda, cuerpo, cookie);

    /// <summary>Da de alta un presupuesto con ese cliente ficticio y una línea, y devuelve su número.</summary>
    private static async Task<long> AltaDeAsync(
        HttpClient cliente, string cookie, string apellido, string nombre, string dni, long codigo)
    {
        using var respuesta = await PostAsync(cliente, cookie, Cuerpo(apellido, nombre, dni, null, null, null, codigo));
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await Http.JsonAsync(respuesta)).GetProperty("numero").GetInt64();
    }

    /// <summary>Agrega al objeto JSON una propiedad que se ignora, hasta que ocupe <paramref name="bytes"/> en UTF-8.</summary>
    private static string ConRelleno(string cuerpo, int bytes)
    {
        var sinRelleno = cuerpo[..^1] + ",\"relleno\":\"\"}";
        var faltan = bytes - Encoding.UTF8.GetByteCount(sinRelleno);
        Assert.True(faltan >= 0, "El cuerpo sin relleno ya es más largo que el pedido.");
        var resultado = cuerpo[..^1] + ",\"relleno\":\"" + new string('a', faltan) + "\"}";
        Assert.Equal(bytes, Encoding.UTF8.GetByteCount(resultado));
        return resultado;
    }

    /// <summary>Texto plano, ningún tipo, y ningún tipo ni cuerpo: 415 sin cuerpo.</summary>
    private static async Task AfirmarSinJsonAsync(HttpClient cliente, string cookie, string ruta, string cuerpo)
    {
        foreach (var contenido in new HttpContent?[]
        {
            new StringContent(cuerpo, Encoding.UTF8, "text/plain"),
            new Http.ContenidoSinLargo(cuerpo, null),
            null,
        })
        {
            var respuesta = await Http.EnviarAsync(cliente, HttpMethod.Put, ruta, contenido, cookie);

            Assert.Equal(HttpStatusCode.UnsupportedMediaType, respuesta.StatusCode);
            Assert.Equal(string.Empty, await respuesta.Content.ReadAsStringAsync());
        }
    }

    /// <summary>Con <c>Content-Length</c> y en partes (sin él): 413, sin datos del pedido en la respuesta.</summary>
    private static async Task AfirmarDemasiadoGrandeAsync(HttpClient cliente, string cookie, string ruta, string cuerpo)
    {
        foreach (var contenido in new HttpContent[]
        {
            new StringContent(cuerpo, Encoding.UTF8, "application/json"),
            new Http.ContenidoSinLargo(cuerpo, "application/json"),
        })
        {
            var respuesta = await Http.EnviarAsync(cliente, HttpMethod.Put, ruta, contenido, cookie);

            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, respuesta.StatusCode);
            var texto = await respuesta.Content.ReadAsStringAsync();
            Assert.DoesNotContain("00.718.294", texto, StringComparison.Ordinal);
            Assert.DoesNotContain("00718294", texto, StringComparison.Ordinal);
        }
    }

    private static StringContent Json(string cuerpo) => new(cuerpo, Encoding.UTF8, "application/json");

    /// <summary>Un cliente ficticio válido (DNI 00.718.294, correo de example.com) como JSON.</summary>
    private static string ClienteJson(string? domicilio = null) =>
        JsonSerializer.Serialize(new
        {
            apellido = "Ficticiez",
            nombre = "Prueboncia",
            dni = "00.718.294",
            domicilio,
            email = "prueba@example.com",
            telefono = (string?)null,
        });

    private static string LineaJson(long codigo, int? orden = null, int cantidad = 1, decimal descuento = 0m) =>
        JsonSerializer.Serialize(new { orden, codigoArticulo = codigo, cantidad, porcentajeDescuento = descuento });

    private static string CuerpoModificacion(long? version, string cliente, params string[] lineas) =>
        "{" + (version is { } valor ? "\"version\":" + valor.ToString(CultureInfo.InvariantCulture) + "," : string.Empty)
        + "\"cliente\":" + cliente + ",\"lineas\":[" + string.Join(",", lineas) + "]}";

    private static string CuerpoEstado(string? estado, long? version) => JsonSerializer.Serialize(new { estado, version });

    private static Task<HttpResponseMessage> PutAsync(HttpClient cliente, string cookie, long numero, string cuerpo) =>
        Http.EnviarAsync(cliente, HttpMethod.Put, $"{Ruta}/{numero}", Json(cuerpo), cookie);

    private static Task<HttpResponseMessage> PutEstadoAsync(HttpClient cliente, string cookie, long numero, string cuerpo) =>
        Http.EnviarAsync(cliente, HttpMethod.Put, $"{Ruta}/{numero}/estado", Json(cuerpo), cookie);

    /// <summary>Da de alta un presupuesto con el cliente ficticio y una línea por código, y devuelve su número.</summary>
    private static async Task<long> AltaAsync(HttpClient cliente, string cookie, params long[] codigos)
    {
        var respuesta = await PostAsync(
            cliente, cookie, Cuerpo("Ficticiez", "Prueboncia", "00.718.294", null, "prueba@example.com", null, codigos));
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await Http.JsonAsync(respuesta)).GetProperty("numero").GetInt64();
    }

    private static async Task DesactivarArticuloAsync(HttpClient cliente, string cookie, long codigo)
    {
        var respuesta = await Http.EnviarAsync(
            cliente, HttpMethod.Put, $"{RutaArticulos}/{codigo}/estado", Json("{\"activo\":false}"), cookie);
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
    }

    /// <summary>409 con ProblemDetails: <c>title</c> fijo, el texto del caso de uso y el <c>motivo</c> (D-05).</summary>
    private static async Task AfirmarConflictoAsync(HttpResponseMessage respuesta, string motivo, string detalle)
    {
        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        var json = await Http.JsonAsync(respuesta);
        Assert.Equal(409, json.GetProperty("status").GetInt32());
        Assert.Equal(TituloConflicto, json.GetProperty("title").GetString());
        Assert.Equal(detalle, json.GetProperty("detail").GetString());
        Assert.Equal(motivo, json.GetProperty("motivo").GetString());
        Assert.False(json.TryGetProperty("errors", out _));
    }

    private static string Cuerpo(
        string? apellido, string? nombre, string? dni, string? domicilio, string? email, string? telefono,
        params long[] codigos) =>
        JsonSerializer.Serialize(new
        {
            cliente = new { apellido, nombre, dni, domicilio, email, telefono },
            lineas = codigos.Select(codigo => new { codigoArticulo = codigo, cantidad = 1, porcentajeDescuento = 0 })
                .ToArray(),
        });

    /// <summary>Un cliente ficticio válido (DNI fuera del rango que se emite) y las líneas JSON tal cual.</summary>
    private static string CuerpoConLineas(params string[] lineas) =>
        "{\"cliente\":{\"apellido\":\"Ficticiez\",\"nombre\":\"Prueboncia\",\"dni\":\"00718294\"},\"lineas\":["
        + string.Join(",", lineas) + "]}";

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

    /// <summary>Una línea con Cantidad 1, Descuento 0 y los tres precios iguales al de venta del artículo.</summary>
    private static void AfirmarLinea(JsonElement linea, long codigo, string descripcion, decimal precio) =>
        AfirmarLinea(linea, codigo, descripcion, precio, 1, 0m, precio, precio);

    /// <summary>Como la de cuatro datos, y además el <c>orden</c> de la línea (D-10 de FEAT-003c).</summary>
    private static void AfirmarLinea(JsonElement linea, int orden, long codigo, string descripcion, decimal precio) =>
        AfirmarLinea(linea, orden, codigo, descripcion, precio, 1, 0m, precio, precio);

    private static void AfirmarLinea(
        JsonElement linea, int orden, long codigo, string descripcion, decimal precio, int cantidad, decimal descuento,
        decimal precioConDescuento, decimal precioFinal)
    {
        Assert.Equal(orden, linea.GetProperty("orden").GetInt32());
        AfirmarLinea(linea, codigo, descripcion, precio, cantidad, descuento, precioConDescuento, precioFinal);
    }

    private static void AfirmarLinea(
        JsonElement linea, long codigo, string descripcion, decimal precio, int cantidad, decimal descuento,
        decimal precioConDescuento, decimal precioFinal)
    {
        Assert.Equal(CamposLinea, Nombres(linea));
        Assert.Equal(codigo, linea.GetProperty("codigoArticulo").GetInt64());
        Assert.Equal(descripcion, linea.GetProperty("descripcion").GetString());
        Assert.Equal(precio, linea.GetProperty("precioUnitario").GetDecimal());
        Assert.Equal(cantidad, linea.GetProperty("cantidad").GetInt32());
        Assert.Equal(descuento, linea.GetProperty("porcentajeDescuento").GetDecimal());
        Assert.Equal(precioConDescuento, linea.GetProperty("precioConDescuento").GetDecimal());
        Assert.Equal(precioFinal, linea.GetProperty("precioFinal").GetDecimal());
    }

    /// <summary>R-04: el 400 de un cuerpo ilegible no trae el mensaje del lector, la ruta JSON ni los valores dados.</summary>
    private static void AfirmarSinDetallesDelLector(string texto, params string[] valores)
    {
        Assert.DoesNotContain("JSON", texto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LineNumber", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("System.", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("$.", texto, StringComparison.Ordinal);
        foreach (var valor in valores)
        {
            Assert.DoesNotContain(valor, texto, StringComparison.Ordinal);
        }
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
