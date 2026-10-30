using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Optica.Infrastructure.Persistencia;

namespace Optica.Infrastructure;

/// <summary>Registro de la infraestructura en la raíz de composición e inicialización de la base.</summary>
public static class ConfiguracionInfraestructura
{
    public static IServiceCollection AgregarInfraestructura(this IServiceCollection servicios, IConfiguration configuracion)
    {
        // La clave se lee al resolver, para que la configuración final (incluida la de pruebas) sea la que manda.
        servicios.AddSingleton(_ => new FabricaConexion(configuracion[FabricaConexion.ClaveDirectorio]));
        servicios.AddDbContext<OpticaDbContext>((proveedor, opciones) =>
            proveedor.GetRequiredService<FabricaConexion>().Configurar(opciones));
        return servicios;
    }

    /// <summary>
    /// Valida el directorio de datos y aplica las migraciones pendientes. Si el directorio no sirve, registra
    /// el error con la ruta y la clave de configuración y relanza la excepción: la Api no arranca.
    /// </summary>
    public static void InicializarBaseDeDatos(this IServiceProvider servicios)
    {
        var registro = servicios.GetService<ILoggerFactory>()?.CreateLogger(typeof(ConfiguracionInfraestructura))
            ?? NullLogger.Instance;
        FabricaConexion? fabrica = null;
        try
        {
            fabrica = servicios.GetRequiredService<FabricaConexion>();
            fabrica.PrepararDirectorio();
            using var alcance = servicios.CreateScope();
            alcance.ServiceProvider.GetRequiredService<OpticaDbContext>().Database.Migrate();
        }
        catch (DirectorioDatosInvalidoException error)
        {
            registro.LogCritical(
                error,
                "La Api no puede arrancar: no se puede usar el directorio de datos {Ruta}. Corregí la clave de configuración {Clave}.",
                error.Ruta,
                error.Clave);
            throw;
        }
        catch (SqliteException error) when (fabrica is not null)
        {
            registro.LogCritical(
                error,
                "La Api no puede arrancar: no se pudo abrir o migrar la base {Archivo} en el directorio de datos {Ruta} " +
                "(puede estar en solo lectura, bloqueada o dañada). Revisá ese archivo o la clave de configuración {Clave}.",
                FabricaConexion.NombreArchivo,
                fabrica.RutaVisible,
                FabricaConexion.ClaveDirectorio);
            throw;
        }
    }
}
