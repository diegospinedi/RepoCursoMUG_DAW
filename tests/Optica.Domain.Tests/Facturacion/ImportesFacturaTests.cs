using Optica.Domain.Comun;
using Optica.Domain.Facturacion;

namespace Optica.Domain.Tests.Facturacion;

public class ImportesFacturaTests
{
    [Fact]
    public void Importes_1815Alicuota21_Neto1500Iva315()
    {
        var importes = ImportesFactura.ParaFacturaB(1815m, 21m);

        Assert.Equal(1815m, importes.Total);
        Assert.Equal(1500m, importes.Neto);
        Assert.Equal(315m, importes.Iva);
        Assert.Equal(21m, importes.Alicuota);
        Assert.True(importes.DiscriminaIva);
    }

    [Fact]
    public void Importes_1000Alicuota21_Neto826_45Iva173_55()
    {
        // 1000 / 1,21 = 826,4462…: se redondea a 2 decimales y el IVA es la diferencia (NFR-04).
        var importes = ImportesFactura.ParaFacturaB(1000m, 21m);

        Assert.Equal(826.45m, importes.Neto);
        Assert.Equal(173.55m, importes.Iva);
        Assert.Equal(importes.Total, importes.Neto + importes.Iva);
    }

    [Fact]
    public void Importes_1105Alicuota10_5_Neto1000Iva105()
    {
        var importes = ImportesFactura.ParaFacturaB(1105m, 10.5m);

        Assert.Equal(1000m, importes.Neto);
        Assert.Equal(105m, importes.Iva);
    }

    [Fact]
    public void Importes_OtrasAlicuotas_NetoRedondeadoEIvaPorDiferencia()
    {
        // Con alícuota 0 el neto es el total y no hay IVA.
        var sinIva = ImportesFactura.ParaFacturaB(1815m, 0m);
        Assert.Equal(1815m, sinIva.Neto);
        Assert.Equal(0m, sinIva.Iva);

        // 2,5: 102,5 / 1,025 = 100 exacto; 0,05 / 1,025 = 0,0487… da 0,05 e IVA 0.
        Assert.Equal(100m, ImportesFactura.ParaFacturaB(102.5m, 2.5m).Neto);
        var chico = ImportesFactura.ParaFacturaB(0.05m, 2.5m);
        Assert.Equal(0.05m, chico.Neto);
        Assert.Equal(0m, chico.Iva);

        // 27: 100 / 1,27 = 78,7401… da 78,74 e IVA 21,26.
        var b27 = ImportesFactura.ParaFacturaB(100m, 27m);
        Assert.Equal(78.74m, b27.Neto);
        Assert.Equal(21.26m, b27.Iva);

        // 5: 0,21 / 1,05 = 0,2 exacto.
        Assert.Equal(0.20m, ImportesFactura.ParaFacturaB(0.21m, 5m).Neto);
    }

    [Fact]
    public void Importes_FacturaC_SoloTotal()
    {
        var importes = ImportesFactura.ParaFacturaC(1815m);

        Assert.Equal(1815m, importes.Total);
        Assert.False(importes.DiscriminaIva);
        Assert.Null(importes.Alicuota);

        // Sin desglose: el neto es el total y el IVA 0 (D-20), así neto + IVA = total también en la C.
        Assert.Equal(1815m, importes.Neto);
        Assert.Equal(0m, importes.Iva);
    }

