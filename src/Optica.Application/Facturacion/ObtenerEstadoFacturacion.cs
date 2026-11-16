using Optica.Domain.Facturacion;

namespace Optica.Application.Facturacion;

/// <summary>
/// El estado de facturación de un presupuesto (FR-20): el comprobante de su factura ("Factura B 0003-00000125") o null,
/// si tiene una emisión pendiente de verificación (FR-24) y, desde FEAT-008, la observación vigente de esa pendiente o null
/// (FR-08, FR-14, FR-15). No lleva datos del cliente.
/// </summary>
public sealed record EstadoFacturacion(string? Comprobante, bool EmisionPendiente, DetalleObservacion? Observacion);

/// <summary>
/// El estado de facturación del presupuesto <c>numero</c>, para que la Api lo sume a la respuesta del GET del presupuesto
/// (D-28). Solo lee la identificación de la factura, sin líneas ni DNI del receptor (D-44), y la observación vigente, que
/// no lleva datos del cliente. Un número sin presupuesto responde sin factura, sin pendiente y sin observación: quien
/// llama ya sabe si el presupuesto existe.
/// </summary>
public sealed class ObtenerEstadoFacturacion(
    IFacturaRepository facturas, IEmisionPendienteRepository pendientes, IObservacionEmisionRepository observaciones)
{
    /// <remarks>
    /// Lee primero la pendiente y después la factura (INFO-1 de la auditoría): la segunda transacción de una emisión
    /// graba la factura y quita la pendiente juntas, así que si termina entre las dos lecturas se ven las dos (Facturar
    /// deshabilitado) y nunca ninguna (Facturar habilitado sobre un presupuesto ya facturado). Solo con pendiente lee la
    /// observación (no existe sin ella), y solo si la observación tiene número relee la pendiente para el tipo y el punto
    /// de venta del comprobante.
    /// </remarks>
    public async Task<EstadoFacturacion> EjecutarAsync(long numero, CancellationToken cancelacion = default)
    {
        var tienePendiente = await pendientes.ExisteAsync(numero, cancelacion);
        var observacion = tienePendiente ? await LeerObservacionAsync(numero, cancelacion) : null;
        var comprobante = await facturas.ObtenerComprobantePorPresupuestoAsync(numero, cancelacion);
        return new EstadoFacturacion(comprobante?.ToString(), tienePendiente, observacion);
    }

    private async Task<DetalleObservacion?> LeerObservacionAsync(long numero, CancellationToken cancelacion)
    {
        if (await observaciones.ObtenerObservacionAsync(numero, cancelacion) is not { } observacion)
        {
            return null;
        }

        if (observacion.Numero is null)
        {
            return DetalleObservacion.Desde(observacion, null);
        }

        // Si la pendiente se quitó entre las lecturas (verificada o descartada), la observación ya no está vigente.
        var pendiente = await pendientes.ObtenerPendienteAsync(numero, cancelacion);
        return pendiente is null ? null : DetalleObservacion.Desde(observacion, pendiente);
    }
}
