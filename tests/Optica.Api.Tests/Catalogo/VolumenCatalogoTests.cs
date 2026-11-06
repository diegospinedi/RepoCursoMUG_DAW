using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Optica.Application.Catalogo;
using Optica.Infrastructure.Persistencia;
using Optica.Pruebas.Compartido;
using Xunit.Abstractions;

namespace Optica.Api.Tests.Catalogo;

/// <summary>
/// Las pruebas de volumen corren solas: ninguna otra clase de este proyecto comparte el procesador con la medición de
/// D-13. Entre proyectos, <c>dotnet test Optica.sln</c> igual corre en paralelo; ese riesgo está aceptado (D-21).
/// </summary>
[CollectionDefinition(Nombre, DisableParallelization = true)]
public sealed class ColeccionVolumen
{
    public const string Nombre = "Volumen";
}

/// <summary>
/// NFR-01 con 10.000 artículos sembrados (D-15): AC-05 por la Api, con los mismos resultados que en FEAT-002a y
/// FEAT-002b, y la medición de la búsqueda del listado de D-02 y D-13, aparte, con el Trait <c>Categoria=Volumen</c>.
/// </summary>
[Collection(ColeccionVolumen.Nombre)]
public sealed class VolumenCatalogoTests(ITestOutputHelper salida)
{
    private const string Ruta = "/api/articulos";
    private const int Calentamiento = 5;
    private const int MedicionesPorEscenario = 20;
    private static readonly TimeSpan LimiteP95 = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Un dato que el dominio rechaza, o una clave repetida, hace fallar la siembra con su propio mensaje y sin insertar
    /// nada: la prueba se detiene ahí, antes de medir.
    /// </summary>
    [Theory]
    [InlineData("descripcion")]
    [InlineData("costo")]
    [InlineData("clave")]
    public async Task Siembra_DatoInvalido_FallaAntesDeMedir(string defecto)
    {
        using var fabrica = new FabricaApi();
        using var cliente = fabrica.CrearClienteSinCookies();
        await Http.DefinirAsync(cliente);
        var articulos = SiembraCatalogo.Generar(20).ToList();
        articulos[7] = defecto switch
        {
            "descripcion" => articulos[7] with { Descripcion = "   " },
            "costo" => articulos[7] with { PrecioCosto = 100.001m },
            _ => articulos[7] with { CodigoProveedor = " vol-00003 " },
        };

        var error = Assert.Throws<InvalidOperationException>(
            () => SiembraCatalogo.Sembrar(CadenaDe(fabrica), articulos));

        Assert.StartsWith("Siembra del catálogo: el artículo 8 ", error.Message, StringComparison.Ordinal);
        Assert.EndsWith("No se insertó ningún artículo.", error.Message, StringComparison.Ordinal);
        Assert.Equal(0L, ContarArticulos(fabrica));
    }

    /// <summary>
    /// AC-05: con 10.000 artículos (1.000 inactivos), el alta, la modificación, la búsqueda por código, por código en el
    /// proveedor y por descripción sin tildes, y la desactivación dan los mismos resultados que en los AC de FEAT-002a y
    /// FEAT-002b.
    /// </summary>
    [Fact]
    public async Task Volumen10000_AltaModificacionBusquedaYDesactivacion_MismosResultados()
    {
        using var fabrica = new FabricaApi();
        using var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        SiembraCatalogo.Sembrar(CadenaDe(fabrica), SiembraCatalogo.Generar());
        Assert.Equal(SiembraCatalogo.Volumen, ContarArticulos(fabrica));

        // Alta (AC-01 y AC-07 de FEAT-002a): 1210 con margen 50 da 1815, con el código siguiente.
        using var alta = await Http.PostJsonAsync(cliente, Ruta, Cuerpo("ABC-1", "Armazón", 1210m, 50m), cookie);
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);
        var nuevo = await JsonAsync(alta);
        var codigo = nuevo.GetProperty("codigo").GetInt64();
        Assert.Equal(SiembraCatalogo.Volumen + 1L, codigo);
        Assert.Equal(1815m, nuevo.GetProperty("precioVenta").GetDecimal());

        // Código repetido con otra forma (AC-02 de FEAT-002a): rechazado en el campo, sin grabar.
        using var repetido = await Http.PostJsonAsync(cliente, Ruta, Cuerpo(" vol-00042 ", "Otro", 100m, 10m), cookie);
        Assert.Equal(HttpStatusCode.BadRequest, repetido.StatusCode);
        Assert.Equal(
            [ValidacionArticulo.AccionCodigoDuplicado],
            (await JsonAsync(repetido)).GetProperty("errors").GetProperty(ValidacionArticulo.CampoCodigoProveedor)
                .EnumerateArray().Select(valor => valor.GetString()!).ToArray());
        Assert.Equal(SiembraCatalogo.Volumen + 1L, ContarArticulos(fabrica));

