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
        _almacen.Guardada = new ConfiguracionNegocio(10.5m, CondicionFiscal.Monotributo, 10m, 1_000m, puntoVenta: null);
        var vigente = await consulta.EjecutarAsync();

        // FR-06: sin caché; la segunda llamada ve el valor cambiado.
        Assert.Equal(2, _almacen.Lecturas);
        Assert.Equal(new ConfiguracionVigente(10.5m, "monotributo", 10m, 1_000m, false), vigente);
    }

    [Fact]
    public async Task Obtener_DevuelvePuntoVentaYAlicuotasAdmitidas()
    {
        _almacen.Guardada = new ConfiguracionNegocio(21m, CondicionFiscal.ResponsableInscripto, 0.01m, null, puntoVenta: 3);

        var vigente = await CrearConsulta().EjecutarAsync();

        // AC-28: la lista de la pantalla sale de la Api; FR-28: el punto de venta guardado.
        Assert.Equal(3, vigente.PuntoVenta);
        Assert.Equal([0m, 2.5m, 5m, 10.5m, 21m, 27m], vigente.AlicuotasAdmitidas);
    }

    [Fact]
    public async Task Obtener_AlicuotaGuardada19_SeLeeSinError()
    {
        // D-06: una alícuota guardada antes de FR-26 se lee; la pantalla pide elegir una de la lista.
        _almacen.Guardada = new ConfiguracionNegocio(19m, CondicionFiscal.ResponsableInscripto, 0.01m, null, puntoVenta: null);

        var vigente = await CrearConsulta().EjecutarAsync();

        Assert.Equal(19m, vigente.AlicuotaIva);
        Assert.Null(vigente.PuntoVenta);
        Assert.DoesNotContain(19m, vigente.AlicuotasAdmitidas);
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
        var texto = new ConfiguracionVigente(17.35m, "monotributo", 43.21m, 765_432.1m, false, 4321).ToString();

        Assert.Equal("ConfiguracionVigente { }", texto);
        Assert.DoesNotContain("17.35", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("monotributo", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("43.21", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("765432", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("4321", texto, StringComparison.Ordinal);
    }
}
