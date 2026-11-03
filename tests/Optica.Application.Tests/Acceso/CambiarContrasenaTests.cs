using Optica.Application.Acceso;
using Optica.Domain.Acceso;

namespace Optica.Application.Tests.Acceso;

public class CambiarContrasenaTests
{
    private const string Actual = "clave-actual-1";
    private const string Nueva = "clave-nueva-22";
    private static readonly DateTime DefinidaAntes = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly AlmacenEnMemoria _almacen = new();
    private readonly HasherFalso _hasher = new();
    private readonly RelojAjustable _reloj = new();

    private CambiarContrasena CrearCasoDeUso() => new(_almacen, _almacen, _hasher, _almacen, _reloj);

    private IngresarConContrasena CrearIngreso() => new(_almacen, _almacen, _hasher, _almacen, _reloj);

    private void DefinirCredencial(string contrasena) =>
        _almacen.Credencial = new CredencialAcceso(_hasher.Calcular(contrasena), DefinidaAntes);

    [Fact]
    public async Task CambiarContrasena_ActualCorrecta_AceptaLaNuevaYRechazaLaAnterior()
    {
        DefinirCredencial(Actual);
        var hashAnterior = _almacen.Credencial!.Hash;
        var calculosAntes = _hasher.Calculos;

        var resultado = await CrearCasoDeUso().EjecutarAsync(Actual, Nueva);

        Assert.Equal(ResultadoCambio.Exito, resultado.Tipo);
        Assert.Empty(resultado.Errores);
        // PBKDF2 nuevo (sal nueva): otro hash, calculado una vez, con la fecha del cambio.
        Assert.Equal(calculosAntes + 1, _hasher.Calculos);
        Assert.NotEqual(hashAnterior, _almacen.Credencial!.Hash);
        Assert.Equal(RelojFijo.Instante.UtcDateTime, _almacen.Credencial.DefinidaEn);
        var evento = Assert.Single(_almacen.Eventos);
        Assert.Equal(TipoEventoAcceso.CambioContrasena, evento.Tipo);
        Assert.Equal(ResultadoEventoAcceso.Exito, evento.Resultado);
        Assert.Equal(RelojFijo.Instante.UtcDateTime, evento.FechaHora);
        // Una sola confirmación: la credencial y el evento juntos.
        Assert.Equal(1, _almacen.Confirmaciones);

        Assert.Equal(ResultadoIngreso.Exito, (await CrearIngreso().EjecutarAsync(Nueva)).Tipo);
        Assert.Equal(ResultadoIngreso.Rechazo, (await CrearIngreso().EjecutarAsync(Actual)).Tipo);
    }

    [Fact]
    public async Task CambiarContrasena_ActualIncorrecta_RechazadaSinCambiarYEventoRegistrado()
    {
        DefinirCredencial(Actual);
        var hashAnterior = _almacen.Credencial!.Hash;
        var calculosAntes = _hasher.Calculos;

        var resultado = await CrearCasoDeUso().EjecutarAsync("otra-clave-mala", Nueva);

        Assert.Equal(ResultadoCambio.Invalido, resultado.Tipo);
        var error = Assert.Single(resultado.Errores);
        Assert.Equal(ValidacionContrasena.CampoActual, error.Campo);
        Assert.Equal(CambiarContrasena.AccionActualIncorrecta, error.Accion);
        Assert.Equal("La contraseña actual no es válida.", CambiarContrasena.AccionActualIncorrecta);
        Assert.Equal(hashAnterior, _almacen.Credencial!.Hash);
        Assert.Equal(DefinidaAntes, _almacen.Credencial.DefinidaEn);
        Assert.Equal(calculosAntes, _hasher.Calculos);
        var evento = Assert.Single(_almacen.Eventos);
        Assert.Equal(TipoEventoAcceso.CambioContrasena, evento.Tipo);
        Assert.Equal(ResultadoEventoAcceso.Rechazo, evento.Resultado);
        Assert.Equal(RelojFijo.Instante.UtcDateTime, evento.FechaHora);
    }

    [Fact]
    public async Task CambiarContrasena_ActualIncorrecta_NoCuentaParaElBloqueo()
    {
        DefinirCredencial(Actual);

        for (var intento = 1; intento <= 5; intento++)
        {
            var resultado = await CrearCasoDeUso().EjecutarAsync("otra-clave-mala", Nueva);
            Assert.Equal(ResultadoCambio.Invalido, resultado.Tipo);
            _reloj.Avanzar(TimeSpan.FromSeconds(1));
        }

        // Decisión del usuario (R-04): cinco rechazos de cambio no bloquean el ingreso.
        Assert.Equal(5, _almacen.Eventos.Count(e => e is { Tipo: TipoEventoAcceso.CambioContrasena, Resultado: ResultadoEventoAcceso.Rechazo }));
        Assert.Equal(ResultadoIngreso.Exito, (await CrearIngreso().EjecutarAsync(Actual)).Tipo);
    }

