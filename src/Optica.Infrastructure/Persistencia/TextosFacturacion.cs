using Optica.Domain.Facturacion;

namespace Optica.Infrastructure.Persistencia;

/// <summary>
/// Los textos del tipo de comprobante, del entorno de ARCA, del concepto y del motivo de una observación en las tablas
/// <c>Factura</c>, <c>EmisionPendiente</c>, <c>ObservacionEmision</c> y <c>DescarteEmision</c>, en minúscula como los
/// CHECK (<c>CK_Factura_Tipo</c>, <c>CK_Factura_Entorno</c>, <c>CK_EmisionPendiente_Concepto</c>,
/// <c>CK_ObservacionEmision_Motivo</c> y los demás). Los comparten los repositorios de facturación sin depender uno del
/// otro.
/// </summary>
internal static class TextosFacturacion
{
    private const string TipoB = "b";
    private const string TipoC = "c";

    private const string Simulado = "simulado";
    private const string Homologacion = "homologacion";
    private const string Produccion = "produccion";

    private const string ConceptoProductos = "productos";
    private const string ConceptoProductosYServicios = "productos_y_servicios";

    private const string MotivoImporteDistinto = "importe_distinto";
    private const string MotivoSinNumero = "sin_numero";
    private const string MotivoOtroEntorno = "otro_entorno";

    /// <exception cref="ArgumentException">El tipo no está definido. El mensaje no incluye el valor.</exception>
    internal static string TextoDe(TipoComprobante tipo) => tipo switch
    {
        TipoComprobante.FacturaB => TipoB,
        TipoComprobante.FacturaC => TipoC,
        _ => throw new ArgumentException("El tipo de comprobante no es válido.", nameof(tipo)),
    };

    /// <exception cref="ArgumentException">El entorno no está definido. El mensaje no incluye el valor.</exception>
    internal static string TextoDe(EntornoArca entorno) => entorno switch
    {
        EntornoArca.Simulado => Simulado,
        EntornoArca.Homologacion => Homologacion,
        EntornoArca.Produccion => Produccion,
        _ => throw new ArgumentException("El entorno de ARCA no es válido.", nameof(entorno)),
    };

    /// <summary>El tipo de un texto guardado; null si no es uno de los dos.</summary>
    internal static TipoComprobante? TipoDe(string texto) => texto switch
    {
        TipoB => TipoComprobante.FacturaB,
        TipoC => TipoComprobante.FacturaC,
        _ => null,
    };

    /// <summary>El entorno de un texto guardado; null si no es uno de los tres.</summary>
    internal static EntornoArca? EntornoDe(string texto) => texto switch
    {
        Simulado => EntornoArca.Simulado,
        Homologacion => EntornoArca.Homologacion,
        Produccion => EntornoArca.Produccion,
        _ => null,
    };

    /// <exception cref="ArgumentException">El concepto no está definido. El mensaje no incluye el valor.</exception>
    internal static string TextoDe(ConceptoFactura concepto) => concepto switch
    {
        ConceptoFactura.Productos => ConceptoProductos,
        ConceptoFactura.ProductosYServicios => ConceptoProductosYServicios,
        _ => throw new ArgumentException("El concepto de la factura no es válido.", nameof(concepto)),
    };

    /// <exception cref="ArgumentException">El motivo no está definido. El mensaje no incluye el valor.</exception>
    internal static string TextoDe(MotivoObservacionEmision motivo) => motivo switch
    {
        MotivoObservacionEmision.ImporteDistinto => MotivoImporteDistinto,
        MotivoObservacionEmision.SinNumero => MotivoSinNumero,
        MotivoObservacionEmision.OtroEntorno => MotivoOtroEntorno,
        _ => throw new ArgumentException("El motivo de la observación no es válido.", nameof(motivo)),
    };

    /// <summary>El concepto de un texto guardado; null si no es uno de los dos.</summary>
    internal static ConceptoFactura? ConceptoDe(string texto) => texto switch
    {
        ConceptoProductos => ConceptoFactura.Productos,
        ConceptoProductosYServicios => ConceptoFactura.ProductosYServicios,
        _ => null,
    };

    /// <summary>El motivo de un texto guardado; null si no es uno de los tres.</summary>
    internal static MotivoObservacionEmision? MotivoDe(string texto) => texto switch
    {
        MotivoImporteDistinto => MotivoObservacionEmision.ImporteDistinto,
        MotivoSinNumero => MotivoObservacionEmision.SinNumero,
        MotivoOtroEntorno => MotivoObservacionEmision.OtroEntorno,
        _ => null,
    };
}
