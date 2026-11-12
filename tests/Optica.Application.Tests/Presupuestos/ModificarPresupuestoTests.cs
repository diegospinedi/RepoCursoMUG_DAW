using Microsoft.Extensions.Logging;
using Optica.Application.Comun;
using Optica.Application.Presupuestos;
using Optica.Domain.Presupuestos;

namespace Optica.Application.Tests.Presupuestos;

/// <summary>
/// <see cref="ModificarPresupuesto"/> (FEAT-003c, Block 3) contra el doble <see cref="PresupuestosEnMemoria"/>, que imita
/// el token de versión y los triggers de cierre. Datos ficticios: DNI 99.999.999 y correos de example.com.
/// </summary>
public class ModificarPresupuestoTests
{
    private const string TextoVersion = "Volvé a abrir el presupuesto para editarlo.";
    private const string TextoLineaAjena = "La línea no corresponde a este presupuesto.";
    private const string TextoArticuloNoDisponible = "Ese artículo ya no está disponible. Quitá la línea y buscalo de nuevo";
    private const string TextoCambiado = "El presupuesto cambió mientras lo editabas. Volvé a abrirlo.";
    private const string TextoFinal = "El presupuesto está en Final y no se puede modificar.";
    private const string TextoTotal = "El total del presupuesto es demasiado alto.";

    private static readonly DateOnly FechaAlta = new(2026, 3, 10);

    private readonly PresupuestosEnMemoria _unidad = new();

    public ModificarPresupuestoTests()
    {
        _unidad.SembrarArticulo(15, "Armazón de acetato", 1850m);
        _unidad.SembrarArticulo(16, "Lente orgánico", 1000m);
    }

    private ModificarPresupuesto CrearCasoDeUso() => new(_unidad, _unidad.Articulos, _unidad);

    private static DatosCliente Cliente(string? domicilio = null) =>
        new("Ficticiez", "Prueboncia", "99.999.999", domicilio, "prueba@example.com", null);

    /// <summary>
    /// Guarda el presupuesto <paramref name="numero"/> con dos líneas: el armazón 15 (1.850 × 2 con 10 %) y el lente 16
    /// (1.000 × 1), con la versión y el estado pedidos.
    /// </summary>
    private Presupuesto Sembrar(
        long numero = 1, EstadoPresupuesto estado = EstadoPresupuesto.Borrador, long version = 3)
    {
        var presupuesto = new Presupuesto(
            numero,
            FechaAlta,
            estado,
            new ClientePresupuesto("Ficticiez", "Prueboncia", "99999999", null, "prueba@example.com", null),
            [
                new LineaPresupuesto(15, "Armazón de acetato", 1850m, 2, 10m, 1665m, 3330m),
                new LineaPresupuesto(16, "Lente orgánico", 1000m, 1, 0m, 1000m, 1000m),
            ],
            version);
        _unidad.Guardados.Add(presupuesto);
        return presupuesto;
    }

    private static DatosPresupuesto Datos(params DatosLinea?[] lineas) => new(Cliente(), lineas);

    private void AssertSinLeerLaBase()
    {
        Assert.Empty(_unidad.Articulos.Accesos);
        Assert.Equal(0, _unidad.Articulos.Operaciones);
    }

    private void AssertSinEscribir(Presupuesto guardado) => Assert.Same(guardado, Assert.Single(_unidad.Guardados));

    [Fact]
    public async Task Modificar_DatosInvalidos_TodosLosErroresSinLeer()
    {
        var guardado = Sembrar();

        var resultado = await CrearCasoDeUso().EjecutarAsync(
            1,
            new DatosPresupuesto(
                new DatosCliente("Ficticiez", "Prueboncia", "123", null, null, null),
                [
                    new DatosLinea(15, 0, 0m, Orden: 1),
                    new DatosLinea(16, 1, 0m, Orden: 0),
                    new DatosLinea(16, 1, 0m, Orden: 1),
                ]),
            version: null);

        Assert.Equal(TipoEdicionPresupuesto.Invalido, resultado.Tipo);
        Assert.Equal(
            [
                new ErrorValidacion("version", TextoVersion),
                new ErrorValidacion("dni", "El DNI debe tener 7 u 8 dígitos"),
                new ErrorValidacion("lineas[0].cantidad", "La cantidad debe ser un número entero mayor a 0"),
                new ErrorValidacion("lineas[1]", TextoLineaAjena),
                new ErrorValidacion("lineas[2]", TextoLineaAjena),
            ],
            resultado.Errores);
        AssertSinLeerLaBase();
        AssertSinEscribir(guardado);
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(9_007_199_254_740_992L)]
    [InlineData(long.MaxValue)]
    public async Task Modificar_VersionFueraDeRango_ErrorEnVersionSinLeer(long version)
    {
        Sembrar();

        var resultado = await CrearCasoDeUso().EjecutarAsync(1, Datos(new DatosLinea(15, 1, 0m)), version);

        Assert.Equal([new ErrorValidacion("version", TextoVersion)], resultado.Errores);
        AssertSinLeerLaBase();
    }

