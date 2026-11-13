using Optica.Domain.Comun;
using Optica.Domain.Configuracion;

namespace Optica.Domain.Facturacion;

/// <summary>
/// Los importes de una factura, con 2 decimales (NFR-04): total, neto, IVA y alícuota. En la Factura B el neto es el
/// total sin IVA y el IVA la diferencia (FR-14); en la C no hay desglose: el neto es el total, el IVA 0 y no hay
/// alícuota (FR-16, D-20). Siempre neto + IVA = total, todos entre 0 y <see cref="ReglasImporte.ImporteMaximo"/>.
/// </summary>
public sealed class ImportesFactura
{
    /// <summary>Reconstruye los importes de una factura guardada; el alta usa <see cref="ParaFacturaB"/> o <see cref="ParaFacturaC"/>.</summary>
    /// <param name="alicuota">Una de <see cref="AlicuotasAdmitidas"/> en la Factura B; null en la C.</param>
    /// <exception cref="ArgumentException">
    /// Algún importe es negativo, tiene más de 2 decimales o supera <see cref="ReglasImporte.ImporteMaximo"/>; neto + IVA
    /// no es el total; la alícuota no es de ARCA; con alícuota, el neto no es el de la fórmula de FR-14; o, sin
    /// alícuota, hay IVA. El mensaje nombra el campo y no incluye el valor recibido.
    /// </exception>
    public ImportesFactura(decimal total, decimal neto, decimal iva, decimal? alicuota)
    {
        ValidarImporte(total, nameof(total), "El total de la factura no es válido.");
        ValidarImporte(neto, nameof(neto), "El neto de la factura no es válido.");
        ValidarImporte(iva, nameof(iva), "El IVA de la factura no es válido.");

        if (neto + iva != total)
        {
            throw new ArgumentException("El neto y el IVA de la factura no suman el total.", nameof(iva));
        }

        if (alicuota is { } valor)
        {
            if (!AlicuotasAdmitidas.EsAdmitida(valor))
            {
                throw new ArgumentException("La alícuota de IVA de la factura no es válida.", nameof(alicuota));
            }

            // WARN-2 de la revisión: un neto que suma bien pero no sale de la fórmula de FR-14 no es de esta alícuota.
            if (neto != CalcularNeto(total, valor))
            {
                throw new ArgumentException("Los importes de la factura no corresponden a la alícuota.", nameof(neto));
            }
        }

        if (alicuota is null && iva != 0m)
        {
            throw new ArgumentException("Una factura sin desglose de IVA no lleva IVA.", nameof(iva));
        }

        Total = total;
        Neto = neto;
        Iva = iva;
        Alicuota = alicuota;
    }

    /// <summary>El total facturado: el del presupuesto (FR-04).</summary>
    public decimal Total { get; }

    /// <summary>En la B, el total sin IVA redondeado a 2 decimales; en la C, el total.</summary>
    public decimal Neto { get; }

    /// <summary>En la B, el total menos el neto; en la C, 0.</summary>
    public decimal Iva { get; }

    /// <summary>La alícuota de IVA de la Factura B, una de <see cref="AlicuotasAdmitidas"/>; null en la C.</summary>
    public decimal? Alicuota { get; }

    /// <summary>Verdadero si los importes discriminan neto e IVA (Factura B).</summary>
    public bool DiscriminaIva => Alicuota is not null;

    /// <summary>
    /// Los importes de una Factura B (FR-14): neto = total / (1 + alícuota / 100) redondeado a 2 decimales con mitad
    /// hacia arriba (D-22) e IVA = total − neto.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// El total es negativo, tiene más de 2 decimales o supera <see cref="ReglasImporte.ImporteMaximo"/>, o la alícuota
    /// no es una de <see cref="AlicuotasAdmitidas"/>. El mensaje nombra el campo y no incluye el valor recibido.
    /// </exception>
    public static ImportesFactura ParaFacturaB(decimal total, decimal alicuota)
    {
        ValidarImporte(total, nameof(total), "El total de la factura no es válido.");
        if (!AlicuotasAdmitidas.EsAdmitida(alicuota))
        {
            throw new ArgumentException("La alícuota de IVA de la factura no es válida.", nameof(alicuota));
        }

        var neto = CalcularNeto(total, alicuota);
        return new ImportesFactura(total, neto, total - neto, alicuota);
    }

    /// <summary>
    /// El neto de FR-14: total / (1 + alícuota / 100), redondeado a 2 decimales con mitad hacia arriba (D-22). Recibe un
    /// total y una alícuota ya validados por quien llama.
    /// </summary>
    private static decimal CalcularNeto(decimal total, decimal alicuota) =>
        // Con alícuota de 0 a 27 el divisor está entre 1 y 1,27: el neto no supera el total y no desborda.
        Math.Round(total / (1m + (alicuota / 100m)), 2, MidpointRounding.AwayFromZero);

    /// <summary>Los importes de una Factura C (FR-16): solo el total, sin desglose de neto e IVA.</summary>
    /// <exception cref="ArgumentException">
    /// El total es negativo, tiene más de 2 decimales o supera <see cref="ReglasImporte.ImporteMaximo"/>. El mensaje no
    /// incluye el valor recibido.
    /// </exception>
    public static ImportesFactura ParaFacturaC(decimal total) => new(total, total, 0m, null);

    private static void ValidarImporte(decimal importe, string campo, string mensaje)
    {
        if (importe < 0m || importe > ReglasImporte.ImporteMaximo || ReglasImporte.TieneMasDeDosDecimales(importe))
        {
            throw new ArgumentException(mensaje, campo);
        }
    }
}
