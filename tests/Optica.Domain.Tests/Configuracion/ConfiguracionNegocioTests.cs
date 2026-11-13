using System.Globalization;
using Optica.Domain.Configuracion;

namespace Optica.Domain.Tests.Configuracion;

public class ConfiguracionNegocioTests
{
    [Fact]
    public void Constructor_ValoresValidos_ExponeLosCuatroValores()
    {
        var configuracion = new ConfiguracionNegocio(10.5m, CondicionFiscal.Monotributo, 10m, 25_000m, puntoVenta: null);

        Assert.Equal(10.5m, configuracion.AlicuotaIva);
        Assert.Equal(CondicionFiscal.Monotributo, configuracion.CondicionFiscal);
        Assert.Equal(10m, configuracion.MultiploRedondeo);
        Assert.Equal(25_000m, configuracion.TopeIdentificacion);
        Assert.False(configuracion.FaltaTope);
    }

    [Fact]
    public void FaltaTope_SinTope_Verdadero()
    {
        var configuracion = new ConfiguracionNegocio(21m, CondicionFiscal.ResponsableInscripto, 0.01m, null, puntoVenta: null);

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
            Leer(alicuota), CondicionFiscal.ResponsableInscripto, Leer(multiplo), tope is null ? null : Leer(tope), puntoVenta: null));

        Assert.Equal(parametro, error.ParamName);
        Assert.DoesNotContain(valor, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(valor.Replace('.', ','), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_CondicionNoDefinida_Rechazado()
    {
        var error = Assert.Throws<ArgumentException>(
            () => new ConfiguracionNegocio(21m, (CondicionFiscal)7, 0.01m, null, puntoVenta: null));

        Assert.Equal("condicionFiscal", error.ParamName);
        Assert.DoesNotContain("7", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CambiaParametrosDePrecio_AnteriorNulo_LanzaError()
    {
        var vigente = new ConfiguracionNegocio(21m, CondicionFiscal.ResponsableInscripto, 0.01m, null, puntoVenta: null);

        var error = Assert.Throws<ArgumentNullException>(() => vigente.CambiaParametrosDePrecioRespectoDe(null!));

        Assert.Equal("anterior", error.ParamName);
        Assert.DoesNotContain("21", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CambiaParametrosDePrecio_SoloTope_Falso()
    {
        // D-07: el tope no interviene en el precio de venta; tampoco una configuración igual.
        var anterior = new ConfiguracionNegocio(21m, CondicionFiscal.ResponsableInscripto, 0.01m, null, puntoVenta: null);

        var soloTope = new ConfiguracionNegocio(21m, CondicionFiscal.ResponsableInscripto, 0.01m, 25_000m, puntoVenta: null);
        var igual = new ConfiguracionNegocio(21.00m, CondicionFiscal.ResponsableInscripto, 0.010m, null, puntoVenta: null);

        Assert.False(soloTope.CambiaParametrosDePrecioRespectoDe(anterior));
        Assert.False(anterior.CambiaParametrosDePrecioRespectoDe(soloTope));
        Assert.False(igual.CambiaParametrosDePrecioRespectoDe(anterior));
    }

    [Fact]
    public void CambiaParametrosDePrecio_AlicuotaCondicionOMultiplo_Verdadero()
    {
        // D-07: la alícuota, la condición fiscal o el múltiplo disparan el recálculo, cada uno por separado.
        var anterior = new ConfiguracionNegocio(21m, CondicionFiscal.ResponsableInscripto, 0.01m, 25_000m, puntoVenta: null);

        var alicuota = new ConfiguracionNegocio(10.5m, CondicionFiscal.ResponsableInscripto, 0.01m, 25_000m, puntoVenta: null);
        var condicion = new ConfiguracionNegocio(21m, CondicionFiscal.Monotributo, 0.01m, 25_000m, puntoVenta: null);
        var multiplo = new ConfiguracionNegocio(21m, CondicionFiscal.ResponsableInscripto, 50m, 25_000m, puntoVenta: null);

        Assert.True(alicuota.CambiaParametrosDePrecioRespectoDe(anterior));
        Assert.True(condicion.CambiaParametrosDePrecioRespectoDe(anterior));
        Assert.True(multiplo.CambiaParametrosDePrecioRespectoDe(anterior));
    }

    // ---- FEAT-007: alícuotas de ARCA (FR-26, D-06) y punto de venta (FR-28, D-07, D-33) ----

    [Fact]
    public void Constructor_PuntoVenta_LoExponeYNoFalta()
    {
        var configuracion = new ConfiguracionNegocio(21m, CondicionFiscal.ResponsableInscripto, 0.01m, null, puntoVenta: 3);

        Assert.Equal(3, configuracion.PuntoVenta);
        Assert.False(configuracion.FaltaPuntoVenta);
        Assert.True(configuracion.AlicuotaAdmitidaPorArca);
    }

    [Fact]
    public void FaltaPuntoVenta_SinPuntoVenta_Verdadero()
    {
        var configuracion = new ConfiguracionNegocio(21m, CondicionFiscal.ResponsableInscripto, 0.01m, null, puntoVenta: null);

        Assert.Null(configuracion.PuntoVenta);
        Assert.True(configuracion.FaltaPuntoVenta);
    }

    [Fact]
    public void Constructor_Alicuota19_SeLeeNoAdmitida()
    {
        // D-06: al leer, una alícuota de 0 a 100 construye la configuración igual.
        var configuracion = new ConfiguracionNegocio(19m, CondicionFiscal.ResponsableInscripto, 0.01m, null, puntoVenta: 3);

        Assert.Equal(19m, configuracion.AlicuotaIva);
        Assert.False(configuracion.AlicuotaAdmitidaPorArca);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100000)]
    public void Constructor_PuntoVentaFueraDeRango_LanzaSinIncluirElValor(int puntoVenta)
    {
        // D-33: el rango del punto de venta también es invariante de lectura.
        var error = Assert.Throws<ArgumentException>(() => new ConfiguracionNegocio(
            21m, CondicionFiscal.ResponsableInscripto, 0.01m, null, puntoVenta));

        Assert.Equal("puntoVenta", error.ParamName);
        Assert.DoesNotContain(puntoVenta.ToString(CultureInfo.InvariantCulture), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParaGrabar_ValoresValidos_ExponeLosCincoValores()
    {
        var configuracion = ConfiguracionNegocio.ParaGrabar(
            10.5m, CondicionFiscal.Monotributo, 10m, 25_000m, puntoVenta: 99999);

        Assert.Equal(10.5m, configuracion.AlicuotaIva);
        Assert.Equal(CondicionFiscal.Monotributo, configuracion.CondicionFiscal);
        Assert.Equal(10m, configuracion.MultiploRedondeo);
        Assert.Equal(25_000m, configuracion.TopeIdentificacion);
        Assert.Equal(99999, configuracion.PuntoVenta);
        Assert.True(configuracion.AlicuotaAdmitidaPorArca);
    }

    [Fact]
    public void ParaGrabar_Alicuota19_LanzaSinIncluirElValor()
    {
        // AC-27: al grabar, solo una de las 6 alícuotas de ARCA.
        var error = Assert.Throws<ArgumentException>(() => ConfiguracionNegocio.ParaGrabar(
            19m, CondicionFiscal.ResponsableInscripto, 0.01m, null, puntoVenta: null));

        Assert.Equal("alicuotaIva", error.ParamName);
        Assert.DoesNotContain("19", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParaGrabar_PuntoVenta0_Lanza()
    {
        var error = Assert.Throws<ArgumentException>(() => ConfiguracionNegocio.ParaGrabar(
            21m, CondicionFiscal.ResponsableInscripto, 0.01m, null, puntoVenta: 0));

        Assert.Equal("puntoVenta", error.ParamName);
    }

    [Fact]
    public void ParaGrabar_CondicionNoDefinida_Rechazado()
    {
        var error = Assert.Throws<ArgumentException>(() => ConfiguracionNegocio.ParaGrabar(
            21m, (CondicionFiscal)7, 0.01m, null, puntoVenta: null));

        Assert.Equal("condicionFiscal", error.ParamName);
    }

    [Fact]
    public void CambiaParametrosDePrecio_SoloPuntoVenta_Falso()
    {
        // El punto de venta no interviene en el precio de venta: no dispara el recálculo.
        var anterior = new ConfiguracionNegocio(21m, CondicionFiscal.ResponsableInscripto, 0.01m, null, puntoVenta: null);
        var conPuntoVenta = new ConfiguracionNegocio(21m, CondicionFiscal.ResponsableInscripto, 0.01m, null, puntoVenta: 3);

        Assert.False(conPuntoVenta.CambiaParametrosDePrecioRespectoDe(anterior));
    }

    private static decimal Leer(string texto) => decimal.Parse(texto, CultureInfo.InvariantCulture);
}
