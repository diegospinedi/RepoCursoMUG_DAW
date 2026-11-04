using System.Globalization;
using Optica.Domain.Catalogo;
using Optica.Domain.Comun;

namespace Optica.Domain.Tests.Catalogo;

public class PoliticaArticuloTests
{
    private const string Codigo = "ABC-1";
    private const string Descripcion = "Armaz\u00F3n met\u00E1lico";

    // UTF-16 inválido (D-16): sustituto alto suelto, bajo suelto entre letras, alto seguido de algo que no es un bajo,
    // alto al final. Van en un [Fact] y no en [InlineData] porque el runner serializa los argumentos.
    private static readonly string[] TextosConUtf16Invalido =
    [
        "\uD800",
        "a\uDC00b",
        "\uDBFFx",
        "ab\uD83D",
        "\uDE00\uD83D",
    ];

    [Fact]
    public void Evaluar_ValoresValidos_SinInfracciones()
    {
        var infracciones = PoliticaArticulo.Evaluar(Codigo, Descripcion, 1210m, 50m);

        Assert.Empty(infracciones);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t \u00A0")]
    [InlineData("\uFEFF")]
    public void Evaluar_CodigoFaltante_Rechazado(string? codigo)
    {
        var infracciones = PoliticaArticulo.Evaluar(codigo, Descripcion, 1210m, 50m);

        Assert.Equal([InfraccionArticulo.CodigoFaltante], infracciones);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\u00A0\t")]
    [InlineData("\uFEFF \u0085")]
    public void Evaluar_DescripcionFaltante_Rechazado(string? descripcion)
    {
        var infracciones = PoliticaArticulo.Evaluar(Codigo, descripcion, 1210m, 50m);

        Assert.Equal([InfraccionArticulo.DescripcionFaltante], infracciones);
    }

    [Fact]
    public void Evaluar_SoloEspaciosDuros_RechazadoComoFaltante()
    {
        var infracciones = PoliticaArticulo.Evaluar("\u00A0\u00A0\u00A0", Descripcion, 1210m, 50m);

        Assert.Equal([InfraccionArticulo.CodigoFaltante], infracciones);
    }

    [Fact]
    public void Evaluar_CodigoConUtf16Invalido_CaracteresInvalidos()
    {
        foreach (var codigo in TextosConUtf16Invalido)
        {
            Assert.Equal(
                [InfraccionArticulo.CodigoConCaracteresInvalidos],
                PoliticaArticulo.Evaluar(codigo, Descripcion, 1210m, 50m));
        }
    }

    [Fact]
    public void Evaluar_DescripcionConUtf16Invalido_CaracteresInvalidos()
    {
        foreach (var descripcion in TextosConUtf16Invalido)
        {
            Assert.Equal(
                [InfraccionArticulo.DescripcionConCaracteresInvalidos],
                PoliticaArticulo.Evaluar(Codigo, descripcion, 1210m, 50m));
        }
    }

    [Fact]
    public void Evaluar_CaracteresInvalidosAntesQueElLargo()
    {
        var infracciones = PoliticaArticulo.Evaluar(
            new string('A', 51) + "\uD800", new string('d', 201) + "\uDC00", 1210m, 50m);

        Assert.Equal(
            [InfraccionArticulo.CodigoConCaracteresInvalidos, InfraccionArticulo.DescripcionConCaracteresInvalidos],
            infracciones);
    }

    [Fact]
    public void Evaluar_ParSustitutoCompleto_Valido()
    {
        // U+1F600 escrito como par alto + bajo: UTF-16 válido.
        var infracciones = PoliticaArticulo.Evaluar("ABC-\uD83D\uDE00", "Lente \uD83D\uDE00", 1210m, 50m);

        Assert.Empty(infracciones);
    }

    [Fact]
    public void Evaluar_Codigo51YDescripcion201_Rechazados()
    {
        var infracciones = PoliticaArticulo.Evaluar(new string('A', 51), new string('d', 201), 1210m, 50m);

        Assert.Equal(
            [InfraccionArticulo.CodigoDemasiadoLargo, InfraccionArticulo.DescripcionDemasiadoLarga],
            infracciones);
    }

    [Fact]
    public void Evaluar_Codigo50YDescripcion200ConEspaciosAlrededor_Validos()
    {
        // D-02: el largo se cuenta después de recortar.
        var codigo = "  " + new string('A', 50) + "\u00A0\uFEFF";
        var descripcion = "\t" + new string('d', 200) + "   ";

        var infracciones = PoliticaArticulo.Evaluar(codigo, descripcion, 1210m, 50m);

        Assert.Empty(infracciones);
    }

    [Fact]
    public void Evaluar_CostoFaltante_Rechazado()
    {
        var infracciones = PoliticaArticulo.Evaluar(Codigo, Descripcion, null, 50m);

        Assert.Equal([InfraccionArticulo.CostoFaltante], infracciones);
    }

    [Fact]
    public void Evaluar_CostoConTresDecimales_Invalido()
    {
        var infracciones = PoliticaArticulo.Evaluar(Codigo, Descripcion, 1210.555m, 50m);

        Assert.Equal([InfraccionArticulo.CostoConMasDeDosDecimales], infracciones);
    }

    [Fact]
    public void Evaluar_CostoConTresDecimalesSobreElMaximo_InformaLaEscala()
    {
        var infracciones = PoliticaArticulo.Evaluar(Codigo, Descripcion, ReglasImporte.ImporteMaximo + 0.001m, 50m);

        Assert.Equal([InfraccionArticulo.CostoConMasDeDosDecimales], infracciones);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-100")]
    [InlineData("-0.01")]
    [InlineData("1210.500")]
    public void Evaluar_CostoCeroYNegativo_Validos(string costo)
    {
        var infracciones = PoliticaArticulo.Evaluar(Codigo, Descripcion, Leer(costo), 50m);

        Assert.Empty(infracciones);
    }

    [Fact]
    public void Evaluar_CostoSobreElMaximoEnModulo_FueraDeRango()
    {
        var sobreElMaximo = ReglasImporte.ImporteMaximo + 0.01m;

        Assert.Equal(
            [InfraccionArticulo.CostoFueraDeRango],
            PoliticaArticulo.Evaluar(Codigo, Descripcion, sobreElMaximo, 50m));
        Assert.Equal(
            [InfraccionArticulo.CostoFueraDeRango],
            PoliticaArticulo.Evaluar(Codigo, Descripcion, -sobreElMaximo, 50m));
    }

    [Fact]
    public void Evaluar_CostoIgualAlMaximoEnModulo_Valido()
    {
        Assert.Empty(PoliticaArticulo.Evaluar(Codigo, Descripcion, ReglasImporte.ImporteMaximo, 0m));
        Assert.Empty(PoliticaArticulo.Evaluar(Codigo, Descripcion, -ReglasImporte.ImporteMaximo, 0m));
    }

    [Fact]
    public void Evaluar_MargenFaltante_Rechazado()
    {
        var infracciones = PoliticaArticulo.Evaluar(Codigo, Descripcion, 1210m, null);

        Assert.Equal([InfraccionArticulo.MargenFaltante], infracciones);
    }

    [Fact]
    public void Evaluar_MargenMenos1_Rechazado()
    {
        var infracciones = PoliticaArticulo.Evaluar(Codigo, Descripcion, 1210m, -1m);

        Assert.Equal([InfraccionArticulo.MargenFueraDeRango], infracciones);
    }

    [Theory]
    [InlineData("1000.01")]
    [InlineData("1000.001")]
    [InlineData("-0.001")]
    public void Evaluar_Margen1000coma01_RechazadoPorRango(string margen)
    {
        // AC-05: el rango antes que la escala.
        var infracciones = PoliticaArticulo.Evaluar(Codigo, Descripcion, 1210m, Leer(margen));

        Assert.Equal([InfraccionArticulo.MargenFueraDeRango], infracciones);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1000")]
    [InlineData("1000.00")]
    [InlineData("999.99")]
    public void Evaluar_MargenBordes0y1000_Validos(string margen)
    {
        var infracciones = PoliticaArticulo.Evaluar(Codigo, Descripcion, 1210m, Leer(margen));

        Assert.Empty(infracciones);
    }

    [Fact]
    public void Evaluar_MargenConTresDecimales_Invalido()
    {
        var infracciones = PoliticaArticulo.Evaluar(Codigo, Descripcion, 1210m, 50.555m);

        Assert.Equal([InfraccionArticulo.MargenConMasDeDosDecimales], infracciones);
    }

    [Fact]
    public void Evaluar_TodoAusente_UnaInfraccionPorCampoEnOrden()
    {
        var infracciones = PoliticaArticulo.Evaluar(null, null, null, null);

        Assert.Equal(
            [
                InfraccionArticulo.CodigoFaltante,
                InfraccionArticulo.DescripcionFaltante,
                InfraccionArticulo.CostoFaltante,
                InfraccionArticulo.MargenFaltante,
            ],
            infracciones);
    }

    [Fact]
    public void Evaluar_VariosCamposInvalidos_DevuelveUnaInfraccionPorCampo()
    {
        var infracciones = PoliticaArticulo.Evaluar(new string('A', 51), " ", 1.555m, 1000.01m);

        Assert.Equal(
            [
                InfraccionArticulo.CodigoDemasiadoLargo,
                InfraccionArticulo.DescripcionFaltante,
                InfraccionArticulo.CostoConMasDeDosDecimales,
                InfraccionArticulo.MargenFueraDeRango,
            ],
            infracciones);
    }

    private static decimal Leer(string texto) => decimal.Parse(texto, CultureInfo.InvariantCulture);
}
