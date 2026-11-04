using System.Reflection;
using Optica.Application.Comun;

namespace Optica.Application.Tests.Comun;

/// <summary>
/// La excepción de conflicto no puede llevar la excepción de EF como causa: sus <c>Entries</c> traen el hash de la
/// contraseña y una excepción interna termina en el log. Con los constructores actuales el compilador impide pasar una
/// causa; esta prueba de regresión es la que impide que vuelva un constructor con causa, público o no.
/// </summary>
public class ConflictoAlConfirmarExceptionTests
{
    [Fact]
    public void ConflictoAlConfirmar_NoAdmiteCausa_NingunConstructorRecibeExcepcion()
    {
        var constructoresConCausa = typeof(ConflictoAlConfirmarException)
            .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(c => c.GetParameters().Any(p => typeof(Exception).IsAssignableFrom(p.ParameterType)))
            .Select(c => c.ToString())
            .ToList();

        Assert.Empty(constructoresConCausa);

        var conflicto = new ConflictoAlConfirmarException("conflicto");
        Assert.Null(conflicto.InnerException);
    }
}
