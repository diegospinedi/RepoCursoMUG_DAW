using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Optica.Api.Tests.Catalogo;
using Optica.Infrastructure.Persistencia;
using Optica.Pruebas.Compartido;
using Xunit.Abstractions;

namespace Optica.Api.Tests.Presupuestos;

/// <summary>
/// NFR-03 y AC-03 de FEAT-003d con 10.000 presupuestos sembrados (9.000 en Borrador y 1.000 en Final, D-01): el alta,
/// la modificación de un Borrador y el paso a Final dan las mismas respuestas que en FEAT-003a, b y c; un Final sigue
/// rechazando cambios; y dos altas simultáneas con encuentro forzado reciben 10.001 y 10.002. Corren en la colección
/// <see cref="ColeccionVolumen"/>, sin paralelismo dentro del proyecto.
/// </summary>
[Collection(ColeccionVolumen.Nombre)]
public sealed class VolumenPresupuestosTests(ITestOutputHelper salida)
{
    private const string Ruta = SesionesConEncuentro.Ruta;
    private const string TituloConflicto = "No se pudo grabar el presupuesto.";
    private const string TextoFinal = "El presupuesto está en Final y no se puede modificar.";

    private static readonly string[] CamposRespuesta =
        ["numero", "fecha", "estado", "cliente", "lineas", "total", "version"];

    private static readonly string[] CamposCliente = ["apellido", "nombre", "dni", "domicilio", "email", "telefono"];

    private static readonly string[] CamposLinea =
    [
        "codigoArticulo", "descripcion", "precioUnitario", "cantidad", "porcentajeDescuento", "precioConDescuento",
        "precioFinal", "orden",
    ];

    private static readonly string FechaSembrada =
        SiembraPresupuestos.FechaSembrada.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// AC-03 (alta): con 10.000 presupuestos, el alta recibe el 10.001 con el cuerpo de FEAT-003a y FEAT-003b: Borrador,
    /// versión 0, la fecha del reloj y la línea con la Cantidad y el Descuento pedidos (1.815 al 50 % por 5 da 907,50 y
    /// 4.537,50); el GET devuelve lo mismo.
    /// </summary>
    [Fact]
    public async Task Volumen_Alta_Numero10001MismoResultado()
    {
        using var fabrica = new FabricaApi();
        using var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        Sembrar(fabrica);
        var codigo = CodigoDe(fabrica, "VOL-00001");

        using var respuesta = await Http.PostJsonAsync(
            cliente, Ruta, SesionesConEncuentro.Cuerpo("Ficticiez", "99999930", 5, 50m, codigo), cookie);

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        Assert.Equal("application/json", respuesta.Content.Headers.ContentType?.MediaType);
        var ubicacion = Assert.IsType<Uri>(respuesta.Headers.Location);
        Assert.Equal($"{Ruta}/10001", ubicacion.IsAbsoluteUri ? ubicacion.AbsolutePath : ubicacion.OriginalString);
        var json = await Http.JsonAsync(respuesta);
        Assert.Equal(CamposRespuesta, Nombres(json));
        Assert.Equal(SiembraPresupuestos.Volumen + 1L, json.GetProperty("numero").GetInt64());
        Assert.Equal(
            DateOnly.FromDateTime(fabrica.Reloj.GetLocalNow().DateTime).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            json.GetProperty("fecha").GetString());
        Assert.Equal("borrador", json.GetProperty("estado").GetString());
        Assert.Equal(0, json.GetProperty("version").GetInt64());
        AfirmarCliente(json, "Ficticiez", "99999930", null);
        var linea = Assert.Single(json.GetProperty("lineas").EnumerateArray());
        AfirmarLinea(linea, 1, codigo, "Armazón acetato 00001", 1815m, 5, 50m, 907.50m, 4537.50m);
        Assert.Equal(4537.50m, json.GetProperty("total").GetDecimal());
        Assert.Equal(json.GetRawText(), (await LeerAsync(cliente, cookie, SiembraPresupuestos.Volumen + 1L)).GetRawText());
        Assert.Equal(SiembraPresupuestos.Volumen + 1L, Contar(fabrica, "SELECT count(*) FROM Presupuesto"));
    }

