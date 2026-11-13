using Microsoft.AspNetCore.DataProtection;
using Optica.Application.Comun;

namespace Optica.Api.Web;

/// <summary>
/// <see cref="IProtectorDatos"/> con la Data Protection de la Api: las mismas claves que la cookie de sesión (en el
/// directorio de datos y, en Windows, protegidas con DPAPI), con un protector por propósito. No sabe qué protege: la
/// infraestructura lo usa, por ejemplo, para el ticket de acceso de ARCA (D-30), y la Api no conoce tipos de ARCA.
/// </summary>
internal sealed class ProtectorDatos(IDataProtectionProvider proveedor) : IProtectorDatos
{
    public byte[] Proteger(string proposito, byte[] datos)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proposito);
        ArgumentNullException.ThrowIfNull(datos);
        return proveedor.CreateProtector(proposito).Protect(datos);
    }

    public byte[] Desproteger(string proposito, byte[] datos)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proposito);
        ArgumentNullException.ThrowIfNull(datos);
        return proveedor.CreateProtector(proposito).Unprotect(datos);
    }
}
