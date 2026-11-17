using Optica.Application.Backup;

namespace Optica.Api.Backup;

/// <summary>
/// Agenda el backup del día (FR-04, FR-08 de FEAT-010, ADR-030): solo decide cuándo pedirlo; qué copiar y cómo lo hace
/// <see cref="HacerBackup"/>, resuelto en un alcance propio por cada ejecución.
/// </summary>
/// <remarks>
/// <para>
/// Al arrancar pide el backup con <see cref="MotivoBackup.AlIniciar"/> (el caso de uso decide si falta el de ayer). Después
/// despierta cada <see cref="Intervalo"/> con el <see cref="TimeProvider"/> y, si <see cref="ConsultarBackupProgramado"/>
/// dice que toca (las 20:00 locales o más, sin la copia programada de hoy en el historial), lo pide con
/// <see cref="MotivoBackup.Programado"/>. Compara con el historial y no con un contador (D-24): tolera la suspensión de la
/// PC y los saltos del reloj. El intento al arrancar sigue pendiente mientras no termine (también sin ubicación, para que
/// la primera copia salga apenas se la elige) y se repite en los despertares; desde la hora programada lo cubre el
/// programado.
/// </para>
/// <para>
/// Reintentos: con otro backup en curso (candado tomado) o sin ubicación, el próximo despertar vuelve a pedirlo. Después de
/// una falla o una excepción (al pedir el backup o al consultar el historial) los despertares no piden nada hasta que pasen
/// <see cref="EsperaTrasFallas"/>: 15, 30 y después 60 minutos como tope. Un pedido que termina bien o el cambio de día local
/// vuelven al ritmo de cada despertar. Así una carpeta desconectada deja pocos errores por día en el Registro de eventos,
/// y una copia que quedó sin anotar no se repite cada 5 minutos.
/// </para>
/// <para>
/// Una excepción queda en el log con su tipo y el servicio sigue: nunca su texto ni su traza, que pueden traer la ruta del
/// backup (D-12). Las fallas del día las registra el caso de uso. Al detener la Api la cancelación llega al caso de uso, que
/// borra el temporal (o lo deja para el próximo backup, que lo borra antes de copiar, D-23), y el servicio termina sin
/// excepción.
/// </para>
/// </remarks>
public sealed partial class ProgramadorBackup(
    IServiceScopeFactory alcances, TimeProvider reloj, ILogger<ProgramadorBackup> registro) : BackgroundService
{
    /// <summary>Cada cuánto despierta a comparar el reloj con el historial (D-24).</summary>
    public static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(5);

    /// <summary>Cuánto se espera para volver a pedir el backup después de la primera, segunda y tercera falla seguidas o más.</summary>
    private static readonly TimeSpan[] EsperasTrasFallas =
        [TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(60)];

    /// <summary>Fallas seguidas desde el último pedido terminado o el último cambio de día.</summary>
    private int _fallasSeguidas;

    /// <summary>El día local de la última falla: al cambiar el día, el espaciado vuelve al principio.</summary>
    private DateOnly _diaDeLasFallas;

    /// <summary>Antes de este momento (UTC) los despertares no piden nada.</summary>
    private DateTimeOffset _proximoIntento = DateTimeOffset.MinValue;

    /// <summary>Cómo terminó un pedido, desde el punto de vista del programador.</summary>
    private enum Desenlace
    {
        /// <summary>Hecho o no hacía falta.</summary>
        Terminado,

        /// <summary>Sin ubicación, otro backup en curso o cancelado al detener: se repite en el próximo despertar.</summary>
        Pendiente,

        /// <summary>Falla del día o excepción: se repite con espaciado.</summary>
        Fallido,
    }

    /// <summary>La espera antes del próximo pedido después de <paramref name="fallasSeguidas"/> fallas seguidas (1 o más).</summary>
    private static TimeSpan EsperaTrasFallas(int fallasSeguidas) =>
        EsperasTrasFallas[Math.Clamp(fallasSeguidas, 1, EsperasTrasFallas.Length) - 1];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var inicioPendiente = await PedirAsync(MotivoBackup.AlIniciar, stoppingToken) != Desenlace.Terminado;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Intervalo, reloj, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            if (!PuedePedir())
            {
                continue;
            }

            var toca = await TocaElProgramadoAsync(stoppingToken);
            if (toca == true)
            {
                // El programado copia siempre (D-20): si termina, cubre también el intento al arrancar.
                if (await PedirAsync(MotivoBackup.Programado, stoppingToken) == Desenlace.Terminado)
                {
                    inicioPendiente = false;
                }
            }
            else if (toca == false && inicioPendiente)
            {
                inicioPendiente = await PedirAsync(MotivoBackup.AlIniciar, stoppingToken) != Desenlace.Terminado;
            }
        }
    }

    /// <summary>
    /// Falso mientras dura la espera después de una falla. Con el cambio de día local el espaciado vuelve al principio.
    /// </summary>
    private bool PuedePedir()
    {
        if (_fallasSeguidas > 0 && ReglaBackupDiario.Hoy(reloj) != _diaDeLasFallas)
        {
            ReiniciarEspaciado();
        }

        return reloj.GetUtcNow() >= _proximoIntento;
    }

    private void ReiniciarEspaciado()
    {
        _fallasSeguidas = 0;
        _proximoIntento = DateTimeOffset.MinValue;
    }

    /// <summary>Anota una falla y fija desde cuándo se puede volver a pedir.</summary>
    private void EspaciarTrasFalla()
    {
        _fallasSeguidas++;
        _diaDeLasFallas = ReglaBackupDiario.Hoy(reloj);
        _proximoIntento = reloj.GetUtcNow() + EsperaTrasFallas(_fallasSeguidas);
    }

    /// <summary>
    /// <see cref="ConsultarBackupProgramado.TocaAsync"/> en un alcance propio. Null si la consulta falló (queda en el log y
    /// se espacia como una falla) o se canceló al detener.
    /// </summary>
    private async Task<bool?> TocaElProgramadoAsync(CancellationToken cancelacion)
    {
        try
        {
            await using var alcance = alcances.CreateAsyncScope();
            return await alcance.ServiceProvider.GetRequiredService<ConsultarBackupProgramado>().TocaAsync(cancelacion);
        }
        catch (OperationCanceledException) when (cancelacion.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception error)
        {
            EspaciarTrasFalla();
            RegistrarConsultaFallida(
                registro, error.GetType().Name, _proximoIntento.UtcDateTime, reloj.GetUtcNow().UtcDateTime);
            return null;
        }
    }

    /// <summary>Pide el backup en un alcance propio y actualiza el espaciado según cómo terminó.</summary>
    private async Task<Desenlace> PedirAsync(MotivoBackup motivo, CancellationToken cancelacion)
    {
        try
        {
            await using var alcance = alcances.CreateAsyncScope();
            var resultado = await alcance.ServiceProvider.GetRequiredService<HacerBackup>()
                .EjecutarAsync(motivo, cancelacion);
            switch (resultado.Estado)
            {
                case EstadoBackup.Hecho or EstadoBackup.NoHaciaFalta:
                    ReiniciarEspaciado();
                    return Desenlace.Terminado;
                case EstadoBackup.Fallido:
                    EspaciarTrasFalla();
                    RegistrarReintento(
                        registro, motivo, resultado.Estado, _proximoIntento.UtcDateTime, reloj.GetUtcNow().UtcDateTime);
                    return Desenlace.Fallido;
                default:
                    // Sin ubicación u otro backup en curso: el próximo despertar lo vuelve a pedir, sin espaciar.
                    return Desenlace.Pendiente;
            }
        }
        catch (OperationCanceledException) when (cancelacion.IsCancellationRequested)
        {
            // La Api se está deteniendo: el caso de uso ya limpió lo suyo.
            return Desenlace.Pendiente;
        }
        catch (Exception error)
        {
            EspaciarTrasFalla();
            RegistrarExcepcion(
                registro, motivo, error.GetType().Name, _proximoIntento.UtcDateTime, reloj.GetUtcNow().UtcDateTime);
            return Desenlace.Fallido;
        }
    }

    // Nunca la ruta ni la excepción (D-12): solo el motivo, el estado, el tipo de error y los momentos.
    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "El backup no se pudo pedir por un error. Motivo: {Motivo}. Tipo de error: {TipoError}. Próximo intento "
            + "desde (UTC): {ProximoIntentoUtc:O}. Fecha y hora UTC: {FechaHoraUtc:O}.")]
    private static partial void RegistrarExcepcion(
        ILogger registro, MotivoBackup motivo, string tipoError, DateTime proximoIntentoUtc, DateTime fechaHoraUtc);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Falló la consulta del historial del backup programado. Tipo de error: {TipoError}. Próximo intento desde "
            + "(UTC): {ProximoIntentoUtc:O}. Fecha y hora UTC: {FechaHoraUtc:O}.")]
    private static partial void RegistrarConsultaFallida(
        ILogger registro, string tipoError, DateTime proximoIntentoUtc, DateTime fechaHoraUtc);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "El backup no terminó. Motivo: {Motivo}. Estado: {Estado}. Próximo intento desde (UTC): "
            + "{ProximoIntentoUtc:O}. Fecha y hora UTC: {FechaHoraUtc:O}.")]
    private static partial void RegistrarReintento(
        ILogger registro, MotivoBackup motivo, EstadoBackup estado, DateTime proximoIntentoUtc, DateTime fechaHoraUtc);
}
