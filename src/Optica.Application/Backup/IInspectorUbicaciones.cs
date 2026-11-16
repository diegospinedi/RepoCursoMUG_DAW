namespace Optica.Application.Backup;

/// <summary>
/// Lo que la grabación de la ubicación del backup necesita saber del sistema de archivos (FR-02, FR-03 de FEAT-010):
/// si una carpeta es local, en qué unidad está, si cae dentro de una carpeta de nube conocida, si existe y si admite
/// escritura. Ningún método lanza: ante una ruta que no se puede evaluar responden el valor que la rechaza (no local,
/// misma unidad, fuera de la nube, inexistente, sin escritura). Ningún método registra la ruta (D-12).
/// </summary>
public interface IInspectorUbicaciones
{
    /// <summary>
    /// La forma normalizada de la ruta, la que se graba: absoluta (<see cref="Path.GetFullPath(string)"/>, sin <c>..</c>)
    /// y sin separador final, salvo en la raíz de una unidad. Null si no se puede normalizar.
    /// </summary>
    string? Normalizar(string ruta);

    /// <summary>
    /// Verdadero si las dos carpetas son la misma o una está dentro de la otra, sobre rutas normalizadas y por segmentos
    /// (D-19). Si alguna no se puede normalizar, verdadero: el lado que rechaza.
    /// </summary>
    bool Superpuestas(string rutaA, string rutaB);

    /// <summary>
    /// Falso para una ruta UNC (<c>\\servidor\recurso</c>), con prefijo de dispositivo (<c>\\?\</c>, <c>\\.\</c>) o en
    /// una unidad de red (D-17): la copia no puede depender de la red ni de una ruta que esquive la normalización.
    /// También falso si la unidad no se encuentra o su tipo es desconocido: cerrado por defecto.
    /// </summary>
    bool EsLocal(string ruta);

    /// <summary>
    /// Verdadero si las dos rutas están en la misma unidad (letra en Windows; punto de montaje sin letras), D-07. Si no se
    /// puede determinar, verdadero: el lado seguro de FR-02.
    /// </summary>
    bool MismaUnidad(string rutaA, string rutaB);

    /// <summary>
    /// Verdadero si la ruta es, o está dentro de, la carpeta local de OneDrive, Google Drive o Dropbox de la cuenta que
    /// corre la Api (FR-02, AC-17, D-18).
    /// </summary>
    bool EnNubeConocida(string ruta);

    /// <summary>Verdadero si la carpeta existe (FR-03).</summary>
    bool Existe(string ruta);

    /// <summary>
    /// Verdadero si en la carpeta se puede escribir: crea un archivo sonda de un byte y lo borra (FR-03). No crea la
    /// carpeta.
    /// </summary>
    bool PuedeEscribir(string ruta);
}
