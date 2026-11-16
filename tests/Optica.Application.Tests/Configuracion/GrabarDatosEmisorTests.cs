using Optica.Application.Comun;
using Optica.Application.Configuracion;
using Optica.Application.Tests.Acceso;
using Optica.Application.Tests.Catalogo;

namespace Optica.Application.Tests.Configuracion;

/// <summary>
/// La grabación de los datos del emisor (FEAT-009, Block 2): los cinco juntos, dentro de la transacción, y un registro
/// con la fecha y hora UTC y los nombres de los campos que cambiaron, nunca sus valores (R-06). Todos los datos son
/// inventados; el CUIT es el ficticio 20-00000000-1 (D-33).
/// </summary>
public class GrabarDatosEmisorTests
{
    private const string RazonSocialAnterior = "Zq7Anterior Ficticia SRL";
    private const string RazonSocialNueva = "Zq7Nueva Ficticia SA";
    private const string Domicilio = "Zq7Calle Inventada 123, Ciudad Ficticia";
    private const string CuitAnterior = "20000000001";
    private const string CuitNuevoConGuiones = "27-00000000-6";
    private const string CuitNuevo = "27000000006";
    private const string IngresosBrutos = "Zq7IB-0001";
    private const string Inicio = "2020-03-01";

    private readonly DatosEmisorEnMemoria _almacen = new();
    private readonly FabricaRegistrosFalsa _registros = new();
    private readonly RelojFijo _reloj = new();

    private GrabarDatosEmisor CrearCasoDeUso() => new(_almacen, _almacen, _reloj, _registros);

    [Fact]
    public async Task Grabar_CincoDatos_ReemplazaYRegistraSinValores()
    {
        _almacen.Guardados = DatosEmisorEnMemoria.Crear(RazonSocialAnterior, Domicilio, CuitAnterior, IngresosBrutos, Inicio);

        var resultado = await CrearCasoDeUso().EjecutarAsync(
            new DatosEmisorRecibidos(RazonSocialNueva, Domicilio, CuitNuevoConGuiones, IngresosBrutos, Inicio));

        // Grabados los cinco, el CUIT sin guiones (AC-01, AC-03), con lectura y reemplazo dentro de la transacción.
        Assert.True(resultado.EsGrabado);
        Assert.Empty(resultado.Errores);
        Assert.Equal(
            new DatosEmisorVigentes(RazonSocialNueva, Domicilio, CuitNuevo, IngresosBrutos, Inicio), resultado.Datos);
        var guardados = _almacen.Guardados;
        Assert.NotNull(guardados);
        Assert.Equal(RazonSocialNueva, guardados.RazonSocial);
        Assert.Equal(Domicilio, guardados.DomicilioComercial);
        Assert.Equal(CuitNuevo, guardados.Cuit.Valor);
        Assert.Equal(IngresosBrutos, guardados.IngresosBrutos);
        Assert.Equal(new DateOnly(2020, 3, 1), guardados.InicioActividades);
        Assert.Equal(1, _almacen.Transacciones);
        Assert.Equal(1, _almacen.Lecturas);
        Assert.Equal(1, _almacen.Reemplazos);
        Assert.Equal(0, _almacen.AccesosFueraDeTransaccion);

        // El registro: su categoría, la fecha y hora UTC y los nombres de los dos campos que cambiaron.
        Assert.Contains(GrabarDatosEmisor.CategoriaRegistro, _registros.Categorias);
        Assert.StartsWith("Optica.Application.Configuracion", GrabarDatosEmisor.CategoriaRegistro, StringComparison.Ordinal);
        var entrada = Assert.Single(_registros.Entradas);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Information, entrada.Nivel);
        Assert.Null(entrada.Error);
        Assert.Equal("razonSocial, cuit", entrada.Valores["CamposCambiados"]);
        Assert.Equal(RelojFijo.Instante.UtcDateTime, entrada.Valores["FechaHoraUtc"]);
        Assert.Contains(RelojFijo.Instante.UtcDateTime.ToString("O"), entrada.Mensaje, StringComparison.Ordinal);

