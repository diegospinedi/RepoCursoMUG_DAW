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
    private const string MensajePaginaInvalida = "La página pedida no es válida.";
    private const string MensajeTamanoPaginaInvalido = "El tamaño de página no es válido.";

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

    /// <summary>
    /// Una página de la búsqueda de facturas (FR-16 a FR-23 y FR-25 de FEAT-009), sin seguimiento. Junta cada factura con
    /// su presupuesto, así Apellido, Nombre y DNI son los del cliente del presupuesto también en una factura a Consumidor
    /// Final (D-03). Combina con "Y" solo los filtros cargados: <c>instr(plegar(Apellido), @apellido) &gt; 0</c>, lo
    /// mismo con Nombre, <c>instr(Dni, @dni) &gt; 0</c>, <c>instr(digitos_comprobante(PuntoVenta, Numero), @numero)
    /// &gt; 0</c> (D-27) y <c>Fecha &gt;= @desde</c> / <c>Fecha &lt;= @hasta</c>, ambos inclusive. Sin <c>LIKE</c>. Ordena
    /// por <c>Fecha</c>, <c>Numero</c> e <c>Id</c> descendentes: el Id es el desempate total, porque el número solo es
    /// único por entorno, tipo y punto de venta (D-20). <see cref="PaginaFacturas.Total"/> cuenta con el mismo filtro. Los
    /// textos viajan como parámetros y llegan ya normalizados por <see cref="FiltroFacturas"/> (R-09). Recorre la tabla
    /// completa, sin índice sobre <c>plegar</c>, como la búsqueda de presupuestos (ADR-011).
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="filtro"/> es null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="pagina"/> es menor que 1, <paramref name="tamanoPagina"/> no es positivo, o el desplazamiento de
    /// la página no entra en un <c>int</c>. No se consulta la base. El mensaje no lleva el valor.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Una fila guardada no se puede leer (una fecha que no existe, un tipo o un punto de venta inválidos): estado
    /// corrupto. El mensaje es fijo y no lleva valores.
    /// </exception>
    public async Task<PaginaFacturas> BuscarAsync(
        FiltroFacturas filtro, int pagina, int tamanoPagina, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(filtro);

        // Textos fijos, sin el valor recibido: ThrowIfLessThan y ThrowIfNegativeOrZero lo agregarían al mensaje.
        if (pagina < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(pagina), MensajePaginaInvalida);
        }

        if (tamanoPagina < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(tamanoPagina), MensajeTamanoPaginaInvalido);
        }

        var desplazamiento = (pagina - 1L) * tamanoPagina;
        if (desplazamiento > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(pagina), MensajePaginaInvalida);
        }

        var consulta = contexto.Facturas.AsNoTracking()
            .Join(
                contexto.Presupuestos.AsNoTracking(),
                factura => factura.PresupuestoNumero,
                presupuesto => presupuesto.Numero,
                (factura, presupuesto) => new { Factura = factura, Cliente = presupuesto });

        // EF Core traduce Contains sobre texto a instr(...) > 0 en SQLite, no a LIKE.
        if (filtro.Apellido is { } apellido)
        {
            consulta = consulta.Where(fila => FuncionesSqlite.Plegar(fila.Cliente.Apellido).Contains(apellido));
        }

        if (filtro.Nombre is { } nombre)
        {
            consulta = consulta.Where(fila => FuncionesSqlite.Plegar(fila.Cliente.Nombre).Contains(nombre));
        }

        if (filtro.Dni is { } dni)
        {
            consulta = consulta.Where(fila => fila.Cliente.Dni.Contains(dni));
        }

        if (filtro.Numero is { } numero)
        {
            consulta = consulta.Where(fila =>
                FuncionesSqlite.DigitosComprobante(fila.Factura.PuntoVenta, fila.Factura.Numero).Contains(numero));
        }

        if (filtro.Desde is { } desde)
        {
            consulta = consulta.Where(fila => fila.Factura.Fecha >= desde);
        }

        if (filtro.Hasta is { } hasta)
        {
            consulta = consulta.Where(fila => fila.Factura.Fecha <= hasta);
        }

        List<FilaResumen> filas;
        int total;
        try
        {
            filas = await consulta
                .OrderByDescending(fila => fila.Factura.Fecha)
                .ThenByDescending(fila => fila.Factura.Numero)
                .ThenByDescending(fila => fila.Factura.Id)
                .Skip((int)desplazamiento)
                .Take(tamanoPagina)
                .Select(fila => new FilaResumen(
                    fila.Factura.PresupuestoNumero, fila.Factura.Tipo, fila.Factura.PuntoVenta, fila.Factura.Numero,
                    fila.Factura.Fecha, fila.Cliente.Apellido, fila.Cliente.Nombre, fila.Cliente.Dni, fila.Factura.Total))
                .ToListAsync(cancelacion);
            total = await consulta.CountAsync(cancelacion);
        }
        catch (FormatException)
        {
            // Microsoft.Data.Sqlite no pudo leer una fecha guardada. Sin la causa: su mensaje repite el texto guardado.
            throw new InvalidOperationException(MensajeFilaInvalida);
        }

        // Las dos consultas no comparten transacción: una emisión entre una y otra puede sumar una factura. Math.Max solo
        // evita que PaginaFacturas lance si el total quedó por debajo de la página (como PresupuestoRepository).
        return new PaginaFacturas([.. filas.Select(AResumen)], Math.Max(total, filas.Count));
    }

    /// <summary>El resumen de una fila de la búsqueda; una fila que no cumple el dominio es estado corrupto.</summary>
    private static ResumenFactura AResumen(FilaResumen fila)
    {
        try
        {
            var comprobante = new NumeroComprobante(TipoDe(fila.Tipo), PuntoVentaDe(fila.PuntoVenta), fila.Numero);
            return new ResumenFactura(
                fila.PresupuestoNumero, comprobante, fila.Fecha, fila.Apellido, fila.Nombre, fila.Dni, fila.Total);
        }
        catch (ArgumentException error)
        {
            throw new InvalidOperationException(MensajeFilaInvalida, error);
        }
    }

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

    /// <summary>
    /// Las columnas de un resultado de la búsqueda. Una clase y no un record: no muestra los datos del cliente en
    /// <see cref="object.ToString"/>.
    /// </summary>
    private sealed class FilaResumen(
        long presupuestoNumero, string tipo, long puntoVenta, long numero, DateOnly fecha, string apellido, string nombre,
        string dni, decimal total)
    {
        public long PresupuestoNumero { get; } = presupuestoNumero;

        public string Tipo { get; } = tipo;

        public long PuntoVenta { get; } = puntoVenta;

        public long Numero { get; } = numero;

        public DateOnly Fecha { get; } = fecha;

        public string Apellido { get; } = apellido;

        public string Nombre { get; } = nombre;

        public string Dni { get; } = dni;

        public decimal Total { get; } = total;
    }
}
