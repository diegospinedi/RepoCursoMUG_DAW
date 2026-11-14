using Microsoft.EntityFrameworkCore;
using Optica.Domain.Facturacion;

namespace Optica.Infrastructure.Persistencia;

/// <summary>
/// Facturas sobre las tablas <c>Factura</c> y <c>LineaFactura</c> (FEAT-007). Solo alta y lectura: no hay métodos para
/// modificar ni borrar una factura (FR-25), y la base lo refuerza con los triggers de la migración <c>Facturas</c>.
/// Mapea filas y entidades sin aritmética: la conversión a centésimos vive en <see cref="OpticaDbContext"/> (ADR-008).
/// <see cref="Agregar"/> solo deja el alta en el contexto compartido, que confirma <see cref="UnidadDeTrabajoEf"/>; una
/// segunda factura para el mismo presupuesto, o el mismo comprobante en el mismo entorno, se informa ahí como conflicto
/// de unicidad (FR-03).
/// </summary>
/// <remarks>
/// Lleva el DNI del receptor (Ley 25.326): no registra nada en el log y sus mensajes de error son fijos, sin valores.
/// </remarks>
public sealed class FacturaRepository(OpticaDbContext contexto) : IFacturaRepository
{
    private const string MensajeFilaInvalida = "La factura guardada no es válida.";

    private const string ConceptoProductos = "productos";
    private const string ConceptoProductosYServicios = "productos_y_servicios";

    /// <summary>
    /// Registra la factura con sus líneas, con <c>Orden</c> 1..n, y <c>CantidadLineas</c>; se guardan en la misma
    /// confirmación (FR-18).
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="factura"/> es null.</exception>
    public void Agregar(Factura factura)
    {
        ArgumentNullException.ThrowIfNull(factura);

        var importes = factura.Importes;
        contexto.Facturas.Add(new FacturaFila
        {
            PresupuestoNumero = factura.PresupuestoNumero,
            Tipo = TextosFacturacion.TextoDe(factura.Tipo),
            PuntoVenta = factura.PuntoVenta,
            Numero = factura.Numero,
            Fecha = factura.Fecha,
            Concepto = TextoDe(factura.Concepto),
            ReceptorDni = factura.Receptor.Dni,
            Total = importes.Total,
            Neto = importes.Neto,
            Iva = importes.Iva,
            Alicuota = importes.Alicuota,
            Cae = factura.Cae,
            VencimientoCae = factura.VencimientoCae,
            Entorno = TextosFacturacion.TextoDe(factura.Entorno),
            CantidadLineas = factura.Lineas.Count,
            Lineas =
            [
                .. factura.Lineas.Select(linea => new LineaFacturaFila
                {
                    Orden = linea.Orden,
                    CodigoArticulo = linea.CodigoArticulo,
                    Descripcion = linea.Descripcion,
                    PrecioUnitario = linea.PrecioUnitario,
                    PrecioConDescuento = linea.PrecioConDescuento,
                    PrecioFinal = linea.PrecioFinal,
                    Cantidad = linea.Cantidad,
                    PorcentajeDescuento = linea.PorcentajeDescuento,
                }),
            ],
        });
    }

    /// <summary>Sin seguimiento, con las líneas ordenadas por <c>Orden</c>; null si el presupuesto no tiene factura.</summary>
    /// <exception cref="InvalidOperationException">
    /// La fila no cumple las reglas del dominio: estado corrupto. El mensaje es fijo y no lleva valores.
    /// </exception>
    public async Task<Factura?> ObtenerPorPresupuestoAsync(long presupuestoNumero, CancellationToken cancelacion = default)
    {
        FacturaFila? fila;
        try
        {
            fila = await contexto.Facturas.AsNoTracking()
                .Include(f => f.Lineas.OrderBy(l => l.Orden))
                .SingleOrDefaultAsync(f => f.PresupuestoNumero == presupuestoNumero, cancelacion);
        }
        catch (FormatException)
        {
            // Microsoft.Data.Sqlite no pudo leer una fecha guardada ("2026-02-30" pasa el CHECK de formato). Sin la causa:
            // su mensaje repite el texto guardado.
            throw new InvalidOperationException(MensajeFilaInvalida);
        }

        return fila is null ? null : AEntidad(fila);
    }

