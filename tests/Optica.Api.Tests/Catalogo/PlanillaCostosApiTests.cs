using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Optica.Api.Acceso;
using Optica.Api.Controllers;
using Optica.Application.Catalogo;
using Optica.Application.Comun;
using Optica.Infrastructure.Persistencia;

namespace Optica.Api.Tests.Catalogo;

/// <summary>
/// <c>POST /api/articulos/planilla-costos</c> (FEAT-011, Block 4): la planilla .xlsx viaja en base64 dentro de un JSON
/// (D-06); la Api la decodifica, llama al caso de uso y traduce su resultado a un DTO propio, sin margen, precio de
/// venta, versión, descripción ni estado (R-05). Las planillas se arman con el SDK de OpenXml en la prueba. Códigos,
/// descripciones y precios ficticios.
/// </summary>
public sealed class PlanillaCostosApiTests
{
    internal const string Ruta = "/api/articulos/planilla-costos";

    private const string RutaArticulos = "/api/articulos";

    private const string MensajeFaltante = "Elegí una planilla .xlsx.";

    private const string MensajeNoEsXlsx =
        "El archivo no es una planilla .xlsx. Guardala como Libro de Excel (.xlsx) y volvé a cargarla.";

    private const string MensajeFormato =
        "El formato de la planilla no es el esperado. Tiene que tener solo dos columnas, con los encabezados " +
        "\"Codigo en el proveedor\" y \"Precio de Costo Nuevo\" en la fila 1.";

    private const string MensajeLimites =
        "La planilla supera las 50.000 filas o tiene celdas demasiado largas. Dividila y cargala por partes. " +
        "Si tiene formato en celdas vacías, quitáselo.";

    private const string MensajeArchivoGrande = "La planilla supera los 10 MB.";

    /// <summary>14 MiB: 10 MB de archivo en base64 más el JSON (D-14).</summary>
    private const int CuerpoMaximo = 14 * 1024 * 1024;

    private const int ArchivoMaximo = 10 * 1024 * 1024;

    private const string Descripcion = "Descripción Zq7-distintiva";

    /// <summary>
    /// AC-01, AC-02, AC-08 y AC-12 por la Api: aplica las filas válidas (la venta se recalcula), lista las no aplicadas
    /// con su razón y las aplicadas para revisar con sus indicaciones; el código largo se acota a 60 caracteres con "…"
    /// (D-10). Contrato: la respuesta solo tiene las claves del DTO, nunca margen, precio de venta, versión, descripción
    /// ni estado del artículo (R-05).
    /// </summary>
    [Fact]
    public async Task Post_PlanillaValida_200ConResultado()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var abc = await GrabarArticuloAsync(cliente, cookie, "ABC-1");
        var neg = await GrabarArticuloAsync(cliente, cookie, "NEG-2");
        var ina = await GrabarArticuloAsync(cliente, cookie, "INA-3");
        var cer = await GrabarArticuloAsync(cliente, cookie, "CER-4");
        var desactivado = await Http.EnviarAsync(
            cliente,
            HttpMethod.Put,
            $"{RutaArticulos}/{ina}/estado",
            new StringContent("{\"activo\":false}", Encoding.UTF8, "application/json"),
            cookie);
        Assert.Equal(HttpStatusCode.OK, desactivado.StatusCode);
        var largo = new string('L', 70);
        var archivo = Planilla(
            ("ABC-1", 2420m),
            ("NO-EXISTE", 100m),
            ("NEG-2", -100m),
            ("INA-3", 400m),
            ("CER-4", 0m),
            (largo, 10m));

        var respuesta = await EnviarAsync(cliente, Cuerpo(archivo), cookie);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("application/json", respuesta.Content.Headers.ContentType?.MediaType);
        var texto = await respuesta.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(texto).RootElement;
        Assert.Equal(6, json.GetProperty("filasLeidas").GetInt32());
        Assert.Equal(4, json.GetProperty("aplicadas").GetInt32());

        var noAplicadas = json.GetProperty("noAplicadas").EnumerateArray().ToList();
        Assert.Equal(2, noAplicadas.Count);
        AfirmarNoAplicada(noAplicadas[0], 3, "NO-EXISTE", "código inexistente");
        AfirmarNoAplicada(noAplicadas[1], 7, new string('L', 59) + "…", "código inexistente");

