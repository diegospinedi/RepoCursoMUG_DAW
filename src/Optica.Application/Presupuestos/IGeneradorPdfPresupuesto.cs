using Optica.Domain.Marca;
using Optica.Domain.Presupuestos;

namespace Optica.Application.Presupuestos;

/// <summary>
/// Arma el PDF de un presupuesto en Final con el logo y los colores de la marca (FR-13 a FR-16 de FEAT-006). La
/// implementación vive en Infrastructure (ADR-018); la aplicación no depende de la biblioteca de PDF.
/// </summary>
public interface IGeneradorPdfPresupuesto
{
    /// <summary>Los bytes del PDF del presupuesto.</summary>
    /// <exception cref="ArgumentNullException">El presupuesto o la marca son null.</exception>
    /// <exception cref="ArgumentException">El presupuesto está en Borrador (FR-12): no se genera su PDF.</exception>
    /// <exception cref="InvalidOperationException">
    /// No se pudo dibujar el PDF (por ejemplo, un logo que no se puede decodificar). El mensaje es un texto fijo, sin
    /// datos del cliente ni rutas, y no lleva la excepción original.
    /// </exception>
    byte[] Generar(Presupuesto presupuesto, IdentidadVisual marca);
}
