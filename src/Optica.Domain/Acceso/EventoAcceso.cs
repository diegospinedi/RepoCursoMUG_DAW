namespace Optica.Domain.Acceso;

/// <summary>Tipo de evento de acceso.</summary>
public enum TipoEventoAcceso
{
    Definicion,
    Ingreso,
    Salida,
}

/// <summary>Resultado de un evento de acceso.</summary>
public enum ResultadoEventoAcceso
{
    Exito,
    Rechazo,
}

/// <summary>
/// Un evento del registro de accesos: qué pasó, con qué resultado y cuándo (UTC). No lleva la contraseña,
/// la IP de origen ni ningún otro dato personal.
/// </summary>
public sealed class EventoAcceso
{
    public EventoAcceso(TipoEventoAcceso tipo, ResultadoEventoAcceso resultado, DateTime fechaHora)
    {
        if (!Enum.IsDefined(tipo))
        {
            throw new ArgumentOutOfRangeException(nameof(tipo));
        }

        if (!Enum.IsDefined(resultado))
        {
            throw new ArgumentOutOfRangeException(nameof(resultado));
        }

        if (fechaHora.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("La fecha y hora del evento debe estar en UTC.", nameof(fechaHora));
        }

        Tipo = tipo;
        Resultado = resultado;
        FechaHora = fechaHora;
    }

    public TipoEventoAcceso Tipo { get; }

    public ResultadoEventoAcceso Resultado { get; }

    /// <summary>Fecha y hora UTC del evento.</summary>
    public DateTime FechaHora { get; }
}
