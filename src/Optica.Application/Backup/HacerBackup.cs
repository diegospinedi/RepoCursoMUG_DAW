using Microsoft.Extensions.Logging;
using Optica.Application.Comun;
using Optica.Domain.Backup;

namespace Optica.Application.Backup;

/// <summary>Por qué se pide el backup (D-20 de FEAT-010).</summary>
public enum MotivoBackup
{
    /// <summary>Al arrancar la Api: copia solo si falta la de ayer (FR-08, <see cref="ReglaBackupDiario"/>).</summary>
    AlIniciar,

    /// <summary>El de las 20:00 (FR-04): copia siempre y reemplaza la copia de hoy después de verificar la nueva.</summary>
    Programado,
}

/// <summary>Cómo terminó un pedido de backup.</summary>
public enum EstadoBackup
{
    /// <summary>La copia del día quedó verificada, renombrada y anotada en el historial.</summary>
    Hecho,

    /// <summary>Al arrancar, con la copia de ayer o la de hoy en el historial: no hizo nada.</summary>
    NoHaciaFalta,

    /// <summary>No hay ubicación elegida: no hizo nada.</summary>
    SinUbicacion,

    /// <summary>Otro backup tenía el candado: no hizo nada (D-22).</summary>
    EnCurso,

    /// <summary>
    /// Falla del día: no se borró ninguna copia, no se anotó en el historial y no se aplicó la retención (FR-07, FR-12).
    /// No hay copia nueva, salvo con <see cref="FallaBackup.Historial"/>: ahí la copia verificada ya quedó en la carpeta y
    /// lo que falló es anotarla.
    /// </summary>
    Fallido,
}

/// <summary>En qué paso falló el backup del día; es lo que va al log, nunca la ruta ni el texto de la excepción.</summary>
public enum FallaBackup
{
    /// <summary>La carpeta no existe, está desconectada o no se puede escribir (al borrar los temporales huérfanos).</summary>
    CarpetaInaccesible,

    /// <summary>La copia con la API de backup no terminó (carpeta llena o desconectada, base que no se abre).</summary>
    Copia,

    /// <summary>La copia no pasó la verificación de integridad o no se pudo leer (FR-07, AC-07).</summary>
    Integridad,

    /// <summary>La copia verificada no se pudo renombrar a su nombre definitivo.</summary>
    Renombre,

    /// <summary>
    /// La copia verificada quedó en la carpeta pero el historial no se pudo grabar: el aviso no la va a ver hasta el
    /// próximo backup, y la retención no se aplicó.
    /// </summary>
    Historial,
}

/// <summary>El resultado de un pedido de backup: el estado, la fecha de la copia si se intentó, y el paso que falló.</summary>
public sealed record ResultadoBackup(EstadoBackup Estado, DateOnly? Fecha = null, FallaBackup? Falla = null);

/// <summary>
/// La regla de "falta el backup de ayer" (FR-08, FR-09, D-11, D-20): una sola, para el intento al arrancar
/// (<see cref="HacerBackup"/>) y para el aviso al ingresar (el estado del backup). Falta si en el historial no hay ni la
/// copia de ayer ni la de hoy: una copia hecha hoy al arrancar cubre la de ayer (D-11).
/// </summary>
public static class ReglaBackupDiario
{
    /// <summary>
    /// Verdadero si no hay backup (<paramref name="ultimoBackup"/> null) o el último es anterior a ayer.
    /// <paramref name="ultimoBackup"/> es la mayor fecha del historial que no pasa de <paramref name="hoy"/> (D-21).
    /// </summary>
    public static bool FaltaLaDeAyer(DateOnly? ultimoBackup, DateOnly hoy) =>
        ultimoBackup is not { } ultimo || ultimo < hoy.AddDays(-1);

    /// <summary><see cref="FaltaLaDeAyer(DateOnly?, DateOnly)"/> con el último backup leído del historial.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="historial"/> es null.</exception>
    public static async Task<bool> FaltaLaDeAyerAsync(
        IHistorialBackupRepository historial, DateOnly hoy, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(historial);
        return FaltaLaDeAyer(await historial.UltimaFechaHastaAsync(hoy, cancelacion), hoy);
    }

    /// <summary>La fecha de hoy en la zona local del reloj (FR-06): la de la PC del local, no la UTC.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="reloj"/> es null.</exception>
    public static DateOnly Hoy(TimeProvider reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);
        return DateOnly.FromDateTime(reloj.GetLocalNow().DateTime);
    }
}

