using Optica.Domain.Facturacion;

namespace Optica.Infrastructure.Arca;

/// <summary>
/// Los códigos de las tablas de parámetros de WSFEv1 que usa la factura a consumidor final (D-20): FEParamGetTiposCbte,
/// FEParamGetTiposDoc, FEParamGetCondicionIvaReceptor, FEParamGetTiposConcepto y FEParamGetTiposIva. Viven acá y no en el
/// dominio, que solo conoce las 6 alícuotas admitidas (RF-27: un cambio normativo toca este módulo). Los usa el cliente
/// de WSFEv1 (spec FEAT-007, Block 6); el simulador no los necesita.
/// </summary>
public static class CodigosWsfe
{
    /// <summary><c>DocTipo</c> del receptor sin identificar (FR-10): 99, "Sin identificar / venta global diaria".</summary>
    public const int DocTipoSinIdentificar = 99;

    /// <summary><c>DocNro</c> del receptor sin identificar (FR-10).</summary>
    public const long DocNroSinIdentificar = 0;

    /// <summary><c>DocTipo</c> del receptor identificado con DNI (FR-11).</summary>
    public const int DocTipoDni = 96;

    /// <summary><c>CondicionIVAReceptorId</c> de Consumidor Final.</summary>
    public const int CondicionIvaConsumidorFinal = 5;

    /// <summary><c>MonId</c> de pesos argentinos.</summary>
    public const string MonedaPesos = "PES";

    /// <summary><c>MonCotiz</c> de pesos argentinos.</summary>
    public const int CotizacionPesos = 1;

    /// <summary><c>CbteTipo</c>: Factura B 6, Factura C 11.</summary>
    /// <exception cref="ArgumentOutOfRangeException">El tipo no está definido.</exception>
    public static int Tipo(TipoComprobante tipo) => tipo switch
    {
        TipoComprobante.FacturaB => 6,
        TipoComprobante.FacturaC => 11,
        _ => throw new ArgumentOutOfRangeException(nameof(tipo), "El tipo de comprobante no es válido."),
    };

    /// <summary><c>Concepto</c>: Productos 1, Productos y servicios 3 (FR-08).</summary>
    /// <exception cref="ArgumentOutOfRangeException">El concepto no está definido.</exception>
    public static int Concepto(ConceptoFactura concepto) => concepto switch
    {
        ConceptoFactura.Productos => 1,
        ConceptoFactura.ProductosYServicios => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(concepto), "El concepto de la factura no es válido."),
    };

    /// <summary>
    /// <c>AlicIva/Id</c> de la alícuota en porcentaje: 0 → 3, 2,5 → 9, 5 → 8, 10,5 → 4, 21 → 5, 27 → 6. Compara el
    /// valor, no la escala (21,00 es 5).
    /// </summary>
    /// <exception cref="ArgumentException">La alícuota no es una de las 6 de ARCA.</exception>
    public static int Alicuota(decimal alicuota) => alicuota switch
    {
        0m => 3,
        2.5m => 9,
        5m => 8,
        10.5m => 4,
        21m => 5,
        27m => 6,
        _ => throw new ArgumentException("La alícuota de IVA no es una de las de ARCA.", nameof(alicuota)),
    };
}
