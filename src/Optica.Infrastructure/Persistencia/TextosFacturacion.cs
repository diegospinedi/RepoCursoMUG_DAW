using Optica.Domain.Facturacion;

namespace Optica.Infrastructure.Persistencia;

/// <summary>
/// Los textos del tipo de comprobante y del entorno de ARCA en las tablas <c>Factura</c> y <c>EmisionPendiente</c>, en
/// minúscula como los CHECK (<c>CK_Factura_Tipo</c>, <c>CK_Factura_Entorno</c> y los de <c>EmisionPendiente</c>). Los
/// comparten los dos repositorios de facturación sin depender uno del otro.
/// </summary>
internal static class TextosFacturacion
{
    private const string TipoB = "b";
    private const string TipoC = "c";

    private const string Simulado = "simulado";
    private const string Homologacion = "homologacion";
    private const string Produccion = "produccion";

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
}
