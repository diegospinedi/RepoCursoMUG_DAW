using Optica.Application.Catalogo;
using Optica.Application.Comun;
using Optica.Domain.Catalogo;

namespace Optica.Application.Tests.Catalogo;

public class ModificarArticuloTests
{
    private const string Duplicado = "Ese código ya pertenece a otro artículo.";
    private const string Inactivo = "Reactivá el artículo para editarlo.";
    private const string Cambiado = "El artículo cambió mientras lo editabas. Volvé a abrirlo.";

    private readonly ArticulosEnMemoria _articulos = new();
    private readonly ConfiguracionVariable _configuracion = new();

    private ModificarArticulo CrearCasoDeUso() => new(_articulos, _configuracion, _articulos);

    private static DatosArticulo Datos(
        string? codigo = "ABC-1",
        string? descripcion = "Armazón de acetato",
        decimal? costo = 1210m,
        decimal? margen = 50m) => new(codigo, descripcion, costo, margen);

    private Articulo SembrarArmazon(bool activo = true, long version = 3) =>
        _articulos.Sembrar("ABC-1", "Armazón de acetato", 1210m, activo, version);

    private void AfirmarSinEscribir(Articulo original)
    {
        Assert.Equal(0, _articulos.Actualizaciones);
        Assert.Equal(0, _articulos.Confirmaciones);
        var guardado = _articulos.Guardado(original.Codigo!.Value);
        Assert.Equal(original.Version, guardado.Version);
        Assert.Equal(original.PrecioVenta, guardado.PrecioVenta);
    }

    [Fact]
    public async Task Modificar_Inexistente_NoEncontrado()
    {
        _articulos.Sembrar("ABC-1");

        var resultado = await CrearCasoDeUso().EjecutarAsync(999, Datos(), 0);

        Assert.Equal(TipoResultadoEdicion.NoEncontrado, resultado.Tipo);
        Assert.False(resultado.EsGrabado);
        Assert.Null(resultado.Articulo);
        Assert.Empty(resultado.Errores);
        Assert.Null(resultado.Detalle);
        Assert.Equal(0, _configuracion.Lecturas);
        Assert.Equal(0, _articulos.Confirmaciones);
    }

    [Fact]
    public async Task Modificar_Inactivo_Rechazado()
    {
        var inactivo = SembrarArmazon(activo: false);

        // Datos inválidos a propósito: el inactivo se rechaza sin evaluarlos (D-02).
        var resultado = await CrearCasoDeUso().EjecutarAsync(
            inactivo.Codigo!.Value, Datos(codigo: null, margen: 60m), inactivo.Version);

        Assert.Equal(TipoResultadoEdicion.Inactivo, resultado.Tipo);
        Assert.Equal(Inactivo, resultado.Detalle);
        Assert.Empty(resultado.Errores);
        Assert.Null(resultado.Articulo);
        AfirmarSinEscribir(inactivo);
        Assert.False(_articulos.Guardado(inactivo.Codigo.Value).Activo);
    }

    [Fact]
    public async Task Modificar_DatosInvalidos_TodosLosErrores()
    {
        var articulo = SembrarArmazon();

        var resultado = await CrearCasoDeUso().EjecutarAsync(
            articulo.Codigo!.Value, new DatosArticulo(new string('X', 51), null, 1.001m, null), articulo.Version);

        Assert.Equal(TipoResultadoEdicion.Invalido, resultado.Tipo);
        Assert.Equal(
            [
                new ErrorValidacion("codigoProveedor", "Usá hasta 50 caracteres."),
                new ErrorValidacion("descripcion", "Completá la descripción."),
                new ErrorValidacion("precioCosto", "Ingresá hasta 2 decimales."),
                new ErrorValidacion("margenUtilidad", "Ingresá el margen de utilidad."),
            ],
            resultado.Errores);
        AfirmarSinEscribir(articulo);

        var fueraDeRango = await CrearCasoDeUso().EjecutarAsync(
            articulo.Codigo.Value, Datos(costo: 10_000_000_000_000_000m, margen: 1000m), articulo.Version);

        Assert.Equal(
            [
                new ErrorValidacion(
                    "margenUtilidad", "El precio de venta resultante es demasiado grande. Revisá el costo o el margen."),
            ],
            fueraDeRango.Errores);
        AfirmarSinEscribir(articulo);
    }

