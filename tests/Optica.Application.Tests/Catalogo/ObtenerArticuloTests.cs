using Optica.Application.Catalogo;

namespace Optica.Application.Tests.Catalogo;

public class ObtenerArticuloTests
{
    private readonly ArticulosEnMemoria _articulos = new();

    [Fact]
    public async Task Obtener_Inexistente_DevuelveNull()
    {
        _articulos.Sembrar("ABC-1");

        Assert.Null(await new ObtenerArticulo(_articulos).EjecutarAsync(999));
    }

    [Fact]
    public async Task Obtener_Existente_DevuelveSusDatos()
    {
        var sembrado = _articulos.Sembrar("ABC-1", "Armazón", -100m);

        var articulo = await new ObtenerArticulo(_articulos).EjecutarAsync(sembrado.Codigo!.Value);

        Assert.Equal(
            new ArticuloGrabado(sembrado.Codigo.Value, "ABC-1", "Armazón", -100m, 50m, -150m, "Precio de costo negativo o cero"),
            articulo);
    }
}
