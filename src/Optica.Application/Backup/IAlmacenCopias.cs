namespace Optica.Application.Backup;

/// <summary>
/// Los archivos de backup de la carpeta configurada (FR-06, FR-10 a FR-12 de FEAT-010, ADR-029): son la verdad sobre qué
/// copias hay. Solo toca archivos cuyo nombre es exactamente el de una copia (<c>optica-AAAA-MM-DD.db</c>) o el de su
/// temporal y sus auxiliares (<see cref="Domain.Backup.CopiaBackup"/>), directamente en <c>carpeta</c>: nunca
/// subcarpetas ni otros archivos (R-03). Los nombres los arma el dominio a partir de la fecha, nunca con texto del
/// usuario.
/// </summary>
/// <remarks>
/// Ningún método crea la carpeta: si no existe (pendrive desconectado) lanzan <see cref="DirectoryNotFoundException"/>.
/// Ningún método registra la ruta (D-12); las excepciones del sistema de archivos pueden traerla en su texto.
/// </remarks>
public interface IAlmacenCopias
{
    /// <summary>La ruta del temporal de la copia de <paramref name="fecha"/>: <c>optica-AAAA-MM-DD.db.parcial</c>.</summary>
    string RutaTemporal(string carpeta, DateOnly fecha);

    /// <summary>Las fechas de las copias terminadas de la carpeta (sin temporales), en cualquier orden.</summary>
    /// <exception cref="IOException">La carpeta no existe o no se puede leer.</exception>
    /// <exception cref="UnauthorizedAccessException">Sin permiso para leer la carpeta.</exception>
    IReadOnlyList<DateOnly> ListarCopias(string carpeta);

    /// <summary>
    /// Las fechas cuyo temporal (<c>optica-AAAA-MM-DD.db.parcial</c>, sin los auxiliares) existe sin la copia terminada de
    /// esa fecha: candidatos a recuperar antes de borrar los huérfanos. En FAT y exFAT el renombre con reemplazo no es
    /// atómico, y un corte entre el borrado de la copia y el renombre deja solo el temporal ya verificado.
    /// </summary>
    /// <exception cref="IOException">La carpeta no existe o no se puede leer.</exception>
    /// <exception cref="UnauthorizedAccessException">Sin permiso para leer la carpeta.</exception>
    IReadOnlyList<DateOnly> ListarTemporalesSinCopia(string carpeta);

    /// <summary>
    /// Renombra el temporal de <paramref name="fecha"/> a la copia de esa fecha, reemplazando la que hubiera: solo se
    /// llama con el temporal ya verificado (D-20).
    /// </summary>
    /// <exception cref="IOException">El temporal no existe, la carpeta se desconectó o el archivo está en uso.</exception>
    /// <exception cref="UnauthorizedAccessException">Sin permiso para renombrar o reemplazar.</exception>
    void RenombrarTemporal(string carpeta, DateOnly fecha);

    /// <summary>
    /// Borra las copias de las <paramref name="fechas"/> que existen como archivo con el nombre exacto; devuelve cuántas
    /// borró. Una fecha sin copia no borra nada.
    /// </summary>
    /// <exception cref="IOException">La carpeta no existe o un archivo está en uso.</exception>
    /// <exception cref="UnauthorizedAccessException">Sin permiso para borrar.</exception>
    int BorrarCopias(string carpeta, IReadOnlyCollection<DateOnly> fechas);

    /// <summary>
    /// Borra todos los temporales de la carpeta (<c>.parcial</c>, <c>.parcial-journal</c>, <c>.parcial-wal</c>,
    /// <c>.parcial-shm</c>, de cualquier fecha): los huérfanos de un backup interrumpido y el de un backup que falló
    /// (D-23).
    /// </summary>
    /// <exception cref="IOException">La carpeta no existe o un archivo está en uso.</exception>
    /// <exception cref="UnauthorizedAccessException">Sin permiso para borrar.</exception>
    void BorrarTemporales(string carpeta);
}
