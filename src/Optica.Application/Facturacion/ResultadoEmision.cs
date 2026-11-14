using System.Diagnostics.CodeAnalysis;
using Optica.Application.Comun;

namespace Optica.Application.Facturacion;

/// <summary>Cómo terminó una emisión (spec FEAT-007, Block 7).</summary>
public enum TipoResultadoEmision
{
    /// <summary>ARCA autorizó y la factura quedó grabada (201).</summary>
    Emitida,

    /// <summary>El concepto falta o no es uno de los dos (400); no se leyó la base.</summary>
    Invalido,

    /// <summary>No hay presupuesto con ese número (404).</summary>
    NoEncontrado,

    /// <summary>No se emitió: <see cref="ResultadoEmision.Motivo"/> dice por qué (409).</summary>
    Conflicto,
}

/// <summary>
/// Por qué no se emitió la factura (D-18, D-31). Cada uno tiene su texto en la Api (<c>motivo</c> del 409).
/// </summary>
public enum MotivoConflictoEmision
{
    /// <summary>El presupuesto está en Borrador (FR-02); no se llamó a ARCA.</summary>
    Borrador,

    /// <summary>El presupuesto ya tiene una factura (FR-03); no se llamó a ARCA.</summary>
    Facturado,

    /// <summary>El presupuesto tiene una emisión pendiente de verificación (FR-24); no se llamó a ARCA.</summary>
    Pendiente,

    /// <summary>El tope de identificación no tiene valor (FR-13); no se llamó a ARCA.</summary>
    SinTope,

    /// <summary>El punto de venta no tiene valor (FR-29); no se llamó a ARCA.</summary>
    SinPuntoVenta,

    /// <summary>Con Responsable Inscripto, la alícuota guardada no es una de ARCA (FR-27); no se llamó a ARCA.</summary>
    AlicuotaNoAdmitida,

    /// <summary>ARCA rechazó la solicitud (FR-21): lleva su código y su descripción; se puede volver a emitir.</summary>
    Rechazada,

    /// <summary>Se envió la solicitud y ARCA no respondió en el plazo (FR-23): la emisión queda pendiente.</summary>
    SinRespuesta,

    /// <summary>
    /// La solicitud no llegó a enviarse (configuración de ARCA incompleta, WSAA, otra emisión en curso que consumió el
    /// plazo): no queda pendiente y se puede volver a intentar.
    /// </summary>
    NoEnviada,

    /// <summary>ARCA autorizó pero la factura no se pudo grabar: la pendiente queda (D-31).</summary>
    PendienteVerificacion,
}

/// <summary>
/// El resultado de <see cref="EmitirFactura"/>: emitida con la factura, inválido con los errores del concepto, no
/// encontrado, o conflicto con su motivo (y, solo en un rechazo de ARCA, su código y su descripción). Ningún resultado
/// lleva datos del cliente.
/// </summary>
public sealed class ResultadoEmision
{
    private ResultadoEmision(
        TipoResultadoEmision tipo,
        FacturaEmitida? factura,
        IReadOnlyList<ErrorValidacion> errores,
        MotivoConflictoEmision? motivo,
        int? codigoArca,
        string? descripcionArca)
    {
        Tipo = tipo;
        Factura = factura;
        Errores = errores;
        Motivo = motivo;
        CodigoArca = codigoArca;
        DescripcionArca = descripcionArca;
    }

    public TipoResultadoEmision Tipo { get; }

    /// <summary>La factura emitida; null salvo en <see cref="TipoResultadoEmision.Emitida"/>.</summary>
    public FacturaEmitida? Factura { get; }

    /// <summary>Los errores de validación; vacío salvo en <see cref="TipoResultadoEmision.Invalido"/>.</summary>
    public IReadOnlyList<ErrorValidacion> Errores { get; }

    /// <summary>El motivo del conflicto; null salvo en <see cref="TipoResultadoEmision.Conflicto"/>.</summary>
    public MotivoConflictoEmision? Motivo { get; }

    /// <summary>El código del rechazo de ARCA; solo con <see cref="MotivoConflictoEmision.Rechazada"/>.</summary>
    public int? CodigoArca { get; }

    /// <summary>La descripción del rechazo de ARCA; solo con <see cref="MotivoConflictoEmision.Rechazada"/>.</summary>
    public string? DescripcionArca { get; }

    /// <summary>Verdadero si la factura se emitió: <see cref="Factura"/> la tiene.</summary>
    [MemberNotNullWhen(true, nameof(Factura))]
    public bool EsEmitida => Tipo == TipoResultadoEmision.Emitida && Factura is not null;

    public static ResultadoEmision NoEncontrado { get; } =
        new(TipoResultadoEmision.NoEncontrado, null, [], null, null, null);

    /// <exception cref="ArgumentNullException"><paramref name="factura"/> es null.</exception>
    public static ResultadoEmision Emitida(FacturaEmitida factura)
    {
        ArgumentNullException.ThrowIfNull(factura);
        return new ResultadoEmision(TipoResultadoEmision.Emitida, factura, [], null, null, null);
    }

    /// <exception cref="ArgumentException">No hay errores.</exception>
    public static ResultadoEmision Invalido(IReadOnlyList<ErrorValidacion> errores)
    {
        if (errores is null || errores.Count == 0)
        {
            throw new ArgumentException("Un resultado inválido necesita al menos un error.", nameof(errores));
        }

        return new ResultadoEmision(TipoResultadoEmision.Invalido, null, [.. errores], null, null, null);
    }

    /// <summary>
    /// Un conflicto. Con <see cref="MotivoConflictoEmision.Rechazada"/>, <paramref name="codigo"/> y
    /// <paramref name="descripcion"/> son obligatorios; con cualquier otro motivo, no se aceptan.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// El motivo no está definido, o el código y la descripción no corresponden al motivo. El mensaje no lleva valores.
    /// </exception>
    public static ResultadoEmision Conflicto(
        MotivoConflictoEmision motivo, int? codigo = null, string? descripcion = null)
    {
        if (!Enum.IsDefined(motivo))
        {
            throw new ArgumentException("El motivo del conflicto no es válido.", nameof(motivo));
        }

        var esRechazo = motivo == MotivoConflictoEmision.Rechazada;
        if (esRechazo != (codigo is not null) || esRechazo != (descripcion is not null))
        {
            throw new ArgumentException(
                "El código y la descripción de ARCA van solo, y siempre, en un rechazo.", nameof(codigo));
        }

        return new ResultadoEmision(TipoResultadoEmision.Conflicto, null, [], motivo, codigo, descripcion);
    }
}
