using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Optica.Infrastructure;
using Optica.Infrastructure.Persistencia;

namespace Optica.Infrastructure.Tests;

/// <summary>
/// Base SQLite en un directorio temporal propio de cada prueba, con las migraciones aplicadas.
/// Al descartarla se cierran las conexiones y se borra el directorio.
/// </summary>
public sealed class BaseTemporal : IDisposable
{
    private readonly List<IServiceScope> _alcances = [];

    public BaseTemporal()
    {
        Directorio = CrearDirectorioTemporal();
        Servicios = CrearServicios(Directorio);
        Servicios.InicializarBaseDeDatos();
    }

    public string Directorio { get; }

    public ServiceProvider Servicios { get; }

    /// <summary>Crea un contexto en un alcance propio, que se descarta junto con la base.</summary>
    public OpticaDbContext CrearContexto()
    {
        var alcance = Servicios.CreateScope();
        _alcances.Add(alcance);
        return alcance.ServiceProvider.GetRequiredService<OpticaDbContext>();
    }

    /// <summary>Directorio vacío bajo la carpeta temporal del sistema, único por llamada.</summary>
    public static string CrearDirectorioTemporal()
    {
        var ruta = Path.Combine(Path.GetTempPath(), "optica-pruebas", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ruta);
        return ruta;
    }

    /// <summary>
    /// Arma la infraestructura como lo hace la Api, con <c>Optica:Datos:Directorio</c> fijado al valor
    /// recibido (null = clave ausente) y, opcionalmente, un proveedor de registro para inspeccionar el log y
    /// una fábrica de conexiones que reemplaza a la que arma la configuración.
    /// </summary>
    public static ServiceProvider CrearServicios(
        string? directorio, ILoggerProvider? registro = null, FabricaConexion? fabrica = null)
    {
        var valores = new Dictionary<string, string?>();
        if (directorio is not null)
        {
            valores[FabricaConexion.ClaveDirectorio] = directorio;
        }

        var configuracion = new ConfigurationBuilder().AddInMemoryCollection(valores).Build();
        var servicios = new ServiceCollection();
        servicios.AddLogging(log =>
        {
            if (registro is not null)
            {
                log.AddProvider(registro);
            }
        });
        servicios.AgregarInfraestructura(configuracion);
        if (fabrica is not null)
        {
            servicios.AddSingleton(fabrica);
        }

        return servicios.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    /// <summary>Borra un directorio de prueba, liberando antes las conexiones agrupadas de SQLite.</summary>
    public static void BorrarDirectorio(string ruta)
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(ruta))
            {
                Directory.Delete(ruta, recursive: true);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Un archivo retenido por el sistema no hace fallar la prueba, pero queda constancia de lo que
            // sobró en la carpeta temporal.
            Console.Error.WriteLine(
                $"[BaseTemporal] No se pudo borrar el directorio de prueba '{ruta}': {error.GetType().Name}: {error.Message}");
        }
    }

    public void Dispose()
    {
        foreach (var alcance in _alcances)
        {
            alcance.Dispose();
        }

        Servicios.Dispose();
        BorrarDirectorio(Directorio);
    }
}
