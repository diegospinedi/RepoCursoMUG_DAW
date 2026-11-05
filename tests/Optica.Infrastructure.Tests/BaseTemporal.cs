using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
        LiberarConexiones(ruta);
        try
        {
            if (Directory.Exists(ruta))
            {
                Directory.Delete(ruta, recursive: true);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Un archivo retenido por el sistema no hace fallar la prueba, pero queda constancia. Solo el tipo de error,
            // como FabricaApi: la ruta, que también repite el mensaje, puede llevar el nombre de usuario (D-30).
            Console.Error.WriteLine(
                $"[BaseTemporal] No se pudo borrar un directorio de prueba ({error.GetType().Name}).");
        }
    }

    /// <summary>
    /// Libera las conexiones agrupadas de SQLite de las bases <c>optica.db</c> que haya en <paramref name="ruta"/> o
    /// debajo, solo las de esas bases: <see cref="SqliteConnection.ClearAllPools"/> es global al proceso y, con pruebas
    /// en paralelo, cierra conexiones de otras bases en uso.
    /// </summary>
    public static void LiberarConexiones(string ruta)
    {
        var bases = Directory.Exists(ruta)
            ? Directory.EnumerateFiles(ruta, FabricaConexion.NombreArchivo, SearchOption.AllDirectories)
                .Select(archivo => Path.GetDirectoryName(archivo)!)
            : [];
        foreach (var carpeta in bases.Append(ruta).Distinct(StringComparer.Ordinal))
        {
            // La misma cadena que arma la infraestructura para ese directorio: es la clave de su grupo de conexiones.
            using var conexion = new SqliteConnection(new FabricaConexion(carpeta).CadenaConexion);
            SqliteConnection.ClearPool(conexion);
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
