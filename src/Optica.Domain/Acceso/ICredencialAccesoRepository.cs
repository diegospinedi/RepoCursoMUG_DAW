namespace Optica.Domain.Acceso;

/// <summary>
/// Persistencia de la única credencial de acceso. <see cref="Agregar"/> y <see cref="Reemplazar"/> solo registran el
/// cambio: lo confirma la unidad de trabajo, junto con los eventos.
/// </summary>
public interface ICredencialAccesoRepository
{
    Task<bool> ExisteAsync(CancellationToken cancelacion = default);

    Task<CredencialAcceso?> ObtenerAsync(CancellationToken cancelacion = default);

    void Agregar(CredencialAcceso credencial);

    /// <summary>
    /// Registra el reemplazo de la credencial leída (<paramref name="anterior"/>) por <paramref name="nueva"/>, con
    /// concurrencia optimista sobre el hash: si al confirmar la credencial guardada ya no tiene el hash de
    /// <paramref name="anterior"/> (otro cambio o un restablecimiento en el medio), la unidad de trabajo informa un
    /// conflicto y no guarda nada.
    /// </summary>
    /// <param name="anterior">
    /// La credencial tal como se leyó, sin modificar: su hash es el que se compara al confirmar.
    /// </param>
    /// <param name="nueva">
    /// Otra instancia, con el hash nuevo. Si fuera la misma que <paramref name="anterior"/> ya modificada (por ejemplo
    /// con <see cref="CredencialAcceso.ReemplazarHash"/>), la comparación se haría contra el hash nuevo y siempre daría
    /// conflicto.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="anterior"/> y <paramref name="nueva"/> son la misma instancia.</exception>
    void Reemplazar(CredencialAcceso anterior, CredencialAcceso nueva);
}
