using Optica.Domain.Presupuestos;

namespace Optica.Domain.Facturacion;

/// <summary>
/// Una línea de la factura: la copia de una línea del presupuesto con su orden (FR-01, FR-18). Cumple las mismas reglas
/// que <see cref="LineaPresupuesto"/> (D-34) y conserva la descripción y los importes aunque el artículo cambie después.
/// No se modifica (FR-25).
/// </summary>
public sealed class LineaFactura
{
    /// <exception cref="ArgumentException">
    /// El orden es menor que 1, o algún dato incumple las reglas de <see cref="LineaPresupuesto"/> (código, descripción,
    /// importes, cantidad o descuento). El mensaje nombra el campo y no incluye el valor recibido.
    /// </exception>
    public LineaFactura(
        int orden, long codigoArticulo, string descripcion, decimal precioUnitario, int cantidad,
        decimal porcentajeDescuento, decimal precioConDescuento, decimal precioFinal)
    {
        if (orden < 1)
        {
            throw new ArgumentException("El orden de la línea no es válido.", nameof(orden));
        }

        // Las mismas invariantes que la línea del presupuesto, con sus mismos mensajes y campos (D-34): una sola regla.
        ReglasLinea.Validar(
            codigoArticulo, descripcion, precioUnitario, cantidad, porcentajeDescuento, precioConDescuento, precioFinal);

        Orden = orden;
        CodigoArticulo = codigoArticulo;
        Descripcion = descripcion;
        PrecioUnitario = precioUnitario;
        Cantidad = cantidad;
        PorcentajeDescuento = porcentajeDescuento;
        PrecioConDescuento = precioConDescuento;
        PrecioFinal = precioFinal;
    }

    /// <summary>Desde 1, en el orden de las líneas del presupuesto.</summary>
    public int Orden { get; }

    public long CodigoArticulo { get; }

    public string Descripcion { get; }

    public decimal PrecioUnitario { get; }

    /// <summary>De 1 a 9.999.</summary>
    public int Cantidad { get; }

    /// <summary>Porcentaje de 0 a 100, con hasta 2 decimales.</summary>
    public decimal PorcentajeDescuento { get; }

    public decimal PrecioConDescuento { get; }

    public decimal PrecioFinal { get; }

    /// <summary>La copia de <paramref name="linea"/> con el orden recibido (FR-01).</summary>
    /// <exception cref="ArgumentNullException"><paramref name="linea"/> es null.</exception>
    /// <exception cref="ArgumentException">El orden es menor que 1.</exception>
    internal static LineaFactura DesdePresupuesto(int orden, LineaPresupuesto linea)
    {
        ArgumentNullException.ThrowIfNull(linea);
        return new LineaFactura(
            orden, linea.CodigoArticulo, linea.Descripcion, linea.PrecioUnitario, linea.Cantidad,
            linea.PorcentajeDescuento, linea.PrecioConDescuento, linea.PrecioFinal);
    }
}
