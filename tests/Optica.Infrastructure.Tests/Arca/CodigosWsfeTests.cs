using Optica.Domain.Configuracion;
using Optica.Domain.Facturacion;
using Optica.Infrastructure.Arca;

namespace Optica.Infrastructure.Tests.Arca;

/// <summary>D-20: los códigos de WSFEv1 que usan el cliente y el simulador.</summary>
public sealed class CodigosWsfeTests
{
    [Fact]
    public void Codigos_ValoresDeD20()
    {
        Assert.Equal(6, CodigosWsfe.Tipo(TipoComprobante.FacturaB));
        Assert.Equal(11, CodigosWsfe.Tipo(TipoComprobante.FacturaC));
        Assert.Equal(99, CodigosWsfe.DocTipoSinIdentificar);
        Assert.Equal(0, CodigosWsfe.DocNroSinIdentificar);
        Assert.Equal(96, CodigosWsfe.DocTipoDni);
        Assert.Equal(5, CodigosWsfe.CondicionIvaConsumidorFinal);
        Assert.Equal(1, CodigosWsfe.Concepto(ConceptoFactura.Productos));
        Assert.Equal(3, CodigosWsfe.Concepto(ConceptoFactura.ProductosYServicios));
        Assert.Equal("PES", CodigosWsfe.MonedaPesos);
        Assert.Equal(1, CodigosWsfe.CotizacionPesos);
    }

    [Theory]
    [InlineData("0", 3)]
    [InlineData("2.5", 9)]
    [InlineData("5", 8)]
    [InlineData("10.5", 4)]
    [InlineData("21", 5)]
    [InlineData("21.00", 5)]
    [InlineData("27", 6)]
    public void Alicuota_LasSeisDeArca(string alicuota, int codigo)
    {
        Assert.Equal(codigo, CodigosWsfe.Alicuota(decimal.Parse(alicuota, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void Alicuota_CubreTodasLasAdmitidas()
    {
        Assert.Equal(
            AlicuotasAdmitidas.Todas.Count,
            AlicuotasAdmitidas.Todas.Select(CodigosWsfe.Alicuota).Distinct().Count());
    }

    [Theory]
    [InlineData("19")]
    [InlineData("10")]
    [InlineData("100")]
    public void Alicuota_NoAdmitida_Rechazada(string alicuota)
    {
        Assert.Throws<ArgumentException>(
            () => CodigosWsfe.Alicuota(decimal.Parse(alicuota, System.Globalization.CultureInfo.InvariantCulture)));
    }
}
