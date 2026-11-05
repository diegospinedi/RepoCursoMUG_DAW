using Optica.Application.Catalogo;
using Optica.Application.Comun;

namespace Optica.Application.Tests.Catalogo;

public class CambiarEstadoArticuloTests
{
    private const string Cambiado = "El artículo cambió. Volvé a cargar el listado.";

    private readonly ArticulosEnMemoria _articulos = new();

    private CambiarEstadoArticulo CrearCasoDeUso() => new(_articulos, _articulos);

    [Fact]
    public async Task CambiarEstado_ActivoFaltante_ErrorEnActivo()
    {
        var articulo = _articulos.Sembrar("ABC-1");

        var resultado = await CrearCasoDeUso().EjecutarAsync(articulo.Codigo!.Value, null);

        Assert.Equal(TipoResultadoEdicion.Invalido, resultado.Tipo);
        Assert.Equal(
            [new ErrorValidacion("activo", "Indicá si el artículo queda activo o inactivo.")], resultado.Errores);
        Assert.Equal(0, _articulos.CambiosDeEstado);
        Assert.Equal(0, _articulos.Confirmaciones);

        var inexistente = await CrearCasoDeUso().EjecutarAsync(999, false);
        Assert.Equal(TipoResultadoEdicion.NoEncontrado, inexistente.Tipo);
        Assert.Equal(0, _articulos.Confirmaciones);
    }

    [Fact]
    public async Task CambiarEstado_ConflictoDeConcurrencia_Cambiado()
    {
        var articulo = _articulos.Sembrar("ABC-1", version: 2);
        var codigo = articulo.Codigo!.Value;

        // Otra escritura confirma entre la lectura y la grabación sin cambiar el estado: Cambiado.
        _articulos.AntesDeConfirmar = () => _articulos.EscribirDesdeOtraPestana(codigo);
        var cambiado = await CrearCasoDeUso().EjecutarAsync(codigo, false);

        Assert.Equal(TipoResultadoEdicion.Cambiado, cambiado.Tipo);
        Assert.Equal(Cambiado, cambiado.Detalle);
        Assert.Null(cambiado.Articulo);
        Assert.True(_articulos.Guardado(codigo).Activo);

        // La otra escritura ya lo desactivó: el estado es el pedido y responde Grabado con el artículo releído.
        _articulos.AntesDeConfirmar = () => _articulos.EscribirDesdeOtraPestana(codigo, activo: false);
        var yaIgual = await CrearCasoDeUso().EjecutarAsync(codigo, false);

        Assert.Equal(TipoResultadoEdicion.Grabado, yaIgual.Tipo);
        Assert.False(yaIgual.Articulo!.Activo);
        Assert.Equal(_articulos.Guardado(codigo).Version, yaIgual.Articulo.Version);

        // La otra escritura lo borró: no encontrado.
        _articulos.AntesDeConfirmar = () => _articulos.EscribirDesdeOtraPestana(codigo, borrar: true);
        var borrado = await CrearCasoDeUso().EjecutarAsync(codigo, true);

        Assert.Equal(TipoResultadoEdicion.NoEncontrado, borrado.Tipo);
    }

    [Fact]
    public async Task CambiarEstado_Desactivar_ConservaLosDatos()
    {
        var articulo = _articulos.Sembrar("ABC-1", "Armazón de acetato", 1210m, version: 5);

        var resultado = await CrearCasoDeUso().EjecutarAsync(articulo.Codigo!.Value, false);

        Assert.True(resultado.EsGrabado);
        var guardado = _articulos.Guardado(articulo.Codigo.Value);
        Assert.False(guardado.Activo);
        Assert.Equal(6, guardado.Version);
        Assert.Equal(
            ("ABC-1", "Armazón de acetato", 1210m, 50m, 1815m),
            (guardado.CodigoProveedor, guardado.Descripcion, guardado.PrecioCosto, guardado.MargenUtilidad,
                guardado.PrecioVenta));
        Assert.Equal("ABC-1", resultado.Articulo.CodigoProveedor);
        Assert.Equal(1815m, resultado.Articulo.PrecioVenta);
        Assert.False(resultado.Articulo.Activo);
        Assert.Equal(1, _articulos.CambiosDeEstado);
        Assert.Equal(0, _articulos.Actualizaciones);
        Assert.Equal(1, _articulos.Confirmaciones);
    }

