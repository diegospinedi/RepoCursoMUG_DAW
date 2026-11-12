using Optica.Domain.Presupuestos;

namespace Optica.Application.Presupuestos;

/// <summary>
/// El presupuesto grabado con ese número, con su cliente, sus líneas y el total que calcula el dominio (D-05), o null si
/// no existe (cualquier <c>long</c>). No registra en el log los datos del cliente (D-17). Las fallas del repositorio,
/// incluido un estado corrupto, se propagan.
/// </summary>
/// <remarks>
/// FEAT-003c: la versión del presupuesto (D-01) y el <c>orden</c> de cada línea (D-10) llegan por
/// <see cref="PresupuestoGrabado.Desde"/>; la pantalla los envía al modificar o pasar a Final.
/// </remarks>
public sealed class ObtenerPresupuesto(IPresupuestoRepository presupuestos)
{
    public async Task<PresupuestoGrabado?> EjecutarAsync(long numero, CancellationToken cancelacion = default)
    {
        var presupuesto = await presupuestos.ObtenerAsync(numero, cancelacion);
        return presupuesto is null ? null : PresupuestoGrabado.Desde(presupuesto);
    }
}
