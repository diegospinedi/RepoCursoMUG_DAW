using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Optica.Application.Presupuestos;
using Optica.Domain.Marca;
using Optica.Domain.Presupuestos;
using Optica.Infrastructure.Pdf;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Graphics.Colors;

namespace Optica.Infrastructure.Tests.Pdf;

/// <summary>
/// El PDF del presupuesto en Final (FR-13 a FR-16, NFR-03): se lee con PdfPig (D-02) para verificar textos, colores,
/// logo y metadatos. Los datos del cliente son ficticios. Los logos PNG y JPEG se arman en la prueba y el SVG es el
/// <c>marca-ejemplo/logo.svg</c> del repositorio; nunca se lee la carpeta <c>/Marca/</c> de una instalación.
/// </summary>
public sealed class GeneradorPdfPresupuestoTests
{
    private const string ColorPrimario = "#0903A0";
    private const string ColorFondo = "#FFFFFF";
    private const string MensajeBorrador = "El PDF solo se genera para un presupuesto en Final.";
    private const string MensajeFalla = "No se pudo generar el PDF del presupuesto.";

    private readonly IGeneradorPdfPresupuesto _generador = new GeneradorPdfPresupuesto();

    [Fact]
    public void Pdf_LogoYColoresDeLaMarca()
    {
        var pdf = _generador.Generar(PresupuestoAc13(), Marca(LogoPng()));

        using var documento = PdfDocument.Open(pdf);
        var pagina = documento.GetPage(1);

        // El logo PNG está como imagen.
        Assert.NotEmpty(pagina.GetImages());

        // El número, la fecha y los encabezados de la tabla, en el color primario (AC-11).
        AfirmarColorDe(pagina, "Presupuesto", ColorPrimario);
        AfirmarColorDe(pagina, "Fecha:", ColorPrimario);
        AfirmarColorDe(pagina, "Descripción", ColorPrimario);

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
    public void Pdf_LogoYColoresDeLaMarca_OtroFondo()
    {
        // El fondo sale de la marca y no es el blanco por defecto de la página.
        var pdf = _generador.Generar(PresupuestoAc13(), Marca(LogoPng(), colorFondo: "#F5F5F5"));

        using var documento = PdfDocument.Open(pdf);
        var pagina = documento.GetPage(1);
        Assert.Contains(
            pagina.Paths,
            p => p.IsFilled
                && p.FillColor is not null
                && EsColor(p.FillColor, "#F5F5F5")
                && p.GetBoundingRectangle() is { } r
                && r.Width >= pagina.Width - 1
                && r.Height >= pagina.Height - 1);
    }

    [Fact]
    public void Pdf_LogoPng_SeDibuja()
    {
        var pdf = _generador.Generar(PresupuestoAc13(), Marca(LogoPng()));

        using var documento = PdfDocument.Open(pdf);
        Assert.Single(documento.GetPage(1).GetImages());
    }

    [Fact]
    public void Pdf_LogoJpeg_SeDibuja()
    {
        var pdf = _generador.Generar(PresupuestoAc13(), Marca(LogoMarca.Crear("logo.jpg", JpegGris8x8())));

        using var documento = PdfDocument.Open(pdf);
        Assert.Single(documento.GetPage(1).GetImages());
    }

    [Fact]
    public void Pdf_LogoSvg_SeDibuja()
    {
        // El SVG de ejemplo del repositorio, con un <text> en Arial: sin fuentes del sistema se dibuja con la fuente
        // incluida (D-14), también en la CI de Linux. Lo que se verifica (trazo como vector, texto leído como "Óptica")
        // depende de la versión fijada de QuestPDF (2026.9.1, con su Skia) y del logo de ejemplo: si cambia alguno de
        // los dos, revisar esta prueba.
        var svg = File.ReadAllBytes(Path.Combine(RaizDelRepositorio(), "marca-ejemplo", "logo.svg"));
        var pdf = _generador.Generar(PresupuestoAc13(), Marca(LogoMarca.Crear("logo.svg", svg)));

        using var documento = PdfDocument.Open(pdf);
        var pagina = documento.GetPage(1);

        // Se dibuja como vector, no como imagen: el trazo gris del marco y los anteojos, y el texto del logo.
        Assert.Empty(pagina.GetImages());
        Assert.Contains(pagina.Paths, p => p.IsStroked && p.StrokeColor is not null && EsColor(p.StrokeColor, "#5A5A5A"));
        Assert.Contains("Óptica", TextoDe(pagina), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("href", "archivo")]
    [InlineData("xlink:href", "archivo")]
    [InlineData("href", "http")]
    public void Pdf_LogoSvgConReferenciaExterna_NoLaSigue(string atributo, string destino)
    {
        // Un SVG que LogoMarca acepta (XML sin DTD, raíz svg) puede igual nombrar un recurso externo en <image>. El
        // generador no tiene que leer un archivo local ni pedir una URL (R-06): el PDF sale sin esa imagen. La URL
        // apunta a un puerto cerrado de 127.0.0.1, así la prueba no sale a internet.
        var carpeta = Path.Combine(Path.GetTempPath(), "optica-pruebas-pdf", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(carpeta);
        try
        {
            var png = Path.Combine(carpeta, "externo.png");
            File.WriteAllBytes(png, Png2x2());
            var url = destino == "archivo" ? new Uri(png).AbsoluteUri : "http://127.0.0.1:1/x.png";
            var svg = Encoding.UTF8.GetBytes(
                "<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" " +
                "width=\"100\" height=\"40\" viewBox=\"0 0 100 40\">" +
                "<rect x=\"1\" y=\"1\" width=\"98\" height=\"38\" fill=\"none\" stroke=\"#5A5A5A\" stroke-width=\"2\"/>" +
                $"<image x=\"10\" y=\"5\" width=\"30\" height=\"30\" {atributo}=\"{url}\"/>" +
                "</svg>");

            var pdf = _generador.Generar(PresupuestoAc13(), Marca(LogoMarca.Crear("logo.svg", svg)));

            using var documento = PdfDocument.Open(pdf);
            var pagina = documento.GetPage(1);

            // El SVG se dibujó (su marco) y no hay ninguna imagen: el recurso externo no se leyó.
            Assert.Contains(pagina.Paths, p => p.IsStroked && p.StrokeColor is not null && EsColor(p.StrokeColor, "#5A5A5A"));
            Assert.Empty(pagina.GetImages());
        }
        finally
        {
            Directory.Delete(carpeta, recursive: true);
        }
    }

    [Fact]
    public void Pdf_LogoSvgConImagenIncrustada_SeDibuja()
    {
        // Control de la prueba anterior: una imagen incrustada en el SVG (data:) sí aparece como imagen en el PDF, así
        // "ninguna imagen" allá significa que el recurso externo no se siguió y no que PdfPig no ve imágenes de un SVG.
        var datos = "data:image/png;base64," + Convert.ToBase64String(Png2x2());
        var svg = Encoding.UTF8.GetBytes(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"100\" height=\"40\" viewBox=\"0 0 100 40\">" +
            $"<image x=\"10\" y=\"5\" width=\"30\" height=\"30\" href=\"{datos}\"/>" +
            "</svg>");

        var pdf = _generador.Generar(PresupuestoAc13(), Marca(LogoMarca.Crear("logo.svg", svg)));

        using var documento = PdfDocument.Open(pdf);
        Assert.NotEmpty(documento.GetPage(1).GetImages());
    }

    [Fact]
    public void Pdf_ClienteConGlifoFueraDeLaFuente_SeGeneraConReemplazo()
    {
        // Un emoji (fuera de la fuente Lato) en el Apellido no corta la generación (D-15). Cómo se dibuja el reemplazo
        // (un solo glifo, que PdfPig lee como U+0000) depende de la versión fijada de QuestPDF (2026.9.1) y de su
        // fuente Lato: si cambia la versión, revisar esta prueba.
        var cliente = new ClientePresupuesto("Ficticio\U0001F600Z", "Prueba", "30123456", null, null, "011 5555-0000");
        var presupuesto = new Presupuesto(
            155, new DateOnly(2026, 3, 10), EstadoPresupuesto.Final, cliente, LineasAc14(), version: 0);

        var pdf = _generador.Generar(presupuesto, Marca(LogoPng()));

        using var documento = PdfDocument.Open(pdf);
        var pagina = documento.GetPage(1);
        Assert.Contains("Presupuesto N.º 155", TextoDe(pagina), StringComparison.Ordinal);

        // En lugar del emoji se dibuja un solo glifo de reemplazo y el texto sigue después de él.
        var dibujado = LetrasSinEspacios(pagina);
        Assert.Matches("Ficticio[^Z]Z", dibujado);
        Assert.DoesNotContain("\U0001F600", dibujado, StringComparison.Ordinal);
    }

    [Fact]
    public void Pdf_FuenteIncluidaEnLaSalida()
    {
        // La fuente Lato viaja con el paquete y se copia junto al ensamblado (D-14); la publicación de la Api la verifica
        // PublicacionSinRutasTests.
        var carpeta = Path.GetDirectoryName(typeof(GeneradorPdfPresupuesto).Assembly.Location)!;
        Assert.True(
            File.Exists(Path.Combine(carpeta, "QuestPDF.Fonts.Lato.br")),
            "La fuente QuestPDF.Fonts.Lato.br no está junto al ensamblado.");
    }

    [Fact]
    public void Pdf_ClienteSinDomicilioNiEmail_SoloCuatroDatos()
    {
        var pdf = _generador.Generar(PresupuestoAc13(), Marca(LogoPng()));

        using var documento = PdfDocument.Open(pdf);
        var texto = TextoDe(documento.GetPage(1));

        Assert.Contains("Presupuesto N.º 155", texto, StringComparison.Ordinal);
        Assert.Contains("Fecha: 10/03/2026", texto, StringComparison.Ordinal);
        Assert.Contains("Apellido: Ficticio", texto, StringComparison.Ordinal);
        Assert.Contains("Nombre: Prueba", texto, StringComparison.Ordinal);
        Assert.Contains("DNI: 30123456", texto, StringComparison.Ordinal);
        Assert.Contains("Nro. de Teléfono: 011 5555-0000", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("Domicilio", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("Email", texto, StringComparison.Ordinal);
    }

    [Fact]
    public void Pdf_ClienteCompleto_SeisDatos()
    {
        var cliente = new ClientePresupuesto(
            "Ficticio", "Prueba", "30123456", "Calle Falsa 123", "prueba@ejemplo.com", "011 5555-0000");
        var presupuesto = new Presupuesto(
            7, new DateOnly(2026, 11, 2), EstadoPresupuesto.Final, cliente, LineasAc14(), version: 3);

        var pdf = _generador.Generar(presupuesto, Marca(LogoPng()));

        using var documento = PdfDocument.Open(pdf);
        var texto = TextoDe(documento.GetPage(1));
        Assert.Contains("Presupuesto N.º 7", texto, StringComparison.Ordinal);
        Assert.Contains("Fecha: 02/11/2026", texto, StringComparison.Ordinal);
        Assert.Contains("Domicilio: Calle Falsa 123", texto, StringComparison.Ordinal);
        Assert.Contains("Email: prueba@ejemplo.com", texto, StringComparison.Ordinal);
    }

    [Fact]
    public void Pdf_LineasConSieteCamposYTotal()
    {
        var pdf = _generador.Generar(PresupuestoAc13(), Marca(LogoPng()));

        using var documento = PdfDocument.Open(pdf);
        var pagina = documento.GetPage(1);
        var texto = TextoDe(pagina);

        // Los encabezados largos ocupan dos renglones en su celda y las palabras se leen por renglón: se comparan las
        // letras en el orden en que se dibujaron, sin espacios.
        var dibujado = LetrasSinEspacios(pagina);
        foreach (var encabezado in new[]
        {
            "Código de Artículo", "Descripción", "Precio Unitario ($)", "Cantidad", "Porcentaje de Descuento (%)",
            "Precio con Descuento ($)", "Precio Final ($)",
        })
        {
            Assert.True(
                dibujado.Contains(encabezado.Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal),
                $"Falta el encabezado \"{encabezado}\".");
        }

        // Primera línea: código, descripción y los cinco importes y cantidades de AC-14, en su orden.
        var palabras = pagina.GetWords().Select(p => p.Text).ToList();
        AfirmarSecuencia(palabras, "1", "Lente", "orgánico", "6,67", "3", "50", "3,34", "10,02");
        AfirmarSecuencia(palabras, "2", "Armazón", "metálico", "2.700,00", "1", "0", "2.700,00", "2.700,00");
        Assert.True(
            texto.IndexOf("Lente orgánico", StringComparison.Ordinal) < texto.IndexOf("Armazón metálico", StringComparison.Ordinal),
            "Las líneas no están en su orden.");
        Assert.DoesNotContain("50,00", palabras);
        Assert.Contains("Total: $ 2.710,02", texto, StringComparison.Ordinal);
    }

    [Fact]
    public void Pdf_DescuentoConDecimales_SinCerosALaDerecha()
    {
        // D-09: el descuento como mostrarDecimal ("12,5"), los importes con 2 decimales y punto de miles.
        var lineas = new[] { new LineaPresupuesto(9, "Estuche", 1234.5m, 2, 12.5m, 1080.19m, 2160.38m) };
        var presupuesto = new Presupuesto(
            20, new DateOnly(2026, 1, 5), EstadoPresupuesto.Final, ClienteAc13(), lineas, version: 1);

        var pdf = _generador.Generar(presupuesto, Marca(LogoPng()));

        using var documento = PdfDocument.Open(pdf);
        var pagina = documento.GetPage(1);
        var palabras = pagina.GetWords().Select(p => p.Text).ToList();
        AfirmarSecuencia(palabras, "9", "Estuche", "1.234,50", "2", "12,5", "1.080,19", "2.160,38");
        Assert.Contains("Total: $ 2.160,38", TextoDe(pagina), StringComparison.Ordinal);
    }

    [Fact]
    public void Pdf_LeyendaIvaIncluido_SinIvaDiscriminado()
    {
        var pdf = _generador.Generar(PresupuestoAc13(), Marca(LogoPng()));

        using var documento = PdfDocument.Open(pdf);
        var texto = TextoDe(documento.GetPage(1));

        Assert.Contains("Precios finales, IVA incluido", texto, StringComparison.Ordinal);
        var apariciones = texto.Split("IVA", StringSplitOptions.None).Length - 1;
        Assert.Equal(1, apariciones);
        foreach (var prohibido in new[] { "alícuota", "Alícuota", "21 %", "21%", "10,5 %", "10,5%", "Neto", "Subtotal" })
        {
            Assert.DoesNotContain(prohibido, texto, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Pdf_MetadatosSinDatosDelCliente()
    {
        var pdf = _generador.Generar(PresupuestoAc13(), Marca(LogoPng()));

        using var documento = PdfDocument.Open(pdf);
        var informacion = documento.Information;
        Assert.Equal("Presupuesto 155", informacion.Title);
        Assert.True(string.IsNullOrEmpty(informacion.Author));
        Assert.True(string.IsNullOrEmpty(informacion.Subject));
        Assert.True(string.IsNullOrEmpty(informacion.Keywords));

        // Ningún valor del diccionario de información lleva un dato del cliente.
        var diccionario = informacion.DocumentInformationDictionary;
        Assert.NotNull(diccionario);
        foreach (var valor in diccionario!.Data.Values)
        {
            var textoValor = valor?.ToString() ?? string.Empty;
            foreach (var dato in new[] { "Ficticio", "Prueba", "30123456", "5555" })
            {
                Assert.DoesNotContain(dato, textoValor, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void Pdf_GeneradorRegistradoEnLaInfraestructura()
    {
        var servicios = new ServiceCollection();
        servicios.AgregarInfraestructura(new ConfigurationBuilder().Build());
        using var proveedor = servicios.BuildServiceProvider();

        Assert.IsType<GeneradorPdfPresupuesto>(proveedor.GetRequiredService<IGeneradorPdfPresupuesto>());
    }

    [Fact]
    public void Pdf_PresupuestoEnBorrador_Rechazado()
    {
        var borrador = Presupuesto.Nuevo(155, new DateOnly(2026, 3, 10), ClienteAc13(), LineasAc14());

        var error = Assert.Throws<ArgumentException>(() => _generador.Generar(borrador, Marca(LogoPng())));
        Assert.Equal(MensajeBorrador, error.Message.Split(" (Parameter", StringSplitOptions.None)[0]);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public void Pdf_PresupuestoNulo_Rechazado()
    {
        Assert.Throws<ArgumentNullException>(() => _generador.Generar(null!, Marca(LogoPng())));
        Assert.Throws<ArgumentNullException>(() => _generador.Generar(PresupuestoAc13(), null!));
    }

    [Fact]
    public void Pdf_LogoIlegible_ErrorSinRuta()
    {
        // Firma PNG válida (la acepta LogoMarca) seguida de bytes que no son una imagen.
        var corrupto = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02, 0x03, 0x04, 0x05 };

        var error = Assert.Throws<InvalidOperationException>(
            () => _generador.Generar(PresupuestoAc13(), Marca(LogoMarca.Crear("logo.png", corrupto))));

        Assert.Equal(MensajeFalla, error.Message);
        Assert.Null(error.InnerException);
        var completo = error.ToString();
        foreach (var dato in new[] { "Ficticio", "Prueba", "30123456", "logo.png" })
        {
            Assert.DoesNotContain(dato, completo, StringComparison.Ordinal);
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Datos ficticios

    private static ClientePresupuesto ClienteAc13() =>
        new("Ficticio", "Prueba", "30123456", domicilio: null, email: null, telefono: "011 5555-0000");

    /// <summary>AC-14: 6,67 × 3 con 50 % de descuento y 2.700,00 × 1 sin descuento; total 2.710,02.</summary>
    private static LineaPresupuesto[] LineasAc14() =>
    [
        new LineaPresupuesto(1, "Lente orgánico", 6.67m, 3, 50m, 3.34m, 10.02m),
        new LineaPresupuesto(2, "Armazón metálico", 2700.00m, 1, 0m, 2700.00m, 2700.00m),
    ];

    /// <summary>AC-13: el presupuesto 155 del 10/03/2026, en Final, sin Domicilio ni Email.</summary>
    private static Presupuesto PresupuestoAc13() =>
        new(155, new DateOnly(2026, 3, 10), EstadoPresupuesto.Final, ClienteAc13(), LineasAc14(), version: 2);

    private static IdentidadVisual Marca(LogoMarca logo, string colorFondo = ColorFondo) =>
        IdentidadVisual.Crear(ColorPrimario, colorFondo, logo, esEjemplo: false);

    private static LogoMarca LogoPng() => LogoMarca.Crear("logo.png", Png2x2());

    // ---------------------------------------------------------------------------------------------------------------
    // Lectura del PDF

    /// <summary>Las palabras de la página en el orden del contenido, separadas por un espacio.</summary>
    private static string TextoDe(Page pagina) => string.Join(' ', pagina.GetWords().Select(p => p.Text));

    /// <summary>Las letras de la página en el orden en que se dibujaron, sin espacios.</summary>
    private static string LetrasSinEspacios(Page pagina) =>
        string.Concat(pagina.Letters.Select(l => l.Value).Where(v => !string.IsNullOrWhiteSpace(v)));

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

    private static string RaizDelRepositorio()
    {
        var directorio = new DirectoryInfo(AppContext.BaseDirectory);
        while (directorio is not null && !File.Exists(Path.Combine(directorio.FullName, "Optica.sln")))
        {
            directorio = directorio.Parent;
        }

        return directorio?.FullName ?? throw new InvalidOperationException("No se encontró Optica.sln.");
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Logos decodificables armados en la prueba

    /// <summary>Un PNG RGB de 2 × 2 píxeles válido: firma, IHDR, IDAT comprimido con zlib e IEND, con sus CRC.</summary>
    private static byte[] Png2x2()
    {
        using var salida = new MemoryStream();
        salida.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        // Ancho 2, alto 2, 8 bits por canal, color RGB (2), compresión 0, filtro 0, sin entrelazado.
        EscribirTrozo(salida, "IHDR", [0, 0, 0, 2, 0, 0, 0, 2, 8, 2, 0, 0, 0]);

        // Cada fila: el byte de filtro (0) y 2 píxeles RGB.
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

    /// <summary>
    /// Un JPEG baseline en escala de grises de 8 × 8 píxeles válido: tabla de cuantización de unos, una tabla de Huffman
    /// DC y una AC con un solo código de 1 bit cada una, y un único bloque con DC 0 y fin de bloque (gris medio).
    /// </summary>
    private static byte[] JpegGris8x8()
    {
        var bytes = new List<byte> { 0xFF, 0xD8 };

        // DQT: tabla 0, precisión de 8 bits, 64 valores en 1.
        bytes.AddRange([0xFF, 0xDB, 0x00, 0x43, 0x00]);
        bytes.AddRange(Enumerable.Repeat((byte)1, 64));

        // SOF0: 8 bits, alto 8, ancho 8, 1 componente (id 1, muestreo 1x1, tabla de cuantización 0).
        bytes.AddRange([0xFF, 0xC0, 0x00, 0x0B, 0x08, 0x00, 0x08, 0x00, 0x08, 0x01, 0x01, 0x11, 0x00]);

        // DHT DC 0 y AC 0: un código de largo 1 para el símbolo 0 (categoría 0 en DC, fin de bloque en AC).
        foreach (var clase in new byte[] { 0x00, 0x10 })
        {
            bytes.AddRange([0xFF, 0xC4, 0x00, 0x14, clase, 0x01]);
            bytes.AddRange(Enumerable.Repeat((byte)0, 15));
            bytes.Add(0x00);
        }

        // SOS: 1 componente con las tablas DC 0 y AC 0, selección espectral 0-63, sin aproximación sucesiva.
        bytes.AddRange([0xFF, 0xDA, 0x00, 0x08, 0x01, 0x01, 0x00, 0x00, 0x3F, 0x00]);

        // Datos: "0" (DC categoría 0) y "0" (fin de bloque), completado con unos.
        bytes.Add(0x3F);
        bytes.AddRange([0xFF, 0xD9]);
        return [.. bytes];
    }
}