    [Theory]
    [InlineData("1234567", ValidacionContrasena.AccionMuyCorta)]
    [InlineData(null, ValidacionContrasena.AccionMuyCorta)]
    [InlineData("x129", ValidacionContrasena.AccionMuyLarga)]
    [InlineData("sustituto-suelto", ValidacionContrasena.AccionCaracteresInvalidos)]
    public async Task CambiarContrasena_NuevaDe7Caracteres_InvalidoSinEvento(string? nueva, string accion)
    {
        nueva = nueva switch
        {
            "x129" => new string('x', 129),
            "sustituto-suelto" => "abcdefg\uD800",
            _ => nueva,
        };
        DefinirCredencial(Actual);
        var hashAnterior = _almacen.Credencial!.Hash;
        var calculosAntes = _hasher.Calculos;

        var resultado = await CrearCasoDeUso().EjecutarAsync(Actual, nueva);

        Assert.Equal(ResultadoCambio.Invalido, resultado.Tipo);
        var error = Assert.Single(resultado.Errores);
        Assert.Equal(ValidacionContrasena.CampoNueva, error.Campo);
        Assert.Equal(accion, error.Accion);
        Assert.Empty(_almacen.Eventos);
        Assert.Equal(0, _almacen.Confirmaciones);
        Assert.Equal(0, _hasher.Verificaciones);
        Assert.Equal(calculosAntes, _hasher.Calculos);
        Assert.Equal(hashAnterior, _almacen.Credencial!.Hash);
    }

    [Theory]
    [InlineData(null, ValidacionContrasena.AccionMuyCorta)]
    [InlineData("1234567", ValidacionContrasena.AccionMuyCorta)]
    [InlineData("x129", ValidacionContrasena.AccionMuyLarga)]
    public async Task CambiarContrasena_ActualFueraDePolitica_InvalidoEnCampoActualSinEvento(string? actual, string accion)
    {
        actual = actual == "x129" ? new string('x', 129) : actual;
        DefinirCredencial(Actual);

        var resultado = await CrearCasoDeUso().EjecutarAsync(actual, Nueva);

        Assert.Equal(ResultadoCambio.Invalido, resultado.Tipo);
        var error = Assert.Single(resultado.Errores);
        Assert.Equal(ValidacionContrasena.CampoActual, error.Campo);
        Assert.Equal(accion, error.Accion);
        Assert.Empty(_almacen.Eventos);
        Assert.Equal(0, _almacen.Consultas);
        Assert.Equal(0, _hasher.Verificaciones);
    }

    [Fact]
    public async Task CambiarContrasena_LasDosFueraDePolitica_ErrorEnCadaCampo()
    {
        DefinirCredencial(Actual);

        var resultado = await CrearCasoDeUso().EjecutarAsync("corta", "otra");

        Assert.Equal(ResultadoCambio.Invalido, resultado.Tipo);
        Assert.Equal(
            [ValidacionContrasena.CampoActual, ValidacionContrasena.CampoNueva],
            resultado.Errores.Select(e => e.Campo).ToArray());
        Assert.Empty(_almacen.Eventos);
    }

    [Fact]
    public async Task CambiarContrasena_SinCredencial_Conflicto()
    {
        var resultado = await CrearCasoDeUso().EjecutarAsync(Actual, Nueva);

        Assert.Equal(ResultadoCambio.SinCredencial, resultado.Tipo);
        Assert.Empty(resultado.Errores);
        Assert.Null(_almacen.Credencial);
        Assert.Empty(_almacen.Eventos);
        Assert.Equal(0, _almacen.Confirmaciones);
        Assert.Equal(0, _hasher.Verificaciones);
        Assert.Equal(0, _hasher.Calculos);
    }

    [Fact]
    public async Task CambiarContrasena_CredencialCambiadaAlConfirmar_ConflictoSinGuardarNada()
    {
        DefinirCredencial(Actual);
        // Otro cambio (o un restablecimiento) confirma justo antes que este.
        var otra = new CredencialAcceso(_hasher.Calcular("clave-de-otro-cambio"), DefinidaAntes);
        _almacen.AlConfirmar = () => _almacen.Credencial = otra;

        var resultado = await CrearCasoDeUso().EjecutarAsync(Actual, Nueva);

        Assert.Equal(ResultadoCambio.Conflicto, resultado.Tipo);
        Assert.Empty(resultado.Errores);
        Assert.Same(otra, _almacen.Credencial);
        Assert.Empty(_almacen.Eventos);
        Assert.Equal(1, _almacen.Confirmaciones);
    }

    [Fact]
    public async Task CambiarContrasena_DuranteUnBloqueoDeIngreso_SeCambia()
    {
        DefinirCredencial(Actual);
        for (var intento = 1; intento <= 5; intento++)
        {
            await CrearIngreso().EjecutarAsync("otra-clave-mala");
        }

        Assert.Equal(ResultadoIngreso.Bloqueado, (await CrearIngreso().EjecutarAsync(Actual)).Tipo);

        var resultado = await CrearCasoDeUso().EjecutarAsync(Actual, Nueva);

        Assert.Equal(ResultadoCambio.Exito, resultado.Tipo);
        Assert.Equal(
            (TipoEventoAcceso.CambioContrasena, ResultadoEventoAcceso.Exito),
            (_almacen.Eventos[^1].Tipo, _almacen.Eventos[^1].Resultado));
    }
}
