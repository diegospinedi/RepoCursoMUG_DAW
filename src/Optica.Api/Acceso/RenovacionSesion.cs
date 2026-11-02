using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Optica.Api.Acceso;

/// <summary>
/// Vencimiento de la sesión por inactividad (FR-04, NFR-02): la cookie dura <see cref="Duracion"/> y se renueva en
/// cada pedido autenticado, así vence a los 60 minutos del último pedido. La renovación propia del framework
/// (<c>SlidingExpiration</c>) solo renueva pasada la mitad del plazo, por eso se renueva acá en cada pedido.
/// </summary>
public static class RenovacionSesion
{
    public static readonly TimeSpan Duracion = TimeSpan.FromMinutes(60);

    /// <summary>
    /// Se engancha en <c>OnValidatePrincipal</c>. Rechaza la sesión que llegó a su vencimiento (el framework la
    /// acepta todavía en el instante exacto del vencimiento) y renueva la vigente, con el reloj de la aplicación.
    /// </summary>
    public static async Task AlValidarAsync(CookieValidatePrincipalContext contexto)
    {
        var ahora = (contexto.Options.TimeProvider ?? TimeProvider.System).GetUtcNow();
        if (contexto.Properties.ExpiresUtc is not { } vence || vence <= ahora)
        {
            contexto.RejectPrincipal();
            await contexto.HttpContext.SignOutAsync(contexto.Scheme.Name);
            return;
        }

        contexto.ShouldRenew = true;
    }
}
