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
        Assert.Equal("borrador", leido.Estado);
        Assert.Equal(new ClienteGrabado("Gómez", "Luis", "30111222", null, null, null), leido.Cliente);
        Assert.Equal([new LineaGrabada(1, "Lente orgánico", 100m, 1, 0m, 100m, 100m)], leido.Lineas);
    }

    /// <summary>
    /// El estado del dominio se devuelve como texto fijo en minúsculas (D-19): Borrador "borrador" y Final "final". FEAT-003a
    /// solo graba Borrador, pero un presupuesto Final guardado se lee con su texto.
    /// </summary>
    [Theory]
    [InlineData(EstadoPresupuesto.Borrador, "borrador")]
    [InlineData(EstadoPresupuesto.Final, "final")]
    public async Task Obtener_CadaEstado_SuTexto(EstadoPresupuesto estado, string texto)
    {
        _unidad.Sembrar(7, estado);

        var leido = await new ObtenerPresupuesto(_unidad).EjecutarAsync(7);

        Assert.NotNull(leido);
        Assert.Equal(texto, leido.Estado);
        Assert.Equal(texto, PresupuestoGrabado.TextoDelEstado(estado));
        Assert.Equal(PresupuestoGrabado.EstadoBorrador, PresupuestoGrabado.TextoDelEstado(EstadoPresupuesto.Borrador));
        Assert.Equal(PresupuestoGrabado.EstadoFinal, PresupuestoGrabado.TextoDelEstado(EstadoPresupuesto.Final));
    }

    /// <summary>
    /// Cerrado por defecto: un estado sin texto asignado lanza con un mensaje fijo, sin el valor. El dominio no construye
    /// un presupuesto con un estado así; la guarda fija que un estado nuevo no pase sin traducción.
    /// </summary>
    [Fact]
    public void TextoDelEstado_EstadoNoPrevisto_Lanza()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => PresupuestoGrabado.TextoDelEstado((EstadoPresupuesto)99));

        Assert.DoesNotContain("99", error.Message, StringComparison.Ordinal);
    }
}
