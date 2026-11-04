using System.Diagnostics;
using Optica.Domain.Comun;
using Optica.Domain.Configuracion;

namespace Optica.Domain.Catalogo;

/// <summary>
/// El desglose de FR-06 (costo y precio sin IVA, sin redondear) y el precio de venta ya redondeado al múltiplo
/// (FR-09).
/// </summary>
public sealed record PrecioCalculado(decimal CostoSinIva, decimal PrecioSinIva, decimal PrecioVenta);

/// <summary>
/// El resultado de <see cref="CalculadoraPrecioVenta.Calcular"/>: o un <see cref="Precio"/>, o la
/// <see cref="Infraccion"/> <see cref="InfraccionArticulo.PrecioVentaFueraDeRango"/> en su lugar (D-06).
/// </summary>
public sealed class ResultadoPrecioVenta
{
    private ResultadoPrecioVenta(PrecioCalculado? precio, InfraccionArticulo? infraccion)
    {
        Precio = precio;
        Infraccion = infraccion;
    }

    public PrecioCalculado? Precio { get; }

    public InfraccionArticulo? Infraccion { get; }

    internal static ResultadoPrecioVenta Calculado(PrecioCalculado precio) => new(precio, null);

    internal static ResultadoPrecioVenta FueraDeRango() => new(null, InfraccionArticulo.PrecioVentaFueraDeRango);
}

/// <summary>Cálculo del precio de venta (FR-06, FR-07, FR-09; ADR-010, D-04, D-05, D-06).</summary>
public static class CalculadoraPrecioVenta
{
    /// <summary>
    /// Calcula el precio de venta exacto como costo × (100 + margen) / 100 en las dos condiciones fiscales (ADR-010:
    /// en Responsable Inscripto la alícuota se cancela) y lo redondea hacia +∞ al múltiplo configurado (D-04). Si el
    /// valor exacto o el redondeado supera <see cref="ReglasImporte.ImporteMaximo"/> en módulo, devuelve
    /// <see cref="InfraccionArticulo.PrecioVentaFueraDeRango"/> en lugar del precio (D-06). Nunca lanza por desborde.
    /// </summary>
    /// <remarks>
    /// Precondición: <paramref name="costo"/> y <paramref name="margen"/> ya pasaron
    /// <see cref="PoliticaArticulo.Evaluar"/> (hasta 2 decimales, costo con módulo hasta
    /// <see cref="ReglasImporte.ImporteMaximo"/>, margen de 0 a 1000). La exactitud de ADR-010 depende de eso: con
    /// entradas de 2 decimales el producto tiene como máximo 4 y <c>decimal</c> lo representa sin error. Con otras
    /// entradas el resultado no se garantiza exacto; solo se garantiza que no desborda.
    /// </remarks>
    public static ResultadoPrecioVenta Calcular(decimal costo, decimal margen, ConfiguracionNegocio configuracion)
    {
        ArgumentNullException.ThrowIfNull(configuracion);

        try
        {
            var exacto = costo * (100m + margen) / 100m;
            if (Math.Abs(exacto) > ReglasImporte.ImporteMaximo)
            {
                return ResultadoPrecioVenta.FueraDeRango();
            }

            var precioVenta = RedondearHaciaArriba(exacto, configuracion.MultiploRedondeo);
            if (Math.Abs(precioVenta) > ReglasImporte.ImporteMaximo)
            {
                return ResultadoPrecioVenta.FueraDeRango();
            }

            var (costoSinIva, precioSinIva) = Desglose(costo, margen, configuracion);
            return ResultadoPrecioVenta.Calculado(new PrecioCalculado(costoSinIva, precioSinIva, precioVenta));
        }
        catch (OverflowException)
        {
            // Solo con valores que la política ya rechaza; aun así, nunca un desborde (D-06).
            return ResultadoPrecioVenta.FueraDeRango();
        }
    }

    // D-04: hacia +∞ también con negativos (−150,004 con múltiplo 0,01 da −150,00).
    private static decimal RedondearHaciaArriba(decimal valor, decimal multiplo) =>
        decimal.Ceiling(valor / multiplo) * multiplo;

    // FR-06 pasos (a) y (b), sin redondear: solo desglose, nunca el valor al que se aplica el redondeo (D-05).
    private static (decimal CostoSinIva, decimal PrecioSinIva) Desglose(
        decimal costo, decimal margen, ConfiguracionNegocio configuracion)
    {
        var factorMargen = 1m + margen / 100m;
        var costoSinIva = configuracion.CondicionFiscal switch
        {
            CondicionFiscal.ResponsableInscripto => costo / (1m + configuracion.AlicuotaIva / 100m),
            CondicionFiscal.Monotributo => costo,
            _ => throw new UnreachableException("Condición fiscal sin cálculo asignado."),
        };

        return (costoSinIva, costoSinIva * factorMargen);
    }
}
