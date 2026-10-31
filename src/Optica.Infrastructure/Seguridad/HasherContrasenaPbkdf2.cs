using System.Globalization;
using System.Security.Cryptography;
using Optica.Application.Acceso;

namespace Optica.Infrastructure.Seguridad;

/// <summary>
/// PBKDF2-HMAC-SHA256 con sal aleatoria (ADR-002). Formato guardado:
/// <c>PBKDF2-SHA256$&lt;iteraciones&gt;$&lt;sal base64&gt;$&lt;hash base64&gt;</c>. Las iteraciones quedan en el
/// formato, así que subirlas más adelante no invalida un hash existente.
/// </summary>
public sealed class HasherContrasenaPbkdf2 : IHasherContrasena
{
    public const string Algoritmo = "PBKDF2-SHA256";
    public const int Iteraciones = 600_000;
    public const int LongitudSal = 16;
    public const int LongitudHash = 32;

    /// <summary>
    /// Piso de iteraciones aceptado al verificar (las de ADR-002): un hash degradado a menos iteraciones no
    /// verifica, aunque sea correcto. Es un literal a propósito, independiente de <see cref="Iteraciones"/>: si
    /// en el futuro se sube el costo de los hashes nuevos, los guardados con 600.000 tienen que seguir
    /// verificando.
    /// </summary>
    public const int MinimoIteraciones = 600_000;

    /// <summary>
    /// Tope de iteraciones aceptado al verificar: un hash manipulado no puede imponer un costo de CPU desmedido.
    /// </summary>
    public const int MaximoIteraciones = 10_000_000;

    private const char Separador = '$';

    public string Calcular(string contrasena)
    {
        ArgumentNullException.ThrowIfNull(contrasena);
        var sal = RandomNumberGenerator.GetBytes(LongitudSal);
        byte[] hash;
        try
        {
            hash = Derivar(contrasena, sal, Iteraciones);
        }
        catch (ArgumentException)
        {
            // Incluye EncoderFallbackException (UTF-8 estricto), cuyo mensaje nombra el carácter y su posición:
            // se reemplaza por una excepción genérica, sin la original como interna.
            throw new ContrasenaNoCodificableException();
        }

        return string.Join(
            Separador,
            Algoritmo,
            Iteraciones.ToString(CultureInfo.InvariantCulture),
            Convert.ToBase64String(sal),
            Convert.ToBase64String(hash));
    }

    public bool Verificar(string contrasena, string hash)
    {
        if (contrasena is null || !TryLeer(hash, out var iteraciones, out var sal, out var esperado))
        {
            return false;
        }

        byte[] calculado;
        try
        {
            calculado = Derivar(contrasena, sal, iteraciones);
        }
        catch (ArgumentException)
        {
            // Incluye EncoderFallbackException: una contraseña que no se puede codificar nunca coincide, y la
            // excepción (que nombra el carácter y su posición) no sale de acá.
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(calculado, esperado);
    }

    private static byte[] Derivar(string contrasena, byte[] sal, int iteraciones) =>
        Rfc2898DeriveBytes.Pbkdf2(contrasena, sal, iteraciones, HashAlgorithmName.SHA256, LongitudHash);

    private static bool TryLeer(string? texto, out int iteraciones, out byte[] sal, out byte[] hash)
    {
        iteraciones = 0;
        sal = [];
        hash = [];
        if (string.IsNullOrEmpty(texto))
        {
            return false;
        }

        var partes = texto.Split(Separador);
        if (partes.Length != 4 || !string.Equals(partes[0], Algoritmo, StringComparison.Ordinal))
        {
            return false;
        }

        if (!int.TryParse(partes[1], NumberStyles.None, CultureInfo.InvariantCulture, out iteraciones)
            || iteraciones < MinimoIteraciones
            || iteraciones > MaximoIteraciones)
        {
            return false;
        }

        return TryBase64(partes[2], LongitudSal, out sal) && TryBase64(partes[3], LongitudHash, out hash);
    }

    private static bool TryBase64(string texto, int longitudEsperada, out byte[] bytes)
    {
        bytes = [];
        var destino = new byte[longitudEsperada + 3];
        if (texto.Length == 0
            || !Convert.TryFromBase64String(texto, destino, out var escritos)
            || escritos != longitudEsperada)
        {
            return false;
        }

        bytes = destino[..escritos];
        return true;
    }
}

/// <summary>
/// La contraseña no se pudo codificar para calcular su hash. El mensaje es genérico y no lleva excepción
/// interna: nunca incluye caracteres ni posiciones de la contraseña.
/// </summary>
public sealed class ContrasenaNoCodificableException : Exception
{
    public ContrasenaNoCodificableException()
        : base("La contraseña no se puede codificar para calcular su hash.")
    {
    }
}
