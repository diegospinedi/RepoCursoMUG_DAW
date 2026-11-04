namespace Optica.Domain.Configuracion;

/// <summary>
/// Persistencia de la única fila de configuración de negocio. Cada lectura va a la base (FR-06), y
/// <see cref="Reemplazar"/> solo registra el cambio: lo confirma la unidad de trabajo.
/// </summary>
public interface IConfiguracionNegocioRepository
{
    /// <summary>La configuración vigente, leída en cada llamada.</summary>
    /// <exception cref="InvalidOperationException">No hay fila de configuración: estado corrupto (D-04).</exception>
    Task<ConfiguracionNegocio> ObtenerAsync(CancellationToken cancelacion = default);

    /// <summary>
    /// Registra el reemplazo de los cuatro valores por los de <paramref name="nueva"/>; la última grabación gana
    /// (D-03). Se guarda al confirmar la unidad de trabajo.
    /// </summary>
    /// <exception cref="InvalidOperationException">No hay fila de configuración: estado corrupto (D-04).</exception>
    void Reemplazar(ConfiguracionNegocio nueva);
}
