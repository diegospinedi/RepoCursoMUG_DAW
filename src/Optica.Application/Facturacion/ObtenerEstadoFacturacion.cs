using Optica.Domain.Facturacion;

namespace Optica.Application.Facturacion;

/// <summary>
/// El estado de facturación de un presupuesto (FR-20): el comprobante de su factura ("Factura B 0003-00000125") o null,
/// y si tiene una emisión pendiente de verificación (FR-24). No lleva datos del cliente.
/// </summary>
public sealed record EstadoFacturacion(string? Comprobante, bool EmisionPendiente);

/// <summary>
/// El estado de facturación del presupuesto <c>numero</c>, para que la Api lo sume a la respuesta del GET del presupuesto
/// (D-28). Solo lee la identificación de la factura, sin líneas ni DNI del receptor (D-44). Un número sin presupuesto
/// responde sin factura y sin pendiente: quien llama ya sabe si el presupuesto existe.
/// </summary>
public sealed class ObtenerEstadoFacturacion(IFacturaRepository facturas, IEmisionPendienteRepository pendientes)
{
    /// <remarks>
    /// Lee primero la pendiente y después la factura (INFO-1 de la auditoría): la segunda transacción de una emisión
    /// graba la factura y quita la pendiente juntas, así que si termina entre las dos lecturas se ven las dos (Facturar
    /// deshabilitado) y nunca ninguna (Facturar habilitado sobre un presupuesto ya facturado).
    /// </remarks>
    public async Task<EstadoFacturacion> EjecutarAsync(long numero, CancellationToken cancelacion = default)
    {
        var pendiente = await pendientes.ExisteAsync(numero, cancelacion);
        var comprobante = await facturas.ObtenerComprobantePorPresupuestoAsync(numero, cancelacion);
        return new EstadoFacturacion(comprobante?.ToString(), pendiente);
    }
}