    [Fact]
    public async Task Modificar_Inexistente_NoEncontrado()
    {
        var resultado = await CrearCasoDeUso().EjecutarAsync(9, Datos(new DatosLinea(15, 1, 0m)), 0);

        Assert.Equal(TipoEdicionPresupuesto.NoEncontrado, resultado.Tipo);
        Assert.Null(resultado.Presupuesto);
        Assert.Empty(_unidad.Guardados);
    }

    /// <summary>AC-07: un Final no se modifica, aunque la versión sea vieja y la línea ajena; sin escribir.</summary>
    [Theory]
    [InlineData(3L, 1)]
    [InlineData(2L, 7)]
    public async Task Modificar_Final_FinalSinEscribir(long version, int orden)
    {
        var guardado = Sembrar(estado: EstadoPresupuesto.Final);

        var resultado = await CrearCasoDeUso().EjecutarAsync(
            1, Datos(new DatosLinea(15, 1, 0m, Orden: orden)), version);

        Assert.Equal(TipoEdicionPresupuesto.Final, resultado.Tipo);
        Assert.Equal(TextoFinal, resultado.Detalle);
        AssertSinEscribir(guardado);
        Assert.DoesNotContain(_unidad.Articulos.Accesos, acceso => acceso.Accion == PresupuestosEnMemoria.AccesoActualizar);
    }

    /// <summary>D-10: la versión se compara antes que el <c>orden</c>: con una versión vieja, cambiado aunque la línea sea ajena.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    public async Task Modificar_VersionVieja_CambiadoAntesQueElOrden(int orden)
    {
        var guardado = Sembrar(version: 3);

        var resultado = await CrearCasoDeUso().EjecutarAsync(
            1, Datos(new DatosLinea(16, 1, 0m, Orden: orden)), 2);

        Assert.Equal(TipoEdicionPresupuesto.Cambiado, resultado.Tipo);
        Assert.Equal(TextoCambiado, resultado.Detalle);
        Assert.Empty(resultado.Errores);
        AssertSinEscribir(guardado);
    }

    /// <summary>
    /// AC-09 y AC-10 (D-02): una línea existente conserva Código, Descripción y Precio Unitario aunque el artículo cambie
    /// de precio o esté inactivo, y recalcula con la Cantidad y el Descuento nuevos; una línea nueva toma el precio actual.
    /// Las líneas se renumeran en el orden recibido; la fecha y el número no cambian; la versión pasa a 4.
    /// </summary>
    [Fact]
    public async Task Modificar_LineasExistentesYNueva_ConservaCopiaYRenumera()
    {
        Sembrar();
        _unidad.Articulos.Guardados.Clear();
        _unidad.SembrarArticulo(15, "Armazón renombrado", 9999m, activo: false);
        _unidad.SembrarArticulo(16, "Lente orgánico", 2000m, activo: false);
        _unidad.SembrarArticulo(17, "Estuche", 500m);

        var resultado = await CrearCasoDeUso().EjecutarAsync(
            1,
            new DatosPresupuesto(
                Cliente("Calle Ficticia 123"),
                [new DatosLinea(17, 2, 0m), new DatosLinea(15, 3, 50m, Orden: 1)]),
            3);

        Assert.Equal(TipoEdicionPresupuesto.Grabado, resultado.Tipo);
        Assert.True(resultado.EsGrabado);
        var grabado = resultado.Presupuesto;
        Assert.Equal(1, grabado.Numero);
        Assert.Equal(FechaAlta, grabado.Fecha);
        Assert.Equal("borrador", grabado.Estado);
        Assert.Equal(4, grabado.Version);
        Assert.Equal("Calle Ficticia 123", grabado.Cliente.Domicilio);
        Assert.Equal("99999999", grabado.Cliente.Dni);
        Assert.Equal(
            [
                new LineaGrabada(17, "Estuche", 500m, 2, 0m, 500m, 1000m, 1),
                new LineaGrabada(15, "Armazón de acetato", 1850m, 3, 50m, 925m, 2775m, 2),
            ],
            grabado.Lineas);
        Assert.Equal(3775m, grabado.Total);
        var guardado = Assert.Single(_unidad.Guardados);
        Assert.Equal(4, guardado.Version);
        Assert.Equal(FechaAlta, guardado.Fecha);
    }

