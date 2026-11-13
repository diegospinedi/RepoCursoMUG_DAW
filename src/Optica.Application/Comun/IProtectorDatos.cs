namespace Optica.Application.Comun;

/// <summary>
/// Cifra y descifra bytes que se guardan en el directorio de datos con las claves de protección de la aplicación (las
/// mismas, protegidas de la misma forma, que las de la sesión). Es un puerto neutro: lo implementa la Api, que no conoce
/// qué protege, y lo usa la infraestructura (por ejemplo, para el ticket de acceso de ARCA, D-30). Cada uso tiene su
/// <c>proposito</c>: lo protegido con uno no se descifra con otro.
/// </summary>
public interface IProtectorDatos
{
    /// <summary>Cifra y autentica <paramref name="datos"/> para <paramref name="proposito"/>.</summary>
    byte[] Proteger(string proposito, byte[] datos);

    /// <summary>Descifra lo que devolvió <see cref="Proteger"/> con el mismo <paramref name="proposito"/>.</summary>
    /// <exception cref="System.Security.Cryptography.CryptographicException">
    /// Los datos no se pueden descifrar: dañados, de otro propósito o protegidos con claves que ya no están.
    /// </exception>
    byte[] Desproteger(string proposito, byte[] datos);
}