    [Fact]
    public async Task Modificar_CodigoDuplicado_ErrorEnCodigoProveedor()
    {
        _articulos.Sembrar("XYZ-9", "Otro");
        var articulo = SembrarArmazon();

        // Por el chequeo previo: la clave normalizada de " xyz-9 " la tiene otro artículo.
        var porChequeo = await CrearCasoDeUso().EjecutarAsync(
            articulo.Codigo!.Value, Datos(codigo: " xyz-9 "), articulo.Version);

        Assert.Equal(TipoResultadoEdicion.Invalido, porChequeo.Tipo);
        Assert.Equal([new ErrorValidacion("codigoProveedor", Duplicado)], porChequeo.Errores);
        AfirmarSinEscribir(articulo);

        // Conservar su propio código no es un duplicado.
        Assert.True((await CrearCasoDeUso().EjecutarAsync(
            articulo.Codigo.Value, Datos(codigo: " abc-1 "), articulo.Version)).EsGrabado);

        // Por el índice único al confirmar (motivo Unicidad): otra escritura tomó la clave en el medio.
        var actual = _articulos.Guardado(articulo.Codigo.Value);
        _articulos.AntesDeConfirmar = () => _articulos.Sembrar("NUEVO-1", "Carrera");
        var porConflicto = await CrearCasoDeUso().EjecutarAsync(
            articulo.Codigo.Value, Datos(codigo: "nuevo-1"), actual.Version);

        Assert.Equal(TipoResultadoEdicion.Invalido, porConflicto.Tipo);
        Assert.Equal([new ErrorValidacion("codigoProveedor", Duplicado)], porConflicto.Errores);
        Assert.Equal("abc-1", _articulos.Guardado(articulo.Codigo.Value).CodigoProveedor);
        Assert.Equal(actual.Version, _articulos.Guardado(articulo.Codigo.Value).Version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1L)]
    [InlineData(9_007_199_254_740_992L)]
    [InlineData(long.MaxValue)]
    public async Task Modificar_VersionFaltante_ErrorEnVersion(long? version)
    {
        var articulo = SembrarArmazon();

        var resultado = await CrearCasoDeUso().EjecutarAsync(articulo.Codigo!.Value, Datos(margen: 60m), version);

        Assert.Equal(TipoResultadoEdicion.Invalido, resultado.Tipo);
        var error = Assert.Single(resultado.Errores);
        Assert.Equal(new ErrorValidacion("version", "Volvé a abrir el artículo para editarlo."), error);
        Assert.Equal(0, _configuracion.Lecturas);
        AfirmarSinEscribir(articulo);
    }