    /// <summary>D-03: un <c>orden</c> inexistente o con otro código es una línea ajena, junto a la línea; sin escribir.</summary>
    [Fact]
    public async Task Modificar_LineaAjena_InvalidoSinEscribir()
    {
        var guardado = Sembrar();

        var resultado = await CrearCasoDeUso().EjecutarAsync(
            1,
            Datos(
                new DatosLinea(15, 1, 0m, Orden: 1),
                new DatosLinea(15, 1, 0m, Orden: 2),
                new DatosLinea(16, 1, 0m, Orden: 3)),
            3);

        Assert.Equal(TipoEdicionPresupuesto.Invalido, resultado.Tipo);
        Assert.Equal(
            [new ErrorValidacion("lineas[1]", TextoLineaAjena), new ErrorValidacion("lineas[2]", TextoLineaAjena)],
            resultado.Errores);
        AssertSinEscribir(guardado);
    }

    /// <summary>Una línea nueva con un artículo inactivo o inexistente se rechaza como en el alta; sin escribir.</summary>
    [Fact]
    public async Task Modificar_ArticuloNuevoInactivoOInexistente_Invalido()
    {
        var guardado = Sembrar();
        _unidad.SembrarArticulo(18, "Cordón", 100m, activo: false);

        var resultado = await CrearCasoDeUso().EjecutarAsync(
            1, Datos(new DatosLinea(18, 1, 0m), new DatosLinea(15, 1, 0m, Orden: 1), new DatosLinea(99, 1, 0m)), 3);

        Assert.Equal(
            [
                new ErrorValidacion("lineas[0]", TextoArticuloNoDisponible),
                new ErrorValidacion("lineas[2]", TextoArticuloNoDisponible),
            ],
            resultado.Errores);
        AssertSinEscribir(guardado);
    }

    /// <summary>
    /// Los topes del alta valen también con líneas existentes: 9.007.199.000.000 × 10 entra en el tope, y con la línea
    /// nueva de 1.000 × 9.999 el total lo supera. Sin escribir.
    /// </summary>
    [Fact]
    public async Task Modificar_TotalExcedido_Invalido()
    {
        var guardado = new Presupuesto(
            1,
            FechaAlta,
            EstadoPresupuesto.Borrador,
            new ClientePresupuesto("Ficticiez", "Prueboncia", "99999999", null, null, null),
            [new LineaPresupuesto(15, "Armazón", 9_007_199_000_000m, 1, 0m, 9_007_199_000_000m, 9_007_199_000_000m)],
            0);
        _unidad.Guardados.Add(guardado);

        var resultado = await CrearCasoDeUso().EjecutarAsync(
            1, Datos(new DatosLinea(15, 10, 0m, Orden: 1), new DatosLinea(16, 9999, 0m)), 0);

        Assert.Equal(TipoEdicionPresupuesto.Invalido, resultado.Tipo);
        Assert.Equal([new ErrorValidacion("lineas", TextoTotal)], resultado.Errores);
        AssertSinEscribir(guardado);
    }

    /// <summary>
    /// D-01: otra sesión grabó entre la lectura y la confirmación. Con motivo de concurrencia, la relectura decide:
    /// cambiado si sigue en Borrador, final si pasó a Final, no encontrado si ya no está.
    /// </summary>
    [Fact]
    public async Task Modificar_ConflictoDeConcurrencia_RelecturaDecide()
    {
        var guardado = Sembrar();
        _unidad.Articulos.AntesDeConfirmar = () => Reemplazar(guardado, guardado.Estado, guardado.Version + 1);

        var cambiado = await CrearCasoDeUso().EjecutarAsync(1, Datos(new DatosLinea(15, 1, 0m, Orden: 1)), 3);

        Assert.Equal(TipoEdicionPresupuesto.Cambiado, cambiado.Tipo);
        Assert.Equal(TextoCambiado, cambiado.Detalle);
        Assert.Equal(4, Assert.Single(_unidad.Guardados).Version);

        // Con motivo de concurrencia forzado, la relectura ve que otra sesión lo pasó a Final.
        _unidad.Guardados.Clear();
        guardado = Sembrar();
        _unidad.Articulos.ErrorAlConfirmar = new ConflictoAlConfirmarException("Conflicto.", MotivoConflicto.Concurrencia);
        _unidad.Articulos.AntesDeConfirmar = () => Reemplazar(guardado, EstadoPresupuesto.Final, 4);
        var final = await CrearCasoDeUso().EjecutarAsync(1, Datos(new DatosLinea(15, 1, 0m, Orden: 1)), 3);
        Assert.Equal(TipoEdicionPresupuesto.Final, final.Tipo);
        Assert.Equal(TextoFinal, final.Detalle);

        _unidad.Guardados.Clear();
        Sembrar();
        _unidad.Articulos.AntesDeConfirmar = () => _unidad.Guardados.Clear();
        var borrado = await CrearCasoDeUso().EjecutarAsync(1, Datos(new DatosLinea(15, 1, 0m, Orden: 1)), 3);
        Assert.Equal(TipoEdicionPresupuesto.NoEncontrado, borrado.Tipo);
    }

