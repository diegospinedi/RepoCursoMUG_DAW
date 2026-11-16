namespace Optica.Application.Facturacion;

/// <summary>
/// Arma el PDF de una factura autorizada (FR-06 a FR-15 de FEAT-009). La implementación vive en Infrastructure
/// (ADR-018, ADR-027); la aplicación no depende de la biblioteca de PDF ni de la de QR.
/// </summary>
public interface IGeneradorPdfFactura
{
    /// <summary>Los bytes del PDF de la factura.</summary>
    /// <exception cref="ArgumentNullException">El documento es null.</exception>
    /// <exception cref="InvalidOperationException">
    /// No se pudo dibujar el PDF o el QR. El mensaje es un texto fijo, sin datos del receptor ni del emisor, y no lleva
    /// la excepción original.
    /// </exception>
    byte[] Generar(DocumentoFactura documento);
}
