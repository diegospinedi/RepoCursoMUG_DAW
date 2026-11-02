namespace Optica.Application.Acceso;

/// <summary>
/// Cálculo y verificación del hash con sal de la contraseña. El llamador pasa la contraseña ya validada y
/// normalizada con <c>PoliticaContrasena.Evaluar(contrasena, out normalizada)</c> (Unicode NFC); el hasher calcula
/// el hash de lo que recibe, sin normalizar.
/// </summary>
public interface IHasherContrasena
{
    /// <summary>
    /// Calcula un hash nuevo, con sal aleatoria propia. Si la contraseña no se puede codificar, lanza una
    /// excepción con un mensaje genérico, sin caracteres ni posiciones de la contraseña.
    /// </summary>
    string Calcular(string contrasena);

    /// <summary>
    /// Indica si la contraseña corresponde al hash guardado. Un hash mal formado o una contraseña que no se puede
    /// codificar devuelven falso; nunca lanza una excepción.
    /// </summary>
    bool Verificar(string contrasena, string hash);
}