    /// <summary>
    /// D-14: si otra sesión pasó el presupuesto a Final, la base responde registro cerrado aunque la versión también haya
    /// cambiado: final, sin escribir.
    /// </summary>
    [Fact]
    public async Task Modificar_ConflictoRegistroCerrado_Final()
    {
        var guardado = Sembrar();
        _unidad.Articulos.AntesDeConfirmar = () => Reemplazar(guardado, EstadoPresupuesto.Final, 4);

        var resultado = await CrearCasoDeUso().EjecutarAsync(1, Datos(new DatosLinea(15, 1, 0m, Orden: 1)), 3);

        Assert.Equal(TipoEdicionPresupuesto.Final, resultado.Tipo);
        Assert.Equal(TextoFinal, resultado.Detalle);
        Assert.Equal(EstadoPresupuesto.Final, Assert.Single(_unidad.Guardados).Estado);
    }

    /// <summary>Un conflicto de unicidad no es un caso previsto de la modificación: se propaga sin atenderlo.</summary>
    [Fact]
    public async Task Modificar_OtroMotivoDeConflicto_SePropaga()
    {
        var guardado = Sembrar();
        _unidad.Articulos.ErrorAlConfirmar = new ConflictoAlConfirmarException("Conflicto.", MotivoConflicto.Unicidad);

        var error = await Assert.ThrowsAsync<ConflictoAlConfirmarException>(() =>
            CrearCasoDeUso().EjecutarAsync(1, Datos(new DatosLinea(15, 1, 0m, Orden: 1)), 3));

        Assert.Equal(MotivoConflicto.Unicidad, error.Motivo);
        AssertSinEscribir(guardado);
    }

    /// <summary>Las lecturas, el registro y la confirmación van dentro de una sola operación en transacción (ADR-013).</summary>
    [Fact]
    public async Task Modificar_LeeYGrabaDentroDeLaOperacion()
    {
        Sembrar();

        var valido = await CrearCasoDeUso().EjecutarAsync(
            1, Datos(new DatosLinea(15, 1, 0m, Orden: 1), new DatosLinea(16, 1, 0m)), 3);

        Assert.True(valido.EsGrabado);
        Assert.Equal(1, _unidad.Articulos.Operaciones);
        Assert.Equal(1, _unidad.Articulos.OperacionesConfirmadas);
        var hastaConfirmar = _unidad.Articulos.Accesos
            .TakeWhile(acceso => acceso.Accion != "ConfirmarAsync")
            .ToList();
        Assert.All(hastaConfirmar, acceso => Assert.True(acceso.DentroDeOperacion, acceso.Accion));
        Assert.Contains(hastaConfirmar, acceso => acceso.Accion == PresupuestosEnMemoria.AccesoActualizar);
        // La relectura del presupuesto grabado va después de la operación.
        Assert.False(_unidad.Articulos.Accesos[^1].DentroDeOperacion);
        Assert.Equal(PresupuestosEnMemoria.AccesoObtener, _unidad.Articulos.Accesos[^1].Accion);
    }

    /// <summary>Ley 25.326: el caso de uso no recibe un registrador, y el resultado no muestra datos en ToString.</summary>
    [Fact]
    public async Task Modificar_NoRegistraDatosDelCliente()
    {
        var parametros = typeof(ModificarPresupuesto).GetConstructors()
            .Concat(typeof(PasarPresupuestoAFinal).GetConstructors())
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parametro => parametro.ParameterType);
        Assert.DoesNotContain(parametros, tipo => typeof(ILogger).IsAssignableFrom(tipo)
            || typeof(ILoggerFactory).IsAssignableFrom(tipo));
        Sembrar();

        var resultado = await CrearCasoDeUso().EjecutarAsync(1, Datos(new DatosLinea(15, 1, 0m, Orden: 1)), 3);

        Assert.True(resultado.EsGrabado);
        foreach (var texto in new[] { resultado.ToString(), resultado.Presupuesto.ToString(), new DatosLinea(15, 7531, 12.34m, 4).ToString() })
        {
            Assert.DoesNotContain("99999999", texto ?? string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain("Ficticiez", texto ?? string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain("example.com", texto ?? string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain("7531", texto ?? string.Empty, StringComparison.Ordinal);
        }
    }

    /// <summary>Reemplaza el guardado como si otra sesión hubiera confirmado con ese estado y esa versión.</summary>
    private void Reemplazar(Presupuesto guardado, EstadoPresupuesto estado, long version)
    {
        var indice = _unidad.Guardados.FindIndex(presupuesto => presupuesto.Numero == guardado.Numero);
        _unidad.Guardados[indice] = new Presupuesto(
            guardado.Numero, guardado.Fecha, estado, guardado.Cliente, guardado.Lineas, version);
    }
}