    [Fact]
    public async Task Modificar_ConflictoDeConcurrencia_Cambiado()
    {
        var articulo = SembrarArmazon();

        // Otra pestaña confirma entre la lectura y la grabación: la versión guardada ya no es la leída.
        _articulos.AntesDeConfirmar = () => _articulos.EscribirDesdeOtraPestana(articulo.Codigo!.Value);
        var cambiado = await CrearCasoDeUso().EjecutarAsync(articulo.Codigo!.Value, Datos(margen: 60m), articulo.Version);

        Assert.Equal(TipoResultadoEdicion.Cambiado, cambiado.Tipo);
        Assert.Equal(Cambiado, cambiado.Detalle);
        Assert.Empty(cambiado.Errores);
        Assert.Equal(1815m, _articulos.Guardado(articulo.Codigo.Value).PrecioVenta);

        // Si la otra escritura lo desactivó, la relectura informa inactivo.
        var actual = _articulos.Guardado(articulo.Codigo.Value);
        _articulos.AntesDeConfirmar = () => _articulos.EscribirDesdeOtraPestana(articulo.Codigo.Value, activo: false);
        var desactivado = await CrearCasoDeUso().EjecutarAsync(articulo.Codigo.Value, Datos(margen: 60m), actual.Version);

        Assert.Equal(TipoResultadoEdicion.Inactivo, desactivado.Tipo);
        Assert.Equal(Inactivo, desactivado.Detalle);

        // Un conflicto de concurrencia forzado sobre un artículo borrado: no encontrado, nunca duplicado.
        var otro = _articulos.Sembrar("DEF-2", "Lente");
        _articulos.AntesDeConfirmar = () => _articulos.EscribirDesdeOtraPestana(otro.Codigo!.Value, borrar: true);
        var borrado = await CrearCasoDeUso().EjecutarAsync(otro.Codigo!.Value, Datos(codigo: "DEF-2"), otro.Version);

        Assert.Equal(TipoResultadoEdicion.NoEncontrado, borrado.Tipo);
        Assert.Empty(borrado.Errores);

        // Con el motivo Concurrencia lanzado tal cual por la unidad de trabajo: Cambiado, sin error de duplicado.
        _articulos.AntesDeConfirmar = null;
        _articulos.EscribirDesdeOtraPestana(articulo.Codigo.Value, activo: true);
        var vigente = _articulos.Guardado(articulo.Codigo.Value);
        _articulos.ErrorAlConfirmar = new ConflictoAlConfirmarException("Conflicto.", MotivoConflicto.Concurrencia);
        var forzado = await CrearCasoDeUso().EjecutarAsync(articulo.Codigo.Value, Datos(margen: 60m), vigente.Version);

        Assert.Equal(TipoResultadoEdicion.Cambiado, forzado.Tipo);
        Assert.Empty(forzado.Errores);
    }

    [Fact]
    public async Task Modificar_VersionDistintaDeLaLeida_CambiadoSinEscribir()
    {
        var articulo = SembrarArmazon(version: 3);

        var resultado = await CrearCasoDeUso().EjecutarAsync(articulo.Codigo!.Value, Datos(margen: 60m), 2);

        Assert.Equal(TipoResultadoEdicion.Cambiado, resultado.Tipo);
        Assert.Equal(Cambiado, resultado.Detalle);
        Assert.Null(resultado.Articulo);
        AfirmarSinEscribir(articulo);
    }

    [Fact]
    public async Task Modificar_VersionDistintaYDesactivado_InactivoRechazado()
    {
        var articulo = SembrarArmazon(activo: false, version: 3);

        var resultado = await CrearCasoDeUso().EjecutarAsync(articulo.Codigo!.Value, Datos(margen: 60m), 2);

        Assert.Equal(TipoResultadoEdicion.Inactivo, resultado.Tipo);
        Assert.Equal(Inactivo, resultado.Detalle);
        AfirmarSinEscribir(articulo);
    }

    [Fact]
    public async Task Modificar_Margen50A60_Graba1936()
    {
        var articulo = SembrarArmazon(version: 3);

        var resultado = await CrearCasoDeUso().EjecutarAsync(articulo.Codigo!.Value, Datos(margen: 60m), 3);

        Assert.True(resultado.EsGrabado);
        Assert.Equal(TipoResultadoEdicion.Grabado, resultado.Tipo);
        Assert.Equal(
            new ArticuloGrabado(articulo.Codigo.Value, "ABC-1", "Armazón de acetato", 1210m, 60m, 1936m, null, true, 4),
            resultado.Articulo);
        var guardado = _articulos.Guardado(articulo.Codigo.Value);
        Assert.Equal(1936m, guardado.PrecioVenta);
        Assert.Equal(4, guardado.Version);
        Assert.Equal(1, _articulos.Actualizaciones);
        Assert.Equal(1, _articulos.Confirmaciones);
    }

    [Fact]
    public async Task Modificar_CanceladoDespuesDeConfirmar_Grabado()
    {
        // La modificación ya quedó confirmada: cortar la relectura haría que la operadora, al reintentar, viera
        // "cambiado" por su propia grabación.
        var articulo = SembrarArmazon(version: 3);
        using var cancelacion = new CancellationTokenSource();
        _articulos.DespuesDeConfirmar = cancelacion.Cancel;

        var resultado = await CrearCasoDeUso().EjecutarAsync(
            articulo.Codigo!.Value, Datos(margen: 60m), 3, cancelacion.Token);

        Assert.True(resultado.EsGrabado);
        Assert.Equal(1936m, resultado.Articulo.PrecioVenta);
        Assert.Equal(4, resultado.Articulo.Version);
    }

