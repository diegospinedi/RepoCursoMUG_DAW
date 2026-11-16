using Optica.Application.Facturacion;
using Optica.Domain.Facturacion;
using Optica.Domain.Presupuestos;

namespace Optica.Application.Tests.Facturacion;

/// <summary>
/// Spec FEAT-009, Block 5 (FR-24, FR-25, AC-24, AC-26, D-03, D-14, D-15): el detalle de la factura de un presupuesto. El
/// Apellido, el Nombre y el DNI salen del cliente del presupuesto, también en una factura a Consumidor Final; el receptor
/// sale de la factura. Datos ficticios: "Pérez, Ana" del PRD con un DNI que empieza con 00 (D-08) y un CAE inventado.
/// </summary>
public sealed class ObtenerFacturaTests
{
    /// <summary>
    /// AC-24 y AC-26: la Factura B 0003-00000125 emitida sin identificar al receptor desde el presupuesto 155 de "Pérez,
    /// Ana" (DNI 00123456): el detalle trae el comprobante como texto, la fecha, el presupuesto 155, Apellido, Nombre y DNI
    /// del cliente del presupuesto, el receptor "Consumidor Final", las líneas, el total, el IVA contenido, el CAE con su
    /// vencimiento y si es simulada. No lleva el punto de venta como dato aparte (D-15).
    /// </summary>
    [Fact]
    public async Task ObtenerFactura_ConsumidorFinal_ClienteDelPresupuesto()
    {
        var almacen = new AlmacenFacturacion();
        var presupuesto = FacturasDePrueba.PresupuestoPerezAna(155, 1500m, 315m);
        almacen.Presupuestos.Add(presupuesto);
        almacen.Facturas.Add(FacturasDePrueba.FacturaB(presupuesto, ReceptorFactura.SinIdentificar, EntornoArca.Simulado));

        var detalle = await new ObtenerFactura(almacen, almacen).EjecutarAsync(155);

        Assert.NotNull(detalle);
        Assert.Equal("Factura B 0003-00000125", detalle.Comprobante);
        Assert.Equal(new DateOnly(2026, 10, 30), detalle.Fecha);
        Assert.Equal(155, detalle.Presupuesto);
        Assert.Equal("Pérez", detalle.Apellido);
        Assert.Equal("Ana", detalle.Nombre);
        Assert.Equal("00123456", detalle.Dni);
        Assert.Equal("Consumidor Final", detalle.Receptor);
        Assert.Equal(
            [("Artículo 1", 1, 1500m), ("Artículo 2", 1, 315m)],
            detalle.Lineas.Select(linea => (linea.Descripcion, linea.Cantidad, linea.Importe)));
        Assert.Equal(1815m, detalle.Total);
        Assert.Equal(315m, detalle.IvaContenido);
        Assert.Equal(FacturasDePrueba.Cae, detalle.Cae);
        Assert.Equal(new DateOnly(2026, 11, 9), detalle.VencimientoCae);
        Assert.True(detalle.Simulada);
        // El detalle lleva datos personales: su ToString no muestra ningún valor (Ley 25.326).
        Assert.DoesNotContain("00123456", detalle.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Pérez", detalle.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// FR-08 en el detalle: una factura emitida con DNI dice "DNI 00123456" como receptor; una Factura C de Produccion no
    /// lleva IVA contenido ni es simulada.
    /// </summary>
    [Fact]
    public async Task ObtenerFactura_ConDniYFacturaC_ReceptorEIvaContenido()
    {
        var almacen = new AlmacenFacturacion();
        var conDni = FacturasDePrueba.PresupuestoPerezAna(155, 150_000m);
        var facturaC = FacturasDePrueba.PresupuestoPerezAna(156, 2710.02m);
        almacen.Presupuestos.AddRange([conDni, facturaC]);
        almacen.Facturas.Add(FacturasDePrueba.FacturaB(conDni, ReceptorFactura.ConDni("00123456"), EntornoArca.Homologacion));
        almacen.Facturas.Add(FacturasDePrueba.FacturaC(facturaC, EntornoArca.Produccion));
        var caso = new ObtenerFactura(almacen, almacen);

        var b = await caso.EjecutarAsync(155);
        var c = await caso.EjecutarAsync(156);

        Assert.NotNull(b);
        Assert.Equal("DNI 00123456", b.Receptor);
        Assert.True(b.Simulada);
        Assert.NotNull(c);
        Assert.Equal("Factura C 0003-00000125", c.Comprobante);
        Assert.Equal("Consumidor Final", c.Receptor);
        Assert.Null(c.IvaContenido);
        Assert.Equal(2710.02m, c.Total);
        Assert.False(c.Simulada);
    }

    /// <summary>Sad path: un presupuesto inexistente o sin factura no tiene detalle.</summary>
    [Fact]
    public async Task ObtenerFactura_InexistenteOSinFactura_Null()
    {
        var almacen = new AlmacenFacturacion();
        almacen.Presupuestos.Add(FacturasDePrueba.PresupuestoPerezAna(155, 1815m));
        var caso = new ObtenerFactura(almacen, almacen);

        Assert.Null(await caso.EjecutarAsync(155));
        Assert.Null(await caso.EjecutarAsync(999));
    }
}

/// <summary>Presupuestos y facturas de prueba de FEAT-009, con datos ficticios.</summary>
internal static class FacturasDePrueba
{
    public const string Cae = "71234567890123";

    public static readonly DateOnly Fecha = new(2026, 10, 30);

    /// <summary>Un presupuesto en Final de "Pérez, Ana" (DNI 00123456) con una línea por cada Precio Final.</summary>
    public static Presupuesto PresupuestoPerezAna(long numero, params decimal[] totales) =>
        new(
            numero,
            Fecha,
            EstadoPresupuesto.Final,
            new ClientePresupuesto("Pérez", "Ana", "00123456", null, null, null),
            [.. totales.Select((total, indice) =>
                new LineaPresupuesto(indice + 1L, $"Artículo {indice + 1}", total, 1, 0m, total, total))],
            version: 1);

    /// <summary>La Factura B 0003-00000125 del presupuesto, alícuota 21, con el receptor y el entorno recibidos.</summary>
    public static Factura FacturaB(Presupuesto presupuesto, ReceptorFactura receptor, EntornoArca entorno) =>
        Armar(presupuesto, TipoComprobante.FacturaB, receptor, ImportesFactura.ParaFacturaB(presupuesto.Total, 21m), entorno);

    /// <summary>La Factura C 0003-00000125 del presupuesto, sin identificar al receptor.</summary>
    public static Factura FacturaC(Presupuesto presupuesto, EntornoArca entorno) =>
        Armar(
            presupuesto, TipoComprobante.FacturaC, ReceptorFactura.SinIdentificar,
            ImportesFactura.ParaFacturaC(presupuesto.Total), entorno);

    private static Factura Armar(
        Presupuesto presupuesto, TipoComprobante tipo, ReceptorFactura receptor, ImportesFactura importes,
        EntornoArca entorno) =>
        new(
            presupuesto.Numero, tipo, 3, 125, Fecha, ConceptoFactura.Productos, receptor, importes, Cae,
            Fecha.AddDays(10), entorno,
            [.. presupuesto.Lineas.Select((linea, indice) => new LineaFactura(
                indice + 1, linea.CodigoArticulo, linea.Descripcion, linea.PrecioUnitario, linea.Cantidad,
                linea.PorcentajeDescuento, linea.PrecioConDescuento, linea.PrecioFinal))]);
}
