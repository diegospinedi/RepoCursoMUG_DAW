using System.Globalization;
using Optica.Domain.Configuracion;

namespace Optica.Domain.Tests.Configuracion;

public class PoliticaConfiguracionNegocioTests
{
    private const string ResponsableInscripto = "responsable_inscripto";
    private const string Monotributo = "monotributo";

    [Fact]
    public void Evaluar_ValoresIniciales_SinInfracciones()
    {
        var infracciones = PoliticaConfiguracionNegocio.Evaluar(21m, ResponsableInscripto, 0.01m, null);

        Assert.Empty(infracciones);
    }

    [Fact]
    public void Evaluar_Alicuota101_Rechazado()
    {
        var infracciones = PoliticaConfiguracionNegocio.Evaluar(101m, ResponsableInscripto, 0.01m, null);

        Assert.Equal([InfraccionConfiguracion.AlicuotaFueraDeRango], infracciones);
    }

    [Fact]
    public void Evaluar_AlicuotaMenos1_Rechazado()
    {
        var infracciones = PoliticaConfiguracionNegocio.Evaluar(-1m, ResponsableInscripto, 0.01m, null);

        Assert.Equal([InfraccionConfiguracion.AlicuotaFueraDeRango], infracciones);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("100")]
    [InlineData("10.5")]
    public void Evaluar_AlicuotaBordes0y100y10coma5_Validos(string alicuota)
    {
        var infracciones = PoliticaConfiguracionNegocio.Evaluar(
            decimal.Parse(alicuota, CultureInfo.InvariantCulture), Monotributo, 0.01m, null);

        Assert.Empty(infracciones);
    }

    [Fact]
    public void Evaluar_Alicuota21coma555_Invalido()
    {
        var infracciones = PoliticaConfiguracionNegocio.Evaluar(21.555m, ResponsableInscripto, 0.01m, null);

        Assert.Equal([InfraccionConfiguracion.AlicuotaConMasDeDosDecimales], infracciones);
    }

    [Fact]
    public void Evaluar_AlicuotaAusente_Faltante()
    {
        var infracciones = PoliticaConfiguracionNegocio.Evaluar(null, ResponsableInscripto, 0.01m, null);

        Assert.Equal([InfraccionConfiguracion.AlicuotaFaltante], infracciones);
    }

    [Fact]
    public void Evaluar_Multiplo0coma001_RechazadoPorMinimo()
    {
        var infracciones = PoliticaConfiguracionNegocio.Evaluar(21m, ResponsableInscripto, 0.001m, null);

        // D-01: el mínimo se evalúa antes que la escala.
        Assert.Equal([InfraccionConfiguracion.MultiploMenorAlMinimo], infracciones);
    }

