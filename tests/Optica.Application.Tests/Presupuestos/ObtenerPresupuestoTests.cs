using Optica.Application.Presupuestos;
using Optica.Domain.Presupuestos;

namespace Optica.Application.Tests.Presupuestos;

public class ObtenerPresupuestoTests
{
    private readonly PresupuestosEnMemoria _unidad = new();

    [Theory]
    [InlineData(2)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(long.MaxValue)]
    public async Task Obtener_Inexistente_Null(long numero)
    {
        _unidad.Sembrar(1);

        var leido = await new ObtenerPresupuesto(_unidad).EjecutarAsync(numero);

        Assert.Null(leido);
    }

    [Fact]
    public async Task Obtener_Existente_DevuelveLoGuardado()
    {
        _unidad.Sembrar(1);
        var guardado = _unidad.Sembrar(154);

        var leido = await new ObtenerPresupuesto(_unidad).EjecutarAsync(154);

        Assert.NotNull(leido);
        Assert.Equal(154, leido.Numero);
        Assert.Equal(guardado.Fecha, leido.Fecha);
        Assert.Equal(EstadoPresupuesto.Borrador, leido.Estado);
        Assert.Equal(new ClienteGrabado("Gómez", "Luis", "30111222", null, null, null), leido.Cliente);
        Assert.Equal([new LineaGrabada(1, "Lente orgánico", 100m, 1, 0m, 100m, 100m)], leido.Lineas);
    }
}