    /// <summary>
    /// AC-03 (modificación): con 10.000 presupuestos, un Borrador sembrado se modifica como en FEAT-003c: 200 con el
    /// mismo número y la misma fecha, Borrador, versión 1, el cliente y las líneas nuevas renumeradas; el GET lo muestra.
    /// </summary>
    [Fact]
    public async Task Volumen_ModificarBorrador_MismoResultado()
    {
        using var fabrica = new FabricaApi();
        using var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        Sembrar(fabrica);
        var numero = UltimoBorrador();
        var codigo = CodigoDe(fabrica, "VOL-00002");
        var antes = await LeerAsync(cliente, cookie, numero);
        Assert.Equal("borrador", antes.GetProperty("estado").GetString());
        Assert.Equal(0, antes.GetProperty("version").GetInt64());

        using var respuesta = await PutAsync(
            cliente, cookie, $"{Ruta}/{numero}",
            CuerpoModificacion(0, ClienteJson("Calle Ficticia 123"), LineaJson(codigo, cantidad: 2)));

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("application/json", respuesta.Content.Headers.ContentType?.MediaType);
        var json = await Http.JsonAsync(respuesta);
        Assert.Equal(CamposRespuesta, Nombres(json));
        Assert.Equal(numero, json.GetProperty("numero").GetInt64());
        Assert.Equal(FechaSembrada, json.GetProperty("fecha").GetString());
        Assert.Equal("borrador", json.GetProperty("estado").GetString());
        Assert.Equal(1, json.GetProperty("version").GetInt64());
        AfirmarCliente(json, "Ficticiez", "99999999", "Calle Ficticia 123");
        var linea = Assert.Single(json.GetProperty("lineas").EnumerateArray());
        var precio = PrecioVentaDe(fabrica, codigo);
        AfirmarLinea(linea, 1, codigo, "Cristal orgánico 00002", precio, 2, 0m, precio, precio * 2);
        Assert.Equal(json.GetRawText(), (await LeerAsync(cliente, cookie, numero)).GetRawText());
        Assert.Equal((long)SiembraPresupuestos.Volumen, Contar(fabrica, "SELECT count(*) FROM Presupuesto"));
    }

    /// <summary>
    /// AC-03 (paso a Final): con 10.000 presupuestos, un Borrador sembrado pasa a Final como en FEAT-003c: 200 con estado
    /// final y versión 1, la misma fecha y las mismas líneas; el GET lo muestra.
    /// </summary>
    [Fact]
    public async Task Volumen_PasarAFinal_MismoResultado()
    {
        using var fabrica = new FabricaApi();
        using var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        Sembrar(fabrica);
        var numero = UltimoBorrador();
        var antes = await LeerAsync(cliente, cookie, numero);

        using var respuesta = await PutAsync(cliente, cookie, $"{Ruta}/{numero}/estado", CuerpoEstado("final", 0));

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var json = await Http.JsonAsync(respuesta);
        Assert.Equal(CamposRespuesta, Nombres(json));
        Assert.Equal(numero, json.GetProperty("numero").GetInt64());
        Assert.Equal("final", json.GetProperty("estado").GetString());
        Assert.Equal(1, json.GetProperty("version").GetInt64());
        Assert.Equal(antes.GetProperty("fecha").GetString(), json.GetProperty("fecha").GetString());
        Assert.Equal(antes.GetProperty("cliente").GetRawText(), json.GetProperty("cliente").GetRawText());
        Assert.Equal(antes.GetProperty("lineas").GetRawText(), json.GetProperty("lineas").GetRawText());
        Assert.Equal(antes.GetProperty("total").GetDecimal(), json.GetProperty("total").GetDecimal());
        Assert.Equal(json.GetRawText(), (await LeerAsync(cliente, cookie, numero)).GetRawText());
        Assert.Equal(
            SiembraPresupuestos.FinalesDelVolumen + 1L, Contar(fabrica, "SELECT count(*) FROM Presupuesto WHERE Estado = 'final'"));
    }

