using Optica.Domain.Comun;

namespace Optica.Domain.Facturacion;

/// <summary>
/// Por qué una verificación no pudo resolver una emisión pendiente y habilita su descarte (FR-07, FR-14, FR-15, D-12).
/// </summary>
public enum MotivoObservacionEmision
{
    /// <summary>ARCA autorizó el número registrado con otro total (FR-07, AC-07).</summary>
    ImporteDistinto,

    /// <summary>La pendiente la grabó FEAT-007 y no tiene número registrado (FR-14, AC-15).</summary>
    SinNumero,

    /// <summary>La pendiente se envió a un entorno de ARCA distinto del configurado (FR-15, AC-16).</summary>
    OtroEntorno,
}

/// <summary>
/// El resultado de verificar una emisión pendiente cuando no se puede recuperar ni emitir (ADR-026): el motivo, el número
/// registrado (si hay), el total registrado, el de ARCA (si se conoce), el entorno de la pendiente y el momento en UTC. Es
/// la condición para descartarla (FR-11). Se agrega y se quita, no se modifica (D-12). No lleva datos del cliente.
/// </summary>
public sealed class ObservacionEmision
{
    /// <param name="momento">Cuándo se verificó; se guarda en UTC.</param>
    /// <exception cref="ArgumentException">
    /// El número de presupuesto es menor que 1; el motivo o el entorno no están definidos; el número no está entre 1 y
    /// <see cref="NumeroComprobante.NumeroMaximo"/>; un total es negativo, tiene más de 2 decimales o supera
    /// <see cref="ReglasImporte.ImporteMaximo"/>; con <see cref="MotivoObservacionEmision.ImporteDistinto"/>, falta el
    /// número o el total de ARCA (D-12); o el momento es el por defecto. El mensaje nombra el campo y no incluye el valor.
    /// </exception>
    public ObservacionEmision(
        long presupuestoNumero, MotivoObservacionEmision motivo, long? numero, decimal totalRegistrado, decimal? totalArca,
        EntornoArca entorno, DateTimeOffset momento)
    {
        ReglasObservacion.ValidarPresupuesto(presupuestoNumero);
        ReglasObservacion.Validar(motivo, numero, totalRegistrado, totalArca);
        ReglasFactura.ValidarEntorno(entorno);
        ReglasObservacion.ValidarMomento(momento);

        PresupuestoNumero = presupuestoNumero;
        Motivo = motivo;
        Numero = numero;
        TotalRegistrado = totalRegistrado;
        TotalArca = totalArca;
        Entorno = entorno;
        Momento = momento.ToUniversalTime();
    }

    public long PresupuestoNumero { get; }

    public MotivoObservacionEmision Motivo { get; }

    /// <summary>El número registrado en la pendiente; null si no tiene (FR-14).</summary>
    public long? Numero { get; }

    /// <summary>El total de la pendiente.</summary>
    public decimal TotalRegistrado { get; }

    /// <summary>El total que informó ARCA para el número; solo con <see cref="MotivoObservacionEmision.ImporteDistinto"/>.</summary>
    public decimal? TotalArca { get; }

    /// <summary>El entorno de la pendiente (FR-15: puede no ser el configurado).</summary>
    public EntornoArca Entorno { get; }

    /// <summary>En UTC (desplazamiento 0).</summary>
    public DateTimeOffset Momento { get; }
}

/// <summary>Validaciones que comparten la observación y el descarte; los mensajes no incluyen el valor recibido.</summary>
internal static class ReglasObservacion
{
    internal static void ValidarPresupuesto(long presupuestoNumero)
    {
        if (presupuestoNumero < 1)
        {
            throw new ArgumentException("El número de presupuesto no es válido.", nameof(presupuestoNumero));
        }
    }

    internal static void ValidarMomento(DateTimeOffset momento)
    {
        if (momento == default)
        {
            throw new ArgumentException("El momento no es válido.", nameof(momento));
        }
    }

    /// <summary>El motivo, el número y los totales; con importe distinto, el número y el total de ARCA son obligatorios.</summary>
    internal static void Validar(MotivoObservacionEmision motivo, long? numero, decimal totalRegistrado, decimal? totalArca)
    {
        if (!Enum.IsDefined(motivo))
        {
            throw new ArgumentException("El motivo no es válido.", nameof(motivo));
        }

        if (numero is < NumeroComprobante.NumeroMinimo or > NumeroComprobante.NumeroMaximo)
        {
            throw new ArgumentException("El número de comprobante no es válido.", nameof(numero));
        }

        if (!EsImporteValido(totalRegistrado))
        {
            throw new ArgumentException("El total registrado no es válido.", nameof(totalRegistrado));
        }

        if (totalArca is { } arca && !EsImporteValido(arca))
        {
            throw new ArgumentException("El total de ARCA no es válido.", nameof(totalArca));
        }

        if (motivo == MotivoObservacionEmision.ImporteDistinto)
        {
            if (numero is null)
            {
                throw new ArgumentException("Un importe distinto exige el número consultado.", nameof(numero));
            }

            if (totalArca is null)
            {
                throw new ArgumentException("Un importe distinto exige el total de ARCA.", nameof(totalArca));
            }
        }
    }

    private static bool EsImporteValido(decimal importe) =>
        importe >= 0m && importe <= ReglasImporte.ImporteMaximo && !ReglasImporte.TieneMasDeDosDecimales(importe);
}
