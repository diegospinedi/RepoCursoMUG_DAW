namespace Optica.Domain.Facturacion;

/// <summary>
/// El registro de descartes de emisiones pendientes (FR-10, ADR-026): solo se agrega. <see cref="Agregar"/> solo registra
/// el alta: la confirma la unidad de trabajo. La base rechaza modificar o borrar un descarte.
/// </summary>
public interface IDescarteEmisionRepository
{
    void Agregar(DescarteEmision descarte);

    /// <summary>Los descartes del presupuesto, en el orden en que se registraron.</summary>
    /// <exception cref="InvalidOperationException">Una fila guardada no es válida. El mensaje es fijo y no lleva datos.</exception>
    Task<IReadOnlyList<DescarteEmision>> ListarDescartesAsync(
        long presupuestoNumero, CancellationToken cancelacion = default);
}
