using System.Text;
using Optica.Domain.Marca;

namespace Optica.Domain.Tests.Marca;

public class IdentidadVisualTests
{
    private static readonly byte[] FirmaPng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    [Theory]
    [InlineData("#0903A0", true)]
    [InlineData("#FFFFFF", true)]
    [InlineData("#abcdef", true)]
    [InlineData("azul", false)]
    [InlineData("#0903A", false)]
    [InlineData("#0903A00", false)]
    [InlineData("0903A0", false)]
    [InlineData("#GGGGGG", false)]
    [InlineData(" #0903A0", false)]
    [InlineData("#0903A0 ", false)]
    [InlineData("#0903A0\n", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void EsColorValido_SoloFormatoRRGGBB(string? color, bool esperado)
    {
        Assert.Equal(esperado, IdentidadVisual.EsColorValido(color));
    }

    [Fact]
    public void Crear_ConColoresValidos_ConservaLosDatos()
    {
        var logo = LogoMarca.Crear("logo.png", Png(10));

        var marca = IdentidadVisual.Crear("#0903A0", "#FFFFFF", logo, esEjemplo: false);

        Assert.Equal("#0903A0", marca.ColorPrimario);
        Assert.Equal("#FFFFFF", marca.ColorFondo);
        Assert.Same(logo, marca.Logo);
        Assert.False(marca.EsEjemplo);
    }

    [Theory]
    [InlineData("azul", "#FFFFFF")]
    [InlineData(null, "#FFFFFF")]
    [InlineData("#0903A0", "blanco")]
    [InlineData("#0903A0", null)]
    public void Crear_ConColorInvalido_RechazadaPorInvalida(string? primario, string? fondo)
    {
        var logo = LogoMarca.Crear("logo.png", Png(10));

        var error = Assert.Throws<MarcaInvalidaException>(() => IdentidadVisual.Crear(primario, fondo, logo, esEjemplo: false));

        // El mensaje explica la regla sin repetir el valor recibido.
        Assert.Contains("#RRGGBB", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("azul", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("blanco", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Crear_SinLogo_Rechazada()
    {
        Assert.Throws<ArgumentNullException>(() => IdentidadVisual.Crear("#0903A0", "#FFFFFF", null!, esEjemplo: true));
    }

    [Theory]
    [InlineData("logo.png", FormatoLogo.Png)]
    [InlineData("logo.jpg", FormatoLogo.Jpeg)]
    [InlineData("logo.svg", FormatoLogo.Svg)]
    [InlineData("mi.logo.png", FormatoLogo.Png)]
    public void FormatoDe_ExtensionesPermitidas(string nombre, FormatoLogo esperado)
    {
        Assert.Equal(esperado, LogoMarca.FormatoDe(nombre));
    }

    [Theory]
    [InlineData("logo.jpeg")]
    [InlineData("logo.PNG")]
    [InlineData("logo.gif")]
    [InlineData("logo.png.exe")]
    [InlineData("logo.png ")]
    [InlineData("logo.png.")]
    [InlineData("logo")]
    [InlineData("")]
    [InlineData(null)]
    public void FormatoDe_ExtensionNoPermitida_SinFormato(string? nombre)
    {
        Assert.Null(LogoMarca.FormatoDe(nombre));
    }

    [Fact]
    public void LogoMarca_PngValido_ConservaFormatoYContenido()
    {
        var contenido = Png(32);

        var logo = LogoMarca.Crear("logo.png", contenido);

        Assert.Equal(FormatoLogo.Png, logo.Formato);
        Assert.Equal(contenido, logo.Contenido.ToArray());
    }

    [Fact]
    public void LogoMarca_CopiaElContenido()
    {
        var contenido = Png(32);
        var logo = LogoMarca.Crear("logo.png", contenido);

        contenido[^1] = 0x7F;

        Assert.NotEqual(contenido, logo.Contenido.ToArray());
    }

    [Fact]
    public void LogoMarca_JpegValido()
    {
        byte[] contenido = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];

        Assert.Equal(FormatoLogo.Jpeg, LogoMarca.Crear("logo.jpg", contenido).Formato);
    }

    [Fact]
    public void LogoMarca_SvgValido()
    {
        var contenido = Encoding.UTF8.GetBytes(
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\"><rect width=\"10\" height=\"10\"/></svg>");

        Assert.Equal(FormatoLogo.Svg, LogoMarca.Crear("logo.svg", contenido).Formato);
    }

    [Fact]
    public void LogoMarca_DeExactamente1MB_Valido()
    {
        Assert.Equal(1024 * 1024, LogoMarca.TamanoMaximo);

        var logo = LogoMarca.Crear("logo.png", Png(LogoMarca.TamanoMaximo));

        Assert.Equal(LogoMarca.TamanoMaximo, logo.Contenido.Length);
    }

    [Fact]
    public void LogoMarca_DeMasDe1MB_RechazadoPorGrande()
    {
        var error = Assert.Throws<MarcaInvalidaException>(() => LogoMarca.Crear("logo.png", Png(LogoMarca.TamanoMaximo + 1)));

        Assert.Contains("1 MB", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LogoMarca_ExtensionNoPermitida_Rechazado()
    {
        Assert.Throws<MarcaInvalidaException>(() => LogoMarca.Crear("logo.gif", Png(10)));
    }

    [Theory]
    [InlineData("logo.png")]
    [InlineData("logo.jpg")]
    [InlineData("logo.svg")]
    public void LogoMarca_ContenidoQueNoEsDelFormato_Rechazado(string nombre)
    {
        // Un archivo de texto con extensión de imagen no se sirve como imagen.
        var texto = Encoding.UTF8.GetBytes("<html><script>alert(1)</script></html>");

        Assert.Throws<MarcaInvalidaException>(() => LogoMarca.Crear(nombre, texto));
    }

    [Theory]
    [InlineData("logo.png")]
    [InlineData("logo.jpg")]
    [InlineData("logo.svg")]
    public void LogoMarca_Vacio_Rechazado(string nombre)
    {
        Assert.Throws<MarcaInvalidaException>(() => LogoMarca.Crear(nombre, []));
    }

    [Theory]
    // Con DTD (posible expansión de entidades o lectura de archivos externos): rechazado sin procesarla.
    [InlineData("<?xml version=\"1.0\"?><!DOCTYPE svg [<!ENTITY a \"aaaa\">]><svg xmlns=\"http://www.w3.org/2000/svg\">&a;</svg>")]
    // Con DTD aunque no declare ni use entidades: también rechazado (la regla es "sin DTD", no "sin entidades").
    [InlineData("<!DOCTYPE svg><svg xmlns=\"http://www.w3.org/2000/svg\"><rect/></svg>")]
    [InlineData("<?xml version=\"1.0\"?><!DOCTYPE svg PUBLIC \"-//W3C//DTD SVG 1.1//EN\" " +
        "\"http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd\"><svg xmlns=\"http://www.w3.org/2000/svg\"><rect/></svg>")]
    // Raíz que no es svg.
    [InlineData("<html xmlns=\"http://www.w3.org/1999/xhtml\"><body/></html>")]
    // svg sin el espacio de nombres de SVG: el navegador no lo muestra como imagen.
    [InlineData("<svg><rect/></svg>")]
    // XML mal formado.
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><rect></svg>")]
    public void LogoMarca_SvgInvalido_Rechazado(string svg)
    {
        Assert.Throws<MarcaInvalidaException>(() => LogoMarca.Crear("logo.svg", Encoding.UTF8.GetBytes(svg)));
    }

    [Fact]
    public void LogoMarca_SinContenido_Rechazado()
    {
        Assert.Throws<ArgumentNullException>(() => LogoMarca.Crear("logo.png", null!));
    }

    private static byte[] Png(int largo)
    {
        var contenido = new byte[largo];
        FirmaPng.AsSpan(0, Math.Min(largo, FirmaPng.Length)).CopyTo(contenido);
        return contenido;
    }
}
