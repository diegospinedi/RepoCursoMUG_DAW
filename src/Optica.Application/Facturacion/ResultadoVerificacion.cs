using System.Diagnostics.CodeAnalysis;
using Optica.Domain.Facturacion;

namespace Optica.Application.Facturacion;

/// <summary>Cómo terminó una verificación (spec FEAT-008, Block 5).</summary>
public enum TipoResultadoVerificacion
{
    /// <summary>ARCA ya tenía el número con el mismo total: la factura se guardó con su CAE (FR-05).</summary>
    Recuperada,

    /// <summary>ARCA no tenía el número: se emitió con un número nuevo y se guardó la factura (FR-06).</summary>
    Emitida,

    /// <summary>No hay presupuesto con ese número.</summary>
    NoEncontrado,

    /// <summary>No se recuperó ni se emitió: <see cref="ResultadoVerificacion.Motivo"/> dice por qué.</summary>
    Conflicto,
}

/// <summary>Por qué una verificación no recuperó ni emitió (cada uno tiene su texto en la Api).</summary>
public enum MotivoConflictoVerificacion
{
    /// <summary>El presupuesto no tiene emisión pendiente.</summary>
    SinPendiente,

    /// <summary>
    /// No pasó la espera desde que quedó pendiente (FR-16): <see cref="ResultadoVerificacion.SegundosRestantes"/>. No se
    /// llamó a ARCA.
    /// </summary>
    Espera,

    /// <summary>ARCA tiene el número con otro total (FR-07, FR-08): observación grabada, nada emitido ni recuperado.</summary>
    ImporteDistinto,

    /// <summary>La pendiente no tiene número (FR-14): observación grabada, sin llamar a ARCA.</summary>
    SinNumero,

    /// <summary>La pendiente es de otro entorno que el configurado (FR-15): observación grabada, sin llamar a ARCA.</summary>
    OtroEntorno,

    /// <summary>La consulta no tuvo respuesta o no llegó a enviarse (FR-12): la pendiente queda intacta.</summary>
    SinRespuesta,

    /// <summary>
    /// La emisión de FR-06 se envió y ARCA no respondió, o autorizó y la factura no se pudo grabar: la pendiente queda con
    /// el número nuevo (FR-13).
    /// </summary>
    SinRespuestaEmision,

    /// <summary>ARCA rechazó la emisión de FR-06: lleva su código y su descripción; no queda pendiente.</summary>
    Rechazada,

    /// <summary>
    /// No se envió nada (otra operación ocupó el plazo, el próximo número, la configuración no arma la solicitud, o la
    /// factura recuperada no valida o no se pudo grabar).
    /// </summary>
    NoEnviada,

    /// <summary>El presupuesto ya tiene una factura.</summary>
    Facturado,
}

/// <summary>
/// La observación vigente de una emisión pendiente, como la muestran la verificación y el GET del presupuesto (FR-08,
/// FR-14, FR-15): el motivo, el comprobante ("Factura B 0003-00000125", o null sin número), el importe registrado, el de
/// ARCA (o null) y el entorno de la pendiente. No lleva datos del cliente.
/// </summary>
public sealed record DetalleObservacion(
    MotivoObservacionEmision Motivo,
    string? Comprobante,
    decimal ImporteRegistrado,
    decimal? ImporteArca,
    EntornoArca Entorno)
{
    /// <summary>
    /// El detalle de <paramref name="observacion"/>; el comprobante se arma con el tipo y el punto de venta de
    /// <paramref name="pendiente"/> y el número de la observación (null si no tiene número o no hay pendiente).
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="observacion"/> es null.</exception>
    public static DetalleObservacion Desde(ObservacionEmision observacion, EmisionPendiente? pendiente)
    {
        ArgumentNullException.ThrowIfNull(observacion);
        var comprobante = observacion.Numero is { } numero && pendiente is not null
            ? new NumeroComprobante(pendiente.Tipo, pendiente.PuntoVenta, numero).ToString()
            : null;
        return new DetalleObservacion(
            observacion.Motivo, comprobante, observacion.TotalRegistrado, observacion.TotalArca, observacion.Entorno);
    }
}

/// <summary>
/// El resultado de <see cref="VerificarEmision"/>: recuperada o emitida con la factura, no encontrado, o conflicto con
/// su motivo y sus datos (los segundos que faltan, la observación, o el código y la descripción de un rechazo). Ningún
/// resultado lleva datos del cliente.
/// </summary>
public sealed class ResultadoVerificacion
{
    private ResultadoVerificacion(
        TipoResultadoVerificacion tipo,
        FacturaEmitida? factura = null,
        MotivoConflictoVerificacion? motivo = null,
        int? segundosRestantes = null,
        DetalleObservacion? observacion = null,
        int? codigoArca = null,
        string? descripcionArca = null)
    {
        Tipo = tipo;
        Factura = factura;
        Motivo = motivo;
        SegundosRestantes = segundosRestantes;
        Observacion = observacion;
        CodigoArca = codigoArca;
        DescripcionArca = descripcionArca;
    }

