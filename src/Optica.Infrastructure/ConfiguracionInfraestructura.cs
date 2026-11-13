using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Optica.Application.Acceso;
using Optica.Application.Comun;
using Optica.Application.Facturacion;
using Optica.Application.Marca;
using Optica.Application.Presupuestos;
using Optica.Domain.Acceso;
using Optica.Domain.Catalogo;
using Optica.Domain.Configuracion;
using Optica.Domain.Facturacion;
using Optica.Domain.Presupuestos;
using Optica.Infrastructure.Arca;
using Optica.Infrastructure.Marca;
using Optica.Infrastructure.Pdf;
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

        // Presupuestos: comparte el contexto (y la unidad de trabajo) del alcance, así el alta lee el último número y
        // graba dentro de la misma transacción (ADR-013, ADR-015).
        servicios.AddScoped<IPresupuestoRepository, PresupuestoRepository>();

        // Facturación (FEAT-007): comparten el contexto (y la unidad de trabajo) del alcance, así la emisión graba la
        // pendiente, y después la factura y la baja de la pendiente, cada una en su transacción (D-09, ADR-021).
        servicios.AddScoped<IFacturaRepository, FacturaRepository>();
        servicios.AddScoped<IEmisionPendienteRepository, EmisionPendienteRepository>();

        // ARCA (FEAT-007, ADR-022): las claves Arca:* se leen al resolver, como el directorio de datos; ValidarArca las
        // resuelve al arrancar. Sin Arca:Entorno, el simulador (R-14). Singleton: el simulador serializa su contador.
        servicios.AddSingleton(_ => OpcionesArca.Leer(configuracion));
        servicios.AddSingleton<IAutorizadorComprobantes>(CrearAutorizador);

        // PDF del presupuesto con QuestPDF (ADR-018). Singleton: no guarda estado y la configuración de QuestPDF es
        // global, fijada una vez en su constructor estático (D-13).
        servicios.AddSingleton<IGeneradorPdfPresupuesto, GeneradorPdfPresupuesto>();

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
    /// Elige el autorizador por <see cref="OpcionesArca.Entorno"/>: el simulador en el directorio de datos, o, en
    /// homologación y producción, <see cref="AutorizadorNoConfigurado"/> mientras el cliente de WSAA y WSFEv1 no esté
    /// disponible (spec FEAT-007, Block 6) o si faltan el CUIT, el certificado o su clave.
    /// </summary>
    private static IAutorizadorComprobantes CrearAutorizador(IServiceProvider proveedor)
    {
        var opciones = proveedor.GetRequiredService<OpcionesArca>();
        return opciones.Entorno == EntornoArca.Simulado
            ? new SimuladorArca(
                proveedor.GetRequiredService<FabricaConexion>().Directorio,
                opciones.RespuestaSimulado,
                opciones.TiempoMaximo,
                proveedor.GetRequiredService<ILogger<SimuladorArca>>())
            : new AutorizadorNoConfigurado(
                opciones.Entorno,
                opciones.TiempoMaximo,
                // Temporal: con credenciales completas, Block 6 registra acá el cliente de WSAA y WSFEv1.
                opciones.CredencialesCompletas
                    ? AutorizadorNoConfigurado.MotivoNoDisponible
                    : AutorizadorNoConfigurado.MotivoNoConfigurada);
    }

    /// <summary>
    /// Lee y valida las claves <c>Arca:*</c> y arma el autorizador. Con un valor inválido registra un crítico con la clave
    /// (nunca el valor ni la excepción) y lanza: la Api no arranca. En simulado advierte que los comprobantes no tienen
    /// validez fiscal (R-10); en homologación o producción sin credenciales advierte qué claves faltan (solo sus nombres).
    /// </summary>
    /// <exception cref="OpcionesArcaInvalidasException">Una clave <c>Arca:*</c> tiene un valor inválido.</exception>
    public static void ValidarArca(this IServiceProvider servicios)
    {
        var registro = servicios.GetService<ILoggerFactory>()?.CreateLogger(typeof(ConfiguracionInfraestructura))
            ?? NullLogger.Instance;
        OpcionesArca opciones;
        try
        {
            opciones = servicios.GetRequiredService<OpcionesArca>();
        }
        catch (OpcionesArcaInvalidasException error)
        {
            registro.LogCritical(
                "La Api no puede arrancar: el valor de la clave de configuración {Clave} no es válido. Corregí esa clave.",
                error.Clave);
            throw;
        }

        // El autorizador no se resuelve acá: el simulador necesita el directorio de datos, cuyo error tiene su propio
        // crítico en InicializarBaseDeDatos.
        if (opciones.Entorno == EntornoArca.Simulado)
        {
            registro.LogWarning(
                "La facturación usa ARCA simulada ({Clave} = Simulado): los comprobantes emitidos no tienen validez " +
                "fiscal. Para facturar con ARCA configurá esa clave, el CUIT y el certificado (docs/instalacion.md).",
                OpcionesArca.ClaveEntorno);

            // R-10: credenciales cargadas y entorno simulado suele ser una instalación a medio cambiar. No se cambia de
            // entorno por eso (R-14): solo se advierte, sin valores.
            if (opciones.CredencialesPresentes)
            {
                registro.LogWarning(
                    "Hay credenciales de ARCA configuradas pero Arca:Entorno es Simulado: la facturación no tiene " +
                    "validez fiscal. Si corresponde facturar con ARCA, configurá {Clave} con Homologacion o Produccion.",
                    OpcionesArca.ClaveEntorno);
            }
        }
        else if (!opciones.CredencialesCompletas)
        {
            registro.LogWarning(
                "La facturación con ARCA ({Clave} = {Entorno}) no está configurada: faltan las claves {Faltantes}. Cada " +
                "emisión va a terminar sin enviarse.",
                OpcionesArca.ClaveEntorno,
                opciones.Entorno,
                string.Join(", ", opciones.ClavesFaltantes));
        }
        else
        {
            // Spec FEAT-007, Block 6: hasta que esté el cliente de WSAA y WSFEv1, también con credenciales.
            registro.LogWarning(
                "La facturación con ARCA ({Clave} = {Entorno}) todavía no está disponible en esta versión: cada emisión " +
                "va a terminar sin enviarse.",
                OpcionesArca.ClaveEntorno,
                opciones.Entorno);
        }
    }

    /// <summary>
    /// Valida el directorio de datos, toma el candado de instancia única y aplica las migraciones pendientes. Si el
    /// directorio no sirve o el candado lo tiene otra instancia, registra un crítico con la ruta visible (sin la
    /// excepción) y lanza: la Api no arranca. El candado se toma antes de migrar, así dos instancias no migran a la vez,
    /// y no lo toma <c>dotnet ef</c>, que no pasa por acá. Antes de todo valida las claves <c>Arca:*</c>
    /// (<see cref="ValidarArca"/>): con un valor inválido la Api tampoco arranca.
    /// </summary>
    public static void InicializarBaseDeDatos(this IServiceProvider servicios)
    {
        var registro = servicios.GetService<ILoggerFactory>()?.CreateLogger(typeof(ConfiguracionInfraestructura))
            ?? NullLogger.Instance;
        // ARCA primero: un valor inválido frena el arranque antes de tomar el candado o migrar (spec FEAT-007, Block 5).
        servicios.ValidarArca();

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
