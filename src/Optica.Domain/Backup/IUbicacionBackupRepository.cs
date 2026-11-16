namespace Optica.Domain.Backup;

/// <summary>
/// Persistencia de la única fila de la ubicación del backup (FR-01 de FEAT-010). Cada lectura va a la base, y
/// <see cref="Reemplazar"/> solo registra el cambio: lo confirma la unidad de trabajo.
/// </summary>
public interface IUbicacionBackupRepository
{
    /// <summary>La ubicación vigente, leída en cada llamada; null si nunca se eligió.</summary>
    /// <exception cref="InvalidOperationException">
    /// La fila guardada no cumple las reglas de lectura: estado corrupto. El mensaje es fijo, sin la ruta.
    /// </exception>
    Task<UbicacionBackup?> ObtenerAsync(CancellationToken cancelacion = default);

    /// <summary>
    /// Registra la ruta de <paramref name="ubicacion"/>: inserta la fila si no existe o la reemplaza; la última grabación
    /// gana. Se guarda al confirmar la unidad de trabajo.
    /// </summary>
    void Reemplazar(UbicacionBackup ubicacion);
}
