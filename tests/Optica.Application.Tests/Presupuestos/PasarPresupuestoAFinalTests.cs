using Optica.Application.Comun;
using Optica.Application.Presupuestos;
using Optica.Domain.Presupuestos;

namespace Optica.Application.Tests.Presupuestos;

/// <summary>
/// <see cref="PasarPresupuestoAFinal"/> (FEAT-003c, Block 3) contra el doble <see cref="PresupuestosEnMemoria"/>. Datos
/// ficticios.
/// </summary>
public class PasarPresupuestoAFinalTests
{
    private const string TextoVersion = "Volvé a abrir el presupuesto para editarlo.";
    private const string TextoCambiado = "El presupuesto cambió mientras lo editabas. Volvé a abrirlo.";
    private const string TextoFinal = "El presupuesto está en Final y no se puede modificar.";

    private readonly PresupuestosEnMemoria _unidad = new();

    private PasarPresupuestoAFinal CrearCasoDeUso() => new(_unidad, _unidad);

    private Presupuesto Sembrar(EstadoPresupuesto estado = EstadoPresupuesto.Borrador, long version = 2)
    {
        var presupuesto = new Presupuesto(
            5,
            new DateOnly(2026, 3, 10),
            estado,
            new ClientePresupuesto("Ficticiez", "Prueboncia", "99999999", null, "prueba@example.com", null),
            [new LineaPresupuesto(15, "Armazón de acetato", 1850m, 2, 10m, 1665m, 3330m)],
            version);
        _unidad.Guardados.Add(presupuesto);
        return presupuesto;
    }

    /// <summary>AC-04: pasa a Final con la versión leída; conserva número, fecha y líneas, y la versión sube a 3.</summary>
    [Fact]
    public async Task PasarAFinal_Borrador_Grabado()
    {
        var guardado = Sembrar();

        var resultado = await CrearCasoDeUso().EjecutarAsync(5, "final", 2);

        Assert.Equal(TipoEdicionPresupuesto.Grabado, resultado.Tipo);
        Assert.True(resultado.EsGrabado);
        Assert.Equal("final", resultado.Presupuesto.Estado);
        Assert.Equal(3, resultado.Presupuesto.Version);
        Assert.Equal(guardado.Fecha, resultado.Presupuesto.Fecha);
        Assert.Equal([new LineaGrabada(15, "Armazón de acetato", 1850m, 2, 10m, 1665m, 3330m, 1)], resultado.Presupuesto.Lineas);
        var final = Assert.Single(_unidad.Guardados);
        Assert.Equal(EstadoPresupuesto.Final, final.Estado);
        Assert.Contains(_unidad.Articulos.Accesos, acceso => acceso.Accion == PresupuestosEnMemoria.AccesoCambiarEstado
            && acceso.DentroDeOperacion);
        Assert.DoesNotContain(_unidad.Articulos.Accesos, acceso => acceso.Accion == PresupuestosEnMemoria.AccesoActualizar);
    }

    /// <summary>Un estado distinto de "final" o "borrador", o una versión inválida: 400 sin leer la base.</summary>
    [Theory]
    [InlineData(null, 2L, "estado")]
    [InlineData("Final", 2L, "estado")]
    [InlineData("anulado", 2L, "estado")]
    [InlineData("final", null, "version")]
    [InlineData("final", -1L, "version")]
    [InlineData("borrador", 9_007_199_254_740_992L, "version")]
    public async Task PasarAFinal_DatosInvalidos_SinLeer(string? estado, long? version, string campo)
    {
        var guardado = Sembrar();

        var resultado = await CrearCasoDeUso().EjecutarAsync(5, estado, version);

        Assert.Equal(TipoEdicionPresupuesto.Invalido, resultado.Tipo);
        var error = Assert.Single(resultado.Errores);
        Assert.Equal(campo, error.Campo);
        if (campo == "version")
        {
            Assert.Equal(TextoVersion, error.Accion);
        }

        Assert.Empty(_unidad.Articulos.Accesos);
        Assert.Same(guardado, Assert.Single(_unidad.Guardados));
    }

    [Fact]
    public async Task PasarAFinal_Inexistente_NoEncontrado()
    {
        var resultado = await CrearCasoDeUso().EjecutarAsync(5, "final", 0);

        Assert.Equal(TipoEdicionPresupuesto.NoEncontrado, resultado.Tipo);
    }