    [Fact]
    public void Importes_TotalOAlicuotaInvalidos_Error()
    {
        foreach (var total in new[] { -0.01m, 1.001m })
        {
            var errorB = Assert.Throws<ArgumentException>(() => ImportesFactura.ParaFacturaB(total, 21m));
            Assert.Equal("total", errorB.ParamName);
            var errorC = Assert.Throws<ArgumentException>(() => ImportesFactura.ParaFacturaC(total));
            Assert.Equal("total", errorC.ParamName);
            Assert.DoesNotContain("1.001", errorC.Message, StringComparison.Ordinal);
        }

        foreach (var alicuota in new[] { 19m, -1m, 101m, 21.5m })
        {
            var error = Assert.Throws<ArgumentException>(() => ImportesFactura.ParaFacturaB(1815m, alicuota));
            Assert.Equal("alicuota", error.ParamName);
            Assert.DoesNotContain("19", error.Message, StringComparison.Ordinal);
        }

        // Un centavo por encima del límite del tipo.
        var sobreMaximo = ReglasImporte.ImporteMaximo + 0.01m;
        Assert.Equal("total", Assert.Throws<ArgumentException>(() => ImportesFactura.ParaFacturaC(sobreMaximo)).ParamName);
        Assert.Equal("total", Assert.Throws<ArgumentException>(() => ImportesFactura.ParaFacturaB(sobreMaximo, 21m)).ParamName);

        // El mayor total del tipo sigue armándose sin desbordar.
        var maximo = ImportesFactura.ParaFacturaB(ReglasImporte.ImporteMaximo, 27m);
        Assert.Equal(ReglasImporte.ImporteMaximo, maximo.Neto + maximo.Iva);
    }

    [Fact]
    public void Importes_Reconstruidos_CumplenLasInvariantes()
    {
        var b = new ImportesFactura(1815m, 1500m, 315m, 21m);
        Assert.Equal(315m, b.Iva);
        var c = new ImportesFactura(1815m, 1815m, 0m, null);
        Assert.False(c.DiscriminaIva);

        // Neto + IVA distinto del total.
        Assert.Equal("iva", Assert.Throws<ArgumentException>(() => new ImportesFactura(1815m, 1500m, 316m, 21m)).ParamName);

        // Negativos o con más de 2 decimales.
        Assert.Equal("neto", Assert.Throws<ArgumentException>(() => new ImportesFactura(1815m, -1m, 1816m, 21m)).ParamName);
        Assert.Equal("neto", Assert.Throws<ArgumentException>(() => new ImportesFactura(1815m, 1814.995m, 0.005m, 21m)).ParamName);
        Assert.Equal("iva", Assert.Throws<ArgumentException>(() => new ImportesFactura(1815m, 1815.01m, -0.01m, 21m)).ParamName);
        Assert.Equal("total", Assert.Throws<ArgumentException>(() => new ImportesFactura(-1m, 0m, 0m, null)).ParamName);

        // Total por encima del límite del tipo.
        var sobreMaximo = ReglasImporte.ImporteMaximo + 0.01m;
        Assert.Equal("total", Assert.Throws<ArgumentException>(() => new ImportesFactura(sobreMaximo, sobreMaximo, 0m, null)).ParamName);

        // Con alícuota, el neto tiene que ser el de la fórmula de FR-14 aunque neto + IVA sume el total.
        var otroNeto = Assert.Throws<ArgumentException>(() => new ImportesFactura(1815m, 1600m, 215m, 21m));
        Assert.Equal("neto", otroNeto.ParamName);
        Assert.StartsWith("Los importes de la factura no corresponden a la alícuota.", otroNeto.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("1600", otroNeto.Message, StringComparison.Ordinal);
        Assert.Equal("neto", Assert.Throws<ArgumentException>(() => new ImportesFactura(1000m, 826.44m, 173.56m, 21m)).ParamName);
        Assert.Equal(826.45m, new ImportesFactura(1000m, 826.45m, 173.55m, 21m).Neto);

        // Alícuota que no es de ARCA, y Factura C con IVA.
        Assert.Equal("alicuota", Assert.Throws<ArgumentException>(() => new ImportesFactura(1815m, 1500m, 315m, 19m)).ParamName);
        Assert.Equal("iva", Assert.Throws<ArgumentException>(() => new ImportesFactura(1815m, 1500m, 315m, null)).ParamName);
    }
}
