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
    /// </summary>
    Task<PaginaArticulos> BuscarAsync(
        string? textoPlegado, int pagina, int tamano, CancellationToken cancelacion = default);

    /// <summary>Verdadero si un artículo distinto del de <paramref name="codigo"/> tiene esa clave normalizada (ADR-009).</summary>
    Task<bool> ExisteClaveDeOtroAsync(string clave, long codigo, CancellationToken cancelacion = default);

    /// <summary>
    /// Registra la modificación del artículo si su versión guardada sigue siendo <paramref name="versionLeida"/>; se
    /// guarda al confirmar la unidad de trabajo.
    /// </summary>
    void Actualizar(Articulo articulo, long versionLeida);

    /// <summary>Registra el cambio de estado del artículo (D-09); se guarda al confirmar la unidad de trabajo.</summary>
    void CambiarEstado(long codigo, bool activo);
}