    /// <summary>AC-08: un Final no vuelve a Borrador ni pasa de nuevo a Final: final, sin escribir.</summary>
    [Theory]
    [InlineData("borrador", 2L)]
    [InlineData("final", 2L)]
    [InlineData("borrador", 1L)]
    public async Task PasarAFinal_SobreFinal_Final(string estado, long version)
    {
        var guardado = Sembrar(EstadoPresupuesto.Final);

        var resultado = await CrearCasoDeUso().EjecutarAsync(5, estado, version);

        Assert.Equal(TipoEdicionPresupuesto.Final, resultado.Tipo);
        Assert.Equal(TextoFinal, resultado.Detalle);
        Assert.Same(guardado, Assert.Single(_unidad.Guardados));
    }

    /// <summary>D-10: "borrador" sobre un Borrador es un estado inválido (400), no un conflicto.</summary>
    [Fact]
    public async Task PasarAFinal_BorradorSobreBorrador_Invalido()
    {
        var guardado = Sembrar();

        var resultado = await CrearCasoDeUso().EjecutarAsync(5, "borrador", 2);

        Assert.Equal(TipoEdicionPresupuesto.Invalido, resultado.Tipo);
        Assert.Equal("estado", Assert.Single(resultado.Errores).Campo);
        Assert.Same(guardado, Assert.Single(_unidad.Guardados));
    }

    [Fact]
    public async Task PasarAFinal_VersionVieja_Cambiado()
    {
        var guardado = Sembrar(version: 2);

        var resultado = await CrearCasoDeUso().EjecutarAsync(5, "final", 1);

        Assert.Equal(TipoEdicionPresupuesto.Cambiado, resultado.Tipo);
        Assert.Equal(TextoCambiado, resultado.Detalle);
        Assert.Same(guardado, Assert.Single(_unidad.Guardados));
    }

    /// <summary>
    /// Un conflicto al confirmar: concurrencia relee (cambiado, final o no encontrado); registro cerrado es final; otro
    /// motivo se propaga.
    /// </summary>
    [Fact]
    public async Task PasarAFinal_ConflictoAlConfirmar_FiltraPorMotivo()
    {
        var guardado = Sembrar();
        _unidad.Articulos.AntesDeConfirmar = () => Reemplazar(guardado, EstadoPresupuesto.Borrador, 3);
        Assert.Equal(TipoEdicionPresupuesto.Cambiado, (await CrearCasoDeUso().EjecutarAsync(5, "final", 2)).Tipo);

        _unidad.Guardados.Clear();
        guardado = Sembrar();
        _unidad.Articulos.AntesDeConfirmar = () => Reemplazar(guardado, EstadoPresupuesto.Final, 3);
        Assert.Equal(TipoEdicionPresupuesto.Final, (await CrearCasoDeUso().EjecutarAsync(5, "final", 2)).Tipo);

        _unidad.Guardados.Clear();
        Sembrar();
        _unidad.Articulos.AntesDeConfirmar = () => _unidad.Guardados.Clear();
        Assert.Equal(TipoEdicionPresupuesto.NoEncontrado, (await CrearCasoDeUso().EjecutarAsync(5, "final", 2)).Tipo);

        Sembrar();
        _unidad.Articulos.AntesDeConfirmar = null;
        _unidad.Articulos.ErrorAlConfirmar =
            new ConflictoAlConfirmarException("Conflicto.", MotivoConflicto.RegistroCerrado);
        Assert.Equal(TipoEdicionPresupuesto.Final, (await CrearCasoDeUso().EjecutarAsync(5, "final", 2)).Tipo);

        _unidad.Articulos.ErrorAlConfirmar = new ConflictoAlConfirmarException("Conflicto.", MotivoConflicto.Unicidad);
        var error = await Assert.ThrowsAsync<ConflictoAlConfirmarException>(
            () => CrearCasoDeUso().EjecutarAsync(5, "final", 2));
        Assert.Equal(MotivoConflicto.Unicidad, error.Motivo);
        Assert.Equal(EstadoPresupuesto.Borrador, Assert.Single(_unidad.Guardados).Estado);
    }

    private void Reemplazar(Presupuesto guardado, EstadoPresupuesto estado, long version)
    {
        var indice = _unidad.Guardados.FindIndex(presupuesto => presupuesto.Numero == guardado.Numero);
        _unidad.Guardados[indice] = new Presupuesto(
            guardado.Numero, guardado.Fecha, estado, guardado.Cliente, guardado.Lineas, version);
    }
}
