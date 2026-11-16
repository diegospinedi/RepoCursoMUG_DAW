namespace Optica.Domain.Configuracion;

/// <summary>
/// Persistencia de la única fila de los datos del emisor (ADR-028). Cada lectura va a la base, y
/// <see cref="Reemplazar"/> solo registra el cambio: lo confirma la unidad de trabajo.
/// </summary>
public interface IDatosEmisorRepository
{
    /// <summary>Los datos vigentes, leídos en cada llamada; null si nunca se cargaron.</summary>
    /// <exception cref="InvalidOperationException">
    /// La fila guardada no cumple las reglas de lectura: estado corrupto. El mensaje es fijo, sin valores.
    /// </exception>
    Task<DatosEmisor?> ObtenerAsync(CancellationToken cancelacion = default);

    /// <summary>
    /// Registra los cinco datos de <paramref name="datos"/>: inserta la fila si no existe o la reemplaza; la última
    /// grabación gana. Se guarda al confirmar la unidad de trabajo.
    /// </summary>
    void Reemplazar(DatosEmisor datos);
}
