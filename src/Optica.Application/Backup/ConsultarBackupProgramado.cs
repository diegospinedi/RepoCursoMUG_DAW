using Optica.Domain.Backup;

namespace Optica.Application.Backup;

/// <summary>
/// Si toca el backup programado de hoy (FR-04 de FEAT-010, D-20, D-24): en la hora local del <see cref="TimeProvider"/> ya
/// son las <see cref="CopiaBackup.HoraProgramada"/> o más y el historial no tiene la copia programada de hoy. Lo consulta
/// el programador de la Api en cada despertar; compara con el historial y no con un contador, así que tolera la suspensión
/// de la PC, los saltos del reloj y los pedidos que no terminaron.
/// </summary>
/// <remarks>
/// La Api no conoce el dominio (la regla de dependencias de la arquitectura): la hora programada y el historial se
/// consultan acá. "Hoy" es el de <see cref="ReglaBackupDiario.Hoy"/>.
/// </remarks>
public sealed class ConsultarBackupProgramado(IHistorialBackupRepository historial, TimeProvider reloj)
{
    /// <summary>Verdadero si ya pasó la hora programada de hoy y su copia programada no está en el historial.</summary>
    /// <exception cref="InvalidOperationException">El historial tiene una fila corrupta (texto fijo, sin rutas).</exception>
    public async Task<bool> TocaAsync(CancellationToken cancelacion = default)
    {
        if (TimeOnly.FromTimeSpan(reloj.GetLocalNow().TimeOfDay) < CopiaBackup.HoraProgramada)
        {
            return false;
        }

        return !await historial.HuboCopiaProgramadaAsync(ReglaBackupDiario.Hoy(reloj), cancelacion);
    }
}
