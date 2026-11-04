using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Optica.Infrastructure.Persistencia;

namespace Optica.Api.Tests;

/// <summary>
/// El modelo de diseño de EF Core se arma una vez por proceso, antes de cualquier fábrica: las Api que arrancan en
/// paralelo y migran lo comparten ya terminado, en lugar de construirlo a la vez.
/// </summary>
public sealed class PrecalentamientoEfTests
{
    [Fact]
    public void PrecalentamientoEf_LaApiUsaElModeloYaArmado()
    {
        using var fabrica = new FabricaApi();
        using var alcance = fabrica.Services.CreateScope();

        var modelo = alcance.ServiceProvider.GetRequiredService<OpticaDbContext>().GetService<IDesignTimeModel>().Model;

        Assert.NotNull(PrecalentamientoEf.ModeloDeDiseno);
        Assert.Same(PrecalentamientoEf.ModeloDeDiseno, modelo);
    }
}
