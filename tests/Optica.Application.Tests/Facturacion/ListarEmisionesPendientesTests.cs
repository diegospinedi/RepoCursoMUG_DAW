using Optica.Application.Facturacion;
using Optica.Domain.Facturacion;

namespace Optica.Application.Tests.Facturacion;

/// <summary>Spec FEAT-008, Block 5 (FR-03): los números de los presupuestos con una emisión pendiente.</summary>
public sealed class ListarEmisionesPendientesTests
{
    /// <summary>
    /// AC-03: con pendientes de los presupuestos 160 y 155 (guardadas en ese orden), la lista es 155 y 160, ascendente,
    /// sin otros datos.
    /// </summary>
    [Fact]
    public async Task Listar_DosPendientes_155y160()
    {
        var almacen = new AlmacenFacturacion();
        var momento = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        almacen.Pendientes.Add(EmisionPendiente.SinNumero(
            160, momento, TipoComprobante.FacturaB, 3, new DateOnly(2026, 10, 1), 1815m, EntornoArca.Simulado));
        almacen.SembrarPendiente(almacen.Sembrar(155));
        almacen.Sembrar(170);

        var numeros = await new ListarEmisionesPendientes(almacen).EjecutarAsync();

        Assert.Equal([155L, 160L], numeros);
    }

    /// <summary>AC-04: sin pendientes, la lista está vacía.</summary>
    [Fact]
    public async Task Listar_SinPendientes_Vacia()
    {
        var almacen = new AlmacenFacturacion();
        almacen.Sembrar(1);

        var numeros = await new ListarEmisionesPendientes(almacen).EjecutarAsync();

        Assert.Empty(numeros);
        Assert.Equal(["ListarPendientes"], almacen.Accesos);
    }
}