    /// <summary>Solo tipo, punto de venta y número, sin seguimiento; null si el presupuesto no tiene factura (FR-20).</summary>
    /// <exception cref="InvalidOperationException">La fila no cumple las reglas del dominio. El mensaje es fijo.</exception>
    public async Task<NumeroComprobante?> ObtenerComprobantePorPresupuestoAsync(
        long presupuestoNumero, CancellationToken cancelacion = default)
    {
        var fila = await contexto.Facturas.AsNoTracking()
            .Where(f => f.PresupuestoNumero == presupuestoNumero)
            .Select(f => new { f.Tipo, f.PuntoVenta, f.Numero })
            .SingleOrDefaultAsync(cancelacion);

        if (fila is null)
        {
            return null;
        }

        try
        {
            return new NumeroComprobante(TipoDe(fila.Tipo), PuntoVentaDe(fila.PuntoVenta), fila.Numero);
        }
        catch (ArgumentException error)
        {
            throw new InvalidOperationException(MensajeFilaInvalida, error);
        }
    }

    public Task<bool> ExisteParaPresupuestoAsync(long presupuestoNumero, CancellationToken cancelacion = default) =>
        contexto.Facturas.AsNoTracking().AnyAsync(f => f.PresupuestoNumero == presupuestoNumero, cancelacion);

    private static string TextoDe(ConceptoFactura concepto) => concepto switch
    {
        ConceptoFactura.Productos => ConceptoProductos,
        ConceptoFactura.ProductosYServicios => ConceptoProductosYServicios,
        _ => throw new ArgumentException("El concepto de la factura no es válido.", nameof(concepto)),
    };

    private static TipoComprobante TipoDe(string texto) =>
        TextosFacturacion.TipoDe(texto) ?? throw new InvalidOperationException(MensajeFilaInvalida);

    private static ConceptoFactura ConceptoDe(string texto) => texto switch
    {
        ConceptoProductos => ConceptoFactura.Productos,
        ConceptoProductosYServicios => ConceptoFactura.ProductosYServicios,
        _ => throw new InvalidOperationException(MensajeFilaInvalida),
    };

    private static int PuntoVentaDe(long puntoVenta) =>
        puntoVenta is < int.MinValue or > int.MaxValue
            ? throw new InvalidOperationException(MensajeFilaInvalida)
            : (int)puntoVenta;

    /// <summary>
    /// La entidad de una fila, reconstruida con los constructores del dominio. Una fila que eludió los CHECK (un texto
    /// desconocido, un DNI o un CAE inválidos, importes que no suman o que no son de la alícuota, líneas con huecos, de
    /// más o de menos) no puede representarse: se informa como estado corrupto y no con la
    /// <see cref="ArgumentException"/> del dominio, que significa entrada inválida. El mensaje no lleva valores; la causa
    /// del dominio nombra el campo y no el valor, así que se puede adjuntar.
    /// </summary>
    private static Factura AEntidad(FacturaFila fila)
    {
        var tipo = TipoDe(fila.Tipo);
        var concepto = ConceptoDe(fila.Concepto);
        var entorno = TextosFacturacion.EntornoDe(fila.Entorno) ?? throw new InvalidOperationException(MensajeFilaInvalida);
        var puntoVenta = PuntoVentaDe(fila.PuntoVenta);

        // La cantidad guardada es la de las líneas leídas, y su orden es 1..n.
        if (fila.CantidadLineas != fila.Lineas.Count)
        {
            throw new InvalidOperationException(MensajeFilaInvalida);
        }

        for (var indice = 0; indice < fila.Lineas.Count; indice++)
        {
            if (fila.Lineas[indice].Orden != indice + 1L || fila.Lineas[indice].Cantidad is < int.MinValue or > int.MaxValue)
            {
                throw new InvalidOperationException(MensajeFilaInvalida);
            }
        }

        try
        {
            var receptor = fila.ReceptorDni is { } dni ? ReceptorFactura.ConDni(dni) : ReceptorFactura.SinIdentificar;
            var importes = new ImportesFactura(fila.Total, fila.Neto, fila.Iva, fila.Alicuota);
            var lineas = fila.Lineas
                .Select(linea => new LineaFactura(
                    (int)linea.Orden, linea.CodigoArticulo, linea.Descripcion, linea.PrecioUnitario, (int)linea.Cantidad,
                    linea.PorcentajeDescuento, linea.PrecioConDescuento, linea.PrecioFinal))
                .ToList();

            return new Factura(
                fila.PresupuestoNumero, tipo, puntoVenta, fila.Numero, fila.Fecha, concepto, receptor, importes, fila.Cae,
                fila.VencimientoCae, entorno, lineas);
        }
        catch (ArgumentException error)
        {
            throw new InvalidOperationException(MensajeFilaInvalida, error);
        }
    }
}
