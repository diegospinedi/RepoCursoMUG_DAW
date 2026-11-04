using System.Globalization;
using Optica.Domain.Comun;
using Optica.Domain.Configuracion;

namespace Optica.Domain.Tests.Comun;

public class ReglasImporteTests
{
    [Fact]
    public void ReglasImporte_MismasRespuestasQueAntes()
    {
        // D-12: la regla se mudó a Comun; PoliticaConfiguracionNegocio la sigue exponiendo con el mismo valor.
        Assert.Equal(92_233_720_368_547_758.07m, ReglasImporte.ImporteMaximo);
        Assert.Equal(ReglasImporte.ImporteMaximo, PoliticaConfiguracionNegocio.ImporteMaximo);
        Assert.Equal(long.MaxValue, decimal.ToInt64(ReglasImporte.ImporteMaximo * 100m));
    }

    [Theory]
    [InlineData("0", false)]
    [InlineData("21", false)]
    [InlineData("10.5", false)]
    [InlineData("0.01", false)]
    [InlineData("21.500", false)]
    [InlineData("0.010", false)]
    [InlineData("1000.000", false)]
    [InlineData("-123.45", false)]
    [InlineData("92233720368547758.07", false)]
    [InlineData("21.555", true)]
    [InlineData("0.001", true)]
    [InlineData("0.015", true)]
    [InlineData("777.777", true)]
    [InlineData("-10.001", true)]
    public void TieneMasDeDosDecimales_PorElValorNoPorLaEscala(string texto, bool esperado)
    {
        var valor = decimal.Parse(texto, CultureInfo.InvariantCulture);

        Assert.Equal(esperado, ReglasImporte.TieneMasDeDosDecimales(valor));
    }
}
