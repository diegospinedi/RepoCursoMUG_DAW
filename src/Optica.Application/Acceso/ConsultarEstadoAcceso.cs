using Optica.Domain.Acceso;

namespace Optica.Application.Acceso;

/// <summary>Estado del acceso: si ya hay una contraseña definida.</summary>
public sealed record EstadoAcceso(bool ContrasenaDefinida);

/// <summary>Informa si ya hay una contraseña definida.</summary>
public sealed class ConsultarEstadoAcceso(ICredencialAccesoRepository credenciales)
{
    public async Task<EstadoAcceso> EjecutarAsync(CancellationToken cancelacion = default) =>
        new(await credenciales.ExisteAsync(cancelacion));
}
