using Optica.Domain.Catalogo;

namespace Optica.Domain.Tests.Catalogo;

// Todo texto cuya forma importa (precompuesto o combinado, BOM, espacio duro) va con escapes \U de 8 dígitos, para que
// una normalización del archivo no cambie lo que se prueba.
public class PlegadoBusquedaTests
{
    [Fact]
    public void Plegar_Armazon_IgualQueArmazonSinTilde()
    {
        // AC-04: "armazon" encuentra "Armazón", precompuesta (U+00F3) o con el acento combinado (U+0301).
        Assert.Equal("armazon", PlegadoBusqueda.Plegar("Armaz\U000000F3n"));
        Assert.Equal("armazon", PlegadoBusqueda.Plegar("ARMAZO\U00000301N"));
        Assert.Equal(PlegadoBusqueda.Plegar("armazon"), PlegadoBusqueda.Plegar("Armaz\U000000F3n"));
        Assert.Equal(
            "armazon metalico", PlegadoBusqueda.Plegar("\U0000FEFF Armaz\U000000F3n met\U000000E1lico\U000000A0"));
    }

    [Fact]
    public void Plegar_Enie_NoEquivaleAEne()
    {
        // D-04: la ñ es una letra; la virgulilla sobre otra letra es un acento más.
        Assert.Equal("a\U000000F1o", PlegadoBusqueda.Plegar("A\U000000D1O"));
        Assert.NotEqual(PlegadoBusqueda.Plegar("ano"), PlegadoBusqueda.Plegar("a\U000000F1o"));
        Assert.Equal("\U000000F1andu", PlegadoBusqueda.Plegar("N\U00000303and\U000000FA"));
        Assert.Equal("sao", PlegadoBusqueda.Plegar("S\U000000E3o"));
        Assert.Equal(
            "\U00000142\U000000F8\U000000E6\U000000DF", PlegadoBusqueda.Plegar("\U00000141\U000000D8\U000000C6\U000000DF"));
    }

    [Fact]
    public void Plegar_Utf16Invalido_NoLanza()
    {
        // ADR-011: recortado, en minúsculas, sin normalizar, y nunca una excepción.
        // En un [Fact] porque el runner serializa los argumentos de [InlineData].
        Assert.Equal("a\uD800\U000000E9", PlegadoBusqueda.Plegar("A\uD800\U000000C9"));
        Assert.Equal("a\uDC00b", PlegadoBusqueda.Plegar("A\uDC00B"));
        Assert.Equal("e\U00000301\uD800", PlegadoBusqueda.Plegar(" E\U00000301\uD800 "));
    }

    [Fact]
    public void Plegar_TildeGraveYDieresis_SeIgnoran()
    {
        // D-04: todas las marcas de acento, no solo la aguda.
        Assert.Equal("cafe", PlegadoBusqueda.Plegar("cafe"));
        Assert.Equal("cafe", PlegadoBusqueda.Plegar("caf\U000000E9"));
        Assert.Equal("cafe", PlegadoBusqueda.Plegar("caf\U000000E8"));
        Assert.Equal("motorhead", PlegadoBusqueda.Plegar("Mot\U000000F6rhead"));
        Assert.Equal(
            "aeiou aeiou",
            PlegadoBusqueda.Plegar("\U000000C4\U000000CB\U000000CF\U000000D6\U000000DC \U000000E2\U000000EA\U000000EE\U000000F4\U000000FB"));
    }

    [Fact]
    public void Plegar_SoloMarcas_QuedaVacio()
    {
        // Las marcas sueltas se quitan después del recorte: lo que queda se vuelve a recortar.
        Assert.Equal(string.Empty, PlegadoBusqueda.Plegar("\U00000301"));
        Assert.Equal(string.Empty, PlegadoBusqueda.Plegar(" \U00000301\U00000308 "));
        Assert.Equal("a", PlegadoBusqueda.Plegar("a \U00000301"));
        Assert.Equal("a", PlegadoBusqueda.Plegar("\U00000301 a"));
    }

    [Theory]
    [InlineData("\U00000144", "n")]
    [InlineData("n\U00000301", "n")]
    [InlineData("n\U00000301\U00000303", "\U000000F1")]
    [InlineData("n\U00000303\U00000303", "\U000000F1")]
    [InlineData("\U000000F1\U00000303", "\U000000F1")]
    [InlineData("N\U00000303\U00000303", "\U000000F1")]
    public void Plegar_CasosRarosDeLaEnie_DaElPlegadoEsperado(string texto, string esperado)
    {
        // "ń" es una n con acento; la virgulilla después de otra marca sobre la n sigue haciendo la ñ; la segunda
        // virgulilla ya no sigue a una n y se quita.
        Assert.Equal(esperado, PlegadoBusqueda.Plegar(texto));
    }

    [Fact]
    public void ClaveOrden_Enie_EntreNyO()
    {
        // D-15: en orden binario "nube" < "ñandú" < "ojo", y la ñ va después de cualquier "n" + letra ASCII.
        var nube = PlegadoBusqueda.ClaveOrden("nube");
        var nandu = PlegadoBusqueda.ClaveOrden("\U000000D1and\U000000FA");
        var ojo = PlegadoBusqueda.ClaveOrden("Ojo");
        var nzz = PlegadoBusqueda.ClaveOrden("nzz");

        Assert.Equal("n{andu", nandu);
        Assert.True(string.CompareOrdinal(nube, nandu) < 0);
        Assert.True(string.CompareOrdinal(nzz, nandu) < 0);
        Assert.True(string.CompareOrdinal(nandu, ojo) < 0);
    }

    [Theory]
    [InlineData("\U000000D1AND\U000000DA", "n{andu")]
    [InlineData("N\U00000303AND\U000000DA", "n{andu")]
    [InlineData("A\U000000D1O", "an{o")]
    public void ClaveOrden_EnieMayusculaOCombinada_UsaLaMismaClave(string texto, string esperado)
    {
        Assert.Equal(esperado, PlegadoBusqueda.ClaveOrden(texto));
    }
}
