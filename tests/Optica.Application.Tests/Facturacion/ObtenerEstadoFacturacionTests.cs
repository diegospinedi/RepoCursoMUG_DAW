using Optica.Application.Facturacion;
using Optica.Domain.Facturacion;
using Optica.Domain.Presupuestos;

namespace Optica.Application.Tests.Facturacion;

/// <summary>
/// Spec FEAT-007, Block 7 (FR-20, D-28): el estado de facturación que suma el GET del presupuesto. Spec FEAT-008, Block 5:
/// con la observación vigente de la pendiente, sin datos del cliente.
/// </summary>
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
        var caso = new ObtenerEstadoFacturacion(almacen, almacen, almacen);

        Assert.Equal(new EstadoFacturacion("Factura B 0003-00000125", false, null), await caso.EjecutarAsync(1));
        Assert.Equal(new EstadoFacturacion(null, true, null), await caso.EjecutarAsync(2));
        Assert.Equal(new EstadoFacturacion(null, false, null), await caso.EjecutarAsync(3));
        Assert.Equal(new EstadoFacturacion(null, false, null), await caso.EjecutarAsync(99));

        // Solo la identificación de la factura: no lee la factura completa (líneas ni DNI, D-44).
        Assert.DoesNotContain("ObtenerFactura", almacen.Accesos);
        Assert.Contains("ObtenerComprobante", almacen.Accesos);
    }

    /// <summary>
    /// INFO-1 de la auditoría: lee primero la pendiente y después la factura. Si una emisión termina entre las dos
    /// lecturas, el estado muestra las dos (botón deshabilitado) y nunca ninguna (botón habilitado sobre un facturado).
    /// Sin pendiente no hay observación que leer.
    /// </summary>
    [Fact]
    public async Task ObtenerEstadoFacturacion_LeePrimeroLaPendiente()
    {
        var almacen = new AlmacenFacturacion();
        almacen.Sembrar(1);

        await new ObtenerEstadoFacturacion(almacen, almacen, almacen).EjecutarAsync(1);

        Assert.Equal(["ExistePendiente", "ObtenerComprobante"], almacen.Accesos);
    }

    /// <summary>
    /// FEAT-008 (FR-08, FR-14, FR-15): con una observación vigente, el estado la suma con el motivo, el comprobante
    /// ("Factura B 0003-00000125", o null sin número), el importe registrado, el de ARCA (o null) y el entorno. No lleva el
    /// DNI ni otro dato del cliente.
    /// </summary>
    [Fact]
    public async Task ObtenerEstadoFacturacion_ConObservacion_MotivoComprobanteEImportes()
    {
        var almacen = new AlmacenFacturacion();
        var presupuesto = almacen.Sembrar(1, EstadoPresupuesto.Final, Presupuestos_.Dni, 1815m);
        var pendiente = almacen.SembrarPendiente(presupuesto);
        almacen.SembrarObservacion(pendiente, MotivoObservacionEmision.ImporteDistinto, 2000m);
        almacen.Sembrar(150);
        var sinNumero = EmisionPendiente.SinNumero(
            150, new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero), TipoComprobante.FacturaB, 3,
            new DateOnly(2026, 10, 1), 1815m, EntornoArca.Homologacion);
        almacen.Pendientes.Add(sinNumero);
        almacen.SembrarObservacion(sinNumero, MotivoObservacionEmision.SinNumero);
        var caso = new ObtenerEstadoFacturacion(almacen, almacen, almacen);

        var importe = await caso.EjecutarAsync(1);
        var sinNumeroEstado = await caso.EjecutarAsync(150);

        Assert.Equal(
            new EstadoFacturacion(
                null, true,
                new DetalleObservacion(
                    MotivoObservacionEmision.ImporteDistinto, "Factura B 0003-00000007", 1815m, 2000m,
                    EntornoArca.Simulado)),
            importe);
        Assert.Equal(
            new EstadoFacturacion(
                null, true,
                new DetalleObservacion(MotivoObservacionEmision.SinNumero, null, 1815m, null, EntornoArca.Homologacion)),
            sinNumeroEstado);
        Assert.DoesNotContain(Presupuestos_.Dni, importe.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Presupuestos_.Apellido, importe.ToString(), StringComparison.Ordinal);
    }
}
