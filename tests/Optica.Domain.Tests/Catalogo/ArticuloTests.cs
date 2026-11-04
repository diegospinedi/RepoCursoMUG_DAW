using System.Globalization;
using Optica.Domain.Catalogo;
using Optica.Domain.Comun;

namespace Optica.Domain.Tests.Catalogo;

public class ArticuloTests
{
    [Fact]
    public void Clave_EspaciosYMayusculas_IgualQueABC1()
    {
        Assert.Equal("ABC-1", NormalizacionCodigo.Clave("ABC-1"));
        Assert.Equal(NormalizacionCodigo.Clave("ABC-1"), NormalizacionCodigo.Clave(" abc-1 "));
    }

    [Fact]
    public void Clave_EspacioDuroYTabulador_IgualQueABC1()
    {
        // D-13: todo espacio en blanco de Unicode en los extremos.
        Assert.Equal("ABC-1", NormalizacionCodigo.Clave("ABC-1\u00A0"));
        Assert.Equal("ABC-1", NormalizacionCodigo.Clave("\tABC-1"));
        Assert.Equal("ABC-1", NormalizacionCodigo.Clave("\u00A0\t abc-1\r\n"));
    }

    [Fact]
    public void Clave_BomYSiguienteLinea_IgualQueABC()
    {
        // D-17: char.IsWhiteSpace más U+FEFF (BOM) en los dos extremos.
        var clave = NormalizacionCodigo.Clave("ABC");

        Assert.Equal(clave, NormalizacionCodigo.Clave("\uFEFFABC"));
        Assert.Equal(clave, NormalizacionCodigo.Clave("ABC\u0085"));
        Assert.Equal(clave, NormalizacionCodigo.Clave("\uFEFF \u0085abc\uFEFF\u00A0"));
    }

    [Fact]
    public void Clave_FormasUnicodeEquivalentes_MismaClave()
    {
        // ADR-009: "é" precompuesta (U+00E9) y "e" + acento agudo combinado (U+0301) son el mismo código.
        Assert.Equal(NormalizacionCodigo.Clave("caf\u00E9"), NormalizacionCodigo.Clave("cafe\u0301"));
        Assert.Equal("CAF\u00C9", NormalizacionCodigo.Clave("cafe\u0301"));
    }

    [Fact]
    public void Recortar_ConservaLosEspaciosInternos()
    {
        Assert.Equal("ABC 1", NormalizacionCodigo.Recortar("\u00A0 ABC 1\t"));
        Assert.Equal("A\uFEFFB", NormalizacionCodigo.Recortar("\uFEFFA\uFEFFB\uFEFF"));
        Assert.Equal("abc", NormalizacionCodigo.Recortar("abc"));
        Assert.Equal(string.Empty, NormalizacionCodigo.Recortar("\u00A0\t \uFEFF"));
    }

    [Fact]
    public void Constructor_ValoresValidos_GuardaRecortadoYClave()
    {
        var articulo = new Articulo(
            null, "  abc-1\u00A0", "\tArmaz\u00F3n met\u00E1lico  ", 1210m, 50m, 1815m);

        Assert.Null(articulo.Codigo);
        Assert.Equal("abc-1", articulo.CodigoProveedor);
        Assert.Equal("ABC-1", articulo.CodigoProveedorClave);
        Assert.Equal("Armaz\u00F3n met\u00E1lico", articulo.Descripcion);
        Assert.Equal(1210m, articulo.PrecioCosto);
        Assert.Equal(50m, articulo.MargenUtilidad);
        Assert.Equal(1815m, articulo.PrecioVenta);
        Assert.False(articulo.CostoNoPositivo);
    }

    [Fact]
    public void Constructor_ConCodigo_ExponeElCodigo()
    {
        var articulo = new Articulo(42L, "ABC-1", "Lente", 1000m, 50m, 1500m);

        Assert.Equal(42L, articulo.Codigo);
    }

    [Theory]
    [InlineData("0", "0", true)]
    [InlineData("-100", "-150", true)]
    [InlineData("-0.01", "-0.01", true)]
    [InlineData("0.01", "0.02", false)]
    public void Articulo_CostoNoPositivo_Advertencia(string costo, string venta, bool esperado)
    {
        var articulo = new Articulo(null, "ABC-1", "Lente", Leer(costo), 50m, Leer(venta));

        Assert.Equal(esperado, articulo.CostoNoPositivo);
    }

    // El último valor es el texto que no debe aparecer en el mensaje; null cuando el campo no trae ningún valor que
    // pueda filtrarse (ausente o solo espacios).
    public static TheoryData<string?, string?, decimal, decimal, decimal, string, string?> ValoresInvalidos => new()
    {
        { null, "Lente", 10m, 10m, 11m, "codigoProveedor", null },
        { "\u00A0 ", "Lente", 10m, 10m, 11m, "codigoProveedor", null },
        { new string('Q', 51), "Lente", 10m, 10m, 11m, "codigoProveedor", "QQQQ" },
        { "ABC-1", " ", 10m, 10m, 11m, "descripcion", null },
        { "ABC-1", new string('W', 201), 10m, 10m, 11m, "descripcion", "WWWW" },
        { "ABC-1", "Lente", 10.555m, 10m, 11m, "precioCosto", "10.555" },
        { "ABC-1", "Lente", ReglasImporte.ImporteMaximo + 0.01m, 10m, 11m, "precioCosto", "92233720368547758" },
        { "ABC-1", "Lente", 10m, 1000.01m, 11m, "margenUtilidad", "1000.01" },
        { "ABC-1", "Lente", 10m, -1m, 11m, "margenUtilidad", "-1" },
        { "ABC-1", "Lente", 10m, 10.555m, 11m, "margenUtilidad", "10.555" },
        { "ABC-1", "Lente", 10m, 10m, 11.555m, "precioVenta", "11.555" },
        { "ABC-1", "Lente", 10m, 10m, ReglasImporte.ImporteMaximo + 0.01m, "precioVenta", "92233720368547758" },
    };

    [Theory]
    [MemberData(nameof(ValoresInvalidos))]
    public void Articulo_ValorInvalido_LanzaSinIncluirElValor(
        string? codigo, string? descripcion, decimal costo, decimal margen, decimal venta, string parametro,
        string? valor)
    {
        var error = Assert.Throws<ArgumentException>(
            () => new Articulo(null, codigo!, descripcion!, costo, margen, venta));

        Assert.Equal(parametro, error.ParamName);
        if (valor is not null)
        {
            Assert.DoesNotContain(valor, error.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(valor.Replace('.', ','), error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Articulo_TextoConUtf16Invalido_LanzaConElParametroDelCampo()
    {
        // D-16: la política lo rechaza antes de que Clave llegue a Normalize, que lanzaría sin ParamName.
        // En un [Fact] porque el runner serializa los argumentos de [InlineData].
        var casos = new (string Codigo, string Descripcion, string Parametro)[]
        {
            ("\uD800", "Lente", "codigoProveedor"),
            ("a\uDC00b", "Lente", "codigoProveedor"),
            ("ABC-1", "\uD800", "descripcion"),
            ("ABC-1", "a\uDC00b", "descripcion"),
        };

        foreach (var (codigo, descripcion, parametro) in casos)
        {
            var error = Assert.Throws<ArgumentException>(
                () => new Articulo(null, codigo, descripcion, 10m, 10m, 11m));

            Assert.Equal(parametro, error.ParamName);
        }
    }

    private static decimal Leer(string texto) => decimal.Parse(texto, CultureInfo.InvariantCulture);
}
