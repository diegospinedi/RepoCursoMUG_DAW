using Optica.Application.Configuracion;

namespace Optica.Application.Tests.Configuracion;

/// <summary>
/// La consulta de los datos del emisor (FEAT-009, Block 2): sin datos cargados, los cinco en null (D-22); con datos, los
/// cinco en la forma del contrato. Datos inventados y CUIT ficticio (D-33).
/// </summary>
public class ObtenerDatosEmisorTests
{
    private readonly DatosEmisorEnMemoria _almacen = new();

    [Fact]
    public async Task Obtener_SinFila_CincoNull()
    {
        var vigentes = await new ObtenerDatosEmisor(_almacen).EjecutarAsync();

        Assert.Equal(new DatosEmisorVigentes(null, null, null, null, null), vigentes);
        Assert.Equal(1, _almacen.Lecturas);
    }

    [Fact]
    public async Task Obtener_ConFila_LosCinco()
    {
        _almacen.Guardados = DatosEmisorEnMemoria.Crear(
            "Zq7Ficticia SA", "Calle Inventada 123", "20-00000000-1", "IB-0001", "2020-03-01");

        var vigentes = await new ObtenerDatosEmisor(_almacen).EjecutarAsync();

        Assert.Equal(
            new DatosEmisorVigentes("Zq7Ficticia SA", "Calle Inventada 123", "20000000001", "IB-0001", "2020-03-01"),
            vigentes);
    }

    [Fact]
    public async Task Obtener_FilaCorrupta_PropagaLaExcepcion()
    {
        var falla = new InvalidOperationException("Los datos del emisor guardados no son válidos.");
        _almacen.ErrorAlLeer = falla;

        var lanzada = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new ObtenerDatosEmisor(_almacen).EjecutarAsync());

        Assert.Same(falla, lanzada);
    }
}
