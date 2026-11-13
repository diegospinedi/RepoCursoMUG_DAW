using System.Globalization;

namespace Optica.Domain.Facturacion;

/// <summary>
/// La identificación de una factura: tipo, punto de venta y número que asigna ARCA (FR-19, FR-20). Se muestra como
/// "Factura B 0003-00000125": el punto de venta con al menos 4 dígitos y el número con 8 (D-19, AC-19). No
/// lleva datos personales.
/// </summary>
public sealed class NumeroComprobante
{
    /// <exception cref="ArgumentException">
    /// El tipo no está definido, el punto de venta no está entre 1 y 99999 o el número no está entre 1 y
    /// <see cref="NumeroMaximo"/>. El mensaje nombra el campo y no incluye el valor recibido.
    /// </exception>
    public NumeroComprobante(TipoComprobante tipo, int puntoVenta, long numero)
    {
        ReglasFactura.ValidarTipo(tipo);
        ReglasFactura.ValidarPuntoVenta(puntoVenta);
        if (numero is < NumeroMinimo or > NumeroMaximo)
        {
            throw new ArgumentException("El número de comprobante no es válido.", nameof(numero));
        }

        Tipo = tipo;
        PuntoVenta = puntoVenta;
        Numero = numero;
    }

    /// <summary>El menor número de comprobante.</summary>
    public const long NumeroMinimo = 1;

    /// <summary>
    /// El mayor número de comprobante: el que entra en los 8 dígitos del formato visible "0003-00000125" (D-19, AC-19).
    /// </summary>
    public const long NumeroMaximo = 99_999_999;

    public TipoComprobante Tipo { get; }

    /// <summary>De 1 a 99999.</summary>
    public int PuntoVenta { get; }

    /// <summary>De 1 a <see cref="NumeroMaximo"/>; lo asigna ARCA.</summary>
    public long Numero { get; }

    /// <summary>"Factura B 0003-00000125".</summary>
    public override string ToString()
    {
        var letra = Tipo == TipoComprobante.FacturaB ? "B" : "C";
        return string.Create(CultureInfo.InvariantCulture, $"Factura {letra} {PuntoVenta:D4}-{Numero:D8}");
    }
}
