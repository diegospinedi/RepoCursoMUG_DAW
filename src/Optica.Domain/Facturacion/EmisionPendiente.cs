namespace Optica.Domain.Facturacion;

/// <summary>
/// Un intento de emisión registrado antes de llamar a ARCA (D-09, ADR-021): el presupuesto, el momento en UTC, la
/// solicitud enviada y el entorno. Mientras exista no se envía otra solicitud para ese presupuesto (FR-24); se agrega y
/// se quita, no se modifica. Lleva el DNI del receptor a través de la solicitud: no sobrescribe
/// <see cref="object.ToString"/> (Ley 25.326).
/// </summary>
public sealed class EmisionPendiente
{
    /// <param name="momento">Cuándo se registró; se guarda en UTC.</param>
    /// <exception cref="ArgumentNullException">La solicitud es null.</exception>
    /// <exception cref="ArgumentException">
    /// El número de presupuesto es menor que 1, el momento es el por defecto o el entorno no está definido. El mensaje
    /// nombra el campo y no incluye el valor recibido.
    /// </exception>
    public EmisionPendiente(
        long presupuestoNumero, DateTimeOffset momento, SolicitudComprobante solicitud, EntornoArca entorno)
    {
        if (presupuestoNumero < 1)
        {
            throw new ArgumentException("El número de presupuesto no es válido.", nameof(presupuestoNumero));
        }

        if (momento == default)
        {
            throw new ArgumentException("El momento de la emisión no es válido.", nameof(momento));
        }

        ArgumentNullException.ThrowIfNull(solicitud);
        ReglasFactura.ValidarEntorno(entorno);

        PresupuestoNumero = presupuestoNumero;
        Momento = momento.ToUniversalTime();
        Solicitud = solicitud;
        Entorno = entorno;
    }

    public long PresupuestoNumero { get; }

    /// <summary>En UTC (desplazamiento 0).</summary>
    public DateTimeOffset Momento { get; }

    public SolicitudComprobante Solicitud { get; }

    /// <summary>El entorno de ARCA al que se envió la solicitud.</summary>
    public EntornoArca Entorno { get; }
}
