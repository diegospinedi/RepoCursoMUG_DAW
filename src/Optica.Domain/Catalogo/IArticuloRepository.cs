namespace Optica.Domain.Catalogo;

/// <summary>
/// Persistencia de los artículos del catálogo. <see cref="Agregar"/> solo registra el alta: la confirma la unidad de
/// trabajo.
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
}