    [Theory]
    [InlineData("0.015")]
    [InlineData("50.005")]
    public void Evaluar_Multiplo0coma015_Invalido(string multiplo)
    {
        var infracciones = PoliticaConfiguracionNegocio.Evaluar(
            21m, ResponsableInscripto, decimal.Parse(multiplo, CultureInfo.InvariantCulture), null);

        Assert.Equal([InfraccionConfiguracion.MultiploConMasDeDosDecimales], infracciones);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void Evaluar_MultiploCeroONegativo_RechazadoPorMinimo(string multiplo)
    {
        var infracciones = PoliticaConfiguracionNegocio.Evaluar(
            21m, ResponsableInscripto, decimal.Parse(multiplo, CultureInfo.InvariantCulture), null);

        Assert.Equal([InfraccionConfiguracion.MultiploMenorAlMinimo], infracciones);
    }

    [Fact]
    public void Evaluar_Tope0coma01_Valido()
    {
        var infracciones = PoliticaConfiguracionNegocio.Evaluar(21m, ResponsableInscripto, 0.01m, 0.01m);

        Assert.Empty(infracciones);
    }

    [Fact]
    public void Evaluar_Alicuota100coma01_RechazadoPorRango()
    {
        // El rango se evalúa antes que la escala.
        var infracciones = PoliticaConfiguracionNegocio.Evaluar(100.01m, ResponsableInscripto, 0.01m, null);

        Assert.Equal([InfraccionConfiguracion.AlicuotaFueraDeRango], infracciones);
    }

    [Fact]
    public void Evaluar_MultiploAusente_Faltante()
    {
        var infracciones = PoliticaConfiguracionNegocio.Evaluar(21m, ResponsableInscripto, null, null);

        Assert.Equal([InfraccionConfiguracion.MultiploFaltante], infracciones);
    }

    [Fact]
    public void Evaluar_Tope0_Rechazado()
    {
        var infracciones = PoliticaConfiguracionNegocio.Evaluar(21m, ResponsableInscripto, 0.01m, 0m);

        Assert.Equal([InfraccionConfiguracion.TopeNoPositivo], infracciones);
    }

    [Fact]
    public void Evaluar_TopeNegativo_Rechazado()
    {
        var infracciones = PoliticaConfiguracionNegocio.Evaluar(21m, ResponsableInscripto, 0.01m, -5m);

        Assert.Equal([InfraccionConfiguracion.TopeNoPositivo], infracciones);
    }

    [Fact]
    public void Evaluar_TopeConTresDecimales_Invalido()
    {
        var infracciones = PoliticaConfiguracionNegocio.Evaluar(21m, ResponsableInscripto, 0.01m, 1000.005m);

        Assert.Equal([InfraccionConfiguracion.TopeConMasDeDosDecimales], infracciones);
    }

    [Fact]
    public void Evaluar_TopeAusente_Valido()
    {
        var infracciones = PoliticaConfiguracionNegocio.Evaluar(21m, Monotributo, 10m, null);

        Assert.Empty(infracciones);
    }

    [Theory]
    [InlineData("Monotributo")]
    [InlineData("")]
    [InlineData("otro")]
    [InlineData(null)]
    [InlineData(" monotributo")]
    public void Evaluar_CondicionDesconocida_Invalido(string? condicion)
    {
        var infracciones = PoliticaConfiguracionNegocio.Evaluar(21m, condicion, 0.01m, null);

        Assert.Equal([InfraccionConfiguracion.CondicionFiscalInvalida], infracciones);
    }

    [Fact]
    public void Evaluar_ImporteSobreElMaximo_Rechazado()
    {
        var sobreElMaximo = PoliticaConfiguracionNegocio.ImporteMaximo + 0.01m;

        var infracciones = PoliticaConfiguracionNegocio.Evaluar(21m, ResponsableInscripto, sobreElMaximo, sobreElMaximo);

        Assert.Equal(
            [InfraccionConfiguracion.MultiploFueraDeRango, InfraccionConfiguracion.TopeFueraDeRango],
            infracciones);
    }

    [Fact]
    public void Evaluar_ImporteIgualAlMaximo_Valido()
    {
        var maximo = PoliticaConfiguracionNegocio.ImporteMaximo;

        var infracciones = PoliticaConfiguracionNegocio.Evaluar(21m, ResponsableInscripto, maximo, maximo);

        Assert.Empty(infracciones);
    }

    [Fact]
    public void ImporteMaximo_EsElMayorImporteCuyosCentavosEntranEnUnEnteroDe64Bits()
    {
        var maximo = PoliticaConfiguracionNegocio.ImporteMaximo;

        Assert.Equal(92_233_720_368_547_758.07m, maximo);
        Assert.Equal(long.MaxValue, decimal.ToInt64(maximo * 100m));
        Assert.Equal(maximo, decimal.Round(maximo, 2));
    }

    [Fact]
    public void Evaluar_VariosCamposInvalidos_DevuelveUnaInfraccionPorCampo()
    {
        var infracciones = PoliticaConfiguracionNegocio.Evaluar(101m, "otro", 0.001m, 25_000m);

        Assert.Equal(
            [
                InfraccionConfiguracion.AlicuotaFueraDeRango,
                InfraccionConfiguracion.CondicionFiscalInvalida,
                InfraccionConfiguracion.MultiploMenorAlMinimo,
            ],
            infracciones);
    }

    [Fact]
    public void Evaluar_CerosALaDerecha_Validos()
    {
        // Así llegan del JSON: escala 3, pero el valor tiene 2 decimales o menos.
        var alicuota = decimal.Parse("21.500", CultureInfo.InvariantCulture);
        var multiplo = decimal.Parse("0.010", CultureInfo.InvariantCulture);
        var tope = decimal.Parse("1000.000", CultureInfo.InvariantCulture);
        Assert.Equal(3, alicuota.Scale);
        Assert.Equal(3, multiplo.Scale);

        var infracciones = PoliticaConfiguracionNegocio.Evaluar(alicuota, ResponsableInscripto, multiplo, tope);

        Assert.Empty(infracciones);
    }

    [Theory]
    [InlineData(ResponsableInscripto, CondicionFiscal.ResponsableInscripto)]
    [InlineData(Monotributo, CondicionFiscal.Monotributo)]
    public void LeerCondicionYTextoDe_SonInversas(string texto, CondicionFiscal condicion)
    {
        Assert.Equal(condicion, PoliticaConfiguracionNegocio.LeerCondicion(texto));
        Assert.Equal(texto, PoliticaConfiguracionNegocio.TextoDe(condicion));
    }

    [Theory]
    [InlineData("Monotributo")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("ResponsableInscripto")]
    public void LeerCondicion_TextoDesconocido_Null(string? texto)
    {
        Assert.Null(PoliticaConfiguracionNegocio.LeerCondicion(texto));
    }

    [Fact]
    public void TextoDe_CondicionNoDefinida_Rechazado()
    {
        var error = Assert.Throws<ArgumentException>(() => PoliticaConfiguracionNegocio.TextoDe((CondicionFiscal)7));

        Assert.Equal("condicion", error.ParamName);
    }
}
