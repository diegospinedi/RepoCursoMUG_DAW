namespace Optica.Domain.Catalogo;

/// <summary>
/// Persistencia de los artículos del catálogo. <see cref="Agregar"/>, <see cref="Actualizar"/> y
/// <see cref="CambiarEstado"/> solo registran el cambio: lo confirma la unidad de trabajo.
/// </summary>
public interface IArticuloRepository
{
    /// <summary>Verdadero si algún artículo tiene esa clave normalizada (ADR-009).</summary>
    Task<bool> ExisteClaveAsync(string clave, CancellationToken cancelacion = default);

    /// <summary>Registra el alta; se guarda al confirmar la unidad de trabajo.</summary>
    void Agregar(Articulo articulo);

    /// <summary>El artículo con ese código autonumérico, o null si no existe.</summary>
    Task<Articulo?> ObtenerAsync(long codigo, CancellationToken cancelacion = default);

    /// <summary>
    /// El artículo con esa clave normalizada, o null si no existe; sirve para leer el código asignado después de
    /// confirmar.
    /// </summary>
    Task<Articulo?> ObtenerPorClaveAsync(string clave, CancellationToken cancelacion = default);

    /// <summary>
    /// Una página de artículos ordenados por descripción (D-01, D-15). Con <paramref name="textoPlegado"/> null
    /// devuelve todos; si no, los que lo contienen en la descripción, en el código en el proveedor (los dos plegados con
    /// <see cref="PlegadoBusqueda.Plegar"/>) o en el código (FR-04, ADR-011). <paramref name="pagina"/> empieza en 1.
    /// Con <paramref name="soloActivos"/> verdadero, solo los artículos activos, también en el total (FR-09 y D-15 de
    /// FEAT-003a); falso lista activos e inactivos, como el listado del catálogo.
    /// </summary>
    Task<PaginaArticulos> BuscarAsync(
        string? textoPlegado, int pagina, int tamano, bool soloActivos = false,
        CancellationToken cancelacion = default);

    /// <summary>Verdadero si un artículo distinto del de <paramref name="codigo"/> tiene esa clave normalizada (ADR-009).</summary>
    Task<bool> ExisteClaveDeOtroAsync(string clave, long codigo, CancellationToken cancelacion = default);

    /// <summary>
    /// Registra la modificación del artículo si su versión guardada sigue siendo <paramref name="versionLeida"/>; se
    /// guarda al confirmar la unidad de trabajo.
    /// </summary>
    void Actualizar(Articulo articulo, long versionLeida);

    /// <summary>Registra el cambio de estado del artículo (D-09); se guarda al confirmar la unidad de trabajo.</summary>
    void CambiarEstado(long codigo, bool activo);

    /// <summary>
    /// Todos los artículos del catálogo, activos e inactivos, para recalcular su precio de venta (FR-01 y AC-03 de
    /// FEAT-002c). Cada uno con el estado y la versión leídos, que <see cref="RegistrarPrecioRecalculado"/> usa como tokens.
    /// </summary>
    Task<IReadOnlyList<Articulo>> ListarParaRecalculoAsync(CancellationToken cancelacion = default);

    /// <summary>
    /// Registra el precio de venta recalculado de <paramref name="articulo"/> (la copia de
    /// <see cref="Articulo.ConPrecioRecalculado"/>): solo cambian el precio de venta y la versión, que se incrementa. El
    /// estado y la versión del artículo son los leídos: si alguno cambió antes de confirmar, la unidad de trabajo lo
    /// informa como conflicto de concurrencia (D-04 de FEAT-002c). Se guarda al confirmar la unidad de trabajo.
    /// </summary>
    /// <exception cref="ArgumentException">El artículo no tiene código.</exception>
    void RegistrarPrecioRecalculado(Articulo articulo);
}