        // Ningún valor, ni anterior ni nuevo, ni en el mensaje ni en sus marcadores (R-06).
        var textoRegistrado = entrada.Mensaje + " " + string.Join(" ", entrada.Valores.Values);
        foreach (var valor in new[]
        {
            RazonSocialAnterior, RazonSocialNueva, Domicilio, CuitAnterior, CuitNuevo, CuitNuevoConGuiones, IngresosBrutos,
            Inicio, "Zq7",
        })
        {
            Assert.DoesNotContain(valor, textoRegistrado, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Grabar_SinDatosPrevios_RegistraLosCincoCampos()
    {
        var resultado = await CrearCasoDeUso().EjecutarAsync(
            new DatosEmisorRecibidos(RazonSocialNueva, Domicilio, CuitAnterior, IngresosBrutos, Inicio));

        Assert.True(resultado.EsGrabado);
        Assert.NotNull(_almacen.Guardados);
        var entrada = Assert.Single(_registros.Entradas);
        Assert.Equal(
            "razonSocial, domicilioComercial, cuit, ingresosBrutos, inicioActividades", entrada.Valores["CamposCambiados"]);
    }

    [Fact]
    public async Task Grabar_SinRazonSocial_RechazadoSinGrabar()
    {
        var anteriores = DatosEmisorEnMemoria.Crear(RazonSocialAnterior, Domicilio, CuitAnterior, IngresosBrutos, Inicio);
        _almacen.Guardados = anteriores;

        var resultado = await CrearCasoDeUso().EjecutarAsync(
            new DatosEmisorRecibidos("   ", Domicilio, CuitNuevoConGuiones, IngresosBrutos, Inicio));

        // AC-02: no se graba y el error va junto al campo, con la acción que lo corrige.
        Assert.False(resultado.EsGrabado);
        Assert.Null(resultado.Datos);
        Assert.Equal(
            [new ErrorValidacion(ValidacionConfiguracion.CampoRazonSocial, ValidacionConfiguracion.AccionRazonSocialFaltante)],
            resultado.Errores);
        Assert.Same(anteriores, _almacen.Guardados);
        Assert.Equal(0, _almacen.Reemplazos);
        Assert.Equal(0, _almacen.Confirmaciones);
        Assert.Empty(_registros.Entradas);
    }

    [Fact]
    public async Task Grabar_VariosInvalidos_DevuelveUnErrorPorCampo()
    {
        var resultado = await CrearCasoDeUso().EjecutarAsync(
            new DatosEmisorRecibidos(null, new string('a', 201), "20-00000000-5", "", "2099-01-01"));

        Assert.False(resultado.EsGrabado);
        Assert.Equal(
            [
                new ErrorValidacion("razonSocial", ValidacionConfiguracion.AccionRazonSocialFaltante),
                new ErrorValidacion("domicilioComercial", ValidacionConfiguracion.AccionDomicilioComercialLargo),
                new ErrorValidacion("cuit", ValidacionConfiguracion.AccionCuitInvalido),
                new ErrorValidacion("ingresosBrutos", ValidacionConfiguracion.AccionIngresosBrutosFaltante),
                new ErrorValidacion("inicioActividades", ValidacionConfiguracion.AccionInicioActividadesPosterior),
            ],
            resultado.Errores);
        Assert.Null(_almacen.Guardados);
        Assert.Empty(_registros.Entradas);
    }

    /// <summary>
    /// Una falla al leer los vigentes o al confirmar revierte la transacción, propaga la misma excepción, deja los datos
    /// como estaban y no registra la grabación.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Grabar_FallaAlLeerOAlConfirmar_NoRegistraNiGraba(bool alLeer)
    {
        var anteriores = DatosEmisorEnMemoria.Crear(RazonSocialAnterior, Domicilio, CuitAnterior, IngresosBrutos, Inicio);
        _almacen.Guardados = anteriores;
        var falla = new InvalidOperationException("Falla de la base.");
        if (alLeer)
        {
            _almacen.ErrorAlLeer = falla;
        }
        else
        {
            _almacen.ErrorAlConfirmar = falla;
        }

        var lanzada = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CrearCasoDeUso().EjecutarAsync(
                new DatosEmisorRecibidos(RazonSocialNueva, Domicilio, CuitNuevoConGuiones, IngresosBrutos, Inicio)));

        Assert.Same(falla, lanzada);
        Assert.Equal(1, _almacen.Transacciones);
        Assert.Same(anteriores, _almacen.Guardados);
        Assert.Equal(alLeer ? 0 : 1, _almacen.Reemplazos);
        Assert.Empty(_registros.Entradas);
    }

    [Fact]
    public async Task Grabar_MismosDatos_RegistraNinguno()
    {
        _almacen.Guardados = DatosEmisorEnMemoria.Crear(RazonSocialAnterior, Domicilio, CuitAnterior, IngresosBrutos, Inicio);

        // El mismo CUIT con guiones y los textos con espacios alrededor: lo grabado es igual a lo vigente.
        var resultado = await CrearCasoDeUso().EjecutarAsync(
            new DatosEmisorRecibidos(
                " " + RazonSocialAnterior + " ", Domicilio, "20-00000000-1", IngresosBrutos, Inicio));

        Assert.True(resultado.EsGrabado);
        Assert.Equal(1, _almacen.Confirmaciones);
        var entrada = Assert.Single(_registros.Entradas);
        Assert.Equal("ninguno", entrada.Valores["CamposCambiados"]);
    }

    [Fact]
    public void DatosEmisorVigentes_ToString_SinValores()
    {
        var vigentes = new DatosEmisorVigentes(RazonSocialNueva, Domicilio, CuitNuevo, IngresosBrutos, Inicio);
        var recibidos = new DatosEmisorRecibidos(RazonSocialNueva, Domicilio, CuitNuevo, IngresosBrutos, Inicio);

        Assert.Equal("DatosEmisorVigentes { }", vigentes.ToString());
        Assert.Equal("DatosEmisorRecibidos { }", recibidos.ToString());
    }
}
