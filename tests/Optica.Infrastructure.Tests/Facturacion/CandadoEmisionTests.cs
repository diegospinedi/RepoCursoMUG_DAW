using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Optica.Application.Facturacion;
using Optica.Infrastructure.Facturacion;

namespace Optica.Infrastructure.Tests.Facturacion;

/// <summary>Spec FEAT-007, Block 7 (D-10, D-32): una emisión a la vez, y la espera del turno se corta con el plazo.</summary>
/// <remarks>
/// Sin esperas reales: <see cref="SemaphoreSlim.WaitAsync(CancellationToken)"/> completa en forma sincrónica cuando hay
/// lugar, así que una tarea que no completó enseguida está esperando el turno.
/// </remarks>
public sealed class CandadoEmisionTests
{
    [Fact]
    public async Task Tomar_UnoALaVez_ElSegundoEsperaHastaQueSeLibera()
    {
        var candado = new CandadoEmision();
        using var plazo = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var primero = await candado.TomarAsync(plazo.Token);
        Assert.NotNull(primero);
        var segundo = candado.TomarAsync(plazo.Token);
        Assert.False(segundo.IsCompleted, "El segundo tomó el candado mientras el primero lo tenía.");

        primero.Dispose();
        var turno = await segundo.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.NotNull(turno);
        turno.Dispose();
    }

    /// <summary>D-32 (sad path): si el plazo vence esperando, devuelve null sin lanzar y no se queda con el candado.</summary>
    [Fact]
    public async Task Tomar_PlazoVencidoEsperando_NullSinTomarlo()
    {
        var candado = new CandadoEmision();
        using var largo = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var primero = await candado.TomarAsync(largo.Token);
        using var plazo = new CancellationTokenSource();

        var espera = candado.TomarAsync(plazo.Token);
        Assert.False(espera.IsCompleted);
        await plazo.CancelAsync();

        Assert.Null(await espera.WaitAsync(TimeSpan.FromSeconds(10)));
        primero!.Dispose();
        var siguiente = await candado.TomarAsync(largo.Token);
        Assert.NotNull(siguiente);
    }

    /// <summary>Liberar dos veces el mismo turno no libera un turno ajeno.</summary>
    [Fact]
    public async Task Liberar_DosVeces_NoLiberaOtroTurno()
    {
        var candado = new CandadoEmision();
        using var plazo = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var primero = await candado.TomarAsync(plazo.Token);
        primero!.Dispose();
        var segundo = await candado.TomarAsync(plazo.Token);

        primero.Dispose();
        var tercero = candado.TomarAsync(plazo.Token);

        Assert.False(tercero.IsCompleted, "Liberar dos veces el primer turno liberó el del segundo.");
        segundo!.Dispose();
        Assert.NotNull(await tercero.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    /// <summary>
    /// WARN-1 de la auditoría: el candado es singleton del contenedor; al apagar la Api el contenedor se descarta, y una
    /// emisión que todavía tiene el turno lo libera después. Eso no lanza.
    /// </summary>
    [Fact]
    public async Task Liberar_DespuesDeApagar_NoLanza()
    {
        var proveedor = new ServiceCollection().AddSingleton<ICandadoEmision, CandadoEmision>().BuildServiceProvider();
        using var plazo = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var turno = await proveedor.GetRequiredService<ICandadoEmision>().TomarAsync(plazo.Token);
        Assert.NotNull(turno);

        await proveedor.DisposeAsync();

        Assert.Null(Record.Exception(turno.Dispose));
    }

    /// <summary>WARN-5 de la auditoría: el candado lo registra la infraestructura, uno solo para toda la Api.</summary>
    [Fact]
    public void AgregarInfraestructura_RegistraElCandadoSingleton()
    {
        var servicios = new ServiceCollection().AgregarInfraestructura(new ConfigurationBuilder().Build());

        var registro = Assert.Single(servicios, descriptor => descriptor.ServiceType == typeof(ICandadoEmision));
        Assert.Equal(ServiceLifetime.Singleton, registro.Lifetime);
        Assert.Equal(typeof(CandadoEmision), registro.ImplementationType);
    }
}
