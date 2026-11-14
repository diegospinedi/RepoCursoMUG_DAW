using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Optica.Api.Tests.Acceso;

/// <summary>
/// FR-06 (AC-03): ninguna respuesta de la Api lleva datos de certificado ni de punto de venta. Hoy no los lleva
/// ninguna; la prueba lo fija para que un cambio futuro no los agregue sin que falle algo. Excepción de FEAT-007
/// (D-05, D-08): el GET de la configuración devuelve el punto de venta, que la operadora carga en esa pantalla (FR-28);
/// el certificado sigue prohibido en todas las respuestas.
/// </summary>
public sealed class SinDatosArcaTests
{
    /// <summary>
    /// Nombres de clave JSON prohibidos, comparados enteros y sin distinguir mayúsculas. No se buscan subcadenas
    /// ("concepto", "receptor" darían falsos positivos) ni se revisan valores libres como el nombre de la marca.
    /// </summary>
    private static readonly HashSet<string> ClavesArca =
        new(["certificado", "puntoDeVenta", "puntoVenta", "ptoVta"], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Las únicas excepciones, por ruta y por camino exacto (D-08 de FEAT-007): el punto de venta en la raíz de la
    /// respuesta de la configuración. Nunca el certificado.
    /// </summary>
    private static readonly Dictionary<string, HashSet<string>> PermitidasPorRuta = new(StringComparer.Ordinal)
    {
        [RutaConfiguracion] = new(["$.puntoVenta"], StringComparer.Ordinal),
    };

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
        // Un presupuesto grabado con ese artículo (FEAT-003a), para pedir su GET con un número que existe. Datos
        // ficticios.
        var presupuesto = await Http.PostJsonAsync(
            cliente,
            RutaPresupuestos,
            "{\"cliente\":{\"apellido\":\"Perez\",\"nombre\":\"Ana\",\"dni\":\"30111222\"},"
            + "\"lineas\":[{\"codigoArticulo\":" + codigoArticulo + ",\"cantidad\":1,\"porcentajeDescuento\":0}]}",
            cookie);
        Assert.Equal(System.Net.HttpStatusCode.Created, presupuesto.StatusCode);
        var numero = (await Http.JsonAsync(presupuesto)).GetProperty("numero").GetInt64();
        // FEAT-006: el PDF solo se genera de un Final; el presupuesto pasa a Final como en la app, así su GET y el de su
        // PDF responden con éxito.
        var final = await Http.EnviarAsync(
            cliente,
            HttpMethod.Put,
            $"{RutaPresupuestos}/{numero}/estado",
            new System.Net.Http.StringContent(
                "{\"estado\":\"final\",\"version\":0}", System.Text.Encoding.UTF8, "application/json"),
            cookie);
        Assert.Equal(System.Net.HttpStatusCode.OK, final.StatusCode);
        // Cada ruta con parámetros, con los valores con los que se la pide. Una ruta con parámetros que no esté acá
        // hace fallar la prueba: hay que agregarla, no excluirla.
        var valores = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [RutaArticulo] = $"{RutaArticulos}/{codigoArticulo}",
            [RutaPresupuesto] = $"{RutaPresupuestos}/{numero}",
            [RutaPdf] = $"{RutaPresupuestos}/{numero}/pdf",
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
        var conPdf = new List<string>();
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

            if (string.Equals(tipo, "application/pdf", StringComparison.OrdinalIgnoreCase))
            {
                // FEAT-006: el PDF del presupuesto es un documento, no JSON; se acepta su cuerpo sin revisar claves.
                conPdf.Add(ruta);
                continue;
            }

            if (!EsJson(tipo))
            {
                // Binario o texto libre (hoy, el logo): fuera del alcance de la revisión de claves JSON.
                continue;
            }

            conJson.Add(ruta);
            using var documento = JsonDocument.Parse(texto);
            hallazgos.AddRange(Hallazgos(ruta, documento.RootElement));
        }

        Assert.True(
            hallazgos.Count == 0,
            "Respuestas con datos de certificado o de punto de venta (AC-03):" + Environment.NewLine
            + string.Join(Environment.NewLine, hallazgos));

