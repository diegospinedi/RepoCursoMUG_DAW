using Optica.Domain.Acceso;

namespace Optica.Domain.Tests.Acceso;

public class PoliticaContrasenaTests
{
    [Fact]
    public void PoliticaContrasena_Limites8y128()
    {
        Assert.Equal(8, PoliticaContrasena.Minimo);
        Assert.Equal(128, PoliticaContrasena.Maximo);

        // Los límites exactos son válidos.
        Assert.Equal(InfraccionContrasena.Ninguna, PoliticaContrasena.Evaluar(new string('a', 8)));
        Assert.Equal(InfraccionContrasena.Ninguna, PoliticaContrasena.Evaluar(new string('a', 128)));
        Assert.True(PoliticaContrasena.EsValida(new string('a', 8)));
        Assert.True(PoliticaContrasena.EsValida(new string('a', 128)));

        // Uno menos y uno más son inválidos, con la regla que incumplen.
        Assert.Equal(InfraccionContrasena.MuyCorta, PoliticaContrasena.Evaluar(new string('a', 7)));
        Assert.Equal(InfraccionContrasena.MuyLarga, PoliticaContrasena.Evaluar(new string('a', 129)));
        Assert.False(PoliticaContrasena.EsValida(new string('a', 7)));
        Assert.False(PoliticaContrasena.EsValida(new string('a', 129)));

        // Vacía o ausente es demasiado corta.
        Assert.Equal(InfraccionContrasena.MuyCorta, PoliticaContrasena.Evaluar(string.Empty));
        Assert.Equal(InfraccionContrasena.MuyCorta, PoliticaContrasena.Evaluar(null));

        // Los espacios cuentan y no se recortan.
        Assert.Equal(InfraccionContrasena.Ninguna, PoliticaContrasena.Evaluar(" abcdef "));
        Assert.Equal(InfraccionContrasena.Ninguna, PoliticaContrasena.Evaluar("        "));
        Assert.Equal(InfraccionContrasena.MuyCorta, PoliticaContrasena.Evaluar(" abcde "));
        Assert.Equal(InfraccionContrasena.MuyLarga, PoliticaContrasena.Evaluar(" " + new string('a', 127) + " "));
    }

    [Fact]
    public void PoliticaContrasena_NormalizaNfcAntesDeContar()
    {
        // "contrañ" en NFD: 8 unidades UTF-16 ("n" + U+0303), 7 caracteres en NFC → demasiado corta.
        var sieteEnNfd = "contra" + "ñ";
        Assert.Equal(8, sieteEnNfd.Length);
        Assert.Equal(InfraccionContrasena.MuyCorta, PoliticaContrasena.Evaluar(sieteEnNfd));
        Assert.False(PoliticaContrasena.EsValida(sieteEnNfd));

        // "ñ" + 127 letras en NFD: 129 unidades, 128 en NFC → válida.
        var cientoVeintiochoEnNfd = "ñ" + new string('a', 127);
        Assert.Equal(129, cientoVeintiochoEnNfd.Length);
        Assert.Equal(InfraccionContrasena.Ninguna, PoliticaContrasena.Evaluar(cientoVeintiochoEnNfd));

        // Normalizar devuelve la forma NFC, y es idempotente.
        var nfd = "contrase" + "ñ" + "a";
        var nfc = "contraseña";
        Assert.Equal(nfc, PoliticaContrasena.Normalizar(nfd));
        Assert.Equal(nfc, PoliticaContrasena.Normalizar(nfc));

        // Un emoji sigue contando como 2 unidades UTF-16.
        Assert.Equal(InfraccionContrasena.Ninguna, PoliticaContrasena.Evaluar("\U0001F600\U0001F600\U0001F600\U0001F600"));
        Assert.Equal(InfraccionContrasena.MuyCorta, PoliticaContrasena.Evaluar("\U0001F600\U0001F600\U0001F600a"));

        // Evaluar devuelve la forma normalizada que se usa después para el hash (una sola normalización).
        Assert.Equal(InfraccionContrasena.Ninguna, PoliticaContrasena.Evaluar(nfd, out var normalizada));
        Assert.Equal(nfc, normalizada);

        // 128 sílabas hangul escritas como jamos (L+V+T): 384 unidades crudas, 128 en NFC → válida.
        var hangulEnJamos = string.Concat(Enumerable.Repeat("각", 128));
        Assert.Equal(384, hangulEnJamos.Length);
        Assert.Equal(InfraccionContrasena.Ninguna, PoliticaContrasena.Evaluar(hangulEnJamos));
    }

    [Theory]
    [InlineData("alto-al-final")]
    [InlineData("bajo-suelto")]
    [InlineData("invertidos")]
    [InlineData("alto-al-medio")]
    public void PoliticaContrasena_UnicodeInvalido_CaracteresInvalidos(string caso)
    {
        var contrasena = caso switch
        {
            "alto-al-final" => "abcdefg\uD800",
            "bajo-suelto" => "abcdefgh\uDC00",
            "invertidos" => "abcdefgh\uDE00\uD83D",
            _ => "abcd\uD800efgh",
        };

        var excepcion = Record.Exception(() =>
            Assert.Equal(InfraccionContrasena.CaracteresInvalidos, PoliticaContrasena.Evaluar(contrasena, out _)));

        Assert.Null(excepcion);
        Assert.False(PoliticaContrasena.EsValida(contrasena));

        // Normalizar no puede devolver una forma canónica: lanza un error genérico, sin datos de la contraseña.
        var error = Assert.Throws<ArgumentException>(() => PoliticaContrasena.Normalizar(contrasena));
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("index", error.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("abcd", error.ToString(), StringComparison.Ordinal);

        // Un par sustituto bien formado (emoji) sigue siendo válido.
        Assert.Equal(InfraccionContrasena.Ninguna, PoliticaContrasena.Evaluar("abcdefg\U0001F600"));
    }

    [Fact]
    public void PoliticaContrasena_UnMillonDeCaracteres_MuyLargaSinNormalizar()
    {
        Assert.Equal(512, PoliticaContrasena.PreTope);

        var unMillon = new string('a', 1_000_000);
        Assert.Equal(InfraccionContrasena.MuyLarga, PoliticaContrasena.Evaluar(unMillon));

        // El pretope se aplica antes de mirar el contenido: un texto enorme e inválido es MuyLarga, no
        // CaracteresInvalidos, y no se normaliza (la forma devuelta queda vacía).
        Assert.Equal(InfraccionContrasena.MuyLarga, PoliticaContrasena.Evaluar(unMillon + "\uD800", out var normalizada));
        Assert.Equal(string.Empty, normalizada);
        Assert.Equal(InfraccionContrasena.MuyLarga, PoliticaContrasena.Evaluar(new string('a', 513), out _));
    }
}
