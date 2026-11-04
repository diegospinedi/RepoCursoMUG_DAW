using System.Globalization;
using Optica.Domain.Configuracion;

namespace Optica.Domain.Tests.Configuracion;

public class ConfiguracionNegocioTests
{
    [Fact]
    public void Constructor_ValoresValidos_ExponeLosCuatroValores()
    {
        var configuracion = new ConfiguracionNegocio(10.5m, CondicionFiscal.Monotributo, 10m, 25_000m);

        Assert.Equal(10.5m, configuracion.AlicuotaIva);
        Assert.Equal(CondicionFiscal.Monotributo, configuracion.CondicionFiscal);
        Assert.Equal(10m, configuracion.MultiploRedondeo);
        Assert.Equal(25_000m, configuracion.TopeIdentificacion);
        Assert.False(configuracion.FaltaTope);
    }

    [Fact]
    public void FaltaTope_SinTope_Verdadero()
    {
        var configuracion = new ConfiguracionNegocio(21m, CondicionFiscal.ResponsableInscripto, 0.01m, null);

        Assert.Null(configuracion.TopeIdentificacion);
        Assert.True(configuracion.FaltaTope);
    }

    [Theory]
    [InlineData("101", "0.01", null, "alicuotaIva", "101")]
    [InlineData("21.555", "0.01", null, "alicuotaIva", "21.555")]
    [InlineData("21", "0.001", null, "multiploRedondeo", "0.001")]
    [InlineData("21", "0.015", null, "multiploRedondeo", "0.015")]
    [InlineData("21", "0.01", "-123.45", "topeIdentificacion", "-123.45")]
    [InlineData("21", "0.01", "777.777", "topeIdentificacion", "777.777")]
    public void Constructor_ValorInvalido_LanzaSinIncluirElValor(
        string alicuota, string multiplo, string? tope, string parametro, string valor)
    {
        var error = Assert.Throws<ArgumentException>(() => new ConfiguracionNegocio(
            Leer(alicuota), CondicionFiscal.ResponsableInscripto, Leer(multiplo), tope is null ? null : Leer(tope)));

        Assert.Equal(parametro, error.ParamName);
        Assert.DoesNotContain(valor, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(valor.Replace('.', ','), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_CondicionNoDefinida_Rechazado()
    {
        var error = Assert.Throws<ArgumentException>(
            () => new ConfiguracionNegocio(21m, (CondicionFiscal)7, 0.01m, null));

        Assert.Equal("condicionFiscal", error.ParamName);
        Assert.DoesNotContain("7", error.Message, StringComparison.Ordinal);
    }

    private static decimal Leer(string texto) => decimal.Parse(texto, CultureInfo.InvariantCulture);
}
