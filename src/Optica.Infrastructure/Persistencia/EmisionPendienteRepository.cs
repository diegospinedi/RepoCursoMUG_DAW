using Microsoft.EntityFrameworkCore;
using Optica.Domain.Facturacion;

namespace Optica.Infrastructure.Persistencia;

/// <summary>
/// Emisiones pendientes sobre la tabla <c>EmisionPendiente</c> (FEAT-007, D-09, ADR-021). Se agregan y se quitan, no se
/// modifican. <see cref="Agregar"/> y <see cref="Quitar"/> solo registran el cambio en el contexto compartido, que
/// confirma <see cref="UnidadDeTrabajoEf"/>; una segunda pendiente para el mismo presupuesto choca con la clave primaria y
/// se informa ahí como conflicto de unicidad (FR-24).
/// </summary>
/// <remarks>
/// Guarda los datos de la solicitud que verifica el PRD 07 (tipo, punto de venta, fecha, total), el momento en UTC y el
/// entorno; no guarda el DNI del receptor (Ley 25.326). No registra nada en el log. La base rechaza modificar una
/// pendiente (<c>TR_EmisionPendiente_Inmutable</c>); por eso, quitar y volver a agregar la del mismo presupuesto exige
/// confirmar la baja en el medio, y <see cref="Agregar"/> lo controla.
/// </remarks>
public sealed class EmisionPendienteRepository(OpticaDbContext contexto) : IEmisionPendienteRepository
{
    private const string MensajeQuitadaSinConfirmar =
        "La emisión pendiente de este presupuesto se quitó y la baja no se confirmó.";

    /// <exception cref="ArgumentNullException"><paramref name="pendiente"/> es null.</exception>
    /// <exception cref="InvalidOperationException">
    /// El contexto tiene la pendiente del mismo presupuesto quitada y sin confirmar: EF Core grabaría la baja y el alta
    /// como un UPDATE, que <c>TR_EmisionPendiente_Inmutable</c> rechaza. No registra nada. El mensaje es fijo.
    /// </exception>
    public void Agregar(EmisionPendiente pendiente)
    {
        ArgumentNullException.ThrowIfNull(pendiente);
        if (contexto.EmisionesPendientes.Local.FindEntry(pendiente.PresupuestoNumero) is { State: EntityState.Deleted })
        {
            throw new InvalidOperationException(MensajeQuitadaSinConfirmar);
        }

        var solicitud = pendiente.Solicitud;
        contexto.EmisionesPendientes.Add(new EmisionPendienteFila
        {
            PresupuestoNumero = pendiente.PresupuestoNumero,
            Momento = pendiente.Momento.UtcDateTime,
            Tipo = TextosFacturacion.TextoDe(solicitud.Tipo),
            PuntoVenta = solicitud.PuntoVenta,
            Fecha = solicitud.Fecha,
            Total = solicitud.Importes.Total,
            Entorno = TextosFacturacion.TextoDe(pendiente.Entorno),
        });
    }

    /// <summary>
    /// Registra la baja de la pendiente del presupuesto, si existe; si no, no registra nada (D-44). La busca con
    /// <c>Find</c>: primero entre las que sigue el contexto (una agregada y sin confirmar se descarta) y si no, en la base.
    /// Marcarla como borrada sin leerla haría fallar la confirmación por concurrencia cuando no hay fila.
    /// </summary>
    public void Quitar(long presupuestoNumero)
    {
        var fila = contexto.EmisionesPendientes.Find(presupuestoNumero);
        if (fila is not null)
        {
            contexto.EmisionesPendientes.Remove(fila);
        }
    }

    public Task<bool> ExisteAsync(long presupuestoNumero, CancellationToken cancelacion = default) =>
        contexto.EmisionesPendientes.AsNoTracking().AnyAsync(p => p.PresupuestoNumero == presupuestoNumero, cancelacion);
}