        // Modificación (AC-02 y AC-03 de FEAT-002b): el margen 50 → 60 da 1936; el costo de un sembrado 1210 → 2420 con
        // margen 50 da 3630. La versión pasa a 1.
        using var margen = await PutAsync(
            cliente, $"{Ruta}/{codigo}", CuerpoModificacion("ABC-1", "Armazón", 1210m, 60m, 0), cookie);
        Assert.Equal(HttpStatusCode.OK, margen.StatusCode);
        var modificado = await JsonAsync(margen);
        Assert.Equal(1936m, modificado.GetProperty("precioVenta").GetDecimal());
        Assert.Equal(1, modificado.GetProperty("version").GetInt64());
        using var costo = await PutAsync(
            cliente, $"{Ruta}/2", CuerpoModificacion("VOL-00002", "Cristal orgánico 00002", 2420m, 50m, 0), cookie);
        Assert.Equal(HttpStatusCode.OK, costo.StatusCode);
        Assert.Equal(3630m, (await JsonAsync(costo)).GetProperty("precioVenta").GetDecimal());

        // Un sembrado inactivo no se modifica (FEAT-002b): 409 con motivo inactivo.
        using var inactivo = await PutAsync(
            cliente, $"{Ruta}/10", CuerpoModificacion("VOL-00010", "Cristal orgánico 00010", 500m, 10m, 0), cookie);
        Assert.Equal(HttpStatusCode.Conflict, inactivo.StatusCode);
        Assert.Equal("inactivo", (await JsonAsync(inactivo)).GetProperty("motivo").GetString());

        // Búsqueda por código autonumérico: solo el artículo nuevo.
        var porCodigo = await ListadoAsync(cliente, cookie, codigo.ToString(CultureInfo.InvariantCulture), 1);
        Assert.Equal(1, porCodigo.GetProperty("total").GetInt32());
        Assert.Equal([codigo], Codigos(porCodigo));

        // Búsqueda parcial por código en el proveedor, en minúsculas: VOL-00040 a VOL-00049, con el inactivo VOL-00040.
        var porProveedor = await ListadoAsync(cliente, cookie, "vol-0004", 1);
        Assert.Equal(10, porProveedor.GetProperty("total").GetInt32());
        Assert.Equal(
            Enumerable.Range(40, 10).Select(i => $"VOL-{i:D5}").ToHashSet(),
            Articulos(porProveedor).Select(a => a.GetProperty("codigoProveedor").GetString()!).ToHashSet());
        Assert.False(Articulos(porProveedor).Single(a => a.GetProperty("codigo").GetInt64() == 40)
            .GetProperty("activo").GetBoolean());

        // Descripción sin tildes (AC-04 de FEAT-002b): "armazon" encuentra los 2.500 "Armazón acetato" sembrados y el
        // nuevo "Armazón", ordenados por descripción y después por código, en páginas de 50; sin costo ni margen.
        var porDescripcion = await ListadoAsync(cliente, cookie, "armazon", 1);
        Assert.Equal(2501, porDescripcion.GetProperty("total").GetInt32());
        Assert.Equal(50, porDescripcion.GetProperty("tamanoPagina").GetInt32());
        Assert.Equal(
            [codigo, .. Enumerable.Range(0, 49).Select(i => (long)((4 * i) + 1))],
            Codigos(porDescripcion));
        Assert.Equal(
            ["Armazón", "Armazón acetato 00001", "Armazón acetato 00005"],
            Articulos(porDescripcion).Take(3).Select(a => a.GetProperty("descripcion").GetString()!).ToArray());
        Assert.DoesNotContain("precioCosto", porDescripcion.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("margen", porDescripcion.GetRawText(), StringComparison.OrdinalIgnoreCase);
        var ultimaArmazon = await ListadoAsync(cliente, cookie, "ARMAZÓN", 51);
        Assert.Equal([9997L], Codigos(ultimaArmazon));

        // Desactivación (AC-05 de FEAT-002b): el artículo queda inactivo con la versión 2 y los datos intactos.
        using var baja = await PutAsync(cliente, $"{Ruta}/{codigo}/estado", "{\"activo\":false}", cookie);
        Assert.Equal(HttpStatusCode.OK, baja.StatusCode);
        var desactivado = await JsonAsync(baja);
        Assert.False(desactivado.GetProperty("activo").GetBoolean());
        Assert.Equal(2, desactivado.GetProperty("version").GetInt64());
        Assert.Equal(1936m, desactivado.GetProperty("precioVenta").GetDecimal());
        var listadoBaja = await ListadoAsync(cliente, cookie, codigo.ToString(CultureInfo.InvariantCulture), 1);
        Assert.False(Assert.Single(Articulos(listadoBaja)).GetProperty("activo").GetBoolean());

        // Sin texto: todo el catálogo; la última página (201) tiene el artículo restante.
        var todo = await ListadoAsync(cliente, cookie, null, 1);
        Assert.Equal(SiembraCatalogo.Volumen + 1, todo.GetProperty("total").GetInt32());
        Assert.Equal(50, Articulos(todo).Count);
        var ultima = await ListadoAsync(cliente, cookie, null, 201);
        Assert.Single(Articulos(ultima));
    }

