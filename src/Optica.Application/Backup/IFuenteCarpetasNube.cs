namespace Optica.Application.Backup;

/// <summary>
/// Las carpetas locales de OneDrive, Google Drive y Dropbox de la cuenta que corre la Api (D-18): dentro de ellas se
/// acepta una ubicación de backup en la misma unidad que la base (FR-02, AC-17). Inyectable, para probar con carpetas
/// falsas.
/// </summary>
public interface IFuenteCarpetasNube
{
    /// <summary>
    /// Las carpetas conocidas en este momento, absolutas; vacía si no hay ninguna. Nunca lanza: una fuente que no se puede
    /// leer (variable ausente, archivo de configuración roto) se omite.
    /// </summary>
    IReadOnlyList<string> Carpetas();
}
