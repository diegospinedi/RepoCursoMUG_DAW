namespace Optica.Domain.Facturacion;

/// <summary>
/// Lo que se pide a ARCA para autorizar una factura: tipo, punto de venta, fecha, concepto, receptor e importes, y, con
/// <see cref="ConceptoFactura.ProductosYServicios"/>, las fechas de servicio y de vencimiento del pago iguales a la del
/// comprobante (FR-09). El número lo asigna ARCA. Lleva el DNI del receptor: no sobrescribe
/// <see cref="object.ToString"/> (Ley 25.326).
/// </summary>
public sealed class SolicitudComprobante
{
    /// <exception cref="ArgumentNullException">El receptor o los importes son null.</exception>
    /// <exception cref="ArgumentException">
    /// El tipo o el concepto no están definidos, el punto de venta no está entre 1 y 99999, la fecha es la por defecto,
    /// o los importes no corresponden al tipo (la B discrimina IVA y la C no). El mensaje nombra el campo y no incluye el
    /// valor recibido.
    /// </exception>
    public SolicitudComprobante(
        TipoComprobante tipo, int puntoVenta, DateOnly fecha, ConceptoFactura concepto, ReceptorFactura receptor,
        ImportesFactura importes)
    {
        ReglasFactura.ValidarTipo(tipo);
        ReglasFactura.ValidarPuntoVenta(puntoVenta);
        ReglasFactura.ValidarFecha(fecha, nameof(fecha), "La fecha del comprobante no es válida.");
        ReglasFactura.ValidarConcepto(concepto);
        ArgumentNullException.ThrowIfNull(receptor);
        ArgumentNullException.ThrowIfNull(importes);
        ReglasFactura.ValidarImportesDelTipo(tipo, importes);

        Tipo = tipo;
        PuntoVenta = puntoVenta;
        Fecha = fecha;
        Concepto = concepto;
        Receptor = receptor;
        Importes = importes;

        if (concepto == ConceptoFactura.ProductosYServicios)
        {
            FechaServicioDesde = fecha;
            FechaServicioHasta = fecha;
            FechaVencimientoPago = fecha;
        }
    }

    public TipoComprobante Tipo { get; }

    /// <summary>El punto de venta de la configuración vigente, de 1 a 99999 (FR-17, FR-29).</summary>
    public int PuntoVenta { get; }

    /// <summary>La fecha local del día de emisión (FR-07, D-21).</summary>
    public DateOnly Fecha { get; }

    public ConceptoFactura Concepto { get; }

    public ReceptorFactura Receptor { get; }

    public ImportesFactura Importes { get; }

    /// <summary>Con Productos y servicios, la fecha del comprobante; con Productos, null (FR-09, AC-08).</summary>
    public DateOnly? FechaServicioDesde { get; }

    /// <summary>Con Productos y servicios, la fecha del comprobante; con Productos, null (FR-09, AC-08).</summary>
    public DateOnly? FechaServicioHasta { get; }

    /// <summary>Con Productos y servicios, la fecha del comprobante; con Productos, null (FR-09, AC-08).</summary>
    public DateOnly? FechaVencimientoPago { get; }
}
