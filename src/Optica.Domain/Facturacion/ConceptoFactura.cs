namespace Optica.Domain.Facturacion;

/// <summary>
/// El concepto que la operadora elige en cada factura (FR-08). Con <see cref="ProductosYServicios"/> se informan a ARCA
/// las fechas de servicio y de vencimiento del pago (FR-09). El código de WSFEv1 vive en la integración con ARCA (D-20).
/// </summary>
public enum ConceptoFactura
{
    Productos,
    ProductosYServicios,
}
