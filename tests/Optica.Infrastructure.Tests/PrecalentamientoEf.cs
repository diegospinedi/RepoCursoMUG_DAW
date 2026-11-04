using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Optica.Infrastructure.Persistencia;

namespace Optica.Infrastructure.Tests;

/// <summary>
/// Arma el modelo de diseño de EF Core una vez por proceso de pruebas, al cargar el ensamblado y antes de cualquier
/// <see cref="BaseTemporal"/>. Varias bases que se inicializan en paralelo llaman a <c>Migrate()</c>, que pide ese
/// modelo (<c>HasPendingModelChanges</c>); si todavía no existe, lo construyen a la vez y alguna lo lee sin terminar
/// ("The model must be finalized…"). En producción no pasa: hay una sola instancia, protegida por el candado
/// <c>optica.instancia</c>. Usa las mismas opciones que la infraestructura (<see cref="FabricaConexion.Configurar"/>),
/// así EF Core comparte el mismo proveedor interno y su caché de modelos, con una base en memoria: no crea ni abre
/// archivos.
/// </summary>
internal static class PrecalentamientoEf
{
    public static IModel? ModeloDeDiseno { get; private set; }

#pragma warning disable CA2255 // Es el ensamblado de pruebas, no una biblioteca: tiene que correr antes de cualquier prueba.
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Precalentar()
    {
        // Como AgregarInfraestructura (AddDbContext con FabricaConexion.Configurar), pero con la base en memoria.
        var servicios = new ServiceCollection().AddLogging();
        servicios.AddDbContext<OpticaDbContext>(opciones =>
        {
            new FabricaConexion(Path.GetTempPath()).Configurar(opciones);
            opciones.UseSqlite("Data Source=:memory:");
        });
        using var proveedor = servicios.BuildServiceProvider();
        using var alcance = proveedor.CreateScope();
        var contexto = alcance.ServiceProvider.GetRequiredService<OpticaDbContext>();
        var modelo = contexto.GetService<IDesignTimeModel>().Model;
        _ = modelo.GetRelationalModel();
        ModeloDeDiseno = modelo;
    }
}