    /// <summary>
    /// Sad path: con 10.000 presupuestos, un Final sembrado rechaza la modificación y la vuelta a Borrador con 409
    /// <c>final</c>, como en FEAT-003c, sin escribir: el GET y las líneas quedan iguales.
    /// </summary>
    [Fact]
    public async Task Volumen_ModificarFinal_409Final()
    {
        using var fabrica = new FabricaApi();
        using var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        Sembrar(fabrica);
        var numero = UltimoFinal();
        var codigo = CodigoDe(fabrica, "VOL-00003");
        var antes = await LeerAsync(cliente, cookie, numero);
        Assert.Equal("final", antes.GetProperty("estado").GetString());
        Assert.Equal(1, antes.GetProperty("version").GetInt64());
        var lineasAntes = Contar(fabrica, "SELECT count(*) FROM LineaPresupuesto");

        using var modificacion = await PutAsync(
            cliente, cookie, $"{Ruta}/{numero}",
            CuerpoModificacion(1, ClienteJson("Calle Ficticia 123"), LineaJson(codigo)));
        await AfirmarConflictoFinalAsync(modificacion);
        using var aBorrador = await PutAsync(cliente, cookie, $"{Ruta}/{numero}/estado", CuerpoEstado("borrador", 1));
        await AfirmarConflictoFinalAsync(aBorrador);

        Assert.Equal(antes.GetRawText(), (await LeerAsync(cliente, cookie, numero)).GetRawText());
        Assert.Equal(lineasAntes, Contar(fabrica, "SELECT count(*) FROM LineaPresupuesto"));
        Assert.Equal(
            (long)SiembraPresupuestos.FinalesDelVolumen, Contar(fabrica, "SELECT count(*) FROM Presupuesto WHERE Estado = 'final'"));
    }

    /// <summary>
    /// AC-03 con AC-01: con 10.000 presupuestos, dos sesiones graban a la vez con encuentro forzado y reciben 10.001 y
    /// 10.002.
    /// </summary>
    [Fact]
    public async Task Volumen_DosAltasSimultaneas_10001Y10002()
    {
        using var sesiones = new SesionesConEncuentro();
        await sesiones.IngresarAsync();
        Sembrar(sesiones.Fabrica);
        var codigo = CodigoDe(sesiones.Fabrica, "VOL-00001");

        var respuestas = await sesiones.DosAltasConEncuentroAsync(
            SesionesConEncuentro.Cuerpo("Ficticiez", "99999931", 1, 0m, codigo),
            SesionesConEncuentro.Cuerpo("Quimerez", "99999932", 2, 0m, codigo));
        using var primera = respuestas.Primera;
        using var segunda = respuestas.Segunda;

        Assert.Equal(HttpStatusCode.Created, primera.StatusCode);
        Assert.Equal(HttpStatusCode.Created, segunda.StatusCode);
        Assert.Equal(SiembraPresupuestos.Volumen + 1L, (await Http.JsonAsync(primera)).GetProperty("numero").GetInt64());
        Assert.Equal(SiembraPresupuestos.Volumen + 2L, (await Http.JsonAsync(segunda)).GetProperty("numero").GetInt64());
        Assert.Equal(
            "Ficticiez",
            (await sesiones.LeerAsync(SiembraPresupuestos.Volumen + 1L, sesiones.CookieA))
                .GetProperty("cliente").GetProperty("apellido").GetString());
        Assert.Equal(
            "Quimerez",
            (await sesiones.LeerAsync(SiembraPresupuestos.Volumen + 2L, sesiones.CookieB))
                .GetProperty("cliente").GetProperty("apellido").GetString());
    }

