using Optica.Application.Comun;
using Optica.Domain.Acceso;

namespace Optica.Application.Acceso;

/// <summary>
/// Traduce la política de contraseñas del dominio a un <see cref="ErrorValidacion"/> con el campo y la acción que lo
/// corrige. La usan los casos de uso que reciben una contraseña; la Api solo traduce el error que ellos devuelven.
/// </summary>
public static class ValidacionContrasena
{
    public const string Campo = "contrasena";
    public const string AccionMuyCorta = "Ingresá una contraseña de 8 caracteres o más";
    public const string AccionMuyLarga = "Ingresá una contraseña de 128 caracteres o menos";
    public const string AccionCaracteresInvalidos = "La contraseña tiene caracteres no válidos. Escribila de nuevo.";

    /// <summary>
    /// Evalúa la política sobre la forma NFC. Una contraseña ausente cuenta como muy corta.
    /// </summary>
    /// <param name="normalizada">La forma NFC, a usar para calcular o verificar el hash; vacía si hay error.</param>
    /// <returns>El error a informar, o null si la contraseña cumple la política.</returns>
    public static ErrorValidacion? Validar(string? contrasena, out string normalizada) =>
        PoliticaContrasena.Evaluar(contrasena, out normalizada) switch
        {
            InfraccionContrasena.Ninguna => null,
            InfraccionContrasena.MuyLarga => new ErrorValidacion(Campo, AccionMuyLarga),
            InfraccionContrasena.CaracteresInvalidos => new ErrorValidacion(Campo, AccionCaracteresInvalidos),
            _ => new ErrorValidacion(Campo, AccionMuyCorta),
        };
}
