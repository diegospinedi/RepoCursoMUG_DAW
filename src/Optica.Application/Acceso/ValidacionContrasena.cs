using Optica.Application.Comun;
using Optica.Domain.Acceso;

namespace Optica.Application.Acceso;

/// <summary>
/// Traduce la política de contraseñas del dominio a un <see cref="ErrorValidacion"/> con el campo y la acción que lo
/// corrige. La usan los casos de uso que reciben una contraseña; la Api solo traduce el error que ellos devuelven.
/// </summary>
public static class ValidacionContrasena
{
    /// <summary>Campo de la contraseña al definirla o al ingresar.</summary>
    public const string Campo = "contrasena";

    /// <summary>Campo de la contraseña actual al cambiarla.</summary>
    public const string CampoActual = "contrasenaActual";

    /// <summary>Campo de la contraseña nueva al cambiarla.</summary>
    public const string CampoNueva = "contrasenaNueva";

    public const string AccionMuyCorta = "Ingresá una contraseña de 8 caracteres o más";
    public const string AccionMuyLarga = "Ingresá una contraseña de 128 caracteres o menos";
    public const string AccionCaracteresInvalidos = "La contraseña tiene caracteres no válidos. Escribila de nuevo.";

    /// <summary>
    /// Evalúa la política sobre la forma NFC. Una contraseña ausente cuenta como muy corta.
    /// </summary>
    /// <param name="normalizada">La forma NFC, a usar para calcular o verificar el hash; vacía si hay error.</param>
    /// <param name="campo">
    /// Campo en el que se informa el error: <see cref="Campo"/> (por defecto), <see cref="CampoActual"/> o
    /// <see cref="CampoNueva"/>.
    /// </param>
    /// <returns>El error a informar, o null si la contraseña cumple la política.</returns>
    /// <exception cref="ArgumentException">El campo es nulo o está vacío.</exception>
    public static ErrorValidacion? Validar(string? contrasena, out string normalizada, string campo = Campo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(campo);
        return PoliticaContrasena.Evaluar(contrasena, out normalizada) switch
        {
            InfraccionContrasena.Ninguna => null,
            InfraccionContrasena.MuyLarga => new ErrorValidacion(campo, AccionMuyLarga),
            InfraccionContrasena.CaracteresInvalidos => new ErrorValidacion(campo, AccionCaracteresInvalidos),
            _ => new ErrorValidacion(campo, AccionMuyCorta),
        };
    }
}
