using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Optica.Infrastructure.Tests;

/// <summary>
/// El modelo de diseño de EF Core se arma una vez por proceso, antes de cualquier base temporal: las que se inicializan
/// en paralelo y migran lo comparten ya terminado, en lugar de construirlo a la vez.
/// </summary>
public sealed class PrecalentamientoEfTests
{
    [Fact]
    public void PrecalentamientoEf_LaBaseTemporalUsaElModeloYaArmado()
    {
        using var baseTemporal = new BaseTemporal();

        var modelo = baseTemporal.CrearContexto().GetService<IDesignTimeModel>().Model;

        Assert.NotNull(PrecalentamientoEf.ModeloDeDiseno);
        Assert.Same(PrecalentamientoEf.ModeloDeDiseno, modelo);
    }
}
