using Optica.Domain.Facturacion;

namespace Optica.Domain.Tests.Facturacion;

public class NumeroComprobanteTests
{
    [Fact]
    public void NumeroComprobante_B3_125_Formato()
    {
        var comprobante = new NumeroComprobante(TipoComprobante.FacturaB, 3, 125);

        Assert.Equal("Factura B 0003-00000125", comprobante.ToString());
        Assert.Equal(TipoComprobante.FacturaB, comprobante.Tipo);
        Assert.Equal(3, comprobante.PuntoVenta);
        Assert.Equal(125, comprobante.Numero);
    }

    [Fact]
    public void NumeroComprobante_PuntoVentaDe5Digitos_C_Formato()
    {
        // El punto de venta lleva al menos 4 dígitos (D-19): con 5 se muestran los 5.
        Assert.Equal("Factura C 12345-00000001", new NumeroComprobante(TipoComprobante.FacturaC, 12345, 1).ToString());
        Assert.Equal("Factura C 99999-99999999", new NumeroComprobante(TipoComprobante.FacturaC, 99999, 99_999_999).ToString());
    }

    [Fact]
    public void NumeroComprobante_FueraDeRango_Error()
    {
        foreach (var puntoVenta in new[] { 0, 100_000 })
        {
            var error = Assert.Throws<ArgumentException>(() => new NumeroComprobante(TipoComprobante.FacturaB, puntoVenta, 1));
            Assert.Equal("puntoVenta", error.ParamName);
            Assert.DoesNotContain("100000", error.Message, StringComparison.Ordinal);
        }

        // WSFEv1 numera de 1 a 99.999.999 (8 dígitos).
        foreach (var numero in new long[] { 0, -7, 100_000_000 })
        {
            var error = Assert.Throws<ArgumentException>(() => new NumeroComprobante(TipoComprobante.FacturaB, 3, numero));
            Assert.Equal("numero", error.ParamName);
        }

        var tipo = Assert.Throws<ArgumentException>(() => new NumeroComprobante((TipoComprobante)9, 3, 1));
        Assert.Equal("tipo", tipo.ParamName);
    }
}
