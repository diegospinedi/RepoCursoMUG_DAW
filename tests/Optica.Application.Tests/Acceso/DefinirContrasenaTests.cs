using Optica.Application.Acceso;
using Optica.Application.Comun;

namespace Optica.Application.Tests.Acceso;

public class DefinirContrasenaTests
{
    private readonly AlmacenEnMemoria _almacen = new();
    private readonly HasherFalso _hasher = new();

    private DefinirContrasena CrearCasoDeUso() =>
        new(_almacen, _almacen, _hasher, _almacen, new RelojFijo());

    [Fact]
    public async Task DefinirContrasena_De7Caracteres_RechazadaPorInvalida()
    {
        var resultado = await CrearCasoDeUso().EjecutarAsync("1234567");

        Assert.Equal(TipoResultado.Invalido, resultado.Tipo);
        var error = Assert.Single(resultado.Errores);
        Assert.Equal("contrasena", error.Campo);
        Assert.Equal("Ingresá una contraseña de 8 caracteres o más", error.Accion);

        // No se guarda nada.
        Assert.Null(_almacen.Credencial);
        Assert.Empty(_almacen.Eventos);
        Assert.Equal(0, _hasher.Calculos);
    }

    [Fact]
    public async Task DefinirContrasena_De129Caracteres_RechazadaPorInvalida()
    {
        var resultado = await CrearCasoDeUso().EjecutarAsync(new string('x', 129));

        Assert.Equal(TipoResultado.Invalido, resultado.Tipo);
        var error = Assert.Single(resultado.Errores);
        Assert.Equal("contrasena", error.Campo);
        Assert.Equal("Ingresá una contraseña de 128 caracteres o menos", error.Accion);

        Assert.Null(_almacen.Credencial);
        Assert.Empty(_almacen.Eventos);
        Assert.Equal(0, _hasher.Calculos);
    }

    [Fact]
    public async Task DefinirContrasena_SieteCaracteresTrasNormalizar_RechazadaPorInvalida()
    {
        // 8 unidades UTF-16 en NFD, 7 caracteres en NFC.
        var resultado = await CrearCasoDeUso().EjecutarAsync("contra" + "ñ");

        Assert.Equal(TipoResultado.Invalido, resultado.Tipo);
        Assert.Equal("Ingresá una contraseña de 8 caracteres o más", Assert.Single(resultado.Errores).Accion);
        Assert.Null(_almacen.Credencial);
        Assert.Equal(0, _hasher.Calculos);
    }

    [Fact]
    public async Task DefinirContrasena_SustitutoSuelto_RechazadaPorCaracteresInvalidos()
    {
        var resultado = await CrearCasoDeUso().EjecutarAsync("abcdefg\uD800");

        Assert.Equal(TipoResultado.Invalido, resultado.Tipo);
        var error = Assert.Single(resultado.Errores);
        Assert.Equal("contrasena", error.Campo);
        Assert.Equal("La contraseña tiene caracteres no válidos. Escribila de nuevo.", error.Accion);
        Assert.Null(_almacen.Credencial);
        Assert.Empty(_almacen.Eventos);
        Assert.Equal(0, _hasher.Calculos);
    }

    [Fact]
    public async Task DefinirContrasena_YaDefinida_ConflictoSinModificar()
    {
        var casoDeUso = CrearCasoDeUso();
        var primero = await casoDeUso.EjecutarAsync("primera-clave");
        Assert.Equal(TipoResultado.Exito, primero.Tipo);
        var credencialOriginal = _almacen.Credencial;
        Assert.NotNull(credencialOriginal);
        var hashOriginal = credencialOriginal.Hash;
        var calculosAntes = _hasher.Calculos;
        var eventosAntes = _almacen.Eventos.Count;

        var segundo = await casoDeUso.EjecutarAsync("segunda-clave");

        Assert.Equal(TipoResultado.Conflicto, segundo.Tipo);
        Assert.Empty(segundo.Errores);
        Assert.Same(credencialOriginal, _almacen.Credencial);
        Assert.Equal(hashOriginal, _almacen.Credencial!.Hash);
        Assert.Equal(calculosAntes, _hasher.Calculos); // no se calculó el hash
        Assert.Equal(eventosAntes, _almacen.Eventos.Count);
        Assert.True(_hasher.Verificar("primera-clave", _almacen.Credencial.Hash));
    }

    [Fact]
    public async Task DefinirContrasena_ConflictoAlConfirmar_DevuelveConflicto()
    {
        // Otra definición ganó entre la consulta y la confirmación: la base rechaza la segunda fila.
        var casoDeUso = new DefinirContrasena(
            _almacen, _almacen, _hasher, new UnidadQueChoca(), new RelojFijo());

        var resultado = await casoDeUso.EjecutarAsync("clave-valida");

        Assert.Equal(TipoResultado.Conflicto, resultado.Tipo);
    }

    private sealed class UnidadQueChoca : IUnidadDeTrabajo
    {
        public Task ConfirmarAsync(CancellationToken cancelacion = default) =>
            throw new ConflictoAlConfirmarException("duplicada");
    }
}
