using Optica.Domain.Catalogo;
using Optica.Domain.Comun;
using Optica.Domain.Configuracion;

namespace Optica.Domain.Tests.Catalogo;

public class CalculadoraPrecioVentaTests
{
    private static ConfiguracionNegocio ResponsableInscripto(decimal alicuota = 21m, decimal multiplo = 0.01m) =>
        new(alicuota, CondicionFiscal.ResponsableInscripto, multiplo, null, puntoVenta: null);

    private static ConfiguracionNegocio Monotributo(decimal alicuota = 21m, decimal multiplo = 0.01m) =>
        new(alicuota, CondicionFiscal.Monotributo, multiplo, null, puntoVenta: null);

    private static PrecioCalculado Precio(decimal costo, decimal margen, ConfiguracionNegocio configuracion)
    {
        var resultado = CalculadoraPrecioVenta.Calcular(costo, margen, configuracion);

        Assert.Null(resultado.Infraccion);
        Assert.NotNull(resultado.Precio);
        return resultado.Precio;
    }

    [Fact]
    public void Calcular_RI_Costo1210Margen50_Desglose1000_1500_Venta1815()
    {
        var precio = Precio(1210m, 50m, ResponsableInscripto());

        Assert.Equal(1000m, precio.CostoSinIva);
        Assert.Equal(1500m, precio.PrecioSinIva);
        Assert.Equal(1815m, precio.PrecioVenta);
    }

    [Fact]
    public void Calcular_RI_Costo1000Margen50_Venta1500()
    {
        Assert.Equal(1500m, Precio(1000m, 50m, ResponsableInscripto()).PrecioVenta);
    }

    [Fact]
    public void Calcular_RI_Costo1210Margen150_Venta3025()
    {
        Assert.Equal(3025m, Precio(1210m, 150m, ResponsableInscripto()).PrecioVenta);
    }

    [Fact]
    public void Calcular_RI_CostoMenos100Margen50_VentaMenos150()
    {
        // Regresión de ADR-010: los tres pasos literales daban −149,99.
        Assert.Equal(-150m, Precio(-100m, 50m, ResponsableInscripto()).PrecioVenta);
    }

    [Fact]
    public void Calcular_Monotributo_Costo1210Margen50_Venta1815()
    {
        var precio = Precio(1210m, 50m, Monotributo());

        Assert.Equal(1210m, precio.CostoSinIva);
        Assert.Equal(1815m, precio.PrecioSinIva);
        Assert.Equal(1815m, precio.PrecioVenta);
    }

    [Fact]
    public void Calcular_Multiplo50_1815coma37_Redondea1850()
    {
        Assert.Equal(1850m, Precio(1815.37m, 0m, Monotributo(multiplo: 50m)).PrecioVenta);
        Assert.Equal(1850m, Precio(1815.37m, 0m, ResponsableInscripto(multiplo: 50m)).PrecioVenta);
    }

    [Fact]
    public void Calcular_Multiplo0coma01_1666coma656_Redondea1666coma66()
    {
        // 1.388,88 × 120 / 100 = 1.666,656 exacto.
        Assert.Equal(1666.66m, Precio(1388.88m, 20m, Monotributo()).PrecioVenta);
        Assert.Equal(1666.66m, Precio(1388.88m, 20m, ResponsableInscripto()).PrecioVenta);
    }

    [Fact]
    public void Calcular_MultiploExacto_NoAgregaUnMultiplo()
    {
        Assert.Equal(1850m, Precio(1850m, 0m, Monotributo(multiplo: 50m)).PrecioVenta);
        Assert.Equal(0m, Precio(0m, 50m, ResponsableInscripto(multiplo: 50m)).PrecioVenta);
    }

    [Fact]
    public void Calcular_CasosMedidosPorLaAuditoria_SinCentavoDeMas()
    {
        Assert.Equal(4.50m, Precio(3m, 50m, ResponsableInscripto(alicuota: 27m)).PrecioVenta);
        Assert.Equal(33.33m, Precio(33.33m, 0m, ResponsableInscripto(alicuota: 27m)).PrecioVenta);
        Assert.Equal(-150m, Precio(-100m, 50m, ResponsableInscripto(alicuota: 21m)).PrecioVenta);
    }

    [Fact]
    public void Calcular_PrecioVentaNoDependeDeLaAlicuota()
    {
        foreach (var alicuota in new[] { 0m, 10.5m, 21m, 27m, 100m })
        {
            Assert.Equal(1815m, Precio(1210m, 50m, ResponsableInscripto(alicuota)).PrecioVenta);
        }
    }

    [Fact]
    public void Calcular_Negativo_RedondeaHaciaMasInfinito()
    {
        // D-04: −1,36 × 110,3 / 100 = −1,50008 → −1,50, nunca −1,51.
        Assert.Equal(-1.50m, Precio(-1.36m, 10.3m, Monotributo()).PrecioVenta);
        // −1.815,37 con múltiplo 50 → −1.800, no −1.850.
        Assert.Equal(-1800m, Precio(-1815.37m, 0m, Monotributo(multiplo: 50m)).PrecioVenta);
    }

    [Fact]
    public void Calcular_PrecioQueNoEntra_Rechazado()
    {
        // El valor exacto supera el máximo.
        var exacto = CalculadoraPrecioVenta.Calcular(ReglasImporte.ImporteMaximo, 1000m, ResponsableInscripto());
        Assert.Null(exacto.Precio);
        Assert.Equal(InfraccionArticulo.PrecioVentaFueraDeRango, exacto.Infraccion);

        var negativo = CalculadoraPrecioVenta.Calcular(-ReglasImporte.ImporteMaximo, 0.01m, Monotributo());
        Assert.Null(negativo.Precio);
        Assert.Equal(InfraccionArticulo.PrecioVentaFueraDeRango, negativo.Infraccion);

        // El exacto entra (…758,07) y el redondeado al múltiplo 0,10 (…758,10) no.
        var redondeado = CalculadoraPrecioVenta.Calcular(ReglasImporte.ImporteMaximo, 0m, Monotributo(multiplo: 0.10m));
        Assert.Null(redondeado.Precio);
        Assert.Equal(InfraccionArticulo.PrecioVentaFueraDeRango, redondeado.Infraccion);
    }

    [Fact]
    public void Calcular_ValoresQueDesbordanElTipo_RechazadoSinExcepcion()
    {
        var resultado = CalculadoraPrecioVenta.Calcular(decimal.MaxValue, 1000m, ResponsableInscripto());

        Assert.Null(resultado.Precio);
        Assert.Equal(InfraccionArticulo.PrecioVentaFueraDeRango, resultado.Infraccion);
    }

    [Fact]
    public void Calcular_PrecioIgualAlMaximo_Valido()
    {
        Assert.Equal(ReglasImporte.ImporteMaximo, Precio(ReglasImporte.ImporteMaximo, 0m, Monotributo()).PrecioVenta);
    }
}
