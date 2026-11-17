namespace Optica.Application.Backup;

/// <summary>
/// Copia la base en uso a un temporal y lo verifica (FR-05, FR-07 de FEAT-010, ADR-029). La implementación usa la API de
/// backup de SQLite, nunca una copia de archivo común (AGENTS.md), y deja la copia sin archivos auxiliares.
/// </summary>
/// <remarks>
/// Ningún método registra la ruta ni la pone en el texto de sus excepciones propias (D-12); las excepciones del sistema
/// de archivos o de la base pueden traerla en su texto, así que quien las atrapa registra solo su tipo.
/// </remarks>
public interface ICopiadorBackup
{
    /// <summary>
    /// Escribe en <paramref name="rutaTemporal"/> una copia consistente de la base con todo lo confirmado hasta el inicio
    /// de la copia (también lo que todavía está en el WAL), sin impedir que se siga grabando (FR-05). La copia queda en
    /// modo de diario de reversión, sin <c>-wal</c> ni <c>-shm</c>, y con el archivo cerrado y liberado al volver.
    /// </summary>
    /// <exception cref="IOException">La carpeta no existe, está llena o se desconectó (o una subclase).</exception>
    /// <exception cref="Exception">La base o la copia no se pudieron abrir o escribir (tipo propio del motor).</exception>
    Task CopiarAsync(string rutaTemporal, CancellationToken cancelacion = default);

    /// <summary>
    /// Verdadero si la copia de <paramref name="rutaTemporal"/> se abre, está en modo de diario de reversión y pasa la
    /// comprobación de integridad completa (FR-07). Falso si no existe, no es una base o está dañada: no lanza por eso.
    /// No modifica el archivo ni deja auxiliares.
    /// </summary>
    Task<bool> VerificarAsync(string rutaTemporal, CancellationToken cancelacion = default);
}