    /// <summary>
    /// D-05 y D-18 de FEAT-002c: la lectura del artículo, la de la configuración, la verificación de la clave, el registro
    /// y la confirmación van dentro de una operación en transacción; la relectura del artículo grabado, después.
    /// </summary>
    [Fact]
    public async Task Modificar_LeeYConfirmaDentroDeLaOperacion()
    {
        var articulo = SembrarArmazon(version: 3);
        var configuracion = new ConfiguracionVariable { Unidad = _articulos };

        var resultado = await new ModificarArticulo(_articulos, configuracion, _articulos).EjecutarAsync(
            articulo.Codigo!.Value, Datos(margen: 60m), 3);

        Assert.True(resultado.EsGrabado);
        Assert.Equal(
            [
                ("ObtenerAsync", true),
                (ConfiguracionVariable.AccesoConfiguracion, true),
                ("ExisteClaveDeOtroAsync", true),
                ("Actualizar", true),
                ("ConfirmarAsync", true),
                ("ObtenerAsync", false),
            ],
            _articulos.Accesos);
        Assert.Equal(1, _articulos.Operaciones);
        Assert.Equal(1, _articulos.OperacionesConfirmadas);
    }

    /// <summary>
    /// D-18 de FEAT-002c: un conflicto de concurrencia atendido termina la operación normalmente (la transacción externa
    /// confirma sin cambios) y la relectura del conflicto ocurre dentro de ella.
    /// </summary>
    [Fact]
    public async Task Modificar_ConflictoAtendido_TerminaLaOperacionSinCambios()
    {
        var articulo = SembrarArmazon(version: 3);
        _articulos.ErrorAlConfirmar = new ConflictoAlConfirmarException("Conflicto.", MotivoConflicto.Concurrencia);

        var resultado = await CrearCasoDeUso().EjecutarAsync(articulo.Codigo!.Value, Datos(margen: 60m), 3);

        Assert.Equal(TipoResultadoEdicion.Cambiado, resultado.Tipo);
        Assert.Equal(1, _articulos.Operaciones);
        Assert.Equal(1, _articulos.OperacionesConfirmadas);
        Assert.Equal(("ObtenerAsync", true), _articulos.Accesos[^1]);
        Assert.Equal(3, _articulos.Guardado(articulo.Codigo.Value).Version);
    }

    /// <summary>Una falla que no es un conflicto revierte la operación y se propaga.</summary>
    [Fact]
    public async Task Modificar_OtraFallaAlConfirmar_RevierteYSePropaga()
    {
        var articulo = SembrarArmazon(version: 3);
        _articulos.ErrorAlConfirmar = new InvalidOperationException("Falla de la base.");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CrearCasoDeUso().EjecutarAsync(articulo.Codigo!.Value, Datos(margen: 60m), 3));

        Assert.Equal(1, _articulos.Operaciones);
        Assert.Equal(0, _articulos.OperacionesConfirmadas);
        Assert.Equal(1815m, _articulos.Guardado(articulo.Codigo!.Value).PrecioVenta);
    }

    [Fact]
    public async Task Modificar_Costo1210A2420_Graba3630()
    {
        var articulo = SembrarArmazon(version: 0);

        var resultado = await CrearCasoDeUso().EjecutarAsync(
            articulo.Codigo!.Value, Datos(descripcion: "  Armazón de acetato  ", costo: 2420m), 0);

        Assert.True(resultado.EsGrabado);
        Assert.Equal(
            new ArticuloGrabado(articulo.Codigo.Value, "ABC-1", "Armazón de acetato", 2420m, 50m, 3630m, null, true, 1),
            resultado.Articulo);
        Assert.Equal(3630m, _articulos.Guardado(articulo.Codigo.Value).PrecioVenta);
        Assert.Equal(1, _configuracion.Lecturas);
    }
}