/// <summary>
/// Hace el backup del día en la ubicación configurada (FR-05 a FR-07, FR-10 a FR-12 de FEAT-010, ADR-029).
/// </summary>
/// <remarks>
/// <para>
/// Un pedido a la vez (<see cref="ICandadoBackup"/>, D-22): con el candado tomado no hace nada. Sin ubicación no hace nada
/// y lo deja en Debug (D-25). Con <see cref="MotivoBackup.AlIniciar"/> copia solo si falta la de ayer
/// (<see cref="ReglaBackupDiario"/>); con <see cref="MotivoBackup.Programado"/> copia siempre (D-20).
/// </para>
/// <para>
/// La fecha de la copia es la de hoy en la zona local del <see cref="TimeProvider"/> (FR-06, D-11). En orden:
/// <list type="number">
/// <item>recupera los temporales verificados que perdieron su copia (en FAT y exFAT el renombre con reemplazo no es
/// atómico: un corte entre el borrado de la copia y el renombre deja solo el temporal) renombrándolos, sin anotarlos en el
/// historial: no se sabe cuándo se hicieron;</item>
/// <item>borra los demás temporales huérfanos (D-23);</item>
/// <item>copia al temporal, lo verifica y lo renombra a <c>optica-AAAA-MM-DD.db</c>, reemplazando la copia de hoy recién
/// ahora (D-20);</item>
/// <item>anota la fecha en el historial y lo confirma;</item>
/// <item>aplica la retención (FR-12).</item>
/// </list>
/// Si falla un paso antes del renombre: borra el temporal, no borra ninguna copia y no anota en el historial; el resultado
/// dice qué paso falló. Si falla el historial, la copia verificada queda en la carpeta, sin anotar y sin retención
/// (<see cref="FallaBackup.Historial"/>). La retención que no se puede aplicar no cambia el resultado: queda como
/// advertencia.
/// </para>
/// <para>
/// La cancelación antes del renombre borra el temporal y se propaga. Después del renombre ya no se cancela: la copia
/// verificada está en la carpeta, y anotarla y aplicar la retención terminan con <see cref="CancellationToken.None"/>.
/// </para>
/// <para>
/// Log con la categoría <see cref="CategoriaRegistro"/>: fecha y hora UTC, motivo, resultado, paso que falló y tipo de la
/// excepción. Nunca la ruta (puede llevar el nombre de una persona, D-12, R-05) ni el texto o la traza de la excepción,
/// que pueden traerla. En Debug el inicio, el pedido con el candado tomado (el programador reintenta), la falta de
/// ubicación y el "no hacía falta"; en Warning un temporal que no se pudo borrar después de una falla (es una copia
/// completa, con datos personales, fuera de la retención) y la retención que no se aplicó; en Error la falla del día.
/// </para>
/// <para>
/// Se resuelve siempre en un alcance propio (uno por ejecución, como hace el programador): comparte el contexto y la
/// unidad de trabajo del alcance con el historial, y su <see cref="IUnidadDeTrabajo.ConfirmarAsync"/> confirmaría
/// cualquier otro cambio que alguien hubiera registrado en ese contexto.
/// </para>
/// </remarks>
public sealed partial class HacerBackup(
    IUbicacionBackupRepository ubicaciones,
    IHistorialBackupRepository historial,
    IUnidadDeTrabajo unidadDeTrabajo,
    ICopiadorBackup copiador,
    IAlmacenCopias almacen,
    ICandadoBackup candado,
    TimeProvider reloj,
    ILoggerFactory fabricaRegistros)
{
    /// <summary>
    /// La categoría del registro del backup. <c>appsettings.json</c> la envía al Registro de eventos de Windows para que el
    /// éxito diario quede auditado (spec FEAT-010, Block 5).
    /// </summary>
    public const string CategoriaRegistro = "Optica.Application.Backup";

    private readonly ILogger _registro =
        (fabricaRegistros ?? throw new ArgumentNullException(nameof(fabricaRegistros))).CreateLogger(CategoriaRegistro);

    public async Task<ResultadoBackup> EjecutarAsync(MotivoBackup motivo, CancellationToken cancelacion = default)
    {
        using var turno = candado.IntentarTomar();
        if (turno is null)
        {
            RegistrarEnCurso(_registro, motivo, AhoraUtc());
            return new ResultadoBackup(EstadoBackup.EnCurso);
        }

        var ubicacion = await ubicaciones.ObtenerAsync(cancelacion);
        if (ubicacion is null)
        {
            RegistrarSinUbicacion(_registro, motivo, AhoraUtc());
            return new ResultadoBackup(EstadoBackup.SinUbicacion);
        }

        var hoy = ReglaBackupDiario.Hoy(reloj);
        if (motivo == MotivoBackup.AlIniciar && !await ReglaBackupDiario.FaltaLaDeAyerAsync(historial, hoy, cancelacion))
        {
            RegistrarNoHaciaFalta(_registro, motivo, AhoraUtc());
            return new ResultadoBackup(EstadoBackup.NoHaciaFalta, hoy);
        }

        return await CopiarAsync(ubicacion.Ruta, hoy, motivo, cancelacion);
    }

    private async Task<ResultadoBackup> CopiarAsync(
        string carpeta, DateOnly hoy, MotivoBackup motivo, CancellationToken cancelacion)
    {
        RegistrarInicio(_registro, motivo, AhoraUtc());
        var paso = FallaBackup.CarpetaInaccesible;
        try
        {
            await RecuperarTemporalesAsync(carpeta, motivo, cancelacion);
            almacen.BorrarTemporales(carpeta);

            paso = FallaBackup.Copia;
            var temporal = almacen.RutaTemporal(carpeta, hoy);
            await copiador.CopiarAsync(temporal, cancelacion);

            paso = FallaBackup.Integridad;
            if (!await copiador.VerificarAsync(temporal, cancelacion))
            {
                return Fallo(carpeta, hoy, motivo, paso, tipoError: null);
            }

            // Recién con la copia nueva verificada se reemplaza la de hoy, si había (D-20).
            paso = FallaBackup.Renombre;
            almacen.RenombrarTemporal(carpeta, hoy);
        }
        catch (OperationCanceledException)
        {
            // Antes de copiar no hay temporal propio: borrar ahí podría llevarse uno que se estaba por recuperar.
            if (paso != FallaBackup.CarpetaInaccesible)
            {
                BorrarTemporales(carpeta);
            }

            throw;
        }
        catch (Exception error)
        {
            return Fallo(carpeta, hoy, motivo, paso, error.GetType().Name);
        }

        // Con la copia verificada ya renombrada, anotarla no se cancela (CancellationToken.None).
        try
        {
            historial.Agregar(hoy, reloj.GetUtcNow());
            await unidadDeTrabajo.ConfirmarAsync(CancellationToken.None);
        }
        catch (Exception error)
        {
            // La copia verificada queda en la carpeta (los archivos son la verdad, ADR-029); sin historial no se aplica la
            // retención: no se borra nada en un backup que no terminó.
            RegistrarCopiaSinHistorial(_registro, motivo, FallaBackup.Historial, error.GetType().Name, AhoraUtc());
            return new ResultadoBackup(EstadoBackup.Fallido, hoy, FallaBackup.Historial);
        }

        var borradas = AplicarRetencion(carpeta, hoy, motivo);
        RegistrarHecho(_registro, motivo, hoy, borradas, AhoraUtc());
        return new ResultadoBackup(EstadoBackup.Hecho, hoy);
    }

    private ResultadoBackup Fallo(string carpeta, DateOnly hoy, MotivoBackup motivo, FallaBackup falla, string? tipoError)
    {
        // Con la carpeta inaccesible todavía no se escribió un temporal propio; los huérfanos los borra el próximo backup.
        if (falla != FallaBackup.CarpetaInaccesible)
        {
            BorrarTemporales(carpeta);
        }

        RegistrarFalla(_registro, motivo, falla, tipoError ?? "ninguno", AhoraUtc());
        return new ResultadoBackup(EstadoBackup.Fallido, hoy, falla);
    }

    /// <summary>
    /// Renombra cada temporal sin copia terminada que pasa la verificación: lo que dejó un renombre con reemplazo cortado
    /// en FAT o exFAT. Uno que no pasa queda para el borrado de huérfanos. No anota en el historial.
    /// </summary>
    private async Task RecuperarTemporalesAsync(string carpeta, MotivoBackup motivo, CancellationToken cancelacion)
    {
        foreach (var fecha in almacen.ListarTemporalesSinCopia(carpeta))
        {
            if (await copiador.VerificarAsync(almacen.RutaTemporal(carpeta, fecha), cancelacion))
            {
                almacen.RenombrarTemporal(carpeta, fecha);
                RegistrarRecuperada(_registro, motivo, fecha, AhoraUtc());
            }
        }
    }

    /// <summary>Borra el temporal de un backup que no terminó; si tampoco se puede (carpeta desconectada), sigue.</summary>
    private void BorrarTemporales(string carpeta)
    {
        try
        {
            almacen.BorrarTemporales(carpeta);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // Intencional: el próximo backup lo borra antes de copiar (D-23).
            RegistrarTemporalesSinBorrar(_registro, error.GetType().Name, AhoraUtc());
        }
    }

    /// <summary>
    /// Borra las copias que <see cref="PoliticaRetencion"/> no conserva (FR-10 a FR-12); devuelve cuántas borró. Solo se
    /// llama con la copia del día verificada y anotada.
    /// </summary>
    private int AplicarRetencion(string carpeta, DateOnly hoy, MotivoBackup motivo)
    {
        try
        {
            var copias = almacen.ListarCopias(carpeta);
            var conservadas = PoliticaRetencion.Conservar(copias, hoy).ToHashSet();
            var sobrantes = copias.Where(c => !conservadas.Contains(c)).Distinct().ToList();
            return almacen.BorrarCopias(carpeta, sobrantes);
        }
        catch (Exception error)
        {
            RegistrarRetencionFallida(_registro, motivo, error.GetType().Name, AhoraUtc());
            return 0;
        }
    }

    private DateTime AhoraUtc() => reloj.GetUtcNow().UtcDateTime;

    // Nunca la ruta ni la excepción (D-12, R-05): solo el motivo, la fecha, el paso y el tipo de error.
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Backup hecho. Motivo: {Motivo}. Fecha de la copia: {FechaCopia}. Copias borradas por la retención: "
            + "{Borradas}. Fecha y hora UTC: {FechaHoraUtc:O}.")]
    private static partial void RegistrarHecho(
        ILogger registro, MotivoBackup motivo, DateOnly fechaCopia, int borradas, DateTime fechaHoraUtc);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "El backup del día falló y no cuenta como backup. Motivo: {Motivo}. Falla: {Falla}. Tipo de error: "
            + "{TipoError}. No se borró ninguna copia. Fecha y hora UTC: {FechaHoraUtc:O}.")]
    private static partial void RegistrarFalla(
        ILogger registro, MotivoBackup motivo, FallaBackup falla, string tipoError, DateTime fechaHoraUtc);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "El backup del día falló al anotarlo: la copia quedó en la carpeta pero no se anotó en el historial; no "
            + "se aplicó la retención. Motivo: {Motivo}. Falla: {Falla}. Tipo de error: {TipoError}. Fecha y hora UTC: "
            + "{FechaHoraUtc:O}.")]
    private static partial void RegistrarCopiaSinHistorial(
        ILogger registro, MotivoBackup motivo, FallaBackup falla, string tipoError, DateTime fechaHoraUtc);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "El backup se hizo pero no se pudo aplicar la retención de copias. Motivo: {Motivo}. Tipo de error: "
            + "{TipoError}. Fecha y hora UTC: {FechaHoraUtc:O}.")]
    private static partial void RegistrarRetencionFallida(
        ILogger registro, MotivoBackup motivo, string tipoError, DateTime fechaHoraUtc);

    // Warning: el temporal es una copia completa de la base, con datos personales, que queda fuera de la retención hasta
    // el próximo backup.
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "No se pudo borrar el temporal de un backup que no terminó: queda en la carpeta del backup hasta el "
            + "próximo. Tipo de error: {TipoError}. Fecha y hora UTC: {FechaHoraUtc:O}.")]
    private static partial void RegistrarTemporalesSinBorrar(ILogger registro, string tipoError, DateTime fechaHoraUtc);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Backup iniciado. Motivo: {Motivo}. Fecha y hora UTC: {FechaHoraUtc:O}.")]
    private static partial void RegistrarInicio(ILogger registro, MotivoBackup motivo, DateTime fechaHoraUtc);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Se recuperó una copia verificada que había quedado como temporal. Motivo: {Motivo}. Fecha de la copia: "
            + "{FechaCopia}. Fecha y hora UTC: {FechaHoraUtc:O}.")]
    private static partial void RegistrarRecuperada(
        ILogger registro, MotivoBackup motivo, DateOnly fechaCopia, DateTime fechaHoraUtc);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Backup no hecho: no hay una ubicación de backup elegida. Motivo: {Motivo}. Fecha y hora UTC: "
            + "{FechaHoraUtc:O}.")]
    private static partial void RegistrarSinUbicacion(ILogger registro, MotivoBackup motivo, DateTime fechaHoraUtc);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Backup no hecho: ya está el de ayer o el de hoy. Motivo: {Motivo}. Fecha y hora UTC: {FechaHoraUtc:O}.")]
    private static partial void RegistrarNoHaciaFalta(ILogger registro, MotivoBackup motivo, DateTime fechaHoraUtc);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Backup no hecho: hay otro backup en curso. Motivo: {Motivo}. Fecha y hora UTC: {FechaHoraUtc:O}.")]
    private static partial void RegistrarEnCurso(ILogger registro, MotivoBackup motivo, DateTime fechaHoraUtc);
}
