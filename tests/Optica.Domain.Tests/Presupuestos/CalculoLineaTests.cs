using System.Globalization;
using Optica.Domain.Comun;
using Optica.Domain.Presupuestos;

namespace Optica.Domain.Tests.Presupuestos;

public class CalculoLineaTests
{
    /// <summary>
    /// La tabla de casos común con <c>web/src/presupuestos/reglas.test.ts</c> (ADR-016): un cambio de la fórmula
    /// obliga a cambiar los dos lados y esta tabla. Columnas: nombre del caso (solo para identificarlo en el informe
    /// de pruebas), Precio Unitario, Porcentaje de Descuento, Cantidad, Precio con Descuento esperado y Precio Final
    /// esperado. El caso AC-07 de la tabla (la suma de dos Precios Finales) está en
    /// <see cref="TablaComun_AC07_SumaDePreciosFinales"/>.
    /// </summary>
    public static TheoryData<string, decimal, decimal, int, decimal, decimal> TablaComun => new()
    {
        { "AC-01", 1000.00m, 10m, 3, 900.00m, 2700.00m },
        { "AC-04", 1000.00m, 100m, 1, 0.00m, 0.00m },
        { "AC-05", 1000.00m, 0m, 1, 1000.00m, 1000.00m },
        { "AC-06", 6.67m, 50m, 3, 3.34m, 10.02m },
        { "mitad hacia arriba 0,025", 0.05m, 50m, 1, 0.03m, 0.03m },
        { "mitad hacia arriba 0,505", 1.01m, 50m, 1, 0.51m, 0.51m },
        { "redondeo hacia abajo", 99.99m, 33.33m, 1, 66.66m, 66.66m },
        { "descuento con 2 decimales", 1000.00m, 12.34m, 1, 876.60m, 876.60m },
        { "cantidad máxima", 1000.00m, 10m, 9_999, 900.00m, 8_999_100.00m },
        { "tope del contrato con la web", 90_071_992_547_409.91m, 0m, 1, 90_071_992_547_409.91m, 90_071_992_547_409.91m },
    };

    [Theory]
    [MemberData(nameof(TablaComun))]
    public void TablaComun_PrecioConDescuentoYPrecioFinal(
        string _, decimal precioUnitario, decimal descuento, int cantidad, decimal conDescuento, decimal final)
    {
        var precioConDescuento = CalculoLinea.PrecioConDescuento(precioUnitario, descuento);

        Assert.Equal(conDescuento, precioConDescuento);
        Assert.Equal(final, CalculoLinea.PrecioFinal(precioConDescuento, cantidad));
    }

    [Fact]
    public void TablaComun_AC07_SumaDePreciosFinales()
    {
        // AC-07 de la tabla común (ADR-016): 6,67 × 3 con 50 % → 10,02 y 1.000,00 × 3 con 10 % → 2.700,00;
        // la suma de los Precios Finales ya redondeados es 2.710,02.
        var finalAc06 = CalculoLinea.PrecioFinal(CalculoLinea.PrecioConDescuento(6.67m, 50m), 3);
        var finalAc01 = CalculoLinea.PrecioFinal(CalculoLinea.PrecioConDescuento(1000.00m, 10m), 3);

        Assert.Equal(10.02m, finalAc06);
        Assert.Equal(2700.00m, finalAc01);
        Assert.Equal(2710.02m, finalAc06 + finalAc01);
    }

    [Fact]
    public void PrecioConDescuento_Ejemplos_AC01_AC04_AC05_AC06()
    {
        Assert.Equal(900.00m, CalculoLinea.PrecioConDescuento(1000.00m, 10m));
        Assert.Equal(0.00m, CalculoLinea.PrecioConDescuento(1000.00m, 100m));
        Assert.Equal(1000.00m, CalculoLinea.PrecioConDescuento(1000.00m, 0m));
        Assert.Equal(3.34m, CalculoLinea.PrecioConDescuento(6.67m, 50m));
    }

    [Fact]
    public void PrecioFinal_RedondeaDespuesDelDescuento()
    {
        // 6,67 × 0,5 = 3,335 → 3,34; 3,34 × 3 = 10,02. Sin redondear antes daría 10,005 → 10,01.
        var precioConDescuento = CalculoLinea.PrecioConDescuento(6.67m, 50m);

        Assert.Equal(10.02m, CalculoLinea.PrecioFinal(precioConDescuento, 3));
        Assert.Equal(2700.00m, CalculoLinea.PrecioFinal(900.00m, 3));
    }

