using Optica.Application.Comun;

namespace Optica.Api.Tests.Errores;

public sealed class RutasPrivadasTests
{
    [Fact]
    public void Ocultar_Windows_ReemplazaLaCarpetaDelUsuarioSinDistinguirMayusculas()
    {
        var texto = @"No se encontró 'c:\users\maria.gomez\AppData\Local\OpticaSistema\marca\logo.png'.";

        var limpio = RutasPrivadas.Ocultar(texto, @"C:\Users\Maria.Gomez\", windows: true);

        Assert.Equal(@"No se encontró '%USERPROFILE%\AppData\Local\OpticaSistema\marca\logo.png'.", limpio);
    }

    [Theory]
    [InlineData("en C:/Users/Maria.Gomez/AppData/x", "en %USERPROFILE%/AppData/x")]
    [InlineData("en c:/USERS/maria.gomez/AppData/x", "en %USERPROFILE%/AppData/x")]
    [InlineData(@"en C:\Users/Maria.Gomez\AppData", @"en %USERPROFILE%\AppData")]
    [InlineData("perfil: C:/Users/Maria.Gomez", "perfil: %USERPROFILE%")]
    [InlineData("en C:/Users/Maria.Gomez.", "en %USERPROFILE%.")]
    public void Ocultar_Windows_ReconoceLaCarpetaEscritaConBarras(string texto, string esperado)
    {
        Assert.Equal(esperado, RutasPrivadas.Ocultar(texto, @"C:\Users\Maria.Gomez", windows: true));
    }

    [Fact]
    public void Ocultar_OtrosSistemas_ReemplazaPorVirgulilla()
    {
        var limpio = RutasPrivadas.Ocultar("en /home/maria/datos y /home/maria/otros", "/home/maria", windows: false);

        Assert.Equal("en ~/datos y ~/otros", limpio);
    }

    [Theory]
    [InlineData("/home/mariana/datos", "/home/mariana/datos")]
    [InlineData("/home/maria_g/datos y /home/maria-g y /home/maria.g", "/home/maria_g/datos y /home/maria-g y /home/maria.g")]
    [InlineData("/home/mariana/datos y /home/maria/datos", "/home/mariana/datos y ~/datos")]
    [InlineData("'/home/maria' y /home/maria", "'~' y ~")]
    public void Ocultar_OtrosSistemas_RespetaElLimiteDelSegmento(string texto, string esperado)
    {
        Assert.Equal(esperado, RutasPrivadas.Ocultar(texto, "/home/maria", windows: false));
    }

    [Fact]
    public void Ocultar_Windows_RespetaElLimiteDelSegmento()
    {
        var texto = @"C:\Users\Maria2\datos y C:/Users/Mariana y C:\Users\Maria\datos";

        var limpio = RutasPrivadas.Ocultar(texto, @"C:\Users\Maria", windows: true);

        Assert.Equal(@"C:\Users\Maria2\datos y C:/Users/Mariana y %USERPROFILE%\datos", limpio);
    }

    [Fact]
    public void Ocultar_OtrosSistemas_DistingueMayusculasYNoTomaLaBarraInvertidaComoSeparador()
    {
        Assert.Equal("/home/Maria/datos", RutasPrivadas.Ocultar("/home/Maria/datos", "/home/maria", windows: false));
        Assert.Equal(@"\home\maria\datos", RutasPrivadas.Ocultar(@"\home\maria\datos", "/home/maria", windows: false));
    }

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    public void Ocultar_SinCarpetaDelUsuario_DejaElTextoIgual(string perfil)
    {
        Assert.Equal("/home/maria/datos", RutasPrivadas.Ocultar("/home/maria/datos", perfil, windows: false));
    }
}