    /// <summary>
    /// D-02 y D-13: con 10.000 artículos, después de 5 búsquedas de calentamiento, 20 búsquedas en cada uno de los 4
    /// escenarios; el p95 de cada escenario, medido como la respuesta HTTP completa de la Api, es menor a 1 s. Corre
    /// también en su propio paso del cierre de CODE (<c>--filter Categoria=Volumen</c>), que es la evidencia (D-21).
    /// </summary>
    [Fact]
    [Trait("Categoria", "Volumen")]
    public async Task Volumen10000_Busqueda_P95MenorA1s()
    {
        using var fabrica = new FabricaApi();
        using var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        SiembraCatalogo.Sembrar(CadenaDe(fabrica), SiembraCatalogo.Generar());

        // Escenario, ruta y total esperado: el total confirma que la búsqueda recorre lo que dice recorrer.
        (string Nombre, string Ruta, int Total, int Filas)[] escenarios =
        [
            ("Texto que no coincide con ninguno", $"{Ruta}?texto=zqx-sin-coincidencia", 0, 0),
            ("Texto que coincide con todos", $"{Ruta}?texto=vol-", SiembraCatalogo.Volumen, 50),
            ("Sin texto, página 1", Ruta, SiembraCatalogo.Volumen, 50),
            ("Sin texto, última página", $"{Ruta}?pagina=200", SiembraCatalogo.Volumen, 50),
        ];

        for (var i = 0; i < Calentamiento; i++)
        {
            await MedirAsync(cliente, cookie, escenarios[i % escenarios.Length].Ruta);
        }

        var tabla = new StringBuilder("Escenario | p50 (ms) | p95 (ms) | máximo (ms)\n");
        var muestras = new Dictionary<string, int>(StringComparer.Ordinal);
        var fallas = new List<string>();
        foreach (var (nombre, ruta, total, filas) in escenarios)
        {
            var tiempos = new List<TimeSpan>(MedicionesPorEscenario);
            for (var i = 0; i < MedicionesPorEscenario; i++)
            {
                var (tiempo, json) = await MedirAsync(cliente, cookie, ruta);
                Assert.Equal(total, json.GetProperty("total").GetInt32());
                Assert.Equal(filas, Articulos(json).Count);
                tiempos.Add(tiempo);
            }

            muestras[nombre] = tiempos.Count;
            tiempos.Sort();
            var p95 = Percentil(tiempos, 95);
            tabla.Append(CultureInfo.InvariantCulture,
                $"{nombre} | {Percentil(tiempos, 50).TotalMilliseconds:F1} | {p95.TotalMilliseconds:F1} | " +
                $"{tiempos[^1].TotalMilliseconds:F1}\n");
            if (p95 >= LimiteP95)
            {
                fallas.Add(nombre);
            }
        }

        salida.WriteLine(tabla.ToString());

        // Los 4 escenarios de D-13, por nombre, con 20 muestras cada uno: un escenario omitido no pasa por bueno.
        Assert.Equal(
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["Texto que no coincide con ninguno"] = 20,
                ["Texto que coincide con todos"] = 20,
                ["Sin texto, página 1"] = 20,
                ["Sin texto, última página"] = 20,
            },
            muestras);
        Assert.True(fallas.Count == 0, $"p95 de 1 s o más en: {string.Join(", ", fallas)}.\n{tabla}");
    }

    /// <summary>
    /// El percentil de la medición es por rango más cercano (el valor ordenado en la posición ⌈p·n/100⌉), no el promedio:
    /// con 19 búsquedas de 10 ms y una de 1 s el p95 es 10 ms, y el promedio, 59,5 ms.
    /// </summary>
    [Fact]
    public void Percentil_RangoMasCercano_NoEsElPromedio()
    {
        TimeSpan[] conPico = [.. Enumerable.Repeat(TimeSpan.FromMilliseconds(10), 19), TimeSpan.FromSeconds(1)];
        TimeSpan[] escalera = [.. Enumerable.Range(1, 20).Select(ms => TimeSpan.FromMilliseconds(ms))];

        Assert.Equal(TimeSpan.FromMilliseconds(10), Percentil(conPico, 95));
        Assert.Equal(TimeSpan.FromSeconds(1), Percentil(conPico, 100));
        Assert.Equal(TimeSpan.FromMilliseconds(19), Percentil(escalera, 95));
        Assert.Equal(TimeSpan.FromMilliseconds(10), Percentil(escalera, 50));
        Assert.Equal(TimeSpan.FromMilliseconds(1), Percentil(escalera, 1));
    }

    /// <summary>Percentil por rango más cercano sobre tiempos ya ordenados de menor a mayor.</summary>
    internal static TimeSpan Percentil(IReadOnlyList<TimeSpan> ordenados, int percentil)
    {
        ArgumentNullException.ThrowIfNull(ordenados);
        ArgumentOutOfRangeException.ThrowIfZero(ordenados.Count);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(percentil);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(percentil, 100);
        var rango = (int)Math.Ceiling(percentil / 100.0 * ordenados.Count);
        return ordenados[Math.Max(rango, 1) - 1];
    }

    /// <summary>La respuesta HTTP completa de la Api: desde el envío hasta leer el cuerpo entero.</summary>
    private static async Task<(TimeSpan Tiempo, JsonElement Json)> MedirAsync(HttpClient cliente, string cookie, string ruta)
    {
        var reloj = Stopwatch.StartNew();
        using var respuesta = await Http.GetAsync(cliente, ruta, cookie);
        var texto = await respuesta.Content.ReadAsStringAsync();
        reloj.Stop();
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        using var documento = JsonDocument.Parse(texto);
        return (reloj.Elapsed, documento.RootElement.Clone());
    }

    /// <summary>El cuerpo JSON de la respuesta, copiado para que el documento se pueda liberar.</summary>
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage respuesta)
    {
        using var documento = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
        return documento.RootElement.Clone();
    }

    private static string CadenaDe(FabricaApi fabrica) =>
        fabrica.Services.GetRequiredService<FabricaConexion>().CadenaConexion;

    private static long ContarArticulos(FabricaApi fabrica)
    {
        using var conexion = new SqliteConnection(CadenaDe(fabrica));
        conexion.Open();
        using var comando = conexion.CreateCommand();
        comando.CommandText = "SELECT count(*) FROM Articulo";
        return (long)comando.ExecuteScalar()!;
    }

    private static async Task<JsonElement> ListadoAsync(HttpClient cliente, string cookie, string? texto, int pagina)
    {
        var consulta = texto is null
            ? $"{Ruta}?pagina={pagina}"
            : $"{Ruta}?texto={Uri.EscapeDataString(texto)}&pagina={pagina}";
        using var respuesta = await Http.GetAsync(cliente, consulta, cookie);
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        return await JsonAsync(respuesta);
    }

    private static List<JsonElement> Articulos(JsonElement listado) =>
        [.. listado.GetProperty("articulos").EnumerateArray()];

    private static long[] Codigos(JsonElement listado) =>
        [.. Articulos(listado).Select(a => a.GetProperty("codigo").GetInt64())];

    private static string Cuerpo(string codigoProveedor, string descripcion, decimal precioCosto, decimal margenUtilidad) =>
        JsonSerializer.Serialize(new { codigoProveedor, descripcion, precioCosto, margenUtilidad });

    private static string CuerpoModificacion(
        string codigoProveedor, string descripcion, decimal precioCosto, decimal margenUtilidad, long version) =>
        JsonSerializer.Serialize(new { codigoProveedor, descripcion, precioCosto, margenUtilidad, version });

    private static Task<HttpResponseMessage> PutAsync(HttpClient cliente, string ruta, string cuerpo, string cookie) =>
        Http.EnviarAsync(
            cliente, HttpMethod.Put, ruta, new StringContent(cuerpo, Encoding.UTF8, "application/json"), cookie);
}
