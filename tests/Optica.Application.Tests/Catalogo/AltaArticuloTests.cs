using Optica.Application.Catalogo;
using Optica.Application.Comun;
using Optica.Application.Configuracion;
using Optica.Domain.Configuracion;

namespace Optica.Application.Tests.Catalogo;

public class AltaArticuloTests
{
    private const string Duplicado = "Ese código ya pertenece a otro artículo.";
    private const string Advertencia = "Precio de costo negativo o cero";

    private readonly ArticulosEnMemoria _articulos = new();
    private readonly ConfiguracionVariable _configuracion = new();

    private AltaArticulo CrearCasoDeUso() => new(_articulos, _configuracion, _articulos);

    private static DatosArticulo Validos(
        string? codigo = "ABC-1",
        string? descripcion = "Armazón de acetato",
        decimal? costo = 1210m,
        decimal? margen = 50m) => new(codigo, descripcion, costo, margen);

    /// <summary>
    /// Alerta de datos: el <c>ToString()</c> de un record muestra todos sus valores, y cualquier log que reciba los datos
    /// (un argumento registrado, una interpolación) mostraría el costo y el margen. No muestra ninguno.
    /// </summary>
    [Fact]
    public void DatosArticulo_ToString_NoMuestraDatos()
    {
        var texto = new DatosArticulo("Zq7-codigo", "Zq7-descripcion", 7654.31m, 43.21m).ToString();

        Assert.DoesNotContain("Zq7", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("7654", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("43", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("=", texto, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Alta_Valida_DevuelveCodigoYPrecio()
    {
        var resultado = await CrearCasoDeUso().EjecutarAsync(Validos(codigo: "  ABC-1 ", descripcion: " Armazón "));

        Assert.True(resultado.EsGrabado);
        Assert.Empty(resultado.Errores);
        var guardado = Assert.Single(_articulos.Guardados);
        Assert.Equal(
            new ArticuloGrabado(guardado.Codigo!.Value, "ABC-1", "Armazón", 1210m, 50m, 1815m, null, true, 0),
            resultado.Articulo);
        Assert.Equal(1815m, guardado.PrecioVenta);
        Assert.Equal(1, _articulos.Confirmaciones);
    }

    [Fact]
    public async Task Alta_CodigoDuplicadoConEspaciosYMinusculas_Rechazado()
    {
        _articulos.Sembrar("ABC-1");

        var resultado = await CrearCasoDeUso().EjecutarAsync(Validos(codigo: " abc-1 "));

        Assert.False(resultado.EsGrabado);
        Assert.Null(resultado.Articulo);
        Assert.Equal([new ErrorValidacion("codigoProveedor", Duplicado)], resultado.Errores);
        Assert.Single(_articulos.Guardados);
        Assert.Equal(0, _articulos.Agregados);
        Assert.Equal(0, _articulos.Confirmaciones);
    }

    [Fact]
    public async Task Alta_ConflictoAlConfirmar_RechazadoComoDuplicado()
    {
        _articulos.ErrorAlConfirmar = new ConflictoAlConfirmarException("Conflicto al confirmar.", MotivoConflicto.Unicidad);

        var resultado = await CrearCasoDeUso().EjecutarAsync(Validos());

        Assert.False(resultado.EsGrabado);
        Assert.Equal([new ErrorValidacion("codigoProveedor", Duplicado)], resultado.Errores);
        Assert.Empty(_articulos.Guardados);
        Assert.Equal(1, _articulos.Confirmaciones);
    }

    [Fact]
    public async Task Alta_OtraFallaAlConfirmar_SePropaga()
    {
        _articulos.ErrorAlConfirmar = new InvalidOperationException("Falla de la base.");

        await Assert.ThrowsAsync<InvalidOperationException>(() => CrearCasoDeUso().EjecutarAsync(Validos()));
    }

    [Theory]
    [InlineData(null, "Armazón", "codigoProveedor", "Completá el código en el proveedor.")]
    [InlineData("   ", "Armazón", "codigoProveedor", "Completá el código en el proveedor.")]
    [InlineData("ABC-1", null, "descripcion", "Completá la descripción.")]
    [InlineData("ABC-1", " \t ", "descripcion", "Completá la descripción.")]
    public async Task Alta_SinCodigoOSinDescripcion_RechazadoSinGrabar(
        string? codigo, string? descripcion, string campo, string accion)
    {
        var resultado = await CrearCasoDeUso().EjecutarAsync(Validos(codigo: codigo, descripcion: descripcion));

        Assert.False(resultado.EsGrabado);
        Assert.Equal([new ErrorValidacion(campo, accion)], resultado.Errores);
        Assert.Equal(0, _configuracion.Lecturas);
        Assert.Equal(0, _articulos.ConsultasDeClave);
        Assert.Equal(0, _articulos.Agregados);
        Assert.Equal(0, _articulos.Confirmaciones);
    }

    [Theory]
    [InlineData("1000.01")]
    [InlineData("-1")]
    public async Task Alta_Margen1000coma01_RechazadoConRango(string margen)
    {
        var resultado = await CrearCasoDeUso().EjecutarAsync(Validos(margen: decimal.Parse(margen, System.Globalization.CultureInfo.InvariantCulture)));

        Assert.False(resultado.EsGrabado);
        Assert.Equal(
            [new ErrorValidacion("margenUtilidad", "El margen debe estar entre 0 y 1.000.")], resultado.Errores);
        Assert.Equal(0, _articulos.Confirmaciones);
    }

    [Fact]
    public async Task Alta_CostoNegativo_GrabaConAdvertencia()
    {
        var resultado = await CrearCasoDeUso().EjecutarAsync(Validos(costo: -100m, margen: 50m));

        Assert.True(resultado.EsGrabado);
        Assert.Equal(-150m, resultado.Articulo.PrecioVenta);
        Assert.Equal(Advertencia, resultado.Articulo.Advertencia);
        Assert.Single(_articulos.Guardados);
    }

    [Fact]
    public async Task Alta_CostoCero_GrabaConAdvertencia()
    {
        var resultado = await CrearCasoDeUso().EjecutarAsync(Validos(costo: 0m));

        Assert.True(resultado.EsGrabado);
        Assert.Equal(0m, resultado.Articulo.PrecioVenta);
        Assert.Equal(Advertencia, resultado.Articulo.Advertencia);
    }

    [Fact]
    public async Task Alta_UsaLaConfiguracionVigente_SinCache()
    {
        var alta = CrearCasoDeUso();

        // AC-04: Responsable Inscripto, alícuota 21, múltiplo 0,01.
        var primero = await alta.EjecutarAsync(Validos(codigo: "A-1", costo: 1210m, margen: 150m));
        // AC-07 con el múltiplo cambiado entre las dos altas: el mismo caso de uso lee la configuración nueva.
        _configuracion.Vigente = new ConfiguracionNegocio(21m, CondicionFiscal.ResponsableInscripto, 50m, null);
        var segundo = await alta.EjecutarAsync(Validos(codigo: "A-2", costo: 1210m, margen: 50m));

        Assert.True(primero.EsGrabado);
        Assert.True(segundo.EsGrabado);
        Assert.Equal(3025m, primero.Articulo.PrecioVenta);
        Assert.Equal(1850m, segundo.Articulo.PrecioVenta);
        Assert.Equal(2, _configuracion.Lecturas);
    }

    [Fact]
    public async Task Alta_Monotributo_PrecioSinDescontarIva()
    {
        _configuracion.Vigente = new ConfiguracionNegocio(21m, CondicionFiscal.Monotributo, 0.01m, null);

        var resultado = await CrearCasoDeUso().EjecutarAsync(Validos(costo: 1210m, margen: 50m));

        Assert.True(resultado.EsGrabado);
        Assert.Equal(1815m, resultado.Articulo.PrecioVenta);
    }

    [Fact]
    public async Task Alta_PrecioFueraDeRango_Rechazado()
    {
        var resultado = await CrearCasoDeUso().EjecutarAsync(Validos(costo: 10_000_000_000_000_000m, margen: 1000m));

        Assert.False(resultado.EsGrabado);
        Assert.Equal(
            [
                new ErrorValidacion(
                    "margenUtilidad", "El precio de venta resultante es demasiado grande. Revisá el costo o el margen."),
            ],
            resultado.Errores);
        Assert.Equal(0, _articulos.ConsultasDeClave);
        Assert.Equal(0, _articulos.Agregados);
        Assert.Equal(0, _articulos.Confirmaciones);
    }

    [Fact]
    public async Task Alta_VariosCamposInvalidos_DevuelveTodosLosErrores()
    {
        var resultado = await CrearCasoDeUso().EjecutarAsync(
            new DatosArticulo(new string('X', 51), null, 1.001m, null));

        Assert.False(resultado.EsGrabado);
        Assert.Equal(
            [
                new ErrorValidacion("codigoProveedor", "Usá hasta 50 caracteres."),
                new ErrorValidacion("descripcion", "Completá la descripción."),
                new ErrorValidacion("precioCosto", "Ingresá hasta 2 decimales."),
                new ErrorValidacion("margenUtilidad", "Ingresá el margen de utilidad."),
            ],
            resultado.Errores);
        Assert.Equal(0, _configuracion.Lecturas);
        Assert.Equal(0, _articulos.Confirmaciones);
    }

    [Fact]
    public async Task Alta_CaracteresInvalidos_RechazadoJuntoAlCampo()
    {
        var resultado = await CrearCasoDeUso().EjecutarAsync(Validos(codigo: "AB\uD800", descripcion: "\uDC00x"));

        Assert.Equal(
            [
                new ErrorValidacion("codigoProveedor", "El texto tiene caracteres no válidos. Escribilo de nuevo."),
                new ErrorValidacion("descripcion", "El texto tiene caracteres no válidos. Escribilo de nuevo."),
            ],
            resultado.Errores);
    }

    [Fact]
    public async Task Alta_SinConfiguracion_PropagaError()
    {
        _configuracion.Vigente = null;

        await Assert.ThrowsAsync<InvalidOperationException>(() => CrearCasoDeUso().EjecutarAsync(Validos()));
        Assert.Equal(0, _articulos.Agregados);
        Assert.Equal(0, _articulos.Confirmaciones);
    }

    [Fact]
    public async Task Alta_ConfirmadoPeroNoSeLeePorClave_EstadoCorrupto()
    {
        _articulos.OcultarAlLeerPorClave = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => CrearCasoDeUso().EjecutarAsync(Validos()));
    }

    [Fact]
    public async Task Alta_CanceladoDespuesDeConfirmar_DevuelveGrabado()
    {
        // El alta ya quedó confirmada: cancelar la lectura posterior haría que la operadora, al reintentar, viera
        // "código duplicado" por su propio artículo.
        using var cancelacion = new CancellationTokenSource();
        _articulos.DespuesDeConfirmar = cancelacion.Cancel;

        var resultado = await CrearCasoDeUso().EjecutarAsync(Validos(), cancelacion.Token);

        Assert.True(resultado.EsGrabado);
        Assert.Equal(Assert.Single(_articulos.Guardados).Codigo, resultado.Articulo.Codigo);
    }

    /// <summary>
    /// D-05 y D-18 de FEAT-002c: la lectura de la configuración, la verificación de la clave, el registro y la
    /// confirmación van dentro de una operación en transacción; la relectura del artículo grabado, después.
    /// </summary>
    [Fact]
    public async Task Alta_LeeYConfirmaDentroDeLaOperacion()
    {
        var configuracion = new ConfiguracionVariable { Unidad = _articulos };

        var resultado = await new AltaArticulo(_articulos, configuracion, _articulos).EjecutarAsync(Validos());

        Assert.True(resultado.EsGrabado);
        Assert.Equal(
            [
                (ConfiguracionVariable.AccesoConfiguracion, true),
                ("ExisteClaveAsync", true),
                ("Agregar", true),
                ("ConfirmarAsync", true),
                ("ObtenerPorClaveAsync", false),
            ],
            _articulos.Accesos);
        Assert.Equal(1, _articulos.Operaciones);
        Assert.Equal(1, _articulos.OperacionesConfirmadas);
    }

    /// <summary>Un rechazo dentro de la operación (duplicado por el índice) la termina sin cambios.</summary>
    [Fact]
    public async Task Alta_ConflictoAlConfirmar_TerminaLaOperacionSinCambios()
    {
        _articulos.ErrorAlConfirmar = new ConflictoAlConfirmarException("Conflicto al confirmar.", MotivoConflicto.Unicidad);

        var resultado = await CrearCasoDeUso().EjecutarAsync(Validos());

        Assert.False(resultado.EsGrabado);
        Assert.Equal(1, _articulos.Operaciones);
        Assert.Equal(1, _articulos.OperacionesConfirmadas);
        Assert.Empty(_articulos.Guardados);
    }

    /// <summary>AC-01 de FEAT-002c: grabada la alícuota 10,5, un alta de costo 1.105 y margen 50 vale 1.657,50.</summary>
    [Fact]
    public async Task Alta_DespuesDeCambiarAlicuota_1105y50_1657coma50()
    {
        var almacen = new CatalogoYConfiguracionEnMemoria();
        var grabacion = await GrabarConfiguracionAsync(almacen, alicuota: 10.5m, multiplo: 0.01m);

        var resultado = await new AltaArticulo(almacen.Articulos, almacen, almacen).EjecutarAsync(
            Validos(costo: 1105m, margen: 50m));

        Assert.True(grabacion.EsGrabado);
        Assert.Equal(10.5m, almacen.Guardada!.AlicuotaIva);
        Assert.True(resultado.EsGrabado);
        Assert.Equal(1657.50m, resultado.Articulo.PrecioVenta);
        Assert.Equal(1657.50m, Assert.Single(almacen.Articulos.Guardados).PrecioVenta);
        Assert.Equal(2, almacen.Articulos.OperacionesConfirmadas);
    }

    /// <summary>D-22 de FEAT-002c: el alta lee la configuración nueva; con múltiplo 50, 1.105 y 50 dan 1.700.</summary>
    [Fact]
    public async Task Alta_DespuesDeCambiarMultiplo50_1105y50_1700()
    {
        var almacen = new CatalogoYConfiguracionEnMemoria();
        var grabacion = await GrabarConfiguracionAsync(almacen, alicuota: 21m, multiplo: 50m);

        var resultado = await new AltaArticulo(almacen.Articulos, almacen, almacen).EjecutarAsync(
            Validos(costo: 1105m, margen: 50m));

        Assert.True(grabacion.EsGrabado);
        Assert.True(resultado.EsGrabado);
        Assert.Equal(1700m, resultado.Articulo.PrecioVenta);
        Assert.Equal(2, almacen.Articulos.OperacionesConfirmadas);
    }

    private static Task<ResultadoGrabacionConfiguracion> GrabarConfiguracionAsync(
        CatalogoYConfiguracionEnMemoria almacen, decimal alicuota, decimal multiplo) =>
        new GrabarConfiguracionConRecalculo(
            almacen, almacen.Articulos, almacen, new Acceso.RelojFijo(), new FabricaRegistrosFalsa())
            .EjecutarAsync(new DatosConfiguracion(alicuota, "responsable_inscripto", multiplo, null));

    [Fact]
    public async Task Alta_DatosNulos_Lanza()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => CrearCasoDeUso().EjecutarAsync(null!));
    }
}
