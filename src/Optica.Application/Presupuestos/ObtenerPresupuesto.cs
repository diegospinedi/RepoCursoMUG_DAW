using Optica.Domain.Presupuestos;

namespace Optica.Application.Presupuestos;

/// <summary>
/// El presupuesto grabado con ese número, con su cliente y sus líneas, o null si no existe (cualquier <c>long</c>). No
/// registra en el log los datos del cliente (D-17). Las fallas del repositorio, incluido un estado corrupto, se propagan.
/// </summary>
public sealed class ObtenerPresupuesto(IPresupuestoRepository presupuestos)
{
    public async Task<PresupuestoGrabado?> EjecutarAsync(long numero, CancellationToken cancelacion = default)
    {
        var presupuesto = await presupuestos.ObtenerAsync(numero, cancelacion);
        return presupuesto is null ? null : PresupuestoGrabado.Desde(presupuesto);
    }
}
