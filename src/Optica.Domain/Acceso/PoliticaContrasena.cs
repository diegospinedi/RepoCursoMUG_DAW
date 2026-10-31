using System.Text;

namespace Optica.Domain.Acceso;

/// <summary>Qué regla de la política de contraseñas incumple un valor.</summary>
public enum InfraccionContrasena
{
    Ninguna,
    MuyCorta,
    MuyLarga,

    /// <summary>El texto no es Unicode válido (por ejemplo, un sustituto UTF-16 suelto).</summary>
    CaracteresInvalidos,
}

/// <summary>
/// Política de la contraseña: Unicode válido y, en su forma NFC, de <see cref="Minimo"/> a <see cref="Maximo"/>
/// unidades UTF-16, contadas sin recortar espacios.
/// </summary>
public static class PoliticaContrasena
{
    public const int Minimo = 8;
    public const int Maximo = 128;

    /// <summary>
    /// Largo crudo a partir del cual se responde <see cref="InfraccionContrasena.MuyLarga"/> sin recorrer ni
    /// normalizar el texto, para acotar el costo de un valor enorme. NFC puede acortar un texto (por ejemplo,
    /// tres jamos hangul forman una sílaba), así que el pretope deja margen sobre <see cref="Maximo"/>.
    /// </summary>
    public const int PreTope = 4 * Maximo;

    /// <summary>
    /// Evalúa la política. En orden: ausente → <see cref="InfraccionContrasena.MuyCorta"/>; largo crudo mayor que
    /// <see cref="PreTope"/> → <see cref="InfraccionContrasena.MuyLarga"/>; Unicode inválido →
    /// <see cref="InfraccionContrasena.CaracteresInvalidos"/>; después, el largo de la forma NFC. Nunca lanza.
    /// </summary>
    /// <param name="normalizada">
    /// La forma NFC, a usar para calcular o verificar el hash, si no hay infracción; vacía si la hay.
    /// </param>
    public static InfraccionContrasena Evaluar(string? contrasena, out string normalizada)
    {
        normalizada = string.Empty;
        if (contrasena is null)
        {
            return InfraccionContrasena.MuyCorta;
        }

        if (contrasena.Length > PreTope)
        {
            return InfraccionContrasena.MuyLarga;
        }

        if (!TryNormalizar(contrasena, out var nfc))
        {
            return InfraccionContrasena.CaracteresInvalidos;
        }

        if (nfc.Length < Minimo)
        {
            return InfraccionContrasena.MuyCorta;
        }

        if (nfc.Length > Maximo)
        {
            return InfraccionContrasena.MuyLarga;
        }

        normalizada = nfc;
        return InfraccionContrasena.Ninguna;
    }

    public static InfraccionContrasena Evaluar(string? contrasena) => Evaluar(contrasena, out _);

    public static bool EsValida(string? contrasena) => Evaluar(contrasena) == InfraccionContrasena.Ninguna;

    /// <summary>
    /// Forma canónica de la contraseña: Unicode NFC, para que "ñ" precompuesta y "n" + tilde combinante sean la
    /// misma contraseña. Es la forma que se evalúa y la que se pasa al hasher.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// El texto no es Unicode válido. El mensaje es genérico: no lleva caracteres ni posiciones de la contraseña.
    /// </exception>
    public static string Normalizar(string contrasena)
    {
        ArgumentNullException.ThrowIfNull(contrasena);
        if (!TryNormalizar(contrasena, out var nfc))
        {
            throw new ArgumentException("La contraseña tiene caracteres no válidos.");
        }

        return nfc;
    }

    private static bool TryNormalizar(string contrasena, out string nfc)
    {
        nfc = string.Empty;
        if (!EsUtf16BienFormado(contrasena))
        {
            return false;
        }

        try
        {
            nfc = contrasena.Normalize(NormalizationForm.FormC);
            return true;
        }
        catch (ArgumentException)
        {
            // Defensa: la excepción original nombra el carácter y su posición; no se propaga.
            return false;
        }
    }

    /// <summary>Cada sustituto alto va seguido de uno bajo, y no hay sustitutos bajos sueltos.</summary>
    private static bool EsUtf16BienFormado(string texto)
    {
        for (var i = 0; i < texto.Length; i++)
        {
            if (char.IsHighSurrogate(texto[i]))
            {
                if (i + 1 >= texto.Length || !char.IsLowSurrogate(texto[i + 1]))
                {
                    return false;
                }

                i++;
            }
            else if (char.IsLowSurrogate(texto[i]))
            {
                return false;
            }
        }

        return true;
    }
}
