using Optica.Application.Comun;
using Optica.Domain.Acceso;

namespace Optica.Application.Tests.Acceso;

/// <summary>El doble se comporta como el repositorio real sobre SQLite.</summary>
public class AlmacenEnMemoriaTests
{
    private static readonly DateTime Ahora = RelojFijo.Instante.UtcDateTime;

    [Fact]
    public async Task Reemplazar_LeidaYaModificada_ConflictoAlConfirmar()
    {
        var almacen = new AlmacenEnMemoria { Credencial = new CredencialAcceso("falso$guardado", Ahora) };
        var actual = await almacen.ObtenerAsync();
        Assert.NotNull(actual);

        // Como en SQLite: modificar lo leído no toca lo guardado, y el reemplazo compara contra el hash de lo leído.
        actual.ReemplazarHash("falso$modificado", Ahora.AddDays(1));
        almacen.Reemplazar(actual, new CredencialAcceso("falso$otra", Ahora.AddDays(1)));

        await Assert.ThrowsAsync<ConflictoAlConfirmarException>(() => almacen.ConfirmarAsync());
        Assert.Equal("falso$guardado", almacen.Credencial!.Hash);
        Assert.Equal(Ahora, almacen.Credencial.DefinidaEn);
    }

    [Fact]
    public async Task Obtener_DevuelveUnaCopiaDeLoGuardado()
    {
        var almacen = new AlmacenEnMemoria { Credencial = new CredencialAcceso("falso$guardado", Ahora) };

        var leida = await almacen.ObtenerAsync();

        Assert.NotNull(leida);
        Assert.NotSame(almacen.Credencial, leida);
        Assert.Equal("falso$guardado", leida.Hash);
        Assert.Equal(Ahora, leida.DefinidaEn);
    }

    [Fact]
    public void Reemplazar_MismaInstancia_Rechazado()
    {
        var almacen = new AlmacenEnMemoria();
        var credencial = new CredencialAcceso("falso$guardado", Ahora);

        var error = Assert.Throws<ArgumentException>(() => almacen.Reemplazar(credencial, credencial));

        Assert.Equal("nueva", error.ParamName);
    }
}
