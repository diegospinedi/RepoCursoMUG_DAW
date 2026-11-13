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
        var infracciones = PoliticaConfiguracionNegocio.Evaluar(21m, ResponsableInscripto, 0.01m, null, puntoVenta: null);

        Assert.Empty(infracciones);
    }

    [Fact]
    public void Evaluar_Alicuota101_Rechazado()
    {
        var infracciones = PoliticaConfiguracionNegocio.Evaluar(101m, ResponsableInscripto, 0.01m, null, puntoVenta: null);

        Assert.Equal([InfraccionConfiguracion.AlicuotaFueraDeRango], infracciones);
    }

    [Fact]
    public void Evaluar_AlicuotaMenos1_Rechazado()
    {
        var infracciones = PoliticaConfiguracionNegocio.Evaluar(-1m, ResponsableInscripto, 0.01m, null, puntoVenta: null);

        Assert.Equal([InfraccionConfiguracion.AlicuotaFueraDeRango], infracciones);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("100")]
    [InlineData("10.5")]
    public void Evaluar_AlicuotaBordes0y100y10coma5_Validos(string alicuota)
    {
        var infracciones = PoliticaConfiguracionNegocio.Evaluar(
            decimal.Parse(alicuota, CultureInfo.InvariantCulture), Monotributo, 0.01m, null, puntoVenta: null);

        Assert.Empty(infracciones);
    }

    [Fact]
    public void Evaluar_Alicuota21coma555_Invalido()
    {
        var infracciones = PoliticaConfiguracionNegocio.Evaluar(21.555m, ResponsableInscripto, 0.01m, null, puntoVenta: null);

        Assert.Equal([InfraccionConfiguracion.AlicuotaConMasDeDosDecimales], infracciones);
    }

    [Fact]
    public void Evaluar_AlicuotaAusente_Faltante()
    {
        var infracciones = PoliticaConfiguracionNegocio.Evaluar(null, ResponsableInscripto, 0.01m, null, puntoVenta: null);

        Assert.Equal([InfraccionConfiguracion.AlicuotaFaltante], infracciones);
    }

    [Fact]
    public void Evaluar_Multiplo0coma001_RechazadoPorMinimo()
    {
        var infracciones = PoliticaConfiguracionNegocio.Evaluar(21m, ResponsableInscripto, 0.001m, null, puntoVenta: null);

        // D-01: el mínimo se evalúa antes que la escala.
        Assert.Equal([InfraccionConfiguracion.MultiploMenorAlMinimo], infracciones);
    }

    [Theory]
    [InlineData("0.015")]
    [InlineData("50.005")]
    public void Evaluar_Multiplo0coma015_Invalido(string multiplo)
    {
        var infracciones = PoliticaConfiguracionNegocio.Evaluar(
            21m, ResponsableInscripto, decimal.Parse(multiplo, CultureInfo.InvariantCulture), null, puntoVenta: null);

        Assert.Equal([InfraccionConfiguracion.MultiploConMasDeDosDecimales], infracciones);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void Evaluar_MultiploCeroONegativo_RechazadoPorMinimo(string multiplo)
    {
        var infracciones = PoliticaConfiguracionNegocio.Evaluar(
            21m, ResponsableInscripto, decimal.Parse(multiplo, CultureInfo.InvariantCulture), null, puntoVenta: null);

        Assert.Equal([InfraccionConfiguracion.MultiploMenorAlMinimo], infracciones);
    }

    [Fact]
    public void Evaluar_Tope0coma01_Valido()
    {
        var infracciones = PoliticaConfiguracionNegocio.Evaluar(21m, ResponsableInscripto, 0.01m, 0.01m, puntoVenta: null);

        Assert.Empty(infracciones);
    }

    [Fact]
    public void Evaluar_Alicuota100coma01_RechazadoPorRango()
    {
        // El rango se evalúa antes que la escala.
        var infracciones = PoliticaConfiguracionNegocio.Evaluar(100.01m, ResponsableInscripto, 0.01m, null, puntoVenta: null);

        Assert.Equal([InfraccionConfiguracion.AlicuotaFueraDeRango], infracciones);
    }

    [Fact]
    public void Evaluar_MultiploAusente_Faltante()
    {
        var infracciones = PoliticaConfiguracionNegocio.Evaluar(21m, ResponsableInscripto, null, null, puntoVenta: null);

        Assert.Equal([InfraccionConfiguracion.MultiploFaltante], infracciones);
    }

    [Fact]
    public void Evaluar_Tope0_Rechazado()
    {
        var infracciones = PoliticaConfiguracionNegocio.Evaluar(21m, ResponsableInscripto, 0.01m, 0m, puntoVenta: null);

        Assert.Equal([InfraccionConfiguracion.TopeNoPositivo], infracciones);
    }

    [Fact]
    public void Evaluar_TopeNegativo_Rechazado()
    {
        var infracciones = PoliticaConfiguracionNegocio.Evaluar(21m, ResponsableInscripto, 0.01m, -5m, puntoVenta: null);

        Assert.Equal([InfraccionConfiguracion.TopeNoPositivo], infracciones);
    }

    [Fact]
    public void Evaluar_TopeConTresDecimales_Invalido()
    {
        var infracciones = PoliticaConfiguracionNegocio.Evaluar(21m, ResponsableInscripto, 0.01m, 1000.005m, puntoVenta: null);

        Assert.Equal([InfraccionConfiguracion.TopeConMasDeDosDecimales], infracciones);
    }

    [Fact]
    public void Evaluar_TopeAusente_Valido()
    {
        var infracciones = PoliticaConfiguracionNegocio.Evaluar(21m, Monotributo, 10m, null, puntoVenta: null);

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
        var infracciones = PoliticaConfiguracionNegocio.Evaluar(21m, condicion, 0.01m, null, puntoVenta: null);

        Assert.Equal([InfraccionConfiguracion.CondicionFiscalInvalida], infracciones);
    }

    [Fact]
    public void Evaluar_ImporteSobreElMaximo_Rechazado()
    {
        var sobreElMaximo = PoliticaConfiguracionNegocio.ImporteMaximo + 0.01m;

        var infracciones = PoliticaConfiguracionNegocio.Evaluar(21m, ResponsableInscripto, sobreElMaximo, sobreElMaximo, puntoVenta: null);

        Assert.Equal(
            [InfraccionConfiguracion.MultiploFueraDeRango, InfraccionConfiguracion.TopeFueraDeRango],
            infracciones);
    }

    [Fact]
    public void Evaluar_ImporteIgualAlMaximo_Valido()
    {
        var maximo = PoliticaConfiguracionNegocio.ImporteMaximo;

        var infracciones = PoliticaConfiguracionNegocio.Evaluar(21m, ResponsableInscripto, maximo, maximo, puntoVenta: null);

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
        var infracciones = PoliticaConfiguracionNegocio.Evaluar(101m, "otro", 0.001m, 25_000m, puntoVenta: null);

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

        var infracciones = PoliticaConfiguracionNegocio.Evaluar(alicuota, ResponsableInscripto, multiplo, tope, puntoVenta: null);

        Assert.Empty(infracciones);
    }

    // ---- FEAT-007: las 6 alícuotas de ARCA al grabar (FR-26, D-06) y el punto de venta (FR-28, D-33) ----

    [Fact]
    public void EvaluarParaGrabar_Alicuota19_Rechazado()
    {
        // AC-27: 19 está entre 0 y 100, pero ARCA no la acepta.
        var infracciones = PoliticaConfiguracionNegocio.EvaluarParaGrabar(19m, ResponsableInscripto, 0.01m, null, null);

        Assert.Equal([InfraccionConfiguracion.AlicuotaNoAdmitida], infracciones);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("2.5")]
    [InlineData("5")]
    [InlineData("10.5")]
    [InlineData("21")]
    [InlineData("27")]
    [InlineData("21.00")]
    public void EvaluarParaGrabar_LasSeisAlicuotas_Validas(string alicuota)
    {
        var infracciones = PoliticaConfiguracionNegocio.EvaluarParaGrabar(
            decimal.Parse(alicuota, CultureInfo.InvariantCulture), ResponsableInscripto, 0.01m, null, 3);

        Assert.Empty(infracciones);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100000)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void EvaluarParaGrabar_PuntoVentaFueraDeRango_Rechazado(int puntoVenta)
    {
        // AC-31: de 1 a 99999.
        var infracciones = PoliticaConfiguracionNegocio.EvaluarParaGrabar(
            21m, ResponsableInscripto, 0.01m, null, puntoVenta);

        Assert.Equal([InfraccionConfiguracion.PuntoVentaFueraDeRango], infracciones);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(99999)]
    [InlineData(null)]
    public void EvaluarParaGrabar_PuntoVentaBordesOSinValor_Validos(int? puntoVenta)
    {
        var infracciones = PoliticaConfiguracionNegocio.EvaluarParaGrabar(
            21m, ResponsableInscripto, 0.01m, null, puntoVenta);

        Assert.Empty(infracciones);
    }

    [Fact]
    public void Evaluar_Alicuota19_ValidaAlLeer()
    {
        // D-06: la regla de las 6 es de escritura; una alícuota de 0 a 100 ya guardada se sigue leyendo.
        var infracciones = PoliticaConfiguracionNegocio.Evaluar(19m, ResponsableInscripto, 0.01m, null, null);

        Assert.Empty(infracciones);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100000)]
    public void Evaluar_PuntoVentaFueraDeRango_RechazadoTambienAlLeer(int puntoVenta)
    {
        // D-33: el rango del punto de venta es invariante de lectura y escritura.
        var infracciones = PoliticaConfiguracionNegocio.Evaluar(21m, ResponsableInscripto, 0.01m, null, puntoVenta);

        Assert.Equal([InfraccionConfiguracion.PuntoVentaFueraDeRango], infracciones);
    }

    [Fact]
    public void EvaluarParaGrabar_Alicuota101_UnaInfraccionPorCampo()
    {
        // Decisión del usuario (2026-11-13): al grabar, toda alícuota fuera de las 6 informa la lista de ARCA.
        var infracciones = PoliticaConfiguracionNegocio.EvaluarParaGrabar(101m, "otro", 0.01m, null, 0);

        Assert.Equal(
            [
                InfraccionConfiguracion.AlicuotaNoAdmitida,
                InfraccionConfiguracion.CondicionFiscalInvalida,
                InfraccionConfiguracion.PuntoVentaFueraDeRango,
            ],
            infracciones);
    }

    [Theory]
    [InlineData("101")]
    [InlineData("50")]
    [InlineData("-1")]
    [InlineData("21.555")]
    [InlineData("100.01")]
    [InlineData("19")]
    public void EvaluarParaGrabar_AlicuotaFueraDeLasSeis_SiempreNoAdmitida(string alicuota)
    {
        // Al grabar no se informan el rango ni los decimales: solo la lista de ARCA (decisión del usuario, 2026-11-13).
        var infracciones = PoliticaConfiguracionNegocio.EvaluarParaGrabar(
            decimal.Parse(alicuota, CultureInfo.InvariantCulture), ResponsableInscripto, 0.01m, null, null);

        Assert.Equal([InfraccionConfiguracion.AlicuotaNoAdmitida], infracciones);
    }

    [Fact]
    public void EvaluarParaGrabar_AlicuotaAusente_Faltante()
    {
        // La excepción: sin alícuota se pide cargarla.
        var infracciones = PoliticaConfiguracionNegocio.EvaluarParaGrabar(null, ResponsableInscripto, 0.01m, null, null);

        Assert.Equal([InfraccionConfiguracion.AlicuotaFaltante], infracciones);
    }

    [Fact]
    public void AlicuotasAdmitidas_SonLasSeisDeArca()
    {
        Assert.Equal([0m, 2.5m, 5m, 10.5m, 21m, 27m], AlicuotasAdmitidas.Todas);
        Assert.True(AlicuotasAdmitidas.EsAdmitida(decimal.Parse("10.50", CultureInfo.InvariantCulture)));
        Assert.False(AlicuotasAdmitidas.EsAdmitida(19m));
        Assert.False(AlicuotasAdmitidas.EsAdmitida(100m));
        Assert.False(AlicuotasAdmitidas.EsAdmitida(-21m));
        // La lista no se puede modificar desde afuera.
        Assert.Throws<NotSupportedException>(() => ((IList<decimal>)AlicuotasAdmitidas.Todas)[0] = 19m);
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