    public TipoResultadoVerificacion Tipo { get; }

    /// <summary>La factura guardada; solo en <see cref="TipoResultadoVerificacion.Recuperada"/> y <c>Emitida</c>.</summary>
    public FacturaEmitida? Factura { get; }

    /// <summary>El motivo; solo en <see cref="TipoResultadoVerificacion.Conflicto"/>.</summary>
    public MotivoConflictoVerificacion? Motivo { get; }

    /// <summary>Los segundos que faltan para verificar, redondeados hacia arriba; solo con <c>Espera</c>.</summary>
    public int? SegundosRestantes { get; }

    /// <summary>La observación grabada; solo con <c>ImporteDistinto</c>, <c>SinNumero</c> y <c>OtroEntorno</c>.</summary>
    public DetalleObservacion? Observacion { get; }

    /// <summary>El código del rechazo de ARCA; solo con <see cref="MotivoConflictoVerificacion.Rechazada"/>.</summary>
    public int? CodigoArca { get; }

    /// <summary>La descripción del rechazo de ARCA; solo con <see cref="MotivoConflictoVerificacion.Rechazada"/>.</summary>
    public string? DescripcionArca { get; }

    /// <summary>Verdadero si se guardó una factura (recuperada o emitida): <see cref="Factura"/> la tiene.</summary>
    [MemberNotNullWhen(true, nameof(Factura))]
    public bool TieneFactura =>
        Tipo is TipoResultadoVerificacion.Recuperada or TipoResultadoVerificacion.Emitida && Factura is not null;

    public static ResultadoVerificacion NoEncontrado { get; } = new(TipoResultadoVerificacion.NoEncontrado);

    /// <exception cref="ArgumentNullException"><paramref name="factura"/> es null.</exception>
    public static ResultadoVerificacion Recuperada(FacturaEmitida factura) =>
        new(TipoResultadoVerificacion.Recuperada, factura ?? throw new ArgumentNullException(nameof(factura)));

    /// <exception cref="ArgumentNullException"><paramref name="factura"/> es null.</exception>
    public static ResultadoVerificacion Emitida(FacturaEmitida factura) =>
        new(TipoResultadoVerificacion.Emitida, factura ?? throw new ArgumentNullException(nameof(factura)));

    /// <summary>Faltan <paramref name="segundos"/> para poder verificar (FR-16).</summary>
    /// <exception cref="ArgumentOutOfRangeException">Los segundos no son positivos.</exception>
    public static ResultadoVerificacion Espera(int segundos)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(segundos, 1);
        return new ResultadoVerificacion(
            TipoResultadoVerificacion.Conflicto, motivo: MotivoConflictoVerificacion.Espera, segundosRestantes: segundos);
    }

    /// <summary>La observación grabada; el motivo es el de la observación.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="observacion"/> es null.</exception>
    public static ResultadoVerificacion Observada(DetalleObservacion observacion)
    {
        ArgumentNullException.ThrowIfNull(observacion);
        var motivo = observacion.Motivo switch
        {
            MotivoObservacionEmision.ImporteDistinto => MotivoConflictoVerificacion.ImporteDistinto,
            MotivoObservacionEmision.SinNumero => MotivoConflictoVerificacion.SinNumero,
            MotivoObservacionEmision.OtroEntorno => MotivoConflictoVerificacion.OtroEntorno,
            _ => throw new ArgumentException("El motivo de la observación no es válido.", nameof(observacion)),
        };
        return new ResultadoVerificacion(TipoResultadoVerificacion.Conflicto, motivo: motivo, observacion: observacion);
    }

    /// <summary>ARCA rechazó la emisión de FR-06.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="descripcion"/> es null.</exception>
    public static ResultadoVerificacion Rechazada(int codigo, string descripcion)
    {
        ArgumentNullException.ThrowIfNull(descripcion);
        return new ResultadoVerificacion(
            TipoResultadoVerificacion.Conflicto, motivo: MotivoConflictoVerificacion.Rechazada, codigoArca: codigo,
            descripcionArca: descripcion);
    }

    /// <summary>
    /// Un conflicto sin datos: <c>SinPendiente</c>, <c>SinRespuesta</c>, <c>SinRespuestaEmision</c>, <c>NoEnviada</c> o
    /// <c>Facturado</c>. Los que llevan datos tienen su propia fábrica.
    /// </summary>
    /// <exception cref="ArgumentException">El motivo no está definido o lleva datos.</exception>
    public static ResultadoVerificacion Conflicto(MotivoConflictoVerificacion motivo)
    {
        if (!Enum.IsDefined(motivo)
            || motivo is MotivoConflictoVerificacion.Espera or MotivoConflictoVerificacion.Rechazada
                or MotivoConflictoVerificacion.ImporteDistinto or MotivoConflictoVerificacion.SinNumero
                or MotivoConflictoVerificacion.OtroEntorno)
        {
            throw new ArgumentException("El motivo del conflicto no es válido sin sus datos.", nameof(motivo));
        }

        return new ResultadoVerificacion(TipoResultadoVerificacion.Conflicto, motivo: motivo);
    }
}
