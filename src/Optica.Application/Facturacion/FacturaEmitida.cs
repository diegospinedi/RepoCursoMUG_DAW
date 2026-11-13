namespace Optica.Application.Facturacion;

/// <summary>
/// La factura recién autorizada, como la responde la Api (FR-19): el comprobante ("Factura B 0003-00000125"), el tipo
/// en su texto (<see cref="TipoB"/> o <see cref="TipoC"/>), el número que asignó ARCA, el CAE y su vencimiento, la fecha
/// del comprobante y los importes (en la C el neto es el total y el IVA 0). No lleva el receptor ni ningún dato del
/// cliente (R-07): la pantalla no los necesita.
/// </summary>
public sealed record FacturaEmitida(
    string Comprobante,
    string Tipo,
    long Numero,
    string Cae,
    DateOnly VencimientoCae,
    DateOnly Fecha,
    decimal Total,
    decimal Neto,
    decimal Iva)
{
    /// <summary>El texto de la Factura B en la respuesta.</summary>
    public const string TipoB = "b";

    /// <summary>El texto de la Factura C en la respuesta.</summary>
    public const string TipoC = "c";
}
