using Optica.Domain.Facturacion;

namespace Optica.Domain.Tests.Facturacion;

/// <summary>
/// Las reglas del comprobante impreso (FR-06, FR-14, FR-15 de FEAT-009): la condición frente al IVA del emisor sale del
/// tipo de comprobante (D-07) y solo Produccion tiene validez fiscal.
/// </summary>
public sealed class ReglasComprobanteImpresoTests
{
    [Fact]
    public void ReglasComprobanteImpreso_CondicionYValidezFiscal()
    {
        // AC-07 y AC-27: la condición sale del tipo y no del parámetro vigente (D-07).
        Assert.Equal("Responsable Inscripto", ReglasComprobanteImpreso.CondicionIvaEmisor(TipoComprobante.FacturaB));
        Assert.Equal("Responsable Monotributo", ReglasComprobanteImpreso.CondicionIvaEmisor(TipoComprobante.FacturaC));
        var tipoInvalido = Assert.Throws<ArgumentException>(
            () => ReglasComprobanteImpreso.CondicionIvaEmisor((TipoComprobante)99));
        Assert.Equal("tipo", tipoInvalido.ParamName);

        // FR-14 y FR-15: Simulado y Homologacion no tienen validez fiscal; Produccion sí.
        Assert.False(ReglasComprobanteImpreso.TieneValidezFiscal(EntornoArca.Simulado));
        Assert.False(ReglasComprobanteImpreso.TieneValidezFiscal(EntornoArca.Homologacion));
        Assert.True(ReglasComprobanteImpreso.TieneValidezFiscal(EntornoArca.Produccion));

        // Un entorno que no existe no tiene validez fiscal: la leyenda queda puesta (R-05).
        Assert.False(ReglasComprobanteImpreso.TieneValidezFiscal((EntornoArca)99));
    }
}
