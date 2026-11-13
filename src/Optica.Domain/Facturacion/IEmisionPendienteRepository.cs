namespace Optica.Domain.Facturacion;

/// <summary>
/// Persistencia de las emisiones pendientes (D-09, ADR-021): una como máximo por presupuesto. Se agregan y se quitan,
/// no se modifican. <see cref="Agregar"/> y <see cref="Quitar"/> solo registran el cambio: lo confirma la unidad de
/// trabajo.
/// </summary>
public interface IEmisionPendienteRepository
{
    /// <summary>
    /// Registra la emisión pendiente. Si el presupuesto ya tiene una, la confirmación falla por unicidad (FR-24).
    /// </summary>
    void Agregar(EmisionPendiente pendiente);

    /// <summary>
    /// Registra la baja de la emisión pendiente del presupuesto <paramref name="presupuestoNumero"/>: autorizada,
    /// rechazada o no enviada (FR-21, FR-22). Una sin respuesta no se quita (FR-23, FR-24). Si el presupuesto no tiene
    /// una emisión pendiente no hace nada: la confirmación de la unidad de trabajo no falla por eso.
    /// </summary>
    void Quitar(long presupuestoNumero);

    /// <summary>Verdadero si el presupuesto <paramref name="presupuestoNumero"/> tiene una emisión pendiente (FR-24).</summary>
    Task<bool> ExisteAsync(long presupuestoNumero, CancellationToken cancelacion = default);
}
