namespace Optica.Domain.Facturacion;

/// <summary>
/// Persistencia de las facturas autorizadas. Solo alta y lectura: no hay métodos para modificar ni eliminar una
/// factura (FR-25). <see cref="Agregar"/> solo registra el alta: la confirma la unidad de trabajo.
/// </summary>
public interface IFacturaRepository
{
    /// <summary>
    /// Registra el alta de la factura con sus líneas, en la misma confirmación (FR-18). Si el presupuesto ya tiene una
    /// factura, la confirmación falla por unicidad (FR-03).
    /// </summary>
    void Agregar(Factura factura);

    /// <summary>La factura emitida desde el presupuesto <paramref name="presupuestoNumero"/>, con sus líneas, o null.</summary>
    Task<Factura?> ObtenerPorPresupuestoAsync(long presupuestoNumero, CancellationToken cancelacion = default);

    /// <summary>
    /// Solo la identificación (tipo, punto de venta y número) de la factura emitida desde el presupuesto
    /// <paramref name="presupuestoNumero"/>, o null si no tiene. Para mostrarla en el presupuesto (FR-20) sin leer las
    /// líneas ni el DNI del receptor.
    /// </summary>
    Task<NumeroComprobante?> ObtenerComprobantePorPresupuestoAsync(
        long presupuestoNumero, CancellationToken cancelacion = default);

    /// <summary>Verdadero si el presupuesto <paramref name="presupuestoNumero"/> ya tiene una factura (FR-03).</summary>
    Task<bool> ExisteParaPresupuestoAsync(long presupuestoNumero, CancellationToken cancelacion = default);

    /// <summary>
    /// Una página de la búsqueda de facturas (FR-16 a FR-23 y FR-25 de FEAT-009): solo los filtros cargados, combinados
    /// con "Y"; Apellido, Nombre y DNI son los del cliente del presupuesto de origen, también en una factura emitida sin
    /// identificar al receptor. De la más reciente a la más antigua por fecha y número, con el Id como desempate (D-20),
    /// y el total de coincidencias con el mismo filtro.
    /// </summary>
    /// <param name="pagina">Desde 1.</param>
    /// <param name="tamanoPagina">Positivo.</param>
    Task<PaginaFacturas> BuscarAsync(
        FiltroFacturas filtro, int pagina, int tamanoPagina, CancellationToken cancelacion = default);
}
