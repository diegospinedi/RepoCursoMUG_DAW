using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Optica.Application.Facturacion;
using Optica.Domain.Configuracion;
using Optica.Domain.Facturacion;
using Optica.Domain.Marca;
using Optica.Infrastructure.Pdf;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Graphics.Colors;

namespace Optica.Infrastructure.Tests.Pdf;

/// <summary>
/// El PDF de la factura (FR-06 a FR-15, NFR-02 de FEAT-009) y el documento que lo alimenta: se lee con PdfPig para
/// verificar textos, colores, imágenes y páginas. Datos ficticios: emisor inventado con CUIT 20-00000000-1 (D-33), DNI
/// que empieza con 00 (D-08), "Pérez, Ana" del PRD y un CAE inventado.
/// </summary>
public sealed class GeneradorPdfFacturaTests
{
    private const string ColorPrimario = "#0903A0";
    private const string ColorFondo = "#FFFFFF";
    private const string MensajeFalla = "No se pudo generar el PDF de la factura.";
    private const string LeyendaSimulado = "COMPROBANTE SIMULADO — SIN VALIDEZ FISCAL";

    private const string RazonSocial = "Óptica Ficticia S.R.L.";
    private const string Domicilio = "Calle Falsa 123";
    private const string CuitCanonico = "20000000001";
    private const string IngresosBrutos = "901-000000-1";
    private const string Cae = "71234567890123";
    private const string DniFicticio = "00123456";

    private readonly IGeneradorPdfFactura _generador = new GeneradorPdfFactura();

    // ---------------------------------------------------------------------------------------------------------------
    // Documento

