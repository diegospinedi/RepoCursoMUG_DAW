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

    /// <summary>
    /// Registra el alta del presupuesto con sus líneas; se guarda al confirmar la unidad de trabajo. Solo un Borrador: un
    /// Final no se graba directo, se graba en Borrador y pasa a Final con <see cref="CambiarEstado"/> (AGENTS.md).
    /// </summary>
    /// <exception cref="ArgumentException">El presupuesto no está en Borrador. El mensaje no lleva valores.</exception>
    void Agregar(Presupuesto presupuesto);

    /// <summary>El presupuesto con ese número, con su cliente y sus líneas, o null si no existe.</summary>
    Task<Presupuesto?> ObtenerAsync(long numero, CancellationToken cancelacion = default);

    /// <summary>
    /// Registra la modificación de un Borrador: el cliente y las líneas de <paramref name="modificado"/> reemplazan a los
    /// de <paramref name="original"/>, y la versión pasa a <paramref name="versionOriginal"/> + 1. Se guarda al confirmar
    /// la unidad de trabajo. Si la versión guardada ya no es <paramref name="versionOriginal"/>, la confirmación falla sin
    /// escribir nada (D-01): con motivo de concurrencia si el presupuesto sigue en Borrador, y de registro cerrado si otra
    /// sesión lo pasó a Final, porque los comandos de las líneas se filtran solo por clave y el trigger de cierre los
    /// rechaza antes (D-14).
    /// </summary>
    void Actualizar(Presupuesto original, Presupuesto modificado, long versionOriginal);

    /// <summary>
    /// Registra el paso a Final de <paramref name="presupuesto"/> (ya pasado a Final en el dominio): solo cambian el estado
    /// y la versión, que pasa a <paramref name="versionOriginal"/> + 1; las líneas no se tocan. Se guarda al confirmar la
    /// unidad de trabajo, con el mismo control de versión que <see cref="Actualizar"/>.
    /// </summary>
    void CambiarEstado(Presupuesto presupuesto, long versionOriginal);

    /// <summary>
    /// Una página de la búsqueda (FR-01 a FR-08 de FEAT-006): los presupuestos que cumplen todos los filtros cargados de
    /// <paramref name="filtro"/>, por número de mayor a menor, salteando <c>(pagina - 1) * tamanoPagina</c> y tomando
    /// <paramref name="tamanoPagina"/>, con el total de cada uno (suma de los Precio Final de sus líneas, D-08) y cuántos
    /// coinciden en total. Una página posterior a la última viene vacía, con el total.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="filtro"/> es null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="pagina"/> es menor que 1, <paramref name="tamanoPagina"/> no es positivo, o el desplazamiento no
    /// entra en un <c>int</c>. No se consulta la base.
    /// </exception>
    Task<PaginaPresupuestos> BuscarAsync(
        FiltroPresupuestos filtro, int pagina, int tamanoPagina, CancellationToken cancelacion = default);
}