    [Fact]
    public async Task CambiarEstado_ConflictoDeUnicidad_SePropaga()
    {
        var codigo = _articulos.Sembrar("ABC-1").Codigo!.Value;
        _articulos.ErrorAlConfirmar = new ConflictoAlConfirmarException("Conflicto.", MotivoConflicto.Unicidad);

        var error = await Assert.ThrowsAsync<ConflictoAlConfirmarException>(
            () => CrearCasoDeUso().EjecutarAsync(codigo, false));

        Assert.Equal(MotivoConflicto.Unicidad, error.Motivo);
        Assert.True(_articulos.Guardado(codigo).Activo);
    }

    [Fact]
    public async Task CambiarEstado_CanceladoDespuesDeConfirmar_Grabado()
    {
        // El cambio ya quedó confirmado: cortar la relectura informaría un error por algo que sí se guardó.
        var articulo = _articulos.Sembrar("ABC-1", version: 2);
        using var cancelacion = new CancellationTokenSource();
        _articulos.DespuesDeConfirmar = cancelacion.Cancel;

        var resultado = await CrearCasoDeUso().EjecutarAsync(articulo.Codigo!.Value, false, cancelacion.Token);

        Assert.True(resultado.EsGrabado);
        Assert.False(resultado.Articulo.Activo);
        Assert.Equal(3, resultado.Articulo.Version);
    }

    [Fact]
    public async Task CambiarEstado_Desactivar_DevuelveLaVersionNueva()
    {
        var articulo = _articulos.Sembrar("ABC-1", "Armazón de acetato", 1210m, version: 5);

        var resultado = await CrearCasoDeUso().EjecutarAsync(articulo.Codigo!.Value, false);

        Assert.True(resultado.EsGrabado);
        Assert.Equal(
            new ArticuloGrabado(articulo.Codigo.Value, "ABC-1", "Armazón de acetato", 1210m, 50m, 1815m, null, false, 6),
            resultado.Articulo);
        Assert.Equal(_articulos.Guardado(articulo.Codigo.Value).Version, resultado.Articulo.Version);
    }

    [Fact]
    public async Task CambiarEstado_Reactivar_QuedaActivo()
    {
        var articulo = _articulos.Sembrar("ABC-1", "Armazón de acetato", 1210m, activo: false, version: 1);

        var resultado = await CrearCasoDeUso().EjecutarAsync(articulo.Codigo!.Value, true);

        Assert.True(resultado.EsGrabado);
        Assert.True(resultado.Articulo.Activo);
        Assert.True(_articulos.Guardado(articulo.Codigo.Value).Activo);
        Assert.Equal(2, _articulos.Guardado(articulo.Codigo.Value).Version);
        Assert.Equal(1, _articulos.Confirmaciones);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CambiarEstado_MismoEstado_NoEscribe(bool activo)
    {
        var articulo = _articulos.Sembrar("ABC-1", activo: activo, version: 7);

        var resultado = await CrearCasoDeUso().EjecutarAsync(articulo.Codigo!.Value, activo);

        Assert.True(resultado.EsGrabado);
        Assert.Equal(activo, resultado.Articulo.Activo);
        Assert.Equal(7, resultado.Articulo.Version);
        Assert.Equal(0, _articulos.CambiosDeEstado);
        Assert.Equal(0, _articulos.Confirmaciones);
        Assert.Equal(7, _articulos.Guardado(articulo.Codigo.Value).Version);
    }
}
