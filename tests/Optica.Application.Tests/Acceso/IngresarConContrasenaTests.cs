using Optica.Application.Acceso;
using Optica.Domain.Acceso;

namespace Optica.Application.Tests.Acceso;

public class IngresarConContrasenaTests
{
    private readonly AlmacenEnMemoria _almacen = new();
    private readonly HasherFalso _hasher = new();

    private IngresarConContrasena CrearCasoDeUso() =>
        new(_almacen, _almacen, _hasher, _almacen, new RelojFijo());

    private void DefinirCredencial(string contrasena) =>
        _almacen.Credencial = new CredencialAcceso(
            _hasher.Calcular(contrasena), new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc));

    [Fact]
    public async Task IngresarConContrasena_SinCredencial_RechazadoSinEvento()
    {
        var resultado = await CrearCasoDeUso().EjecutarAsync("cualquier-clave");

        Assert.Equal(ResultadoIngreso.SinCredencial, resultado.Tipo);
        Assert.Empty(_almacen.Eventos);
        Assert.Equal(0, _hasher.Verificaciones);
    }

    [Fact]
    public async Task IngresarConContrasena_Incorrecta_RechazadaYEventoRegistrado()
    {
        DefinirCredencial("clave-correcta");

        var resultado = await CrearCasoDeUso().EjecutarAsync("clave-incorrecta");

        Assert.Equal(ResultadoIngreso.Rechazo, resultado.Tipo);
        var evento = Assert.Single(_almacen.Eventos);
        Assert.Equal(TipoEventoAcceso.Ingreso, evento.Tipo);
        Assert.Equal(ResultadoEventoAcceso.Rechazo, evento.Resultado);
        Assert.Equal(RelojFijo.Instante.UtcDateTime, evento.FechaHora);
        Assert.Equal(DateTimeKind.Utc, evento.FechaHora.Kind);
    }

    [Fact]
    public async Task IngresarConContrasena_Correcta_ExitoYEventoRegistrado()
    {
        DefinirCredencial("clave-correcta");

        var resultado = await CrearCasoDeUso().EjecutarAsync("clave-correcta");

        Assert.Equal(ResultadoIngreso.Exito, resultado.Tipo);
        var evento = Assert.Single(_almacen.Eventos);
        Assert.Equal(TipoEventoAcceso.Ingreso, evento.Tipo);
        Assert.Equal(ResultadoEventoAcceso.Exito, evento.Resultado);
        Assert.Equal(RelojFijo.Instante.UtcDateTime, evento.FechaHora);
    }

    [Theory]
    [InlineData(null, ValidacionContrasena.AccionMuyCorta)]
    [InlineData("1234567", ValidacionContrasena.AccionMuyCorta)]
    [InlineData("x129", ValidacionContrasena.AccionMuyLarga)]
    [InlineData("sustituto-suelto", ValidacionContrasena.AccionCaracteresInvalidos)]
    [InlineData("un-millon", ValidacionContrasena.AccionMuyLarga)]
    public async Task IngresarConContrasena_FueraDePolitica_InvalidoSinEventoNiHash(string? contrasena, string accion)
    {
        // Los valores que no entran en un atributo (sustitutos sueltos) o son enormes se arman acá.
        contrasena = contrasena switch
        {
            "x129" => new string('x', 129),
            "sustituto-suelto" => "abcdefg\uD800",
            "un-millon" => new string('x', 1_000_000),
            _ => contrasena,
        };

        DefinirCredencial("clave-correcta");

        var resultado = await CrearCasoDeUso().EjecutarAsync(contrasena);

        // Decisión del usuario (2026-10-31, ADR-005): 400 con campo y acción, sin evento y sin calcular el hash.
        Assert.Equal(ResultadoIngreso.Invalido, resultado.Tipo);
        var error = Assert.Single(resultado.Errores);
        Assert.Equal(ValidacionContrasena.Campo, error.Campo);
        Assert.Equal(accion, error.Accion);
        Assert.Empty(_almacen.Eventos);
        Assert.Equal(0, _almacen.Confirmaciones);
        Assert.Equal(0, _hasher.Verificaciones);
    }

    [Fact]
    public async Task IngresarConContrasena_FueraDePoliticaSinCredencial_InvalidoSinConsultarLaBase()
    {
        var resultado = await CrearCasoDeUso().EjecutarAsync("corta");

        Assert.Equal(ResultadoIngreso.Invalido, resultado.Tipo);
        Assert.Equal(ValidacionContrasena.AccionMuyCorta, Assert.Single(resultado.Errores).Accion);
        Assert.Equal(0, _almacen.Consultas);
        Assert.Empty(_almacen.Eventos);
    }

    [Theory]
    [InlineData("contrase" + "ñ" + "a", "contraseña")] // definida en NFD, ingresada en NFC
    [InlineData("contraseña", "contrase" + "ñ" + "a")] // definida en NFC, ingresada en NFD
    [InlineData("caf" + "é" + "-con-leche", "café-con-leche")] // vocal con acento combinante
    public async Task IngresarConContrasena_FormasUnicodeDistintas_Exito(string definida, string ingresada)
    {
        Assert.NotEqual(definida, ingresada);
        var definir = new DefinirContrasena(_almacen, _almacen, _hasher, _almacen, new RelojFijo());
        Assert.True((await definir.EjecutarAsync(definida)).EsExito);

        var resultado = await CrearCasoDeUso().EjecutarAsync(ingresada);

        Assert.Equal(ResultadoIngreso.Exito, resultado.Tipo);
        Assert.Equal(ResultadoEventoAcceso.Exito, _almacen.Eventos[^1].Resultado);
    }
}
