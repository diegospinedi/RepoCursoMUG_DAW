using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Optica.Domain.Backup;

namespace Optica.Infrastructure.Persistencia;

/// <summary>
/// El historial de backups verificados sobre la tabla <c>BackupRealizado</c> (FEAT-010, D-08, ADR-029). Solo se agrega:
/// la base rechaza modificar o borrar una fila (<c>TR_BackupRealizado_Inmutable</c>, <c>TR_BackupRealizado_NoSeBorra</c>).
/// <see cref="Agregar"/> solo registra el alta en el contexto compartido, que confirma <see cref="UnidadDeTrabajoEf"/>.
/// La zona horaria local sale de <see cref="TimeProvider.LocalTimeZone"/>, inyectado: la regla de las 20:00 no lee la
/// zona de la máquina por su cuenta y una prueba puede fijarla. Sin rutas; no registra nada en el log.
/// </summary>
public sealed class HistorialBackupRepository(OpticaDbContext contexto, TimeProvider reloj) : IHistorialBackupRepository
{
    private const string MensajeFilaInvalida = "Una fecha del historial de backups no es válida.";

    /// <summary>
    /// Solo registra el alta en el contexto compartido, como <see cref="DescarteEmisionRepository"/>: la graba
    /// <see cref="UnidadDeTrabajoEf"/> cuando Application confirma (el backup, después del renombre verificado).
    /// </summary>
    public void Agregar(DateOnly fecha, DateTimeOffset momentoUtc)
    {
        contexto.BackupsRealizados.Add(new BackupRealizadoFila
        {
            Fecha = Texto(fecha),
            MomentoUtc = momentoUtc.UtcDateTime,
        });
    }

    /// <summary>
    /// La mayor fecha que no pasa de <paramref name="hoy"/>, comparada como texto <c>yyyy-MM-dd</c> en la base (el
    /// orden del texto es el de las fechas) con el índice sobre <c>Fecha</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">La fecha guardada no existe (pasa el GLOB): texto fijo, sin valores.</exception>
    public async Task<DateOnly?> UltimaFechaHastaAsync(DateOnly hoy, CancellationToken cancelacion = default)
    {
        var limite = Texto(hoy);
        var ultima = await contexto.BackupsRealizados.AsNoTracking()
            .Where(b => string.Compare(b.Fecha, limite) <= 0)
            .MaxAsync(b => (string?)b.Fecha, cancelacion);
        return ultima is null ? null : Fecha(ultima);
    }

    /// <summary>
    /// Lee las filas de <paramref name="fecha"/> (pocas: una por backup de ese día) y pasa cada momento UTC a la hora
    /// local de la zona inyectada; basta una en [<paramref name="fecha"/> a las <see cref="CopiaBackup.HoraProgramada"/>,
    /// el día siguiente a las 00:00). Un momento de otro día local (reloj o zona corridos) no cuenta.
    /// </summary>
    public async Task<bool> HuboCopiaProgramadaAsync(DateOnly fecha, CancellationToken cancelacion = default)
    {
        var texto = Texto(fecha);
        var momentos = await contexto.BackupsRealizados.AsNoTracking()
            .Where(b => b.Fecha == texto)
            .Select(b => b.MomentoUtc)
            .ToListAsync(cancelacion);

        var zona = reloj.LocalTimeZone;
        var desde = fecha.ToDateTime(CopiaBackup.HoraProgramada, DateTimeKind.Unspecified);
        var hasta = fecha.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return momentos
            .Select(momento => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(momento, DateTimeKind.Utc), zona))
            .Any(local => local >= desde && local < hasta);
    }

    private static string Texto(DateOnly fecha) =>
        fecha.ToString(CopiaBackup.FormatoFecha, CultureInfo.InvariantCulture);

    private static DateOnly Fecha(string texto) =>
        DateOnly.TryParseExact(texto, CopiaBackup.FormatoFecha, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha)
            ? fecha
            : throw new InvalidOperationException(MensajeFilaInvalida);
}