    /// <summary>
    /// AC-16 de FEAT-006 (NFR-01, D-03): con 10.000 presupuestos sembrados, 20 búsquedas por Apellido por la Api, medidas
    /// como la respuesta HTTP completa: 10 de "Ficticio", que coincide con los 10.000 (el peor caso; la primera y la
    /// última página entre ellas), y 10 de textos que no coinciden con ninguno. Al menos 19 de las 20 responden en menos
    /// de 2 s. Cada respuesta confirma con el total y las filas que la búsqueda recorrió lo que dice recorrer.
    /// </summary>
    [Fact]
    [Trait("Categoria", "Volumen")]
    public async Task Volumen_20BusquedasPorApellido_19BajoDosSegundos()
    {
        const int Paginas = SiembraPresupuestos.Volumen / 50;
        using var fabrica = new FabricaApi();
        using var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        Sembrar(fabrica);

        // Las páginas de "Ficticio": la primera, la última y ocho intermedias.
        var paginas = new[] { 1, Paginas, 2, 50, 100, 150, Paginas - 1, 3, 75, 125 };
        var busquedas = paginas
            .Select(pagina => (Apellido: "Ficticio", Pagina: pagina, Total: SiembraPresupuestos.Volumen, Filas: 50))
            .Concat(Enumerable.Range(1, 10).Select(i => (
                Apellido: string.Create(CultureInfo.InvariantCulture, $"Zqxnoexiste{i:D2}"), Pagina: 1, Total: 0, Filas: 0)))
            .ToList();
        Assert.Equal(20, busquedas.Count);
        Assert.Contains(busquedas, b => b.Pagina == 1 && b.Total > 0);
        Assert.Contains(busquedas, b => b.Pagina == Paginas);

        // Calentamiento, sin medir ni contar: "Ficticio" página 1, un texto sin coincidencias y la página 200.
        foreach (var (apellido, pagina) in new[] { ("Ficticio", 1), ("Zqxcalentamiento", 1), ("Ficticio", Paginas) })
        {
            using var previa = await Http.PostJsonAsync(
                cliente, RutaBusqueda, JsonSerializer.Serialize(new { apellido, pagina }), cookie);
            Assert.Equal(HttpStatusCode.OK, previa.StatusCode);
        }

        var tabla = new StringBuilder("Apellido | Página | Tiempo (ms)\n");
        var bajoDosSegundos = 0;
        foreach (var (apellido, pagina, total, filas) in busquedas)
        {
            var cuerpo = JsonSerializer.Serialize(new { apellido, pagina });
            var reloj = Stopwatch.StartNew();
            using var respuesta = await Http.PostJsonAsync(cliente, RutaBusqueda, cuerpo, cookie);
            var texto = await respuesta.Content.ReadAsStringAsync();
            reloj.Stop();

            Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
            using var documento = JsonDocument.Parse(texto);
            var json = documento.RootElement;
            Assert.Equal(total, json.GetProperty("total").GetInt32());
            var presupuestos = json.GetProperty("presupuestos");
            Assert.Equal(filas, presupuestos.GetArrayLength());
            if (filas > 0)
            {
                // Por número de mayor a menor: la página p empieza en 10.000 − 50·(p − 1).
                Assert.Equal(
                    SiembraPresupuestos.Volumen - (50L * (pagina - 1)), presupuestos[0].GetProperty("numero").GetInt64());
            }

            if (reloj.Elapsed < LimiteBusqueda)
            {
                bajoDosSegundos++;
            }

            tabla.Append(CultureInfo.InvariantCulture, $"{apellido} | {pagina} | {reloj.Elapsed.TotalMilliseconds:F1}\n");
        }

        salida.WriteLine(tabla.ToString());
        Assert.True(bajoDosSegundos >= 19, $"Solo {bajoDosSegundos} de 20 búsquedas respondieron en menos de 2 s.\n{tabla}");
    }

    private const string RutaBusqueda = Ruta + "/busqueda";

    /// <summary>El límite de AC-16: la grilla en menos de 2 segundos.</summary>
    private static readonly TimeSpan LimiteBusqueda = TimeSpan.FromSeconds(2);

    /// <summary>Siembra los 10.000 presupuestos (1.000 Finales) y comprueba los conteos; anota la duración.</summary>
    private void Sembrar(FabricaApi fabrica)
    {
        var reloj = Stopwatch.StartNew();
        SiembraPresupuestos.Sembrar(CadenaDe(fabrica));
        reloj.Stop();
        salida.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Siembra de {SiembraPresupuestos.Volumen} presupuestos: {reloj.Elapsed.TotalMilliseconds:F0} ms."));
        Assert.Equal((long)SiembraPresupuestos.Volumen, Contar(fabrica, "SELECT count(*) FROM Presupuesto"));
        Assert.Equal(
            (long)SiembraPresupuestos.FinalesDelVolumen,
            Contar(fabrica, "SELECT count(*) FROM Presupuesto WHERE Estado = 'final'"));
    }

    private static long UltimoBorrador() =>
        Enumerable.Range(1, SiembraPresupuestos.Volumen).Select(n => (long)n).Last(n => !EsFinal(n));

    private static long UltimoFinal() =>
        Enumerable.Range(1, SiembraPresupuestos.Volumen).Select(n => (long)n).Last(EsFinal);

    private static bool EsFinal(long numero) =>
        SiembraPresupuestos.EsFinal(numero, SiembraPresupuestos.Volumen, SiembraPresupuestos.FinalesDelVolumen);

    private static async Task AfirmarConflictoFinalAsync(HttpResponseMessage respuesta)
    {
        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        var json = await Http.JsonAsync(respuesta);
        Assert.Equal(409, json.GetProperty("status").GetInt32());
        Assert.Equal(TituloConflicto, json.GetProperty("title").GetString());
        Assert.Equal(TextoFinal, json.GetProperty("detail").GetString());
        Assert.Equal("final", json.GetProperty("motivo").GetString());
        Assert.False(json.TryGetProperty("errors", out _));
    }

