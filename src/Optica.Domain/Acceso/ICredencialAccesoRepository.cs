namespace Optica.Domain.Acceso;

/// <summary>
/// Persistencia de la única credencial de acceso. <see cref="Agregar"/> solo registra el cambio: lo confirma
/// la unidad de trabajo, junto con los eventos.
/// </summary>
public interface ICredencialAccesoRepository
{
    Task<bool> ExisteAsync(CancellationToken cancelacion = default);

    Task<CredencialAcceso?> ObtenerAsync(CancellationToken cancelacion = default);

    void Agregar(CredencialAcceso credencial);
}
