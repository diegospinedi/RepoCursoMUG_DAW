using Optica.Application.Acceso;
using Optica.Domain.Acceso;

namespace Optica.Application.Tests.Acceso;

public class EstadoYSalidaTests
{
    private readonly AlmacenEnMemoria _almacen = new();

    [Fact]
    public async Task ConsultarEstadoAcceso_ReflejaSiHayCredencial()
    {
        var casoDeUso = new ConsultarEstadoAcceso(_almacen);
        Assert.False((await casoDeUso.EjecutarAsync()).ContrasenaDefinida);

        _almacen.Credencial = new CredencialAcceso("falso$hash", RelojFijo.Instante.UtcDateTime);

        Assert.True((await casoDeUso.EjecutarAsync()).ContrasenaDefinida);
    }

    [Fact]
    public async Task CerrarSesion_RegistraEventoSalida()
    {
        await new CerrarSesion(_almacen, _almacen, new RelojFijo()).EjecutarAsync();

        var evento = Assert.Single(_almacen.Eventos);
        Assert.Equal(TipoEventoAcceso.Salida, evento.Tipo);
        Assert.Equal(ResultadoEventoAcceso.Exito, evento.Resultado);
        Assert.Equal(RelojFijo.Instante.UtcDateTime, evento.FechaHora);
    }
}
