using Optica.Domain.Comun;

namespace Optica.Domain.Presupuestos;

/// <summary>
/// El cálculo de los importes de una línea del presupuesto (FR-04, FR-05, NFR-02; D-04, ADR-016) y la política de
/// Cantidad y Porcentaje de Descuento de la línea (FR-02, FR-03, D-01). Trabaja en centésimos enteros (ADR-008) y
/// redondea cada paso a 2 decimales con mitad hacia arriba. La web aplica las mismas fórmulas en espejo para mostrar
/// (<c>web/src/presupuestos/reglas.ts</c>); la tabla de casos común de <c>CalculoLineaTests</c> y
/// <c>reglas.test.ts</c> es la que controla que no diverjan.
/// </summary>
public static class CalculoLinea
{
    /// <summary>La menor Cantidad de una línea (FR-02).</summary>
    public const int CantidadMinima = 1;

    /// <summary>La mayor Cantidad de una línea (FR-02, D-01).</summary>
    public const int CantidadMaxima = 9_999;

    /// <summary>El menor Porcentaje de Descuento de una línea (FR-03).</summary>
    public const decimal DescuentoMinimo = 0m;

    /// <summary>El mayor Porcentaje de Descuento de una línea (FR-03).</summary>
    public const decimal DescuentoMaximo = 100m;

    internal const string MensajeCantidadInvalida = "La cantidad no es válida.";
    internal const string MensajeDescuentoInvalido = "El porcentaje de descuento no es válido.";
    internal const string MensajePrecioUnitarioInvalido = "El precio unitario no es válido.";
    internal const string MensajePrecioConDescuentoInvalido = "El precio con descuento no es válido.";
    internal const string MensajePrecioFinalInvalido = "El precio final no es válido.";

    private const decimal CentesimosPorUnidad = 100m;

    // Multiplicar centésimos enteros (escala 0) por 0,01 da el importe con escala 2: 90000 → 900.00, no 900.
    private const decimal UnidadesPorCentesimo = 0.01m;

    // 100 % expresado en centésimos de punto porcentual.
    private const decimal CienPorCientoEnCentesimos = 10_000m;

    /// <summary>
    /// Precio Unitario × (10000 − D) / 10000, con el Precio Unitario y el descuento D en centésimos, redondeado a
    /// 2 decimales con mitad hacia arriba (FR-04, D-04). Con entradas de 2 decimales y hasta
    /// <see cref="ReglasImporte.ImporteMaximo"/> el producto entra en <c>decimal</c> sin error y nunca supera el
    /// Precio Unitario. El resultado tiene escala 2.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// El Precio Unitario es negativo, tiene más de 2 decimales o no entra en el tipo; o el descuento no está entre
    /// 0 y 100 con hasta 2 decimales. El mensaje nombra el campo y no incluye el valor recibido.
    /// </exception>
    public static decimal PrecioConDescuento(decimal precioUnitario, decimal porcentajeDescuento)
    {
        ValidarImporte(precioUnitario, nameof(precioUnitario), MensajePrecioUnitarioInvalido);
        ValidarDescuento(porcentajeDescuento);
        return CalcularPrecioConDescuento(precioUnitario, porcentajeDescuento);
    }

    /// <summary>
    /// El Precio con Descuento ya redondeado por la Cantidad (FR-05). Con un Precio con Descuento de 2 decimales el
    /// producto ya tiene 2 decimales: no hay nada que redondear. Devuelve el producto exacto, con escala 2, aunque
    /// supere <see cref="ReglasImporte.ImporteMaximo"/>; rechazarlo es de quien lo usa
    /// (<see cref="LineaPresupuesto.DesdeArticulo"/>). No desborda: el máximo es ImporteMaximo × 9.999.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// El Precio con Descuento es negativo, tiene más de 2 decimales o no entra en el tipo; o la Cantidad no está
    /// entre <see cref="CantidadMinima"/> y <see cref="CantidadMaxima"/>. El mensaje nombra el campo y no incluye el
    /// valor recibido.
    /// </exception>
    public static decimal PrecioFinal(decimal precioConDescuento, int cantidad)
    {
        ValidarImporte(precioConDescuento, nameof(precioConDescuento), MensajePrecioConDescuentoInvalido);
        ValidarCantidad(cantidad);
        return CalcularPrecioFinal(precioConDescuento, cantidad);
    }

    /// <summary>Verdadero si la Cantidad está entre <see cref="CantidadMinima"/> y <see cref="CantidadMaxima"/>.</summary>
    internal static bool EsCantidadValida(int cantidad) => cantidad is >= CantidadMinima and <= CantidadMaxima;

    /// <summary>
    /// Verdadero si el descuento está entre <see cref="DescuentoMinimo"/> y <see cref="DescuentoMaximo"/>, ambos
    /// inclusive, con hasta 2 decimales (FR-03).
    /// </summary>
    internal static bool EsDescuentoValido(decimal porcentajeDescuento) =>
        porcentajeDescuento is >= DescuentoMinimo and <= DescuentoMaximo
        && !ReglasImporte.TieneMasDeDosDecimales(porcentajeDescuento);

    /// <exception cref="ArgumentException">La Cantidad no es válida; el campo es <c>cantidad</c>.</exception>
    internal static void ValidarCantidad(int cantidad)
    {
        if (!EsCantidadValida(cantidad))
        {
            throw new ArgumentException(MensajeCantidadInvalida, nameof(cantidad));
        }
    }

    /// <exception cref="ArgumentException">El descuento no es válido; el campo es <c>porcentajeDescuento</c>.</exception>
    internal static void ValidarDescuento(decimal porcentajeDescuento)
    {
        if (!EsDescuentoValido(porcentajeDescuento))
        {
            throw new ArgumentException(MensajeDescuentoInvalido, nameof(porcentajeDescuento));
        }
    }

    /// <summary>Rechaza un importe negativo, con más de 2 decimales o que no entra en el tipo.</summary>
    /// <exception cref="ArgumentException">Con el mensaje y el campo recibidos, sin el valor.</exception>
    internal static void ValidarImporte(decimal importe, string campo, string mensaje)
    {
        if (importe < 0m || importe > ReglasImporte.ImporteMaximo || ReglasImporte.TieneMasDeDosDecimales(importe))
        {
            throw new ArgumentException(mensaje, campo);
        }
    }

    /// <summary><see cref="PrecioConDescuento"/> con los argumentos ya validados por quien llama.</summary>
    internal static decimal CalcularPrecioConDescuento(decimal precioUnitario, decimal porcentajeDescuento)
    {
        var unitarioEnCentesimos = precioUnitario * CentesimosPorUnidad;
        var descuentoEnCentesimos = porcentajeDescuento * CentesimosPorUnidad;
        var exacto = unitarioEnCentesimos * (CienPorCientoEnCentesimos - descuentoEnCentesimos)
            / CienPorCientoEnCentesimos;

        // Los importes no son negativos: alejarse del cero es redondear mitad hacia arriba.
        return decimal.Round(exacto, 0, MidpointRounding.AwayFromZero) * UnidadesPorCentesimo;
    }

    /// <summary><see cref="PrecioFinal"/> con los argumentos ya validados por quien llama.</summary>
    internal static decimal CalcularPrecioFinal(decimal precioConDescuento, int cantidad)
    {
        // Con hasta 2 decimales el valor en centésimos es entero: Round solo baja la escala a 0, no cambia el valor.
        var centesimos = decimal.Round(precioConDescuento * CentesimosPorUnidad, 0, MidpointRounding.AwayFromZero);
        return centesimos * cantidad * UnidadesPorCentesimo;
    }
}
