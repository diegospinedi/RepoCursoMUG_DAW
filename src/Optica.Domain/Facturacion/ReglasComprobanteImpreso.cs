namespace Optica.Domain.Facturacion;

/// <summary>
/// Las reglas del comprobante impreso (FR-06, FR-14 y FR-15 de FEAT-009), en el dominio para que valgan desde cualquier
/// interfaz que lo arme: la condición frente al IVA del emisor según el tipo de comprobante, si el comprobante tiene
/// validez fiscal según el entorno de ARCA en que se emitió, cómo se nombra al receptor (FR-08) y si lleva IVA contenido
/// (FR-11, FR-12).
/// </summary>
public static class ReglasComprobanteImpreso
{
    /// <summary>La condición frente al IVA del emisor de una Factura B.</summary>
    public const string CondicionResponsableInscripto = "Responsable Inscripto";

    /// <summary>La condición frente al IVA del emisor de una Factura C.</summary>
    public const string CondicionMonotributo = "Responsable Monotributo";

    /// <summary>
    /// La condición frente al IVA del emisor según el tipo de comprobante (D-07): la B la emite un Responsable
    /// Inscripto y la C un Responsable Monotributo. Sale del tipo y no del parámetro de condición fiscal vigente, así un
    /// cambio posterior del parámetro no altera el PDF de una factura vieja (AC-07, AC-27).
    /// </summary>
    /// <exception cref="ArgumentException">El tipo no está definido. El mensaje no incluye el valor recibido.</exception>
    public static string CondicionIvaEmisor(TipoComprobante tipo) => tipo switch
    {
        TipoComprobante.FacturaB => CondicionResponsableInscripto,
        TipoComprobante.FacturaC => CondicionMonotributo,
        _ => throw new ArgumentException("El tipo de comprobante no es válido.", nameof(tipo)),
    };

    /// <summary>
    /// Verdadero solo para una factura emitida en <see cref="EntornoArca.Produccion"/>: en Simulado el CAE no existe en
    /// ARCA y Homologacion es el entorno de prueba (FR-14, FR-15). Cualquier otro valor no tiene validez fiscal, así la
    /// leyenda de comprobante simulado queda puesta ante la duda (R-05).
    /// </summary>
    public static bool TieneValidezFiscal(EntornoArca entorno) => entorno == EntornoArca.Produccion;

    /// <summary>Cómo se nombra al receptor de una factura emitida sin identificarlo (FR-08).</summary>
    public const string ReceptorConsumidorFinal = "Consumidor Final";

    /// <summary>El prefijo del receptor identificado con su DNI: "DNI 00123456" (FR-08).</summary>
    public const string PrefijoReceptorDni = "DNI ";

    /// <summary>
    /// El receptor como texto (FR-08): <see cref="ReceptorConsumidorFinal"/> si se emitió sin identificarlo, o
    /// "DNI 00123456" si se identificó. Lleva el DNI: quien lo use no lo registra (Ley 25.326).
    /// </summary>
    /// <exception cref="ArgumentNullException">El receptor es null.</exception>
    public static string TextoReceptor(ReceptorFactura receptor)
    {
        ArgumentNullException.ThrowIfNull(receptor);
        return receptor.Dni is { } dni ? PrefijoReceptorDni + dni : ReceptorConsumidorFinal;
    }

    /// <summary>
    /// Verdadero solo para la Factura B, que muestra el IVA contenido (FR-11, Ley 27.743); la C no lo lleva (FR-12).
    /// Cualquier otro valor no lo lleva.
    /// </summary>
    public static bool LlevaIvaContenido(TipoComprobante tipo) => tipo == TipoComprobante.FacturaB;
}
