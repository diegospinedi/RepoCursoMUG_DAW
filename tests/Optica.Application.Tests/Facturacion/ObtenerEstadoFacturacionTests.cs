using Optica.Application.Facturacion;

namespace Optica.Application.Tests.Facturacion;

/// <summary>Spec FEAT-007, Block 7 (FR-20, D-28): el estado de facturación que suma el GET del presupuesto.</summary>
public sealed class ObtenerEstadoFacturacionTests
{
    [Fact]
    public async Task ObtenerEstadoFacturacion_ConFacturaYSinFactura()
    {
        var almacen = new AlmacenFacturacion();
        var facturado = almacen.Sembrar(1);
        var conPendiente = almacen.Sembrar(2);
        almacen.Sembrar(3);
        almacen.SembrarFactura(facturado, numero: 125);
        almacen.SembrarPendiente(conPendiente);
        var caso = new ObtenerEstadoFacturacion(almacen, almacen);

        Assert.Equal(new EstadoFacturacion("Factura B 0003-00000125", false), await caso.EjecutarAsync(1));
        Assert.Equal(new EstadoFacturacion(null, true), await caso.EjecutarAsync(2));
        Assert.Equal(new EstadoFacturacion(null, false), await caso.EjecutarAsync(3));
        Assert.Equal(new EstadoFacturacion(null, false), await caso.EjecutarAsync(99));

        // Solo la identificación de la factura: no lee la factura completa (líneas ni DNI, D-44).
        Assert.DoesNotContain("ObtenerFactura", almacen.Accesos);
        Assert.Contains("ObtenerComprobante", almacen.Accesos);
    }

    /// <summary>
    /// INFO-1 de la auditoría: lee primero la pendiente y después la factura. Si una emisión termina entre las dos
    /// lecturas, el estado muestra las dos (botón deshabilitado) y nunca ninguna (botón habilitado sobre un facturado).
    /// </summary>
    [Fact]
    public async Task ObtenerEstadoFacturacion_LeePrimeroLaPendiente()
    {
        var almacen = new AlmacenFacturacion();
        almacen.Sembrar(1);

        await new ObtenerEstadoFacturacion(almacen, almacen).EjecutarAsync(1);

        Assert.Equal(["ExistePendiente", "ObtenerComprobante"], almacen.Accesos);
    }
}
