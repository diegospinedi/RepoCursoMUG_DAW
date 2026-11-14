namespace Optica.Domain.Facturacion;

/// <summary>
/// El tipo de factura a consumidor final: B con Responsable Inscripto (FR-05) y C con Monotributo (FR-06). El código de
/// WSFEv1 de cada uno vive en la integración con ARCA (D-20).
/// </summary>
public enum TipoComprobante
{
    FacturaB,
    FacturaC,
}