    [Theory]
    [InlineData("123")]
    [InlineData("7123456789012A")]
    [InlineData("712345678901234")]
    [InlineData("")]
    public void Documento_CaeInvalido_Rechazado(string cae)
    {
        var error = Assert.Throws<ArgumentException>(() => DocumentoDirecto(cae: cae));

        Assert.Equal("cae", error.ParamName);
        if (cae.Length > 0)
        {
            Assert.DoesNotContain(cae, error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Documento_EmisorFaltante_Rechazado()
    {
        AfirmarRechazo("razonSocial", () => DocumentoDirecto(razonSocial: ""));
        AfirmarRechazo("razonSocial", () => DocumentoDirecto(razonSocial: null!));
        AfirmarRechazo("domicilioComercial", () => DocumentoDirecto(domicilio: "   "));
        AfirmarRechazo("cuitEmisor", () => DocumentoDirecto(cuit: ""));
        AfirmarRechazo("cuitEmisor", () => DocumentoDirecto(cuit: "20000000002"));
        AfirmarRechazo("ingresosBrutos", () => DocumentoDirecto(ingresosBrutos: ""));

        // Ningún mensaje repite el valor recibido.
        var error = Assert.Throws<ArgumentException>(() => DocumentoDirecto(cuit: "20000000002"));
        Assert.DoesNotContain("20000000002", error.Message, StringComparison.Ordinal);

        static void AfirmarRechazo(string campo, Func<DocumentoFactura> crear) =>
            Assert.Equal(campo, Assert.ThrowsAny<ArgumentException>(() => crear()).ParamName);
    }

    [Fact]
    public void Documento_TotalNegativoOTipoInvalido_Rechazado()
    {
        Assert.Equal("totalCentavos", Assert.Throws<ArgumentException>(() => DocumentoDirecto(total: -1)).ParamName);
        Assert.Equal(
            "tipo",
            Assert.Throws<ArgumentException>(() => DocumentoDirecto(tipo: (TipoComprobante)7)).ParamName);

        // La URL del QR siempre es la de ARCA (R-04).
        Assert.Equal(
            "urlQr",
            Assert.Throws<ArgumentException>(() => DocumentoDirecto(url: "https://otro.example/fe/qr/?p=e30=")).ParamName);
    }

    [Fact]
    public void Documento_Desde_TomaReglasYImportesDeLaFactura()
    {
        var documento = DocumentoFactura.Desde(
            FacturaB(ReceptorFactura.SinIdentificar, EntornoArca.Homologacion), Emisor(), "Pérez", "Ana", Marca(LogoPng()));

        Assert.Equal("Responsable Inscripto", documento.CondicionIva);
        Assert.True(documento.LlevaLeyendaSimulado);
        Assert.Equal(271002, documento.TotalCentavos);
        Assert.Equal([1002L, 270000L], documento.Lineas.Select(l => l.ImporteCentavos).ToArray());
        Assert.NotNull(documento.IvaContenidoCentavos);
        Assert.True(documento.EsConsumidorFinalSinIdentificar);
        Assert.Null(documento.DniReceptor);
        Assert.Null(documento.ApellidoReceptor);
        Assert.StartsWith(QrArca.UrlBase, documento.UrlQr, StringComparison.Ordinal);

        // El documento lleva datos personales: no los muestra en ToString.
        var conDni = DocumentoFactura.Desde(
            FacturaB(ReceptorFactura.ConDni(DniFicticio), EntornoArca.Produccion), Emisor(), "Pérez", "Ana", Marca(LogoPng()));
        var texto = conDni.ToString()!;
        foreach (var dato in new[] { DniFicticio, "Pérez", "Ana", QrArca.UrlBase })
        {
            Assert.DoesNotContain(dato, texto, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Documento_UrlDelQrSoloDesdeQrArca()
    {
        // D-23 (R-11) por construcción: fuera de Application no hay ninguna vía pública para pasar una URL del QR
        // arbitraria (que podría llevar el DNI en Simulado u Homologacion); la única entrada es Desde(Factura, ...),
        // que la arma con QrArca.
        var tipo = typeof(DocumentoFactura);
        Assert.Empty(tipo.GetConstructors());
        var entradas = tipo.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(m => m.ReturnType == tipo)
            .ToList();
        var desde = Assert.Single(entradas);
        Assert.Equal(nameof(DocumentoFactura.Desde), desde.Name);
        Assert.DoesNotContain(
            desde.GetParameters(),
            p => p.ParameterType == typeof(string) && p.Name!.Contains("url", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(desde.GetParameters(), p => p.ParameterType == typeof(Factura));
    }

    [Theory]
    [InlineData(EntornoArca.Simulado)]
    [InlineData(EntornoArca.Homologacion)]
    public void Documento_FueraDeProduccionConDni_QrSinDni(EntornoArca entorno)
    {
        // El PDF muestra el DNI (FR-08), pero el QR de un comprobante sin validez fiscal lleva 99 y 0 (D-23).
        var documento = DocumentoFactura.Desde(
            FacturaB(ReceptorFactura.ConDni(DniFicticio), entorno), Emisor(), "Pérez", "Ana", Marca(LogoPng()));

        Assert.Equal(DniFicticio, documento.DniReceptor);
        var json = System.Text.Json.JsonDocument.Parse(
            Convert.FromBase64String(documento.UrlQr[QrArca.UrlBase.Length..])).RootElement;
        Assert.Equal(99, json.GetProperty("tipoDocRec").GetInt32());
        Assert.Equal(0L, json.GetProperty("nroDocRec").GetInt64());
    }

    // ---------------------------------------------------------------------------------------------------------------
    // PDF

    [Fact]
    public void Pdf_LogoYColores()
    {
        var pdf = _generador.Generar(Documento(FacturaB(ReceptorFactura.SinIdentificar, EntornoArca.Produccion)));

        using var documento = PdfDocument.Open(pdf);
        var pagina = documento.GetPage(1);

        // El logo PNG y el QR, como imágenes.
        Assert.Equal(2, pagina.GetImages().Count());

        // El título del comprobante en el color primario (AC-06).
        AfirmarColorDe(pagina, "Factura", ColorPrimario);

        // El fondo de la página: un rectángulo relleno con el color de fondo que cubre la página.
        Assert.Contains(
            pagina.Paths,
            p => p.IsFilled
                && p.FillColor is not null
                && EsColor(p.FillColor, ColorFondo)
                && p.GetBoundingRectangle() is { } r
                && r.Width >= pagina.Width - 1
                && r.Height >= pagina.Height - 1);
    }

    [Fact]
    public void Pdf_EmisorYCondicionResponsableInscripto()
    {
        var pdf = _generador.Generar(Documento(FacturaB(ReceptorFactura.SinIdentificar, EntornoArca.Produccion)));

        var texto = TextoDe(pdf);
        Assert.Contains("Razón social: " + RazonSocial, texto, StringComparison.Ordinal);
        Assert.Contains("Domicilio comercial: " + Domicilio, texto, StringComparison.Ordinal);
        Assert.Contains("CUIT: 20-00000000-1", texto, StringComparison.Ordinal);
        Assert.Contains("Ingresos Brutos: " + IngresosBrutos, texto, StringComparison.Ordinal);
        Assert.Contains("Inicio de actividades: 01/04/2015", texto, StringComparison.Ordinal);
        Assert.Contains("Condición frente al IVA: Responsable Inscripto", texto, StringComparison.Ordinal);
    }

    [Fact]
    public void Pdf_FacturaCCondicionMonotributo()
    {
        var pdf = _generador.Generar(Documento(FacturaC(ReceptorFactura.SinIdentificar, EntornoArca.Produccion)));

        var texto = TextoDe(pdf);
        Assert.Contains("Condición frente al IVA: Responsable Monotributo", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("Responsable Inscripto", texto, StringComparison.Ordinal);
        Assert.Contains("Factura C", texto, StringComparison.Ordinal);
    }

    [Fact]
    public void Pdf_ComprobanteYFecha_B000300000125()
    {
        var pdf = _generador.Generar(Documento(FacturaB(ReceptorFactura.SinIdentificar, EntornoArca.Produccion)));

        var texto = TextoDe(pdf);
        Assert.Contains("Factura B", texto, StringComparison.Ordinal);
        Assert.Contains("Punto de venta: 0003", texto, StringComparison.Ordinal);
        Assert.Contains("Número: 00000125", texto, StringComparison.Ordinal);
        Assert.Contains("Fecha de emisión: 30/10/2026", texto, StringComparison.Ordinal);

        // El título del documento es el comprobante, sin datos personales.
        using var documento = PdfDocument.Open(pdf);
        Assert.Equal("Factura B 0003-00000125", documento.Information.Title);
        Assert.True(string.IsNullOrEmpty(documento.Information.Author));
    }

    [Fact]
    public void Pdf_ConsumidorFinalSinDni()
    {
        // AC-09: emitida sin identificar al receptor; el cliente del presupuesto no aparece en el PDF (FR-08).
        var pdf = _generador.Generar(Documento(FacturaB(ReceptorFactura.SinIdentificar, EntornoArca.Produccion)));

        var texto = TextoDe(pdf);
        Assert.Contains("Receptor: Consumidor Final", texto, StringComparison.Ordinal);
        foreach (var prohibido in new[] { "DNI", DniFicticio, "Pérez", "Apellido", "Nombre:" })
        {
            Assert.DoesNotContain(prohibido, texto, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Pdf_ReceptorConDniApellidoYNombre()
    {
        // AC-10: el DNI de Factura.Receptor, con Apellido y Nombre del cliente del presupuesto.
        var pdf = _generador.Generar(Documento(FacturaB(ReceptorFactura.ConDni(DniFicticio), EntornoArca.Produccion)));

        var texto = TextoDe(pdf);
        Assert.Contains("DNI: 00123456", texto, StringComparison.Ordinal);
        Assert.Contains("Apellido: Pérez", texto, StringComparison.Ordinal);
        Assert.Contains("Nombre: Ana", texto, StringComparison.Ordinal);

        // Ningún metadato lleva un dato del receptor.
        using var documento = PdfDocument.Open(pdf);
        foreach (var valor in documento.Information.DocumentInformationDictionary!.Data.Values)
        {
            var textoValor = valor?.ToString() ?? string.Empty;
            foreach (var dato in new[] { DniFicticio, "Pérez", "Ana" })
            {
                Assert.DoesNotContain(dato, textoValor, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void Pdf_LineasYTotal_271002()
    {
        // AC-11 y NFR-02: importes desde centavos con 2 decimales, coma decimal y punto de miles.
        var pdf = _generador.Generar(Documento(FacturaB(ReceptorFactura.SinIdentificar, EntornoArca.Produccion)));

        using var documento = PdfDocument.Open(pdf);
        var pagina = documento.GetPage(1);
        var palabras = pagina.GetWords().Select(p => p.Text).ToList();
        AfirmarSecuencia(palabras, "Lente", "orgánico", "3", "10,02");
        AfirmarSecuencia(palabras, "Armazón", "metálico", "1", "2.700,00");
        var texto = string.Join(' ', palabras);
        Assert.True(
            texto.IndexOf("Lente orgánico", StringComparison.Ordinal) < texto.IndexOf("Armazón metálico", StringComparison.Ordinal),
            "Las líneas no están en su orden.");
        Assert.Contains("Total: $ 2.710,02", texto, StringComparison.Ordinal);
        Assert.Contains("Descripción", texto, StringComparison.Ordinal);
        Assert.Contains("Cantidad", texto, StringComparison.Ordinal);
    }

    [Fact]
    public void Pdf_CaeVencimientoEImagenQr()
    {
        // Logo SVG (vectorial): la única imagen del PDF es el QR.
        var documentoFactura = DocumentoFactura.Desde(
            FacturaB(ReceptorFactura.SinIdentificar, EntornoArca.Produccion), Emisor(), "Pérez", "Ana", Marca(LogoSvg()));

        var pdf = _generador.Generar(documentoFactura);

        using var documento = PdfDocument.Open(pdf);
        var pagina = documento.GetPage(1);
        var texto = string.Join(' ', pagina.GetWords().Select(p => p.Text));
        Assert.Contains("CAE: " + Cae, texto, StringComparison.Ordinal);
        Assert.Contains("Vencimiento del CAE: 09/11/2026", texto, StringComparison.Ordinal);

        // El QR: una imagen cuadrada con módulos suficientes para la URL de ARCA (D-16: el escaneo es manual).
        var imagen = Assert.Single(pagina.GetImages());
        Assert.Equal(imagen.WidthInSamples, imagen.HeightInSamples);
        Assert.True(imagen.WidthInSamples >= 25, "La imagen del QR es demasiado chica.");

        // La URL no se escribe como texto en el PDF.
        Assert.DoesNotContain("afip.gob.ar", texto, StringComparison.Ordinal);
    }

    [Fact]
    public void Pdf_FacturaB_LeyendaLey27743Con31500()
    {
        // AC-13: Factura B de $1.815,00 con alícuota 21: IVA contenido $315,00, sin discriminarlo por línea.
        var lineas = new[] { new LineaFactura(1, 7, "Anteojo de sol", 1815.00m, 1, 0m, 1815.00m, 1815.00m) };
        var factura = new Factura(
            155, TipoComprobante.FacturaB, 3, 125, new DateOnly(2026, 10, 30), ConceptoFactura.Productos,
            ReceptorFactura.SinIdentificar, ImportesFactura.ParaFacturaB(1815.00m, 21m), Cae, new DateOnly(2026, 11, 9),
            EntornoArca.Produccion, lineas);

        var texto = TextoDe(_generador.Generar(Documento(factura)));

        Assert.Contains("Régimen de Transparencia Fiscal al Consumidor (Ley 27.743)", texto, StringComparison.Ordinal);
        Assert.Contains("IVA contenido: $ 315,00", texto, StringComparison.Ordinal);
        Assert.Equal(1, Apariciones(texto, "315,00"));
    }

    [Fact]
    public void Pdf_FacturaC_SinIvaContenido()
    {
        // AC-14: la Factura C no muestra ningún importe de IVA contenido.
        var lineas = new[] { new LineaFactura(1, 7, "Anteojo de sol", 1815.00m, 1, 0m, 1815.00m, 1815.00m) };
        var factura = new Factura(
            155, TipoComprobante.FacturaC, 3, 125, new DateOnly(2026, 10, 30), ConceptoFactura.Productos,
            ReceptorFactura.SinIdentificar, ImportesFactura.ParaFacturaC(1815.00m), Cae, new DateOnly(2026, 11, 9),
            EntornoArca.Produccion, lineas);

        var texto = TextoDe(_generador.Generar(Documento(factura)));

        Assert.Contains("Total: $ 1.815,00", texto, StringComparison.Ordinal);
        foreach (var prohibido in new[] { "IVA contenido", "27.743", "Transparencia", "315,00", "Neto", "Alícuota" })
        {
            Assert.DoesNotContain(prohibido, texto, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(EntornoArca.Simulado)]
    [InlineData(EntornoArca.Homologacion)]
    public void Pdf_SimuladoYHomologacion_LeyendaEnCadaPagina(EntornoArca entorno)
    {
        // AC-15 (R-05): 70 líneas no entran en una página; la leyenda va en el encabezado de cada una.
        var lineas = Enumerable.Range(1, 70)
            .Select(i => new LineaFactura(i, i, $"Artículo {i}", 10.00m, 1, 0m, 10.00m, 10.00m))
            .ToArray();
        var factura = new Factura(
            155, TipoComprobante.FacturaB, 3, 125, new DateOnly(2026, 10, 30), ConceptoFactura.Productos,
            ReceptorFactura.SinIdentificar, ImportesFactura.ParaFacturaB(700.00m, 21m), Cae, new DateOnly(2026, 11, 9),
            entorno, lineas);

        var pdf = _generador.Generar(Documento(factura));

        using var documento = PdfDocument.Open(pdf);
        Assert.True(documento.NumberOfPages >= 2, "La factura de prueba tenía que ocupar dos páginas.");
        foreach (var pagina in documento.GetPages())
        {
            var palabras = pagina.GetWords().ToList();
            Assert.Contains(LeyendaSimulado, string.Join(' ', palabras.Select(p => p.Text)), StringComparison.Ordinal);

            // En el encabezado: arriba de la página (PDF mide y desde abajo).
            var simulado = palabras.First(p => p.Text == "SIMULADO");
            Assert.True(
                simulado.BoundingBox.Bottom > pagina.Height * 0.8,
                $"La leyenda no está en el encabezado de la página {pagina.Number}.");
        }
    }

    [Fact]
    public void Pdf_Produccion_SinLeyenda()
    {
        // AC-16: emitida en producción, sin la leyenda.
        var pdf = _generador.Generar(Documento(FacturaB(ReceptorFactura.SinIdentificar, EntornoArca.Produccion)));

        var texto = TextoDe(pdf);
        Assert.DoesNotContain("SIMULADO", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("SIN VALIDEZ FISCAL", texto, StringComparison.Ordinal);
        Assert.Contains("Factura B", texto, StringComparison.Ordinal);
    }

    [Fact]
    public void Pdf_LogoInvalido_SeGeneraSinLogo()
    {
        // Firma PNG válida (la acepta LogoMarca) seguida de bytes que no son una imagen: el PDF sale sin logo.
        var corrupto = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02, 0x03, 0x04, 0x05 };
        var documentoFactura = DocumentoFactura.Desde(
            FacturaB(ReceptorFactura.SinIdentificar, EntornoArca.Homologacion), Emisor(), "Pérez", "Ana",
            Marca(LogoMarca.Crear("logo.png", corrupto)));

        var pdf = _generador.Generar(documentoFactura);

        using var documento = PdfDocument.Open(pdf);
        var pagina = documento.GetPage(1);

        // Solo el QR, y el resto del comprobante completo, con la leyenda.
        Assert.Single(pagina.GetImages());
        var texto = string.Join(' ', pagina.GetWords().Select(p => p.Text));
        Assert.Contains("Factura B", texto, StringComparison.Ordinal);
        Assert.Contains("CAE: " + Cae, texto, StringComparison.Ordinal);
        Assert.Contains(LeyendaSimulado, texto, StringComparison.Ordinal);
    }

    [Fact]
    public void Pdf_FallaInterna_ErrorConTextoFijo()
    {
        // Una URL de ARCA más larga que lo que entra en un QR: QRCoder falla. El error no lleva la causa ni datos.
        var documentoFactura = DocumentoDirecto(
            dni: DniFicticio, url: QrArca.UrlBase + new string('A', 5000));

        var error = Assert.Throws<InvalidOperationException>(() => _generador.Generar(documentoFactura));

        Assert.Equal(MensajeFalla, error.Message);
        Assert.Null(error.InnerException);
        var completo = error.ToString();
        foreach (var dato in new[] { DniFicticio, "Pérez", RazonSocial, "AAAA", "QRCoder", "QuestPDF" })
        {
            Assert.DoesNotContain(dato, completo, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Pdf_GeneradorRegistradoEnLaInfraestructura()
    {
        var servicios = new ServiceCollection();
        servicios.AgregarInfraestructura(new ConfigurationBuilder().Build());
        using var proveedor = servicios.BuildServiceProvider();

        var generador = proveedor.GetRequiredService<IGeneradorPdfFactura>();
        Assert.IsType<GeneradorPdfFactura>(generador);
        Assert.Same(generador, proveedor.GetRequiredService<IGeneradorPdfFactura>());
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Datos ficticios

    private static DatosEmisor Emisor() =>
        DatosEmisor.Leer(RazonSocial, Domicilio, CuitCanonico, IngresosBrutos, new DateOnly(2015, 4, 1));

    /// <summary>AC-11: una línea de 3 unidades con importe 10,02 y otra de 2.700,00; total 2.710,02.</summary>
    private static LineaFactura[] Lineas() =>
    [
        new LineaFactura(1, 1, "Lente orgánico", 6.67m, 3, 50m, 3.34m, 10.02m),
        new LineaFactura(2, 2, "Armazón metálico", 2700.00m, 1, 0m, 2700.00m, 2700.00m),
    ];

    /// <summary>AC-08: la Factura B 0003-00000125 del 30/10/2026.</summary>
    private static Factura FacturaB(ReceptorFactura receptor, EntornoArca entorno) =>
        new(155, TipoComprobante.FacturaB, 3, 125, new DateOnly(2026, 10, 30), ConceptoFactura.Productos, receptor,
            ImportesFactura.ParaFacturaB(2710.02m, 21m), Cae, new DateOnly(2026, 11, 9), entorno, Lineas());

    private static Factura FacturaC(ReceptorFactura receptor, EntornoArca entorno) =>
        new(155, TipoComprobante.FacturaC, 3, 125, new DateOnly(2026, 10, 30), ConceptoFactura.Productos, receptor,
            ImportesFactura.ParaFacturaC(2710.02m), Cae, new DateOnly(2026, 11, 9), entorno, Lineas());

    /// <summary>El documento de la factura con el cliente "Pérez, Ana" del presupuesto y el logo PNG.</summary>
    private static DocumentoFactura Documento(Factura factura) =>
        DocumentoFactura.Desde(factura, Emisor(), "Pérez", "Ana", Marca(LogoPng()));

    private static DocumentoFactura DocumentoDirecto(
        string razonSocial = RazonSocial, string domicilio = Domicilio, string cuit = CuitCanonico,
        string ingresosBrutos = IngresosBrutos, TipoComprobante tipo = TipoComprobante.FacturaB, long total = 271002,
        string cae = Cae, string? dni = null, string? url = null) =>
        new(
            razonSocial, domicilio, cuit, ingresosBrutos, new DateOnly(2015, 4, 1), tipo, 3, 125,
            new DateOnly(2026, 10, 30), dni, dni is null ? null : "Pérez", dni is null ? null : "Ana",
            [new LineaDocumentoFactura("Lente orgánico", 1, total < 0 ? 0 : total)], total,
            tipo == TipoComprobante.FacturaB ? 0 : null, cae, new DateOnly(2026, 11, 9),
            url ?? QrArca.UrlBase + "e30=", EntornoArca.Homologacion, Marca(LogoPng()));

    private static IdentidadVisual Marca(LogoMarca logo) =>
        IdentidadVisual.Crear(ColorPrimario, ColorFondo, logo, esEjemplo: false);

    private static LogoMarca LogoPng() => LogoMarca.Crear("logo.png", Png2x2());

    private static LogoMarca LogoSvg() => LogoMarca.Crear(
        "logo.svg",
        Encoding.UTF8.GetBytes(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"100\" height=\"40\" viewBox=\"0 0 100 40\">" +
            "<rect x=\"1\" y=\"1\" width=\"98\" height=\"38\" fill=\"none\" stroke=\"#5A5A5A\" stroke-width=\"2\"/>" +
            "</svg>"));

    // ---------------------------------------------------------------------------------------------------------------
    // Lectura del PDF

    /// <summary>Las palabras de todas las páginas en el orden del contenido, separadas por un espacio.</summary>
    private static string TextoDe(byte[] pdf)
    {
        using var documento = PdfDocument.Open(pdf);
        return string.Join(' ', documento.GetPages().SelectMany(p => p.GetWords()).Select(p => p.Text));
    }

    private static int Apariciones(string texto, string buscado) =>
        texto.Split(buscado, StringSplitOptions.None).Length - 1;

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

    /// <summary>Todas las letras de la primera palabra igual a <paramref name="palabra"/> tienen el color pedido.</summary>
    private static void AfirmarColorDe(Page pagina, string palabra, string color)
    {
        var encontrada = pagina.GetWords().FirstOrDefault(p => p.Text == palabra);
        Assert.True(encontrada is not null, $"No aparece \"{palabra}\".");
        Assert.All(encontrada!.Letters, letra => Assert.True(EsColor(letra.Color, color), $"\"{palabra}\" no está en {color}."));
    }

    private static bool EsColor(IColor color, string hex)
    {
        var (r, g, b) = color.ToRGBValues();
        var esperado = Convert.FromHexString(hex.AsSpan(1));
        return Cerca(r, esperado[0]) && Cerca(g, esperado[1]) && Cerca(b, esperado[2]);

        static bool Cerca(double valor, byte componente) => Math.Abs((valor * 255) - componente) < 1.5;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Logo decodificable armado en la prueba

    /// <summary>Un PNG RGB de 2 × 2 píxeles válido: firma, IHDR, IDAT comprimido con zlib e IEND, con sus CRC.</summary>
    private static byte[] Png2x2()
    {
        using var salida = new MemoryStream();
        salida.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        EscribirTrozo(salida, "IHDR", [0, 0, 0, 2, 0, 0, 0, 2, 8, 2, 0, 0, 0]);
        byte[] filas = [0, 0x09, 0x03, 0xA0, 0xFF, 0xFF, 0xFF, 0, 0xFF, 0xFF, 0xFF, 0x09, 0x03, 0xA0];
        using (var comprimido = new MemoryStream())
        {
            using (var zlib = new ZLibStream(comprimido, CompressionLevel.Optimal, leaveOpen: true))
            {
                zlib.Write(filas);
            }

            EscribirTrozo(salida, "IDAT", comprimido.ToArray());
        }

        EscribirTrozo(salida, "IEND", []);
        return salida.ToArray();
    }

    private static void EscribirTrozo(Stream salida, string tipo, byte[] datos)
    {
        var tipoBytes = Encoding.ASCII.GetBytes(tipo);
        EscribirEnteroBigEndian(salida, (uint)datos.Length);
        salida.Write(tipoBytes);
        salida.Write(datos);
        EscribirEnteroBigEndian(salida, Crc32([.. tipoBytes, .. datos]));
    }

    private static void EscribirEnteroBigEndian(Stream salida, uint valor) =>
        salida.Write([(byte)(valor >> 24), (byte)(valor >> 16), (byte)(valor >> 8), (byte)valor]);

    private static uint Crc32(byte[] datos)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var dato in datos)
        {
            crc ^= dato;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }
}
