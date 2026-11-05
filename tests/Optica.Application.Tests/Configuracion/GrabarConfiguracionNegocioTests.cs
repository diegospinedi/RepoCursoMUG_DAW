using Microsoft.Extensions.Logging;
using Optica.Application.Comun;
using Optica.Application.Configuracion;
using Optica.Application.Tests.Acceso;
using Optica.Domain.Configuracion;

namespace Optica.Application.Tests.Configuracion;

public class GrabarConfiguracionNegocioTests
{
    private readonly ConfiguracionEnMemoria _almacen = new();
    private readonly RegistroFalso<GrabarConfiguracionNegocio> _registro = new();
    private readonly RelojFijo _reloj = new();

    private GrabarConfiguracionNegocio CrearCasoDeUso() => new(_almacen, _almacen, _reloj, _registro);

    private ObtenerConfiguracionNegocio CrearConsulta() => new(_almacen);

    private static DatosConfiguracion Validos(
        decimal? alicuota = 21m,
        string? condicion = "responsable_inscripto",
        decimal? multiplo = 0.01m,
        decimal? tope = 10_000_000m) => new(alicuota, condicion, multiplo, tope);

    [Fact]
    public async Task Grabar_TopeNuevo_ObtenerDevuelveElNuevo()
    {
        var resultado = await CrearCasoDeUso().EjecutarAsync(Validos(tope: 4_500_000.50m));

        Assert.Equal(TipoResultado.Exito, resultado.Tipo);
        Assert.Empty(resultado.Errores);
        var vigente = await CrearConsulta().EjecutarAsync();
        Assert.Equal(4_500_000.50m, vigente.TopeIdentificacion);
        Assert.False(vigente.FaltaTope);
        Assert.Equal(21m, vigente.AlicuotaIva);
        Assert.Equal("responsable_inscripto", vigente.CondicionFiscal);
        Assert.Equal(0.01m, vigente.MultiploRedondeo);
        Assert.Equal(1, _almacen.Confirmaciones);
    }

    [Fact]
    public async Task Grabar_Monotributo_ObtenerDevuelveMonotributo()
    {
        var resultado = await CrearCasoDeUso().EjecutarAsync(Validos(condicion: "monotributo"));

        Assert.Equal(TipoResultado.Exito, resultado.Tipo);
        Assert.Equal(CondicionFiscal.Monotributo, _almacen.Guardada!.CondicionFiscal);
        Assert.Equal("monotributo", (await CrearConsulta().EjecutarAsync()).CondicionFiscal);
    }

    [Fact]
    public async Task Grabar_Alicuota101_RechazadoSinGrabar()
    {
        var anterior = _almacen.Guardada;

        var resultado = await CrearCasoDeUso().EjecutarAsync(Validos(alicuota: 101m));

        Assert.Equal(TipoResultado.Invalido, resultado.Tipo);
        var error = Assert.Single(resultado.Errores);
        Assert.Equal(new ErrorValidacion("alicuotaIva", "La alícuota de IVA debe estar entre 0 y 100."), error);
        Assert.Same(anterior, _almacen.Guardada);
        Assert.Equal(0, _almacen.Reemplazos);
        Assert.Equal(0, _almacen.Confirmaciones);
    }

    [Fact]
    public async Task Grabar_Multiplo0coma001_RechazadoConMinimo()
    {
        var resultado = await CrearCasoDeUso().EjecutarAsync(Validos(multiplo: 0.001m));

        Assert.Equal(TipoResultado.Invalido, resultado.Tipo);
        var error = Assert.Single(resultado.Errores);
        Assert.Equal(new ErrorValidacion("multiploRedondeo", "El valor mínimo es 0,01."), error);
        Assert.Equal(0, _almacen.Confirmaciones);
    }

    [Fact]
    public async Task Grabar_Tope0_RechazadoMayorACero()
    {
        var resultado = await CrearCasoDeUso().EjecutarAsync(Validos(tope: 0m));

        Assert.Equal(TipoResultado.Invalido, resultado.Tipo);
        var error = Assert.Single(resultado.Errores);
        Assert.Equal(new ErrorValidacion("topeIdentificacion", "El tope debe ser mayor a 0."), error);
        Assert.Equal(0, _almacen.Confirmaciones);
    }

