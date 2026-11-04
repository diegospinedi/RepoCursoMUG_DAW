using Optica.Application.Configuracion;
using Optica.Domain.Configuracion;

namespace Optica.Application.Tests.Configuracion;

public class ObtenerConfiguracionNegocioTests
{
    private readonly ConfiguracionEnMemoria _almacen = new();

    private ObtenerConfiguracionNegocio CrearConsulta() => new(_almacen);

    [Fact]
    public async Task Obtener_ValoresIniciales_DevuelveLosCuatroYFaltaTope()
    {
        var vigente = await CrearConsulta().EjecutarAsync();

        Assert.Equal(new ConfiguracionVigente(21m, "responsable_inscripto", 0.01m, null, true), vigente);
    }

    [Fact]
    public async Task Obtener_CadaLlamada_LeeElRepositorio()
    {
        var consulta = CrearConsulta();

        await consulta.EjecutarAsync();
        _almacen.Guardada = new ConfiguracionNegocio(10.5m, CondicionFiscal.Monotributo, 10m, 1_000m);
        var vigente = await consulta.EjecutarAsync();

        // FR-06: sin caché; la segunda llamada ve el valor cambiado.
        Assert.Equal(2, _almacen.Lecturas);
        Assert.Equal(new ConfiguracionVigente(10.5m, "monotributo", 10m, 1_000m, false), vigente);
    }

    [Fact]
    public async Task Obtener_SinFila_PropagaError()
    {
        _almacen.Guardada = null;

        await Assert.ThrowsAsync<InvalidOperationException>(() => CrearConsulta().EjecutarAsync());
    }
}