        // AC-08: -100 y 0 se aplican, con la indicación, y AC-12: el inactivo se aplica y sigue inactivo.
        var revisar = json.GetProperty("revisar").EnumerateArray().ToList();
        Assert.Equal(3, revisar.Count);
        AfirmarParaRevisar(revisar[0], 4, "NEG-2", -100m, "actualizado con precio negativo o cero");
        AfirmarParaRevisar(revisar[1], 5, "INA-3", 400m, "inactivo");
        AfirmarParaRevisar(revisar[2], 6, "CER-4", 0m, "actualizado con precio negativo o cero");

        // Contrato del DTO (R-05): solo estas claves en todo el árbol, ni el texto del artículo.
        string[] permitidas =
            ["filasLeidas", "aplicadas", "noAplicadas", "revisar", "fila", "codigo", "razon", "precioCosto", "indicaciones"];
        var claves = Claves(json).ToHashSet(StringComparer.Ordinal);
        Assert.Subset(permitidas.ToHashSet(StringComparer.Ordinal), claves);
        foreach (var prohibida in new[] { "margen", "precioVenta", "version", "descripcion", "activo" })
        {
            Assert.DoesNotContain(claves, clave => clave.Contains(prohibida, StringComparison.OrdinalIgnoreCase));
        }

        // "activo" no se busca en el texto: la indicación "inactivo" lo contiene.
        foreach (var prohibida in new[] { "margen", "precioVenta", "version", "descripcion" })
        {
            Assert.DoesNotContain(prohibida, texto, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain("Zq7", texto, StringComparison.Ordinal);
        Assert.DoesNotContain(largo, texto, StringComparison.Ordinal);

        // AC-01: 2.420 con margen 50 da 3.630,00 y queda grabado; AC-08 y AC-12 también.
        var abcLeido = await LeerArticuloAsync(cliente, cookie, abc);
        Assert.Equal((2420m, 3630m, 1L), Costos(abcLeido));
        Assert.Equal((-100m, -150m, 1L), Costos(await LeerArticuloAsync(cliente, cookie, neg)));
        Assert.Equal((0m, 0m, 1L), Costos(await LeerArticuloAsync(cliente, cookie, cer)));
        var inaLeido = await LeerArticuloAsync(cliente, cookie, ina);
        Assert.Equal((400m, 600m, 2L), Costos(inaLeido));
        Assert.False(inaLeido.GetProperty("activo").GetBoolean());
        Assert.Equal(50m, inaLeido.GetProperty("margenUtilidad").GetDecimal());
    }

    /// <summary>
    /// Sin planilla (sin la clave, null, vacía, solo espacios, o un cuerpo vacío): 400 en <c>errors.planilla</c> con
    /// "Elegí una planilla .xlsx.".
    /// </summary>
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"planilla\":null}")]
    [InlineData("{\"planilla\":\"\"}")]
    [InlineData("{\"planilla\":\"   \"}")]
    [InlineData("")]
    public async Task Post_PlanillaFaltante_400(string cuerpo)
    {
        await AfirmarRechazoSinCambiosAsync(_ => cuerpo, MensajeFaltante);
    }

    /// <summary>
    /// Un texto que no es base64 (caracteres fuera del alfabeto, un largo imposible) o una planilla que no es texto: 400
    /// con el mensaje de "no es .xlsx", sin repetir lo recibido.
    /// </summary>
    [Theory]
    [InlineData("{\"planilla\":\"Zq7 no es base64!!\"}")]
    [InlineData("{\"planilla\":\"QUJD\\u00e9\"}")]
    [InlineData("{\"planilla\":\"QUJ\"}")]
    [InlineData("{\"planilla\":12345}")]
    [InlineData("{\"planilla\":[\"UEsDBA==\"]}")]
    [InlineData("{\"planilla\":")]
    public async Task Post_Base64Invalido_400NoEsXlsx(string cuerpo)
    {
        var texto = await AfirmarRechazoSinCambiosAsync(_ => cuerpo, MensajeNoEsXlsx);
        Assert.DoesNotContain("Zq7", texto, StringComparison.Ordinal);
    }

    /// <summary>
    /// AC-07: un .csv con los mismos datos o un .xls (Excel 97-2003, contenedor OLE con la firma
    /// <c>D0 CF 11 E0 A1 B1 1A E1</c>), bien codificados en base64, no son .xlsx: 400 y nada cambia.
    /// </summary>
    [Theory]
    [InlineData("csv")]
    [InlineData("xls")]
    public async Task Post_ArchivoCsv_400PideXlsx(string tipo)
    {
        var archivo = tipo == "csv"
            ? Encoding.UTF8.GetBytes("Codigo en el proveedor;Precio de Costo Nuevo\r\nABC-1;2420\r\n")
            : ArchivoXls();

        await AfirmarRechazoSinCambiosAsync(_ => Cuerpo(archivo), MensajeNoEsXlsx);
    }

    /// <summary>AC-04: una tercera columna rechaza la planilla completa con el mensaje de formato; nada se actualiza.</summary>
    [Fact]
    public async Task Post_TerceraColumna_400FormatoNoEsperadoYNadaActualizado()
    {
        var archivo = Libro(
            Fila(1, EnLinea("A1", "Codigo en el proveedor"), EnLinea("B1", "Precio de Costo Nuevo"), EnLinea("C1", "Notas")),
            Fila(2, EnLinea("A2", "ABC-1"), Numerica("B2", 2420m), EnLinea("C2", "x")));

        await AfirmarRechazoSinCambiosAsync(_ => Cuerpo(archivo), MensajeFormato);
    }

    /// <summary>
    /// Los dos límites, por separado. <c>filas</c>: una planilla que pasa un límite del lector (una fila de datos con
    /// índice 50.002 o una celda de más de 64 KB) da 400 con el mensaje de límites de D-24. <c>tamano</c>: un archivo
    /// decodificado de más de 10 MB que todavía entra en el cuerpo da 400 "La planilla supera los 10 MB." (D-25); en el
    /// límite exacto de 10 MB el archivo llega al lector (aquí no es un .xlsx). Nada se actualiza.
    /// </summary>
    [Theory]
    [InlineData("filas")]
    [InlineData("tamano")]
    public async Task Post_ExcedeLimites_400Rechazado(string limite)
    {
        if (limite == "filas")
        {
            var indiceAlto = Libro(
                Fila(1, EnLinea("A1", "Codigo en el proveedor"), EnLinea("B1", "Precio de Costo Nuevo")),
                Fila(2, EnLinea("A2", "ABC-1"), Numerica("B2", 2420m)),
                Fila(50_002, EnLinea("A50002", "DEF-2"), Numerica("B50002", 100m)));
            await AfirmarRechazoSinCambiosAsync(_ => Cuerpo(indiceAlto), MensajeLimites);

            var celdaLarga = Planilla((new string('x', 65_537), 2420m));
            await AfirmarRechazoSinCambiosAsync(_ => Cuerpo(celdaLarga), MensajeLimites);
            return;
        }

        var cuerpoDemasiado = Cuerpo(new byte[ArchivoMaximo + 1]);
        Assert.True(Encoding.UTF8.GetByteCount(cuerpoDemasiado) <= CuerpoMaximo, "El cuerpo pasaría el 413.");
        await AfirmarRechazoSinCambiosAsync(_ => cuerpoDemasiado, MensajeArchivoGrande);

        // Control: exactamente 10 MB no es "supera los 10 MB"; el lector lo rechaza por no ser .xlsx.
        await AfirmarRechazoSinCambiosAsync(_ => Cuerpo(new byte[ArchivoMaximo]), MensajeNoEsXlsx);
    }

    /// <summary>
    /// D-15: un conflicto de concurrencia al confirmar responde 409 con el <c>motivo</c> "cambiado" de
    /// <c>ConflictoArticulo</c>, sin <c>errors</c>; la transacción se revierte y nada queda aplicado.
    /// </summary>
    [Fact]
    public async Task Post_Conflicto_409Cambiado()
    {
        var choque = new Choque();
        using var fabrica = new FabricaApi(servicios =>
        {
            servicios.RemoveAll<IUnidadDeTrabajo>();
            servicios.AddScoped<IUnidadDeTrabajo>(proveedor => new UnidadQueChoca(
                new UnidadDeTrabajoEf(proveedor.GetRequiredService<OpticaDbContext>()), choque));
        });
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie, "ABC-1");
        choque.Activo = true;

        var respuesta = await EnviarAsync(cliente, Cuerpo(Planilla(("ABC-1", 2420m))), cookie);

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        var json = await Http.JsonAsync(respuesta);
        Assert.Equal(409, json.GetProperty("status").GetInt32());
        Assert.Equal("No se pudo grabar el artículo.", json.GetProperty("title").GetString());
        Assert.Equal("cambiado", json.GetProperty("motivo").GetString());
        Assert.Equal(
            "Un artículo cambió mientras se cargaba la planilla. No se actualizó ningún costo. Volvé a cargarla.",
            json.GetProperty("detail").GetString());
        Assert.False(json.TryGetProperty("errors", out _));
        Assert.Equal((1210m, 1815m, 0L), Costos(await LeerArticuloAsync(cliente, cookie, codigo)));
    }

    /// <summary>
    /// R-02: el cuerpo acepta hasta 14 MiB (<c>[RequestSizeLimit]</c>); un byte más, 413 con <c>Content-Length</c> y sin
    /// él (en partes), sin cuerpo y sin tocar nada. En el límite exacto el pedido llega a la acción (aquí, 400 por
    /// planilla faltante).
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Post_CuerpoDeMas_413(bool conLargo)
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie, "ABC-1");

        var rechazo = await EnviarCuerpoAsync(cliente, CuerpoDeBytes(CuerpoMaximo + 1), conLargo, cookie);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, rechazo.StatusCode);
        Assert.DoesNotContain("aaaa", await rechazo.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var aceptado = await EnviarCuerpoAsync(cliente, CuerpoDeBytes(CuerpoMaximo), conLargo, cookie);
        await AfirmarErrorPlanillaAsync(aceptado, MensajeFaltante);
        Assert.Equal((1210m, 1815m, 0L), Costos(await LeerArticuloAsync(cliente, cookie, codigo)));
    }

    /// <summary>
    /// R-01: un formulario de otro sitio solo puede mandar multipart, urlencoded o texto plano sin preflight; el
    /// endpoint exige JSON (<see cref="LimitesDelCuerpo"/>) y responde 415 sin cuerpo, sin tocar nada.
    /// </summary>
    [Theory]
    [InlineData("multipart")]
    [InlineData("urlencoded")]
    [InlineData("textoPlano")]
    public async Task Post_Multipart_415(string variante)
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie, "ABC-1");
        var archivo = Planilla(("ABC-1", 2420m));
        HttpContent contenido = variante switch
        {
            "multipart" => new MultipartFormDataContent
            {
                { new ByteArrayContent(archivo), "planilla", "costos.xlsx" },
            },
            "urlencoded" => new FormUrlEncodedContent(
                new Dictionary<string, string> { ["planilla"] = Convert.ToBase64String(archivo) }),
            _ => new StringContent(Cuerpo(archivo), Encoding.UTF8, "text/plain"),
        };

        var respuesta = await Http.EnviarAsync(cliente, HttpMethod.Post, Ruta, contenido, cookie);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, respuesta.StatusCode);
        Assert.Equal(string.Empty, await respuesta.Content.ReadAsStringAsync());
        AfirmarQueElEndpointExiste(fabrica);
        Assert.Equal((1210m, 1815m, 0L), Costos(await LeerArticuloAsync(cliente, cookie, codigo)));
    }

    /// <summary>
    /// R-01: sin sesión, 401 sin cuerpo antes del 415 y del 413; el endpoint existe (no es el 401 de una ruta
    /// desconocida) y no es anónimo; nada cambia.
    /// </summary>
    [Fact]
    public async Task Post_SinSesion_401()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie, "ABC-1");
        var archivo = Planilla(("ABC-1", 2420m));
        AfirmarQueElEndpointExiste(fabrica);
        Assert.False(RutasAnonimas.Contiene(HttpMethods.Post, Ruta));

        foreach (var contenido in new HttpContent?[]
        {
            new StringContent(Cuerpo(archivo), Encoding.UTF8, "application/json"),
            new MultipartFormDataContent { { new ByteArrayContent(archivo), "planilla", "costos.xlsx" } },
            new StringContent(CuerpoDeBytes(CuerpoMaximo + 1), Encoding.UTF8, "application/json"),
            null,
        })
        {
            var respuesta = await Http.EnviarAsync(cliente, HttpMethod.Post, Ruta, contenido);

            Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
            Assert.Null(respuesta.Headers.Location);
            Assert.Equal(string.Empty, await respuesta.Content.ReadAsStringAsync());
        }

        Assert.Equal((1210m, 1815m, 0L), Costos(await LeerArticuloAsync(cliente, cookie, codigo)));
    }

    /// <summary>
    /// D-25: el limitador de concurrencia del endpoint admite una carga en curso y una en fila. Con una retenida dentro
    /// del lector y otra en fila, la tercera responde 429 sin cuerpo, sin llegar al caso de uso y sin que se lea su
    /// cuerpo: el limitador corre antes del model binding. Sin sesión, 401 aunque el limitador esté lleno (la
    /// autorización va antes). Al liberar, la que estaba en fila entra.
    /// </summary>
    [Fact]
    public async Task Post_LimitadorLleno_429SinLeerElCuerpo()
    {
        var lector = new LectorBloqueante();
        using var fabrica = new FabricaApi(servicios =>
        {
            servicios.RemoveAll<ILectorPlanillaCostos>();
            servicios.AddSingleton<ILectorPlanillaCostos>(lector);
        });
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var cuerpo = Cuerpo(Planilla(("ABC-1", 2420m)));
        var enFila = new ContenidoContado(2 * 1024 * 1024);
        var sobrante = new ContenidoContado(2 * 1024 * 1024);

        try
        {
            // La primera ocupa el único lugar, retenida dentro del lector.
            var primera = EnviarAsync(cliente, cuerpo, cookie);
            Assert.True(lector.Dentro.Wait(TimeSpan.FromSeconds(30)), "La primera carga no llegó al lector.");

            // Dos más: una entra a la fila y la otra sobra.
            var segunda = Http.EnviarAsync(cliente, HttpMethod.Post, Ruta, enFila, cookie);
            var tercera = Http.EnviarAsync(cliente, HttpMethod.Post, Ruta, sobrante, cookie);
            var pendientes = new[] { segunda, tercera };
            var primeraRespuesta = await Task.WhenAny(Task.WhenAny(pendientes), Task.Delay(TimeSpan.FromSeconds(20)));
            Assert.True(
                primeraRespuesta is Task<Task<HttpResponseMessage>>, "Ningún pedido fue rechazado con el limitador lleno.");
            var terminada = await (Task<Task<HttpResponseMessage>>)primeraRespuesta;
            var rechazada = await terminada;
            Assert.Equal(HttpStatusCode.TooManyRequests, rechazada.StatusCode);
            Assert.Equal(string.Empty, await rechazada.Content.ReadAsStringAsync());
            var contenidoRechazado = terminada == segunda ? enFila : sobrante;
            Assert.True(
                contenidoRechazado.Escritos < contenidoRechazado.Largo,
                $"Se leyó el cuerpo rechazado completo ({contenidoRechazado.Escritos} bytes).");

            // Sin sesión: 401, no 429, con el limitador lleno.
            var sinSesion = await EnviarAsync(cliente, cuerpo, cookie: null);
            Assert.Equal(HttpStatusCode.Unauthorized, sinSesion.StatusCode);

            // Solo la primera llegó al caso de uso.
            Assert.Equal(1, lector.Entradas);

            lector.Liberar();
            await AfirmarErrorPlanillaAsync(await primera, MensajeNoEsXlsx);
            var admitida = await (terminada == segunda ? tercera : segunda);
            await AfirmarErrorPlanillaAsync(admitida, MensajeFaltante);
        }
        finally
        {
            lector.Liberar();
        }
    }

    /// <summary>
    /// R-05: ninguno de los seis registros nuevos muestra valores en su <c>ToString()</c> (MVC registra en Trace los
    /// argumentos de la acción con su <c>ToString()</c>).
    /// </summary>
    [Fact]
    public void Registros_ToString_NoMuestranValores()
    {
        const string Testigo = "QZ7-testigo";
        Assert.Equal("SolicitudPlanillaCostos { }", new SolicitudPlanillaCostos(Testigo).ToString());
        Assert.Equal(
            "FilaNoAplicadaRespuesta { }", new FilaNoAplicadaRespuesta(2, Testigo, "código inexistente").ToString());
        Assert.Equal(
            "FilaParaRevisarRespuesta { }", new FilaParaRevisarRespuesta(2, Testigo, 98765m, ["inactivo"]).ToString());
        Assert.Equal(
            "RespuestaPlanillaCostos { }",
            new RespuestaPlanillaCostos(1, 0, [new FilaNoAplicadaRespuesta(2, Testigo, "x")], []).ToString());
        Assert.Equal(
            "FilaNoAplicadaPlanilla { }", new FilaNoAplicadaPlanilla(2, Testigo, "código inexistente").ToString());
        Assert.Equal(
            "FilaParaRevisarPlanilla { }", new FilaParaRevisarPlanilla(2, Testigo, 98765m, ["inactivo"]).ToString());
    }

    /// <summary>
    /// D-10: el código de la respuesta se muestra con hasta 60 caracteres ("…" incluido) y nunca lleva sustitutos UTF-16
    /// sueltos (se reemplazan por U+FFFD antes de serializar); el corte no parte un par de sustitutos válido.
    /// </summary>
    /// <remarks>
    /// Un <c>[Fact]</c> y no un <c>[Theory]</c>: xUnit serializa los datos de <c>InlineData</c> en UTF-8 y un sustituto
    /// suelto llega a la prueba ya convertido en U+FFFD, con lo que la prueba no probaría nada.
    /// </remarks>
    [Fact]
    public void AcotarCodigo_CortoYSustitutos_ReemplazaSueltos()
    {
        static string Acotar(string codigo) => Optica.Api.Controllers.RespuestaPlanillaCostos.AcotarCodigo(codigo);

        var sueltoAlto = "A" + (char)0xD800 + "B";
        Assert.Equal(3, sueltoAlto.Length);
        Assert.Equal("A�B", Acotar(sueltoAlto));
        Assert.Equal("�", Acotar(((char)0xDC00).ToString()));
        Assert.Equal("x�", Acotar("x" + (char)0xDBFF));
        Assert.Equal("��", Acotar(((char)0xDC00).ToString() + (char)0xD800));
    }

    [Fact]
    public void AcotarCodigo_Largo_60CaracteresConPuntosSuspensivos()
    {
        static string Acotar(string codigo) => Optica.Api.Controllers.RespuestaPlanillaCostos.AcotarCodigo(codigo);

        // Controles: un código corto y un par de sustitutos válido quedan como vinieron.
        Assert.Equal("ABC-1", Acotar("ABC-1"));
        Assert.Equal("\U0001F600", Acotar("\U0001F600"));

        var sesenta = new string('x', 60);
        Assert.Equal(sesenta, Acotar(sesenta));
        Assert.Equal(new string('x', 59) + "…", Acotar(new string('x', 61)));

        // El corte cae entre las dos mitades de un par válido: se deja afuera el par entero.
        Assert.Equal(new string('x', 58) + "…", Acotar(new string('x', 58) + "\U0001F600yy"));

        // Un sustituto suelto se reemplaza antes de cortar.
        Assert.Equal(new string('x', 58) + "�…", Acotar(new string('x', 58) + "\uD800yy"));
    }

    // ---- Auxiliares ----

    /// <summary>
    /// Arma un .xlsx con el SDK: los encabezados del formato en la fila 1 y una fila por par (código en texto en línea,
    /// precio numérico) desde la fila 2.
    /// </summary>
    internal static byte[] Planilla(params (string Codigo, decimal Precio)[] filas)
    {
        var todas = new List<Row>
        {
            Fila(1, EnLinea("A1", "Codigo en el proveedor"), EnLinea("B1", "Precio de Costo Nuevo")),
        };
        var numero = 2u;
        foreach (var (codigo, precio) in filas)
        {
            todas.Add(Fila(numero, EnLinea($"A{numero}", codigo), Numerica($"B{numero}", precio)));
            numero++;
        }

        return Libro([.. todas]);
    }

    /// <summary>Un .xlsx válido con una sola fila de encabezados, los que se pasan (para el formato inesperado).</summary>
    internal static byte[] PlanillaConEncabezados(string encabezadoA, string encabezadoB) =>
        Libro(
            Fila(1, EnLinea("A1", encabezadoA), EnLinea("B1", encabezadoB)),
            Fila(2, EnLinea("A2", "ABC-1"), Numerica("B2", 2420m)));

    /// <summary>
    /// El comienzo de un .xls (Excel 97-2003): la firma del contenedor OLE <c>D0 CF 11 E0 A1 B1 1A E1</c> y el resto de
    /// un encabezado de 512 bytes.
    /// </summary>
    private static byte[] ArchivoXls()
    {
        var archivo = new byte[512];
        new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }.CopyTo(archivo, 0);
        return archivo;
    }

    /// <summary>El cuerpo JSON con la planilla en base64.</summary>
    internal static string Cuerpo(byte[] archivo) =>
        "{\"planilla\":\"" + Convert.ToBase64String(archivo) + "\"}";

    /// <summary>Un cuerpo JSON de exactamente <paramref name="bytes"/> bytes, sin planilla y con un relleno que se ignora.</summary>
    internal static string CuerpoDeBytes(int bytes)
    {
        const string SinRelleno = "{\"planilla\":\"\",\"relleno\":\"\"}";
        var faltan = bytes - Encoding.UTF8.GetByteCount(SinRelleno);
        Assert.True(faltan >= 0, "El cuerpo sin relleno ya es más largo que el pedido.");
        var cuerpo = "{\"planilla\":\"\",\"relleno\":\"" + new string('a', faltan) + "\"}";
        Assert.Equal(bytes, Encoding.UTF8.GetByteCount(cuerpo));
        return cuerpo;
    }

    internal static Task<HttpResponseMessage> EnviarAsync(HttpClient cliente, string cuerpo, string? cookie) =>
        Http.PostJsonAsync(cliente, Ruta, cuerpo, cookie);

    /// <summary>Graba un artículo activo con costo 1210 y margen 50 (venta 1815) y devuelve su código.</summary>
    internal static async Task<long> GrabarArticuloAsync(HttpClient cliente, string cookie, string codigoProveedor)
    {
        var cuerpo = JsonSerializer.Serialize(new
        {
            codigoProveedor,
            descripcion = Descripcion,
            precioCosto = 1210m,
            margenUtilidad = 50m,
        });
        var respuesta = await Http.PostJsonAsync(cliente, RutaArticulos, cuerpo, cookie);
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await Http.JsonAsync(respuesta)).GetProperty("codigo").GetInt64();
    }

    private static Task<HttpResponseMessage> EnviarCuerpoAsync(
        HttpClient cliente, string cuerpo, bool conLargo, string cookie)
    {
        HttpContent contenido = conLargo
            ? new StringContent(cuerpo, Encoding.UTF8, "application/json")
            : new Http.ContenidoSinLargo(cuerpo, "application/json");
        return Http.EnviarAsync(cliente, HttpMethod.Post, Ruta, contenido, cookie);
    }

    /// <summary>
    /// Graba ABC-1, manda el cuerpo, exige el 400 con el mensaje en <c>errors.planilla</c> y que el artículo no cambió.
    /// Devuelve el texto de la respuesta.
    /// </summary>
    private static async Task<string> AfirmarRechazoSinCambiosAsync(Func<long, string> cuerpo, string mensaje)
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var codigo = await GrabarArticuloAsync(cliente, cookie, "ABC-1");

        var respuesta = await EnviarAsync(cliente, cuerpo(codigo), cookie);

        var texto = await AfirmarErrorPlanillaAsync(respuesta, mensaje);
        Assert.Equal((1210m, 1815m, 0L), Costos(await LeerArticuloAsync(cliente, cookie, codigo)));
        return texto;
    }

    /// <summary>400 ValidationProblemDetails con un solo error: <c>errors.planilla</c> con el mensaje.</summary>
    private static async Task<string> AfirmarErrorPlanillaAsync(HttpResponseMessage respuesta, string mensaje)
    {
        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        var texto = await respuesta.Content.ReadAsStringAsync();
        var errores = JsonDocument.Parse(texto).RootElement.GetProperty("errors").EnumerateObject()
            .ToDictionary(
                campo => campo.Name,
                campo => campo.Value.EnumerateArray().Select(valor => valor.GetString()!).ToArray());
        Assert.Equal(new Dictionary<string, string[]> { ["planilla"] = [mensaje] }, errores);
        return texto;
    }

    private static async Task<JsonElement> LeerArticuloAsync(HttpClient cliente, string cookie, long codigo)
    {
        var respuesta = await Http.GetAsync(cliente, $"{RutaArticulos}/{codigo}", cookie);
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        return await Http.JsonAsync(respuesta);
    }

    private static (decimal Costo, decimal Venta, long Version) Costos(JsonElement articulo) =>
        (articulo.GetProperty("precioCosto").GetDecimal(),
            articulo.GetProperty("precioVenta").GetDecimal(),
            articulo.GetProperty("version").GetInt64());

    private static void AfirmarNoAplicada(JsonElement fila, int numero, string codigo, string razon)
    {
        Assert.Equal(numero, fila.GetProperty("fila").GetInt32());
        Assert.Equal(codigo, fila.GetProperty("codigo").GetString());
        Assert.Equal(razon, fila.GetProperty("razon").GetString());
        Assert.Equal(3, fila.EnumerateObject().Count());
    }

    private static void AfirmarParaRevisar(
        JsonElement fila, int numero, string codigo, decimal precioCosto, params string[] indicaciones)
    {
        Assert.Equal(numero, fila.GetProperty("fila").GetInt32());
        Assert.Equal(codigo, fila.GetProperty("codigo").GetString());
        Assert.Equal(precioCosto, fila.GetProperty("precioCosto").GetDecimal());
        Assert.Equal(indicaciones, fila.GetProperty("indicaciones").EnumerateArray().Select(i => i.GetString()!));
        Assert.Equal(4, fila.EnumerateObject().Count());
    }

    /// <summary>Todas las claves de objeto del árbol JSON.</summary>
    private static IEnumerable<string> Claves(JsonElement elemento) => elemento.ValueKind switch
    {
        JsonValueKind.Object => elemento.EnumerateObject()
            .SelectMany(propiedad => Claves(propiedad.Value).Prepend(propiedad.Name)),
        JsonValueKind.Array => elemento.EnumerateArray().SelectMany(Claves),
        _ => [],
    };

    /// <summary>El POST está registrado: un 401 o un 415 no son los de una ruta inexistente.</summary>
    private static void AfirmarQueElEndpointExiste(FabricaApi fabrica)
    {
        var existe = fabrica.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Any(endpoint =>
                "/" + (endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/') == Ruta
                && endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains(HttpMethods.Post) == true);
        Assert.True(existe, $"POST {Ruta} no está registrado.");
    }

    private static byte[] Libro(params Row[] filas)
    {
        using var memoria = new MemoryStream();
        using (var documento = SpreadsheetDocument.Create(memoria, SpreadsheetDocumentType.Workbook))
        {
            var libro = documento.AddWorkbookPart();
            libro.Workbook = new Workbook(new Sheets());
            var hoja = libro.AddNewPart<WorksheetPart>();
            hoja.Worksheet = new Worksheet(new SheetData(filas));
            libro.Workbook.Sheets!.Append(new Sheet { Id = libro.GetIdOfPart(hoja), SheetId = 1, Name = "Costos" });
        }

        return memoria.ToArray();
    }

    private static Row Fila(uint numero, params Cell[] celdas) => new(celdas) { RowIndex = numero };

    private static Cell Numerica(string referencia, decimal valor) => new()
    {
        CellReference = referencia,
        CellValue = new CellValue(valor.ToString(CultureInfo.InvariantCulture)),
    };

    private static Cell EnLinea(string referencia, string texto) => new()
    {
        CellReference = referencia,
        DataType = CellValues.InlineString,
        InlineString = new InlineString(new Text(texto)),
    };

    /// <summary>
    /// Un lector que retiene cada lectura hasta <see cref="Liberar"/> y responde "no es .xlsx": deja una carga ocupando
    /// el limitador y el candado (D-25).
    /// </summary>
    private sealed class LectorBloqueante : ILectorPlanillaCostos
    {
        private readonly ManualResetEventSlim _liberar = new();
        private int _entradas;

        public ManualResetEventSlim Dentro { get; } = new();

        public int Entradas => Volatile.Read(ref _entradas);

        public LecturaPlanilla Leer(ReadOnlyMemory<byte> archivo)
        {
            Interlocked.Increment(ref _entradas);
            Dentro.Set();
            _liberar.Wait(TimeSpan.FromSeconds(60));
            return LecturaPlanilla.NoEsXlsx.Instancia;
        }

        public void Liberar() => _liberar.Set();
    }

    /// <summary>
    /// Un cuerpo JSON válido de <see cref="Largo"/> bytes, sin planilla y con relleno, escrito de a 16 KB: cuenta cuántos
    /// bytes llegó a escribir. Uno rechazado sin leerlo queda corto.
    /// </summary>
    private sealed class ContenidoContado : HttpContent
    {
        private readonly byte[] _datos;
        private long _escritos;

        public ContenidoContado(int largo)
        {
            _datos = Encoding.UTF8.GetBytes(CuerpoDeBytes(largo));
            Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        }

        public long Largo => _datos.Length;

        public long Escritos => Interlocked.Read(ref _escritos);

        protected override async Task SerializeToStreamAsync(Stream destino, TransportContext? contexto)
        {
            const int Trozo = 16 * 1024;
            for (var desde = 0; desde < _datos.Length; desde += Trozo)
            {
                var largo = Math.Min(Trozo, _datos.Length - desde);
                await destino.WriteAsync(_datos.AsMemory(desde, largo));
                await destino.FlushAsync();
                Interlocked.Add(ref _escritos, largo);
            }
        }

        protected override bool TryComputeLength(out long largo)
        {
            largo = _datos.Length;
            return true;
        }
    }

    /// <summary>Si la próxima confirmación choca; lo comparten los alcances de una misma prueba.</summary>
    private sealed class Choque
    {
        public bool Activo { get; set; }
    }

    /// <summary>
    /// La unidad de trabajo real que, con el <see cref="Choque"/> activo, lanza un conflicto de concurrencia al confirmar
    /// dentro de la operación: la transacción real se revierte, como cuando otra escritura cambió un artículo (D-15).
    /// </summary>
    private sealed class UnidadQueChoca(IUnidadDeTrabajo interna, Choque choque) : IUnidadDeTrabajo
    {
        public Task<T> EjecutarEnTransaccionAsync<T>(
            Func<CancellationToken, Task<T>> operacion, CancellationToken cancelacion = default) =>
            interna.EjecutarEnTransaccionAsync(operacion, cancelacion);

        public Task ConfirmarAsync(CancellationToken cancelacion = default) =>
            choque.Activo
                ? throw new ConflictoAlConfirmarException("Conflicto de prueba.", MotivoConflicto.Concurrencia)
                : interna.ConfirmarAsync(cancelacion);
    }
}