    [Fact]
    public async Task Grabar_VariosCamposInvalidos_DevuelveTodosLosErrores()
    {
        var resultado = await CrearCasoDeUso().EjecutarAsync(Validos(alicuota: 101m, condicion: "exento", tope: -1m));

        Assert.Equal(TipoResultado.Invalido, resultado.Tipo);
        Assert.Equal(
            [
                new ErrorValidacion("alicuotaIva", "La alícuota de IVA debe estar entre 0 y 100."),
                new ErrorValidacion("condicionFiscal", "Elegí Responsable Inscripto o Monotributo."),
                new ErrorValidacion("topeIdentificacion", "El tope debe ser mayor a 0."),
            ],
            resultado.Errores);
        Assert.Equal(0, _almacen.Reemplazos);
        Assert.Equal(0, _almacen.Confirmaciones);
    }

    [Fact]
    public async Task Grabar_TopeAusente_GrabaSinTope()
    {
        // D-02: también se puede vaciar un tope ya cargado.
        _almacen.Guardada = new ConfiguracionNegocio(21m, CondicionFiscal.ResponsableInscripto, 0.01m, 5_000_000m);

        var resultado = await CrearCasoDeUso().EjecutarAsync(Validos(tope: null));

        Assert.Equal(TipoResultado.Exito, resultado.Tipo);
        var vigente = await CrearConsulta().EjecutarAsync();
        Assert.Null(vigente.TopeIdentificacion);
        Assert.True(vigente.FaltaTope);
    }

    [Fact]
    public async Task Grabar_Exitoso_RegistraValoresAnterioresYNuevos()
    {
        _almacen.Guardada = new ConfiguracionNegocio(21m, CondicionFiscal.ResponsableInscripto, 0.01m, null);

        await CrearCasoDeUso().EjecutarAsync(Validos(
            alicuota: 10.5m, condicion: "monotributo", multiplo: 10m, tope: 4_500_000m));

        var entrada = Assert.Single(_registro.Entradas);
        Assert.Equal(LogLevel.Information, entrada.Nivel);
        Assert.StartsWith("Configuración de negocio grabada", entrada.Mensaje);
        Assert.Null(entrada.Error);
        Assert.Equal(21m, entrada.Valores["AlicuotaIvaAnterior"]);
        Assert.Equal("responsable_inscripto", entrada.Valores["CondicionFiscalAnterior"]);
        Assert.Equal(0.01m, entrada.Valores["MultiploRedondeoAnterior"]);
        Assert.Null(entrada.Valores["TopeIdentificacionAnterior"]);
        Assert.Equal(10.5m, entrada.Valores["AlicuotaIva"]);
        Assert.Equal("monotributo", entrada.Valores["CondicionFiscal"]);
        Assert.Equal(10m, entrada.Valores["MultiploRedondeo"]);
        Assert.Equal(4_500_000m, entrada.Valores["TopeIdentificacion"]);
        Assert.Equal(RelojFijo.Instante.UtcDateTime, entrada.Valores["FechaHoraUtc"]);
        // Se registra lo confirmado: el registro va después de la confirmación.
        Assert.Equal(1, _almacen.Confirmaciones);
    }

    [Fact]
    public async Task Grabar_Invalido_NoRegistraNiConfirma()
    {
        var resultado = await CrearCasoDeUso().EjecutarAsync(new DatosConfiguracion(null, null, null, 0.005m));

        Assert.Equal(TipoResultado.Invalido, resultado.Tipo);
        Assert.Equal(4, resultado.Errores.Count);
        Assert.Empty(_registro.Entradas);
        Assert.Equal(0, _almacen.Lecturas);
        Assert.Equal(0, _almacen.Reemplazos);
        Assert.Equal(0, _almacen.Confirmaciones);
    }

    [Fact]
    public async Task Grabar_ConflictoAlConfirmar_SePropaga()
    {
        var anterior = _almacen.Guardada;
        _almacen.ErrorAlConfirmar = new ConflictoAlConfirmarException("Conflicto de prueba.", MotivoConflicto.Concurrencia);

        await Assert.ThrowsAsync<ConflictoAlConfirmarException>(
            () => CrearCasoDeUso().EjecutarAsync(Validos(tope: 1m)));

        Assert.Same(anterior, _almacen.Guardada);
        Assert.Empty(_registro.Entradas);
    }

    [Fact]
    public async Task Grabar_SinFila_PropagaError()
    {
        _almacen.Guardada = null;

        await Assert.ThrowsAsync<InvalidOperationException>(() => CrearCasoDeUso().EjecutarAsync(Validos()));

        Assert.Equal(0, _almacen.Confirmaciones);
        Assert.Empty(_registro.Entradas);
    }
}