        // Los GET que hoy devuelven JSON se revisaron de verdad; el de la sesión responde 204 sin cuerpo.
        Assert.Contains("/api/acceso/estado", conJson);
        Assert.Contains("/api/marca", conJson);
        Assert.Contains(RutaArticulo, conJson);
        // El listado de FEAT-002b, con el artículo grabado en su primera página.
        Assert.Contains(RutaArticulos, conJson);
        // El presupuesto grabado de FEAT-003a.
        Assert.Contains(RutaPresupuesto, conJson);
        // La configuración, con la excepción del punto de venta (D-08 de FEAT-007).
        Assert.Contains(RutaConfiguracion, conJson);
        // El PDF del presupuesto en Final (FEAT-006) se pidió y respondió application/pdf.
        Assert.Equal([RutaPdf], conPdf);
        Assert.Contains("/api/sesion", sinCuerpo);
    }

    /// <summary>
    /// FEAT-007 (D-08, R-03): la ruta nueva de facturación tampoco devuelve el certificado ni el punto de venta como clave:
    /// ni el 201 de la emisión (el punto de venta va dentro del texto del comprobante, no como dato), ni el GET del
    /// presupuesto facturado, ni el 409 de un rechazo de ARCA.
    /// </summary>
    [Fact]
    public async Task SinDatosArca_RutaFactura_SinCertificadoNiPuntoVenta()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var numero = await PresupuestoFinalFacturableAsync(cliente, cookie);

        var emitida = await Http.PostJsonAsync(
            cliente, $"{RutaPresupuestos}/{numero}/factura", "{\"concepto\":\"productos\"}", cookie);
        Assert.Equal(System.Net.HttpStatusCode.Created, emitida.StatusCode);
        using var factura = JsonDocument.Parse(await emitida.Content.ReadAsStringAsync());
        var leido = await Http.GetAsync(cliente, $"{RutaPresupuestos}/{numero}", cookie);
        Assert.Equal(System.Net.HttpStatusCode.OK, leido.StatusCode);
        using var presupuesto = JsonDocument.Parse(await leido.Content.ReadAsStringAsync());
        Assert.True(presupuesto.RootElement.TryGetProperty("factura", out _), "El GET no trae la factura.");

        Assert.Empty(Hallazgos(RutaFactura, factura.RootElement));
        Assert.Empty(Hallazgos(RutaPresupuesto, presupuesto.RootElement));

        // El rechazo de ARCA (D-18) lleva código y descripción, nunca datos del certificado.
        using var conRechazo = new FabricaApi(respuestaArca: FabricaApi.RespuestaArcaRechazar);
        var otroCliente = conRechazo.CrearClienteSinCookies();
        var otraCookie = await Http.DefinirAsync(otroCliente);
        var otroNumero = await PresupuestoFinalFacturableAsync(otroCliente, otraCookie);
        var rechazo = await Http.PostJsonAsync(
            otroCliente, $"{RutaPresupuestos}/{otroNumero}/factura", "{\"concepto\":\"productos\"}", otraCookie);
        Assert.Equal(System.Net.HttpStatusCode.Conflict, rechazo.StatusCode);
        using var problema = JsonDocument.Parse(await rechazo.Content.ReadAsStringAsync());
        Assert.True(problema.RootElement.TryGetProperty("codigo", out _), "El 409 del rechazo no trae el código.");
        Assert.Empty(Hallazgos(RutaFactura, problema.RootElement));
    }

    /// <summary>Configura la emisión, graba un artículo y un presupuesto, lo pasa a Final y devuelve su número.</summary>
    private static async Task<long> PresupuestoFinalFacturableAsync(HttpClient cliente, string cookie)
    {
        var configuracion = await Http.PostJsonAsync(
            cliente,
            RutaConfiguracion,
            "{\"alicuotaIva\":21,\"condicionFiscal\":\"responsable_inscripto\",\"multiploRedondeo\":0.01,"
            + "\"topeIdentificacion\":100000,\"puntoVenta\":3}",
            cookie);
        Assert.Equal(System.Net.HttpStatusCode.OK, configuracion.StatusCode);
        var articulo = await Http.PostJsonAsync(
            cliente,
            RutaArticulos,
            """{"codigoProveedor":"ABC-1","descripcion":"Armazón","precioCosto":1210,"margenUtilidad":50}""",
            cookie);
        Assert.Equal(System.Net.HttpStatusCode.Created, articulo.StatusCode);
        var codigo = (await Http.JsonAsync(articulo)).GetProperty("codigo").GetInt64();
        var alta = await Http.PostJsonAsync(
            cliente,
            RutaPresupuestos,
            "{\"cliente\":{\"apellido\":\"Perez\",\"nombre\":\"Ana\",\"dni\":\"00718294\"},"
            + "\"lineas\":[{\"codigoArticulo\":" + codigo + ",\"cantidad\":1,\"porcentajeDescuento\":0}]}",
            cookie);
        Assert.Equal(System.Net.HttpStatusCode.Created, alta.StatusCode);
        var numero = (await Http.JsonAsync(alta)).GetProperty("numero").GetInt64();
        var final = await Http.EnviarAsync(
            cliente,
            HttpMethod.Put,
            $"{RutaPresupuestos}/{numero}/estado",
            new System.Net.Http.StringContent(
                "{\"estado\":\"final\",\"version\":0}", System.Text.Encoding.UTF8, "application/json"),
            cookie);
        Assert.Equal(System.Net.HttpStatusCode.OK, final.StatusCode);
        return numero;
    }

    private const string RutaFactura = "/api/presupuestos/{numero:long}/factura";
    private const string RutaArticulos = "/api/articulos";
    private const string RutaArticulo = "/api/articulos/{codigo:long}";
    private const string RutaPresupuestos = "/api/presupuestos";
    private const string RutaPresupuesto = "/api/presupuestos/{numero:long}";
    private const string RutaPdf = "/api/presupuestos/{numero:long}/pdf";
    private const string RutaConfiguracion = "/api/configuracion";

    [Fact]
    public async Task SinDatosArca_Configuracion_PermitePuntoVentaYRechazaCertificado()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);

        // D-05: el GET de la configuración devuelve el punto de venta, y eso no es un hallazgo.
        var respuesta = await Http.GetAsync(cliente, RutaConfiguracion, cookie);
        Assert.Equal(System.Net.HttpStatusCode.OK, respuesta.StatusCode);
        using var real = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
        Assert.True(real.RootElement.TryGetProperty("puntoVenta", out _), "El GET de la configuración no trae puntoVenta.");
        Assert.Empty(Hallazgos(RutaConfiguracion, real.RootElement));

        // D-08: la excepción es solo esa clave en la raíz de esa ruta; el certificado sigue prohibido también ahí.
        using var conCertificado = JsonDocument.Parse("""{"puntoVenta":3,"certificado":"x"}""");
        Assert.Equal(
            ["GET /api/configuracion devuelve la clave \"certificado\" (en $.certificado)"],
            Hallazgos(RutaConfiguracion, conCertificado.RootElement));
        using var anidado = JsonDocument.Parse("""{"otro":{"puntoVenta":3}}""");
        Assert.Single(Hallazgos(RutaConfiguracion, anidado.RootElement));
        using var otraRuta = JsonDocument.Parse("""{"puntoVenta":3}""");
        Assert.Single(Hallazgos("/api/marca", otraRuta.RootElement));
    }

    /// <summary>Las claves de ARCA de una respuesta, salvo las permitidas para esa ruta en <see cref="PermitidasPorRuta"/>.</summary>
    private static List<string> Hallazgos(string ruta, JsonElement raiz)
    {
        var permitidas = PermitidasPorRuta.GetValueOrDefault(ruta);
        return
        [
            .. Claves(raiz, "$")
                .Where(par => ClavesArca.Contains(par.Clave))
                .Where(par => permitidas is null || !permitidas.Contains(par.Camino))
                .Select(par => $"GET {ruta} devuelve la clave \"{par.Clave}\" (en {par.Camino})"),
        ];
    }

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
