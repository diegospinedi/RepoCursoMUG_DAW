namespace Optica.Domain.Backup;

/// <summary>
/// El historial de backups verificados (D-08 de FEAT-010, ADR-029): una fila por cada copia que pasó la verificación,
/// con la fecha de su nombre y el momento en que se hizo. Solo se agrega. Sirve para que el aviso al ingresar muestre la
/// fecha del último backup aunque la ubicación no esté accesible (FR-09, AC-18); qué copias hay de verdad lo dicen los
/// archivos de la carpeta.
/// </summary>
public interface IHistorialBackupRepository
{
    /// <summary>
    /// Registra en el historial el backup verificado de <paramref name="fecha"/>, hecho en <paramref name="momentoUtc"/>.
    /// Solo registra el alta: se guarda cuando quien lo usa confirma la unidad de trabajo (el backup, después del
    /// renombre verificado), que define la transacción.
    /// </summary>
    void Agregar(DateOnly fecha, DateTimeOffset momentoUtc);

    /// <summary>
    /// La mayor fecha del historial que no pasa de <paramref name="hoy"/> (D-21: una copia con fecha futura no es el
    /// último backup); null si no hay ninguna.
    /// </summary>
    /// <exception cref="InvalidOperationException">La fecha guardada no existe: estado corrupto, con texto fijo.</exception>
    Task<DateOnly?> UltimaFechaHastaAsync(DateOnly hoy, CancellationToken cancelacion = default);

    /// <summary>
    /// Verdadero si hay una fila de <paramref name="fecha"/> cuyo momento, en hora local, está en [las
    /// <see cref="CopiaBackup.HoraProgramada"/> de ese día, las 00:00 del día siguiente): el backup programado de ese día
    /// ya se hizo (D-11, D-20). La zona local la da la implementación, de forma inyectable.
    /// </summary>
    Task<bool> HuboCopiaProgramadaAsync(DateOnly fecha, CancellationToken cancelacion = default);
}
