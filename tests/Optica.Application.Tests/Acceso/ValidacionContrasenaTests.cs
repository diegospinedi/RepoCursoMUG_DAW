using Optica.Application.Acceso;
using Optica.Application.Comun;

namespace Optica.Application.Tests.Acceso;

public class ValidacionContrasenaTests
{
    public static TheoryData<string?, string> Invalidas => new()
    {
        { null, ValidacionContrasena.AccionMuyCorta },
        { "1234567", ValidacionContrasena.AccionMuyCorta },
        { new string('x', 129), ValidacionContrasena.AccionMuyLarga },
        { "abcdefg\uD800", ValidacionContrasena.AccionCaracteresInvalidos },
    };

    // Sin enumerar en el descubrimiento: xUnit serializa los datos y un sustituto suelto llegaría como U+FFFD.
    [Theory]
    [MemberData(nameof(Invalidas), DisableDiscoveryEnumeration = true)]
    public void ValidacionContrasena_CampoComoParametro_ErrorEnEseCampo(string? contrasena, string accion)
    {
        Assert.Equal("contrasena", ValidacionContrasena.Campo);
        Assert.Equal("contrasenaActual", ValidacionContrasena.CampoActual);
        Assert.Equal("contrasenaNueva", ValidacionContrasena.CampoNueva);

        // Con el campo indicado, el error va en ese campo, con la misma acción y sin forma normalizada.
        foreach (var campo in new[] { ValidacionContrasena.CampoActual, ValidacionContrasena.CampoNueva })
        {
            var error = ValidacionContrasena.Validar(contrasena, out var normalizada, campo);

            Assert.Equal(new ErrorValidacion(campo, accion), error);
            Assert.Equal(string.Empty, normalizada);
        }

        // Sin indicarlo, el comportamiento de FEAT-001a: el error va en "contrasena".
        Assert.Equal(new ErrorValidacion("contrasena", accion), ValidacionContrasena.Validar(contrasena, out _));
    }

    [Fact]
    public void ValidacionContrasena_ValidaConCampo_SinErrorYNormalizada()
    {
        // "é" descompuesta (e + acento combinado) se normaliza a NFC.
        var error = ValidacionContrasena.Validar("café-valido", out var normalizada, ValidacionContrasena.CampoNueva);

        Assert.Null(error);
        Assert.Equal("café-valido", normalizada);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void ValidacionContrasena_CampoVacio_Rechazado(string? campo)
    {
        Assert.ThrowsAny<ArgumentException>(() => ValidacionContrasena.Validar("clave-valida", out _, campo!));
    }
}
