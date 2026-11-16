using Optica.Domain.Facturacion;
using Optica.Domain.Presupuestos;

namespace Optica.Application.Facturacion;

/// <summary>
/// Una línea del detalle de la factura (FR-24): descripción, cantidad e importe (el Precio Final de la línea, como en el
/// PDF). No lleva datos del cliente.
/// </summary>
public sealed record LineaFacturaDetalle(string Descripcion, int Cantidad, decimal Importe);

/// <summary>
/// El detalle de la factura de un presupuesto (FR-24 de FEAT-009), como lo responde la Api: el comprobante como texto
/// ("Factura B 0003-00000125", sin punto de venta como dato aparte, D-15), la fecha, el número del presupuesto de origen,
/// Apellido, Nombre y DNI del cliente del presupuesto (FR-25, D-03, también si se emitió sin identificar al receptor), el
/// receptor tal como lo dice la factura (<see cref="ReglasComprobanteImpreso.TextoReceptor"/>: "Consumidor Final" o
/// "DNI 00123456"), las líneas, el total,
/// el IVA contenido (null en la Factura C), el CAE con su vencimiento y si es simulada (no se emitió en Produccion).
/// </summary>
/// <remarks>
/// Lleva datos personales (Ley 25.326): <see cref="object.ToString"/> no muestra ningún valor (D-17 de FEAT-003a).
/// </remarks>
public sealed record FacturaDetalle(
    string Comprobante,
    DateOnly Fecha,
    long Presupuesto,
    string Apellido,
    string Nombre,
    string Dni,
    string Receptor,
    IReadOnlyList<LineaFacturaDetalle> Lineas,
    decimal Total,
    decimal? IvaContenido,
    string Cae,
    DateOnly VencimientoCae,
    bool Simulada)
{
    private bool PrintMembers(System.Text.StringBuilder constructor) => false;

    /// <summary>El detalle de <paramref name="factura"/>, con el cliente de <paramref name="presupuesto"/>.</summary>
    internal static FacturaDetalle Desde(Factura factura, Presupuesto presupuesto)
    {
        var cliente = presupuesto.Cliente;
        return new FacturaDetalle(
            factura.Comprobante.ToString(),
            factura.Fecha,
            factura.PresupuestoNumero,
            cliente.Apellido,
            cliente.Nombre,
            cliente.Dni,
            ReglasComprobanteImpreso.TextoReceptor(factura.Receptor),
            [.. factura.Lineas.Select(linea => new LineaFacturaDetalle(linea.Descripcion, linea.Cantidad, linea.PrecioFinal))],
            factura.Importes.Total,
            ReglasComprobanteImpreso.LlevaIvaContenido(factura.Tipo) ? factura.Importes.Iva : null,
            factura.Cae,
            factura.VencimientoCae,
            !ReglasComprobanteImpreso.TieneValidezFiscal(factura.Entorno));
    }
}

/// <summary>
/// El detalle de la factura emitida desde el presupuesto <c>numero</c> (FR-24, FR-25 de FEAT-009, D-14): lee el
/// presupuesto (inexistente, null) y su factura con las líneas por
/// <see cref="IFacturaRepository.ObtenerPorPresupuestoAsync"/> (sin factura, null). Solo lectura: no hay acciones de
/// edición ni de eliminación (FR-24, RF-32).
/// </summary>
/// <remarks>
/// No abre una transacción de escritura y no recibe un registrador: no registra los datos del cliente (R-02). Las fallas
/// de los repositorios se propagan; la Api responde el 500 genérico.
/// </remarks>
public sealed class ObtenerFactura(IFacturaRepository facturas, IPresupuestoRepository presupuestos)
{
    public async Task<FacturaDetalle?> EjecutarAsync(long numero, CancellationToken cancelacion = default)
    {
        if (await presupuestos.ObtenerAsync(numero, cancelacion) is not { } presupuesto)
        {
            return null;
        }

        var factura = await facturas.ObtenerPorPresupuestoAsync(numero, cancelacion);
        return factura is null ? null : FacturaDetalle.Desde(factura, presupuesto);
    }
}
