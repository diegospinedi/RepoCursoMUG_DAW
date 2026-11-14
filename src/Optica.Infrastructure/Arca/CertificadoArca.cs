using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Optica.Infrastructure.Arca;

/// <summary>
/// Carga el certificado de la empresa para ARCA desde el PFX de <c>Arca:Certificado:Ruta</c> con su clave
/// (<c>Arca:Certificado:Clave</c>, D-13). La clave privada queda solo en memoria (sin guardarla en el almacén de la PC
/// cuando el sistema lo permite) y nunca sale del proceso (R-01). Ni la clave ni la ruta van a un mensaje: una falla
/// lanza <see cref="CertificadoArcaException"/> con un texto fijo y sin la excepción original.
/// </summary>
internal static class CertificadoArca
{
    /// <exception cref="CertificadoArcaException">
    /// El archivo no existe, no se puede leer, no es un PFX, la clave es incorrecta o no trae clave privada RSA.
    /// </exception>
    public static X509Certificate2 Cargar(string ruta, string clave)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruta);
        ArgumentNullException.ThrowIfNull(clave);

        // macOS no admite claves efímeras; ahí se usa el conjunto por defecto (solo desarrollo).
        var opciones = OperatingSystem.IsMacOS() ? X509KeyStorageFlags.DefaultKeySet : X509KeyStorageFlags.EphemeralKeySet;
        X509Certificate2 certificado;
        try
        {
            certificado = X509CertificateLoader.LoadPkcs12FromFile(ruta, clave, opciones);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new CertificadoArcaException("el archivo no existe");
        }
        catch (UnauthorizedAccessException)
        {
            throw new CertificadoArcaException("no hay permiso para leer el archivo");
        }
        catch (IOException)
        {
            throw new CertificadoArcaException("no se pudo leer el archivo");
        }
        catch (CryptographicException)
        {
            throw new CertificadoArcaException("la clave es incorrecta o el archivo no es un PFX");
        }

        if (!certificado.HasPrivateKey)
        {
            certificado.Dispose();
            throw new CertificadoArcaException("el archivo no trae la clave privada");
        }

        return certificado;
    }
}

/// <summary>
/// El certificado de ARCA no se pudo cargar. <see cref="Exception.Message"/> es un texto fijo con la causa general,
/// sin la ruta ni la clave, y no lleva la excepción original (su texto puede traer la ruta con el usuario de la PC).
/// </summary>
internal sealed class CertificadoArcaException : Exception
{
    public CertificadoArcaException(string causa)
        : base(causa)
    {
    }
}
