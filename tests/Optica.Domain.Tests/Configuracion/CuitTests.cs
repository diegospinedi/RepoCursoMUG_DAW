using Optica.Domain.Configuracion;

namespace Optica.Domain.Tests.Configuracion;

/// <summary>
/// Spec FEAT-009, Block 1 (FR-03, AC-03, AC-04): el CUIT como objeto de valor, con dígito verificador de módulo 11. Los
/// CUIT de ejemplo son los del PRD (20-12345678-6 válido, 20-12345678-5 inválido).
/// </summary>
public class CuitTests
{
    [Theory]
    [InlineData("20-12345678-6")]
    [InlineData("20123456786")]
    [InlineData("2-0-1-2-3-4-5-6-7-8-6")] // guiones en cualquier posición
    [InlineData(" 20-12345678-6 ")] // recortado, como el resto de los textos del emisor
    [InlineData("\uFEFF20-12345678-6\uFEFF")] // con la marca de orden de bytes pegada desde otro programa
    public void Cuit_ConGuiones_SeGuardaSinGuiones(string texto)
    {
        var cuit = Cuit.Interpretar(texto);

        Assert.NotNull(cuit);
        Assert.Equal("20123456786", cuit.Valor);

        // El mismo CUIT escrito de dos formas es el mismo valor.
        Assert.Equal(Cuit.Interpretar("20123456786"), cuit);

        // Solo la forma sin guiones es canónica: la que se guarda y la que exige Arca:Cuit (D-18).
        Assert.True(Cuit.EsValidoCanonico(cuit.Valor));
        Assert.Equal(texto.Trim() == "20123456786", Cuit.EsValidoCanonico(texto));
    }

    [Theory]
    [InlineData("20-12345678-5")] // el del PRD (AC-04)
    [InlineData("20123456785")]
    [InlineData("20000000002")]
    [InlineData("20000000010")] // la suma da un verificador 10: ningún dígito lo representa
    public void Cuit_DigitoVerificadorInvalido_Rechazado(string texto)
    {
        Assert.Null(Cuit.Interpretar(texto));
        Assert.False(Cuit.EsValidoCanonico(texto.Replace("-", string.Empty, StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("2012345678")] // el del PRD (AC-04): 10 dígitos
    [InlineData("201234567860")] // 12 dígitos
    [InlineData("20-1234567-6")]
    [InlineData("")]
    [InlineData("---")]
    [InlineData(null)]
    [InlineData("2O123456786")] // una letra O en lugar de un cero
    [InlineData("20.12345678.6")] // otro separador
    [InlineData("20 12345678 6")] // espacios internos
    [InlineData("\uFF120123456786")] // un dígito de ancho completo, que no es ASCII
    public void Cuit_LargoInvalido_Rechazado(string? texto)
    {
        Assert.Null(Cuit.Interpretar(texto));
        Assert.False(Cuit.EsValidoCanonico(texto));
    }
}
