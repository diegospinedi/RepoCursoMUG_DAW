namespace Optica.Domain.Facturacion;

/// <summary>
/// Un resultado de la búsqueda de facturas (FR-23 de FEAT-009): el número del presupuesto de origen, el comprobante
/// (tipo, punto de venta y número), la fecha, Apellido, Nombre y DNI del cliente del presupuesto (FR-25, también si se
/// emitió sin identificar al receptor) y el total.
/// </summary>
/// <remarks>
/// Lleva datos personales (Ley 25.326): es una clase y no un record, así <see cref="object.ToString"/> no muestra
/// Apellido, Nombre ni DNI (R-01).
/// </remarks>
public sealed class ResumenFactura
{
    /// <exception cref="ArgumentNullException">El comprobante, Apellido, Nombre o DNI es null.</exception>
    public ResumenFactura(
        long presupuestoNumero, NumeroComprobante comprobante, DateOnly fecha, string apellido, string nombre, string dni,
        decimal total)
    {
        ArgumentNullException.ThrowIfNull(comprobante);
        ArgumentNullException.ThrowIfNull(apellido);
        ArgumentNullException.ThrowIfNull(nombre);
        ArgumentNullException.ThrowIfNull(dni);

        PresupuestoNumero = presupuestoNumero;
        Comprobante = comprobante;
        Fecha = fecha;
        Apellido = apellido;
        Nombre = nombre;
        Dni = dni;
        Total = total;
    }

    /// <summary>El número del presupuesto que le dio origen.</summary>
    public long PresupuestoNumero { get; }

    /// <summary>Tipo, punto de venta y número: "Factura B 0003-00000125".</summary>
    public NumeroComprobante Comprobante { get; }

    /// <summary>La fecha del comprobante.</summary>
    public DateOnly Fecha { get; }

    /// <summary>El Apellido del cliente del presupuesto, como se guarda (sin plegar).</summary>
    public string Apellido { get; }

    /// <summary>El Nombre del cliente del presupuesto, como se guarda (sin plegar).</summary>
    public string Nombre { get; }

    /// <summary>El DNI del cliente del presupuesto, como se guarda: solo dígitos.</summary>
    public string Dni { get; }

    /// <summary>El total de la factura.</summary>
    public decimal Total { get; }
}
