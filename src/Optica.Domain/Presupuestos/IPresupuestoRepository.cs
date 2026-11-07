namespace Optica.Domain.Presupuestos;

/// <summary>
/// Persistencia de los presupuestos. <see cref="Agregar"/> solo registra el alta: la confirma la unidad de trabajo.
/// </summary>
public interface IPresupuestoRepository
{
    /// <summary>
    /// El mayor número de presupuesto grabado, o null si no hay ninguno (D-07). Se lee dentro de la transacción de
    /// escritura del alta (ADR-013).
    /// </summary>
    Task<long?> ObtenerUltimoNumeroAsync(CancellationToken cancelacion = default);

    /// <summary>Registra el alta del presupuesto con sus líneas; se guarda al confirmar la unidad de trabajo.</summary>
    void Agregar(Presupuesto presupuesto);

    /// <summary>El presupuesto con ese número, con su cliente y sus líneas, o null si no existe.</summary>
    Task<Presupuesto?> ObtenerAsync(long numero, CancellationToken cancelacion = default);
}