    [Fact]
    public void PrecioConDescuento_MitadHaciaArriba()
    {
        // Mitad hacia arriba, no redondeo bancario: 0,025 → 0,03 (el bancario daría 0,02) y 0,505 → 0,51.
        Assert.Equal(0.03m, CalculoLinea.PrecioConDescuento(0.05m, 50m));
        Assert.Equal(0.51m, CalculoLinea.PrecioConDescuento(1.01m, 50m));
        Assert.Equal(0.01m, CalculoLinea.PrecioConDescuento(0.01m, 50m));
        Assert.Equal(66.66m, CalculoLinea.PrecioConDescuento(99.99m, 33.33m));
    }

    [Fact]
    public void Precios_ConEscala2()
    {
        // La Api serializa el decimal con su escala: 900.00 como el precioUnitario, no 900.
        Assert.Equal("900.00", CalculoLinea.PrecioConDescuento(1000m, 10m).ToString(CultureInfo.InvariantCulture));
        Assert.Equal("0.00", CalculoLinea.PrecioConDescuento(1000m, 100m).ToString(CultureInfo.InvariantCulture));
        Assert.Equal("2700.00", CalculoLinea.PrecioFinal(900m, 3).ToString(CultureInfo.InvariantCulture));
        Assert.Equal("21.50", CalculoLinea.PrecioFinal(21.500m, 1).ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Limites_CantidadDe1A9999()
    {
        Assert.Equal(1, CalculoLinea.CantidadMinima);
        Assert.Equal(9_999, CalculoLinea.CantidadMaxima);
    }

    [Fact]
    public void LimiteDelTipo_NoDesborda()
    {
        // El mayor Precio Unitario que admite el tipo, sin descuento y con la mitad: el cálculo no desborda.
        Assert.Equal(ReglasImporte.ImporteMaximo, CalculoLinea.PrecioConDescuento(ReglasImporte.ImporteMaximo, 0m));
        Assert.Equal(46_116_860_184_273_879.04m, CalculoLinea.PrecioConDescuento(ReglasImporte.ImporteMaximo, 50m));

        // PrecioFinal devuelve el producto exacto aunque supere el límite: decidir el rechazo es de DesdeArticulo.
        Assert.Equal(
            ReglasImporte.ImporteMaximo * CalculoLinea.CantidadMaxima,
            CalculoLinea.PrecioFinal(ReglasImporte.ImporteMaximo, CalculoLinea.CantidadMaxima));
    }

    [Fact]
    public void Argumentos_FueraDeRango_Error()
    {
        var casos = new (string Campo, string Valor, Func<decimal> Calcular)[]
        {
            ("precioUnitario", "-150", () => CalculoLinea.PrecioConDescuento(-150m, 0m)),
            ("precioUnitario", "1850.555", () => CalculoLinea.PrecioConDescuento(1850.555m, 0m)),
            ("precioUnitario", "92233720368547758.08", () => CalculoLinea.PrecioConDescuento(ReglasImporte.ImporteMaximo + 0.01m, 0m)),
            ("porcentajeDescuento", "-0.01", () => CalculoLinea.PrecioConDescuento(1850m, -0.01m)),
            ("porcentajeDescuento", "100.01", () => CalculoLinea.PrecioConDescuento(1850m, 100.01m)),
            ("porcentajeDescuento", "10.555", () => CalculoLinea.PrecioConDescuento(1850m, 10.555m)),
            ("precioConDescuento", "-777", () => CalculoLinea.PrecioFinal(-777m, 1)),
            ("precioConDescuento", "1.001", () => CalculoLinea.PrecioFinal(1.001m, 1)),
            ("cantidad", "0", () => CalculoLinea.PrecioFinal(1850m, 0)),
            ("cantidad", "10000", () => CalculoLinea.PrecioFinal(1850m, 10_000)),
            ("cantidad", "-4321", () => CalculoLinea.PrecioFinal(1850m, -4321)),
        };

        foreach (var (campo, valor, calcular) in casos)
        {
            var error = Assert.Throws<ArgumentException>(() => calcular());
            Assert.Equal(campo, error.ParamName);
            Assert.DoesNotContain(valor, error.Message, StringComparison.Ordinal);
        }
    }
}
