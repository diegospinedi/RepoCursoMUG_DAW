namespace Optica.Application.Backup;

/// <summary>
/// Lo que el restaurador leyó de una copia, o del temporal ya restaurado, abiertos en solo lectura (D-23, D-28): si
/// existe, si es una copia completa (modo de diario de reversión y comprobación de integridad correcta), las migraciones
/// de su historial (null si no tiene la tabla de migraciones), el número interno de su última factura (0 si no tiene
/// facturas o es de una versión anterior a la facturación) y si tiene todos los triggers de protección (de solo agregar
/// y de registros cerrados) que crean las migraciones de su historial, cada uno con el texto con que lo crea su migración.
/// </summary>
/// <remarks>Ningún dato de clientes: solo identificadores de migraciones, un número y dos indicadores.</remarks>
public sealed record InspeccionCopia(
    bool Existe, bool Integra, IReadOnlyList<string>? Migraciones, long UltimaFactura, bool TriggersCompletos = true);

/// <summary>
/// El puerto de la restauración de la base desde una copia de backup (FR-13 de FEAT-010, D-27, D-29, ADR-029, ADR-030):
/// verificar la copia, restaurarla a un temporal verificado, apartar la base actual y poner la restaurada, y volver atrás.
/// La implementación usa la API de backup de SQLite, nunca una copia de archivo común (AGENTS.md), y abre la copia siempre
/// en solo lectura. Solo vale con la Api detenida: quien la usa tiene tomado el candado de instancia.
/// </summary>
/// <remarks>
/// <para>
/// No migra: la Api migra al arrancar (D-28). No escribe datos en la base restaurada ni deja un evento en ella: solo deja
/// su diario en modo WAL, como toda base de la Api.
/// </para>
/// <para>
/// Ningún método registra rutas ni las pone en el texto de sus excepciones propias (D-12); las excepciones del sistema de
/// archivos o de la base pueden traerlas en su texto, así que quien las atrapa informa solo su tipo.
/// </para>
/// </remarks>
public interface IRestauradorBase
{
    /// <summary>Las migraciones de esta versión de la aplicación, en el orden en que se aplican.</summary>
    IReadOnlyList<string> MigracionesConocidas();

    /// <summary>
    /// Abre la copia en solo lectura, sin crearla ni dejar archivos auxiliares, y la inspecciona. Una copia inexistente,
    /// que no es una base o que está dañada no lanza: se informa en el resultado.
    /// </summary>
    Task<InspeccionCopia> InspeccionarCopiaAsync(string rutaCopia, CancellationToken cancelacion = default);

    /// <summary>
    /// Cuántas facturas con CAE de la base actual son posteriores a la última de la copia (número interno mayor que
    /// <paramref name="ultimaFacturaDeLaCopia"/>): las que, después de restaurar, quedan solo en la base anterior (R-10).
    /// Null si la base actual no se pudo leer (por ejemplo, porque está dañada); no lanza por eso.
    /// </summary>
    Task<int?> ContarFacturasPosterioresAsync(long ultimaFacturaDeLaCopia, CancellationToken cancelacion = default);

    /// <summary>
    /// Restaura la copia a un temporal en la carpeta de datos (<c>optica.db.restaurando</c>) con la API de backup, lo
    /// inspecciona con las mismas comprobaciones que <see cref="InspeccionarCopiaAsync"/> y, si es una copia completa, lo
    /// deja en modo WAL y vuelve a comprobar su integridad. Devuelve la inspección del temporal: quien llama le aplica la
    /// misma regla que a la copia (D-28) antes de usarlo. No toca la base actual.
    /// </summary>
    /// <exception cref="IOException">La carpeta de datos está llena o no se puede escribir (o una subclase).</exception>
    /// <exception cref="Exception">La copia o el temporal no se pudieron abrir o escribir (tipo propio del motor).</exception>
    Task<InspeccionCopia> RestaurarATemporalAsync(string rutaCopia, CancellationToken cancelacion = default);

    /// <summary>
    /// Aparta la base actual con sus archivos auxiliares a una carpeta nueva <c>anterior/AAAAMMDD-HHMMSS</c> dentro de la
    /// carpeta de datos y pone el temporal verificado como base. Devuelve esa carpeta relativa a la carpeta de datos (sin
    /// la ruta de la carpeta de datos, que puede llevar el nombre de una persona). Si un paso posterior al apartado falla,
    /// devuelve la base actual a su lugar antes de lanzar: la carpeta de datos nunca queda sin la base.
    /// </summary>
    /// <exception cref="VueltaAtrasFallidaException">Falló un paso y tampoco se pudo devolver la base actual.</exception>
    /// <exception cref="IOException">Falló un paso; la base actual volvió a su lugar (o una subclase).</exception>
    /// <exception cref="UnauthorizedAccessException">Sin permiso; la base actual volvió a su lugar.</exception>
    string ApartarYPonerRestaurada();

    /// <summary>
    /// Vuelta atrás antes del intercambio: borra el temporal (y sus auxiliares) de una restauración que no terminó. Sin
    /// temporal, no hace nada.
    /// </summary>
    /// <exception cref="IOException">El temporal no se pudo borrar.</exception>
    /// <exception cref="UnauthorizedAccessException">Sin permiso para borrar el temporal.</exception>
    void DescartarTemporal();
}

/// <summary>
/// Falló el intercambio y tampoco se pudo devolver la base actual a su lugar: queda en <see cref="CarpetaAnterior"/>, y la
/// base restaurada, como temporal en la carpeta de datos. El mensaje no lleva rutas; la causa sí puede traerlas en su
/// texto, así que se informa solo su tipo.
/// </summary>
public sealed class VueltaAtrasFallidaException : Exception
{
    public VueltaAtrasFallidaException(string carpetaAnterior, Exception causa)
        : base("No se pudo poner la base restaurada ni devolver la base actual a la carpeta de datos.", causa)
    {
        CarpetaAnterior = carpetaAnterior;
    }

    /// <summary>La carpeta con la base actual, relativa a la carpeta de datos (<c>anterior/AAAAMMDD-HHMMSS</c>).</summary>
    public string CarpetaAnterior { get; }
}
