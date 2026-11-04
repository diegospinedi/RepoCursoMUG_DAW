using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Optica.Application.Acceso;
using Optica.Application.Comun;
using Optica.Application.Marca;
using Optica.Domain.Acceso;
using Optica.Domain.Catalogo;
using Optica.Domain.Configuracion;
using Optica.Infrastructure.Marca;
using Optica.Infrastructure.Persistencia;
using Optica.Infrastructure.Seguridad;

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
        servicios.AddSingleton<IDatosLocales>(proveedor => new DatosLocales(
            proveedor.GetRequiredService<FabricaConexion>(), proveedor.GetRequiredService<IServiceScopeFactory>()));

        // Candado de instancia única (optica.instancia en el directorio de datos). La Api lo toma al inicializar la base
        // y lo guarda en CandadoTomado, que el contenedor descarta (y con él libera el candado) al descartar el host.
        servicios.AddSingleton<ICandadoInstancia>(proveedor =>
            new CandadoInstanciaArchivo(proveedor.GetRequiredService<FabricaConexion>().Directorio));
        servicios.AddSingleton<CandadoTomado>();

        // Acceso: los repositorios y la unidad de trabajo comparten el OpticaDbContext del alcance.
        servicios.AddScoped<ICredencialAccesoRepository, CredencialAccesoRepository>();
        servicios.AddScoped<IRegistroEventosAcceso, RegistroEventosAccesoEf>();
        servicios.AddScoped<IUnidadDeTrabajo, UnidadDeTrabajoEf>();
        servicios.AddSingleton<IHasherContrasena, HasherContrasenaPbkdf2>();

        // Configuración de negocio: comparte el contexto (y la unidad de trabajo) del alcance; lee la base en cada
        // operación (FR-06).
        servicios.AddScoped<IConfiguracionNegocioRepository, ConfiguracionNegocioRepository>();

        // Catálogo: comparte el contexto (y la unidad de trabajo) del alcance.
        servicios.AddScoped<IArticuloRepository, ArticuloRepository>();

        // Marca: la instalada en Optica:Marca:Directorio o la de ejemplo copiada junto a los binarios. Singleton: se lee
        // una vez y queda en memoria hasta reiniciar la Api.
        servicios.AddSingleton<IProveedorMarca>(proveedor => new ProveedorMarcaArchivo(
            configuracion[ProveedorMarcaArchivo.ClaveDirectorio],
            Path.Combine(AppContext.BaseDirectory, ProveedorMarcaArchivo.CarpetaEjemplo),
            proveedor.GetRequiredService<ILogger<ProveedorMarcaArchivo>>()));

        // Reloj UTC del sistema, salvo que la raíz de composición (o una prueba) registre otro.
        servicios.TryAddSingleton(TimeProvider.System);
        return servicios;
    }

    /// <summary>
    /// Valida el directorio de datos, toma el candado de instancia única y aplica las migraciones pendientes. Si el
    /// directorio no sirve o el candado lo tiene otra instancia, registra un crítico con la ruta visible (sin la
    /// excepción) y lanza: la Api no arranca. El candado se toma antes de migrar, así dos instancias no migran a la vez,
    /// y no lo toma <c>dotnet ef</c>, que no pasa por acá.
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
            servicios.GetRequiredService<CandadoTomado>().Tomar(fabrica);
            using var alcance = servicios.CreateScope();
            alcance.ServiceProvider.GetRequiredService<OpticaDbContext>().Database.Migrate();
        }
        catch (DirectorioDatosInvalidoException error)
        {
            // Sin la excepción adjunta: el mensaje ya nombra la ruta visible y la clave, y su traza llevaría las rutas
            // de compilación (con el usuario de quien compiló) si se publican los PDB sin PathMap.
            registro.LogCritical(
                "La Api no puede arrancar: no se puede usar el directorio de datos {Ruta}. Corregí la clave de configuración {Clave}.",
                error.Ruta,
                error.Clave);
            throw;
        }
        catch (InstanciaEnMarchaException) when (fabrica is not null)
        {
            // Sin la excepción adjunta, por lo mismo: solo la ruta visible.
            registro.LogCritical(
                "La Api no puede arrancar: otra instancia de la aplicación ya usa el directorio de datos {Ruta}, o no hay " +
                "permisos sobre su archivo {Archivo}. Detené la otra instancia, revisá los permisos de ese archivo o la " +
                "clave de configuración {Clave}.",
                fabrica.RutaVisible,
                CandadoInstanciaArchivo.NombreArchivo,
                FabricaConexion.ClaveDirectorio);
            throw;
        }
        catch (SqliteException error) when (fabrica is not null)
        {
            // Sin la excepción adjunta: su texto puede traer la ruta expandida, con el nombre de usuario de la PC. El
            // código de error de SQLite alcanza para el diagnóstico y no lleva datos personales.
            registro.LogCritical(
                "La Api no puede arrancar: no se pudo abrir o migrar la base {Archivo} en el directorio de datos {Ruta} " +
                "(código de error de SQLite {CodigoSqlite}; puede estar en solo lectura, bloqueada o dañada). Revisá " +
                "ese archivo o la clave de configuración {Clave}.",
                FabricaConexion.NombreArchivo,
                fabrica.RutaVisible,
                error.SqliteErrorCode,
                FabricaConexion.ClaveDirectorio);
            throw;
        }
    }
}
