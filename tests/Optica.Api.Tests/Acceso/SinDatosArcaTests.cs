using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Optica.Api.Tests.Acceso;

/// <summary>
/// FR-06 (AC-03): ninguna respuesta de la Api lleva datos de certificado ni de punto de venta. Hoy no los lleva
/// ninguna; la prueba lo fija para que un cambio futuro no los agregue sin que falle algo.
/// </summary>
public sealed class SinDatosArcaTests
{
    /// <summary>
    /// Nombres de clave JSON prohibidos, comparados enteros y sin distinguir mayúsculas. No se buscan subcadenas
    /// ("concepto", "receptor" darían falsos positivos) ni se revisan valores libres como el nombre de la marca.
    /// </summary>
    private static readonly HashSet<string> ClavesArca =
        new(["certificado", "puntoDeVenta", "puntoVenta", "ptoVta"], StringComparer.OrdinalIgnoreCase);

    [Fact]
    public async Task ApiConSesion_NingunGet_DevuelveDatosArca()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);

        // Un artículo grabado, para pedir su GET con un código que existe (una respuesta 404 no revisaría nada).
        var grabado = await Http.PostJsonAsync(
            cliente,
            RutaArticulos,
            """{"codigoProveedor":"ABC-1","descripcion":"Armazón","precioCosto":1210,"margenUtilidad":50}""",
            cookie);
        Assert.Equal(System.Net.HttpStatusCode.Created, grabado.StatusCode);
        var codigoArticulo = (await Http.JsonAsync(grabado)).GetProperty("codigo").GetInt64();
        // Cada ruta con parámetros, con los valores con los que se la pide. Una ruta con parámetros que no esté acá
        // hace fallar la prueba: hay que agregarla, no excluirla.
        var valores = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [RutaArticulo] = $"{RutaArticulos}/{codigoArticulo}",
        };

        // Lo realmente registrado en la Api, no una lista escrita a mano: un GET nuevo entra solo en la revisión.
        var rutas = fabrica.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint =>
                endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains(HttpMethods.Get) ?? true)
            .Select(endpoint => "/" + (endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/'))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        // Si la enumeración no encontrara los GET conocidos, la prueba no estaría probando nada.
        Assert.Contains("/api/acceso/estado", rutas);
        Assert.Contains("/api/sesion", rutas);
        Assert.Contains("/api/marca", rutas);

        var conJson = new List<string>();
        var sinCuerpo = new List<string>();
        var hallazgos = new List<string>();
        foreach (var ruta in rutas)
        {
            var pedida = ruta;
            if (ruta.Contains('{', StringComparison.Ordinal))
            {
                Assert.True(
                    valores.TryGetValue(ruta, out var conValores),
                    $"GET {ruta} tiene parámetros de ruta: esta prueba no sabe con qué valores pedirlo; agregalos.");
                pedida = conValores!;
            }

            var respuesta = await Http.GetAsync(cliente, pedida, cookie);
            Assert.True(
                respuesta.IsSuccessStatusCode,
                $"GET {ruta} respondió {(int)respuesta.StatusCode} con sesión; no se pudo revisar su respuesta.");

            var tipo = respuesta.Content.Headers.ContentType?.MediaType;
            var texto = await respuesta.Content.ReadAsStringAsync();
            if (texto.Length == 0)
            {
                sinCuerpo.Add(ruta);
                continue;
            }

            if (!EsJson(tipo))
            {
                // Binario o texto libre (hoy, el logo): fuera del alcance de la revisión de claves JSON.
                continue;
            }

            conJson.Add(ruta);
            using var documento = JsonDocument.Parse(texto);
            foreach (var (clave, camino) in Claves(documento.RootElement, "$"))
            {
                if (ClavesArca.Contains(clave))
                {
                    hallazgos.Add($"GET {ruta} devuelve la clave \"{clave}\" (en {camino})");
                }
            }
        }

        Assert.True(
            hallazgos.Count == 0,
            "Respuestas con datos de certificado o de punto de venta (AC-03):" + Environment.NewLine
            + string.Join(Environment.NewLine, hallazgos));

        // Los GET que hoy devuelven JSON se revisaron de verdad; el de la sesión responde 204 sin cuerpo.
        Assert.Contains("/api/acceso/estado", conJson);
        Assert.Contains("/api/marca", conJson);
        Assert.Contains(RutaArticulo, conJson);
        Assert.Contains("/api/sesion", sinCuerpo);
    }

    private const string RutaArticulos = "/api/articulos";
    private const string RutaArticulo = "/api/articulos/{codigo:long}";

    [Fact]
    public void ClavesArca_ComparaNombresEnterosSinDistinguirMayusculas()
    {
        using var documento = JsonDocument.Parse(
            """{"concepto":1,"receptor":{"PtoVta":2},"lista":[{"CERTIFICADO":"x"}],"nombre":"punto de venta"}""");

        var encontradas = Claves(documento.RootElement, "$")
            .Where(par => ClavesArca.Contains(par.Clave))
            .Select(par => par.Camino)
            .ToList();

        Assert.Equal(["$.receptor.PtoVta", "$.lista[0].CERTIFICADO"], encontradas);
    }

    private static bool EsJson(string? tipo) =>
        tipo is not null
        && (tipo.Equals("application/json", StringComparison.OrdinalIgnoreCase)
            || tipo.EndsWith("+json", StringComparison.OrdinalIgnoreCase));

    /// <summary>Todos los nombres de clave del documento, a cualquier profundidad, con su camino.</summary>
    private static IEnumerable<(string Clave, string Camino)> Claves(JsonElement elemento, string camino)
    {
        switch (elemento.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var propiedad in elemento.EnumerateObject())
                {
                    var hijo = $"{camino}.{propiedad.Name}";
                    yield return (propiedad.Name, hijo);
                    foreach (var interna in Claves(propiedad.Value, hijo))
                    {
                        yield return interna;
                    }
                }

                break;
            case JsonValueKind.Array:
                var indice = 0;
                foreach (var item in elemento.EnumerateArray())
                {
                    foreach (var interna in Claves(item, $"{camino}[{indice}]"))
                    {
                        yield return interna;
                    }

                    indice++;
                }

                break;
        }
    }
}
