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

    /// <summary>
    /// Alerta de datos (D-28 de FEAT-002c): la alícuota, la condición fiscal y el tope son información comercial
    /// confidencial; el <c>ToString()</c> del record no los muestra, por si llega a un log.
    /// </summary>
    [Fact]
    public void ConfiguracionVigente_ToString_NoMuestraDatos()
    {
        var texto = new ConfiguracionVigente(17.35m, "monotributo", 43.21m, 765_432.1m, false).ToString();

        Assert.Equal("ConfiguracionVigente { }", texto);
        Assert.DoesNotContain("17.35", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("monotributo", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("43.21", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("765432", texto, StringComparison.Ordinal);
    }
}