    private static void AfirmarCliente(JsonElement json, string apellido, string dni, string? domicilio)
    {
        var datos = json.GetProperty("cliente");
        Assert.Equal(CamposCliente, Nombres(datos));
        Assert.Equal(apellido, datos.GetProperty("apellido").GetString());
        Assert.Equal("Prueboncia", datos.GetProperty("nombre").GetString());
        Assert.Equal(dni, datos.GetProperty("dni").GetString());
        Assert.Equal(domicilio, Texto(datos.GetProperty("domicilio")));
    }

    private static void AfirmarLinea(
        JsonElement linea, int orden, long codigo, string descripcion, decimal precio, int cantidad, decimal descuento,
        decimal precioConDescuento, decimal precioFinal)
    {
        Assert.Equal(CamposLinea, Nombres(linea));
        Assert.Equal(orden, linea.GetProperty("orden").GetInt32());
        Assert.Equal(codigo, linea.GetProperty("codigoArticulo").GetInt64());
        Assert.Equal(descripcion, linea.GetProperty("descripcion").GetString());
        Assert.Equal(precio, linea.GetProperty("precioUnitario").GetDecimal());
        Assert.Equal(cantidad, linea.GetProperty("cantidad").GetInt32());
        Assert.Equal(descuento, linea.GetProperty("porcentajeDescuento").GetDecimal());
        Assert.Equal(precioConDescuento, linea.GetProperty("precioConDescuento").GetDecimal());
        Assert.Equal(precioFinal, linea.GetProperty("precioFinal").GetDecimal());
    }

    /// <summary>Un cliente ficticio válido (DNI 99.999.999, correo de example.com) como JSON.</summary>
    private static string ClienteJson(string? domicilio) =>
        JsonSerializer.Serialize(new
        {
            apellido = "Ficticiez",
            nombre = "Prueboncia",
            dni = "99.999.999",
            domicilio,
            email = "prueba@example.com",
            telefono = (string?)null,
        });

    private static string LineaJson(long codigo, int cantidad = 1) =>
        JsonSerializer.Serialize(new { codigoArticulo = codigo, cantidad, porcentajeDescuento = 0m });

    private static string CuerpoModificacion(long version, string cliente, params string[] lineas) =>
        "{\"version\":" + version.ToString(CultureInfo.InvariantCulture) + ",\"cliente\":" + cliente + ",\"lineas\":["
        + string.Join(",", lineas) + "]}";

    private static string CuerpoEstado(string estado, long version) => JsonSerializer.Serialize(new { estado, version });

    private static Task<HttpResponseMessage> PutAsync(HttpClient cliente, string cookie, string ruta, string cuerpo) =>
        Http.EnviarAsync(cliente, HttpMethod.Put, ruta, new StringContent(cuerpo, Encoding.UTF8, "application/json"), cookie);

    private static async Task<JsonElement> LeerAsync(HttpClient cliente, string cookie, long numero)
    {
        using var respuesta = await Http.GetAsync(cliente, $"{Ruta}/{numero}", cookie);
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        return (await Http.JsonAsync(respuesta)).Clone();
    }

    private static string CadenaDe(FabricaApi fabrica) =>
        fabrica.Services.GetRequiredService<FabricaConexion>().CadenaConexion;

    /// <summary>El código autonumérico de un artículo sembrado, leído de la base (no se supone).</summary>
    private static long CodigoDe(FabricaApi fabrica, string codigoProveedor) =>
        Escalar<long>(fabrica, "SELECT Id FROM Articulo WHERE CodigoProveedor = $valor", codigoProveedor);

    private static decimal PrecioVentaDe(FabricaApi fabrica, long codigo) =>
        Escalar<long>(fabrica, "SELECT PrecioVentaCentavos FROM Articulo WHERE Id = $valor", codigo) / 100m;

    private static long Contar(FabricaApi fabrica, string consulta) => Escalar<long>(fabrica, consulta, null);

    private static T Escalar<T>(FabricaApi fabrica, string consulta, object? valor)
    {
        using var conexion = new SqliteConnection(CadenaDe(fabrica));
        conexion.Open();
        using var comando = conexion.CreateCommand();
        comando.CommandText = consulta;
        if (valor is not null)
        {
            comando.Parameters.AddWithValue("$valor", valor);
        }

        return (T)(comando.ExecuteScalar() ?? throw new InvalidOperationException("La consulta no devolvió ninguna fila."));
    }

    private static string? Texto(JsonElement valor) =>
        valor.ValueKind == JsonValueKind.Null ? null : valor.GetString();

    private static string[] Nombres(JsonElement json) =>
        json.EnumerateObject().Select(propiedad => propiedad.Name).ToArray();
}
