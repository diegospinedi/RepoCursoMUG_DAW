namespace Optica.Domain.Acceso;

/// <summary>Resultado de evaluar <see cref="PoliticaBloqueo"/>: si el ingreso está bloqueado y hasta cuándo (UTC).</summary>
public sealed record EstadoBloqueo
{
    private EstadoBloqueo(DateTime? hasta) => Hasta = hasta;

    /// <summary>Sin bloqueo vigente.</summary>
    public static EstadoBloqueo Libre { get; } = new((DateTime?)null);

    public bool EstaBloqueado => Hasta is not null;

    /// <summary>Fecha y hora UTC en que termina el bloqueo; nula si no hay bloqueo.</summary>
    public DateTime? Hasta { get; }

    /// <exception cref="ArgumentException"><paramref name="hasta"/> no está en UTC.</exception>
    public static EstadoBloqueo BloqueadoHasta(DateTime hasta) =>
        hasta.Kind == DateTimeKind.Utc
            ? new(hasta)
            : throw new ArgumentException("El fin del bloqueo debe estar en UTC.", nameof(hasta));
}

/// <summary>
/// Bloqueo del ingreso por intentos fallidos (FR-01, FR-02, NFR-01, ADR-006). Se deriva del registro de eventos:
/// cuentan los <c>ingreso/rechazo</c> posteriores al último evento de <see cref="EventosQueReinician"/>, en orden de
/// registro. Con <c>k</c> rechazos y <c>n = k / 5</c> (entero), si <c>n ≥ 1</c> el ingreso queda bloqueado mientras
/// <c>0 ≤ ahora − fecha(rechazo 5·n) &lt; 5 minutos</c>. El ancla es el rechazo que cruzó el múltiplo de 5, así una
/// carrera concurrente que agrega un sexto no pierde el bloqueo. Si <c>ahora</c> es anterior al ancla (la hora de la
/// PC se atrasó) no hay bloqueo: el siguiente múltiplo de 5 se ancla con la hora nueva. <c>ingreso/bloqueado</c> y
/// <c>cambio_contrasena/rechazo</c> no cuentan ni reinician. Pura y sincrónica: no consulta la base.
/// </summary>
public static class PoliticaBloqueo
{
    /// <summary>Rechazos consecutivos que bloquean el ingreso.</summary>
    public const int RechazosParaBloquear = 5;

    /// <summary>Cuánto dura el bloqueo desde el rechazo que lo ancla.</summary>
    public static readonly TimeSpan Duracion = TimeSpan.FromMinutes(5);

    /// <summary>El único evento que suma a la cuenta de rechazos.</summary>
    public static (TipoEventoAcceso Tipo, ResultadoEventoAcceso Resultado) EventoQueCuenta { get; } =
        (TipoEventoAcceso.Ingreso, ResultadoEventoAcceso.Rechazo);

    /// <summary>Los eventos que reinician la cuenta de rechazos.</summary>
    public static IReadOnlyList<(TipoEventoAcceso Tipo, ResultadoEventoAcceso Resultado)> EventosQueReinician { get; } =
    [
        (TipoEventoAcceso.Ingreso, ResultadoEventoAcceso.Exito),
        (TipoEventoAcceso.Definicion, ResultadoEventoAcceso.Exito),
        (TipoEventoAcceso.CambioContrasena, ResultadoEventoAcceso.Exito),
        (TipoEventoAcceso.Restablecimiento, ResultadoEventoAcceso.Exito),
    ];

    /// <summary>Evalúa el bloqueo con las fechas UTC de los rechazos desde el último reinicio, en orden de registro.</summary>
    /// <exception cref="ArgumentException">Alguna fecha no está en UTC.</exception>
    public static EstadoBloqueo Evaluar(IReadOnlyList<DateTime> rechazos, DateTime ahora)
    {
        ArgumentNullException.ThrowIfNull(rechazos);
        ExigirUtc(ahora, nameof(ahora));
        foreach (var rechazo in rechazos)
        {
            ExigirUtc(rechazo, nameof(rechazos));
        }

        var multiplos = rechazos.Count / RechazosParaBloquear;
        if (multiplos == 0)
        {
            return EstadoBloqueo.Libre;
        }

        var ancla = rechazos[(multiplos * RechazosParaBloquear) - 1];
        var transcurrido = ahora - ancla;
        return transcurrido >= TimeSpan.Zero && transcurrido < Duracion
            ? EstadoBloqueo.BloqueadoHasta(ancla + Duracion)
            : EstadoBloqueo.Libre;
    }

    /// <summary>
    /// Las fechas de los <see cref="EventoQueCuenta"/> posteriores al último evento que reinicia la cuenta, a partir de
    /// eventos dados en orden de registro. Oráculo de referencia: la aplicación no lo usa en producción (la consulta
    /// del registro de eventos resuelve lo mismo en SQL), y las pruebas de Infrastructure comparan esa consulta contra
    /// este método sobre los mismos eventos.
    /// </summary>
    public static IReadOnlyList<DateTime> RechazosDesdeUltimoReinicio(IEnumerable<EventoAcceso> eventosEnOrden)
    {
        ArgumentNullException.ThrowIfNull(eventosEnOrden);
        var rechazos = new List<DateTime>();
        foreach (var evento in eventosEnOrden)
        {
            if (Reinicia(evento))
            {
                rechazos.Clear();
            }
            else if ((evento.Tipo, evento.Resultado) == EventoQueCuenta)
            {
                rechazos.Add(evento.FechaHora);
            }
        }

        return rechazos;
    }

    private static bool Reinicia(EventoAcceso evento) =>
        EventosQueReinician.Contains((evento.Tipo, evento.Resultado));

    private static void ExigirUtc(DateTime fecha, string parametro)
    {
        if (fecha.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Las fechas del bloqueo deben estar en UTC.", parametro);
        }
    }
}
