using Optica.Application.Catalogo;
using Optica.Application.Comun;

namespace Optica.Application.Tests.Catalogo;

public class BuscarArticulosTests
{
    private readonly ArticulosEnMemoria _articulos = new();

    private BuscarArticulos CrearCasoDeUso() => new(_articulos);

    [Theory]
    [InlineData(201, false, "Usá hasta 200 caracteres.")]
    [InlineData(10, true, "El texto tiene caracteres no válidos. Escribilo de nuevo.")]
    public async Task Buscar_TextoInvalido_DevuelveErrorEnTexto(int largo, bool sustitutoSuelto, string accion)
    {
        _articulos.Sembrar("ABC-1", "Armazón");
        var texto = " " + new string('a', largo) + (sustitutoSuelto ? "\uD800" : string.Empty) + " ";

        var resultado = await CrearCasoDeUso().EjecutarAsync(texto, null);

        Assert.False(resultado.EsValido);
        Assert.Equal([new ErrorValidacion("texto", accion)], resultado.Errores);
        Assert.Empty(resultado.Articulos);
        Assert.Equal(0, _articulos.Busquedas);
    }

    [Fact]
    public async Task Buscar_Texto200Caracteres_Aceptado()
    {
        var texto = new string('a', 200);

        var resultado = await CrearCasoDeUso().EjecutarAsync("  " + texto + " \t", null);

        Assert.True(resultado.EsValido);
        Assert.Equal((texto, 1, 50, false), _articulos.UltimaBusqueda!.Value);
    }

    [Fact]
    public async Task Buscar_Texto201Caracteres_Rechazado()
    {
        var resultado = await CrearCasoDeUso().EjecutarAsync(new string('a', 201), null);

        Assert.Equal([new ErrorValidacion("texto", "Usá hasta 200 caracteres.")], resultado.Errores);
        Assert.Equal(0, _articulos.Busquedas);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10_001)]
    [InlineData(int.MaxValue)]
    public async Task Buscar_PaginaFueraDeRango_DevuelveErrorEnPagina(int pagina)
    {
        var resultado = await CrearCasoDeUso().EjecutarAsync(null, pagina);

        Assert.False(resultado.EsValido);
        // El texto es fijo para cualquier valor: no repite el recibido.
        Assert.Equal([new ErrorValidacion("pagina", "Pedí una página entre 1 y 10.000.")], resultado.Errores);
        Assert.Equal(0, _articulos.Busquedas);
    }

    [Fact]
    public async Task Buscar_SinTexto_PrimeraPaginaDe50()
    {
        for (var i = 0; i < 51; i++)
        {
            _articulos.Sembrar($"C-{i:D2}", $"Artículo {i:D2}");
        }

        var ausente = await CrearCasoDeUso().EjecutarAsync(null, null);
        var busquedaAusente = _articulos.UltimaBusqueda;
        var vacio = await CrearCasoDeUso().EjecutarAsync("  \t ", null);

        Assert.True(ausente.EsValido);
        Assert.Empty(ausente.Errores);
        Assert.Equal(((string?)null, 1, 50, false), busquedaAusente);
        Assert.Equal(((string?)null, 1, 50, false), _articulos.UltimaBusqueda);
        Assert.Equal(1, ausente.Pagina);
        Assert.Equal(50, ausente.TamanoPagina);
        Assert.Equal(51, ausente.Total);
        Assert.Equal(50, ausente.Articulos.Count);
        Assert.Equal("Artículo 00", ausente.Articulos[0].Descripcion);
        Assert.Equal(50, vacio.Articulos.Count);

        var segunda = await CrearCasoDeUso().EjecutarAsync(null, 2);
        Assert.Equal(2, segunda.Pagina);
        Assert.Equal("Artículo 50", Assert.Single(segunda.Articulos).Descripcion);
    }

    [Fact]
    public async Task Buscar_Armazon_PliegaElTexto()
    {
        var conTilde = _articulos.Sembrar("AR-1", "Armazón de acetato", 1210m);
        var sinTilde = _articulos.Sembrar("AR-2", "armazon metálico", 100m, activo: false);
        _articulos.Sembrar("LE-1", "Lente orgánico");

        var resultado = await CrearCasoDeUso().EjecutarAsync("  ARMAZÓN ", null);

        Assert.True(resultado.EsValido);
        Assert.Equal(("armazon", 1, 50, false), _articulos.UltimaBusqueda!.Value);
        Assert.Equal(2, resultado.Total);
        Assert.Equal(
            [
                new ArticuloListado(conTilde.Codigo!.Value, "AR-1", "Armazón de acetato", 1815m, true),
                new ArticuloListado(sinTilde.Codigo!.Value, "AR-2", "armazon metálico", 150m, false),
            ],
            resultado.Articulos);
    }
}
