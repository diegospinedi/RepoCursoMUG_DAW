using Optica.Domain.Catalogo;

namespace Optica.Domain.Tests.Catalogo;

public class PaginaArticulosTests
{
    [Fact]
    public void PaginaArticulos_Valida_ExponeArticulosYTotal()
    {
        var pagina = new PaginaArticulos([Articulo(1L), Articulo(2L)], 120);

        Assert.Equal([1L, 2L], pagina.Articulos.Select(articulo => articulo.Codigo!.Value));
        Assert.Equal(120, pagina.Total);
        Assert.Empty(new PaginaArticulos([], 0).Articulos);
    }

    [Fact]
    public void PaginaArticulos_TotalMenorQueLosArticulos_Lanza()
    {
        var error = Assert.Throws<ArgumentException>(() => new PaginaArticulos([Articulo(1L), Articulo(2L)], 1));

        Assert.Equal("total", error.ParamName);
        Assert.DoesNotContain("1", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("2", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PaginaArticulos_TotalNegativo_Lanza()
    {
        var error = Assert.Throws<ArgumentException>(() => new PaginaArticulos([], -1));

        Assert.Equal("total", error.ParamName);
        Assert.DoesNotContain("-1", error.Message, StringComparison.Ordinal);
    }

    private static Articulo Articulo(long codigo) =>
        new(codigo, $"C-{codigo}", "Lente", 10m, 0m, 10m, activo: true, version: 0);
}
