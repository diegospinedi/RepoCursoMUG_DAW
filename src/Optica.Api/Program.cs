using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.RateLimiting;
using Optica.Api.Acceso;
using Optica.Api.Controllers;
using Optica.Api.Errores;
using Optica.Api.Web;
using Optica.Application.Acceso;
using Optica.Application.Catalogo;
using Optica.Application.Comun;
using Optica.Application.Configuracion;
using Optica.Application.Marca;
using Optica.Infrastructure;
using Optica.Infrastructure.Persistencia;

// Raíz de composición única. HTTPS: Kestrel escucha solo en el endpoint Kestrel:Endpoints:Https de appsettings.json
// y toma el certificado del almacén de Windows con Kestrel:Certificates:Default:Store=My, :Location=CurrentUser y
// :Subject=OpticaSistema, que se fijan al instalar (variables de entorno o un appsettings.Production.json local,
// ignorado por git); sin esas claves, en desarrollo, usa el certificado de desarrollo de .NET. Ni el certificado ni
// ninguna contraseña van en appsettings ni en el repo.
// AllowedHosts es "localhost" en appsettings.json; al instalar se amplía con el nombre (y, si se usa, la IP) de la PC
// en la red local (docs/instalacion.md). Un pedido con otro Host recibe 400.
// Sin CORS: la SPA se sirve desde el mismo origen. Nunca se registra el cuerpo de los pedidos (definir, ingresar y
// cambiar traen contraseñas en claro): no agregar HttpLogging de cuerpos.
//
// `Optica.Api restablecer-contrasena` no levanta la Api: con la misma configuración (raíz del contenido fija en la
// carpeta de la aplicación, para leer su appsettings.json desde donde se lo ejecute) restablece la contraseña por
// consola y termina (FR-04, ADR-007). No hay endpoint ni pantalla de restablecimiento (FR-05).
if (Program.EsComandoRestablecer(args))
{
    return await Program.EjecutarComandoAsync(
        () =>
        {
            var constructor = WebApplication.CreateBuilder(Program.OpcionesDelComando());
            Program.ConfigurarServicios(constructor);
            // La consola es la del operador: EF Core no escribe ahí sus comandos (Information); los avisos y errores, sí.
            constructor.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
            return constructor.Build();
        },
        args,
        Console.In,
        Console.Out);
}

var builder = WebApplication.CreateBuilder(args);
Program.ConfigurarServicios(builder);
var app = builder.Build();

// Toma el candado de instancia única del directorio de datos (si otra instancia lo tiene, la Api no arranca) y aplica
// las migraciones pendientes. Si el directorio de datos no se puede crear o escribir, registra el error con la ruta y
// la clave Optica:Datos:Directorio, y la Api no arranca.
app.Services.InicializarBaseDeDatos();

app.UseExceptionHandler();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();

// La SPA compilada (Optica:Web:Directorio, por defecto wwwroot), anónima, con X-Content-Type-Options: nosniff y con la
// Content-Security-Policy de SpaEstatica.PoliticaContenido (R-11): default-src 'self'; frame-ancestors 'none';
// base-uri 'none'; form-action 'self'; object-src 'none'. Las rutas de /api siguen de largo al ruteo y a la
// autorización.
app.UsarSpa();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

// Content-Type JSON en todo POST (415) y tamaño máximo del cuerpo (413), antes del limitador (R-04, R-05).
app.Use(LimitesDelCuerpo.AplicarAsync);

app.UseRateLimiter();

app.MapControllers();

app.Run();
return 0;

/// <summary>Punto de entrada; parcial y público para que las pruebas levanten la Api y ejecuten el comando.</summary>
public partial class Program
{
    /// <summary>Verdadero si el primer argumento es exactamente <see cref="ComandoRestablecerContrasena.Nombre"/>.</summary>
    public static bool EsComandoRestablecer(IReadOnlyList<string> argumentos) =>
        argumentos.Count > 0 && string.Equals(argumentos[0], ComandoRestablecerContrasena.Nombre, StringComparison.Ordinal);

    /// <summary>
    /// Opciones del builder del comando: la raíz del contenido es la carpeta de la aplicación, no la carpeta desde
    /// donde se lo ejecuta, para que lea el mismo appsettings.json que la Api. Los argumentos no pasan a la
    /// configuración.
    /// </summary>
    public static WebApplicationOptions OpcionesDelComando() => new() { ContentRootPath = AppContext.BaseDirectory };

    /// <summary>Registra todos los servicios de la aplicación; la misma composición para la Api y para el comando.</summary>
    public static void ConfigurarServicios(WebApplicationBuilder builder)
    {
        // Infraestructura: base, repositorios, hasher, datos locales y candado de instancia. Los errores de la base no
        // se registran tal cual (su texto puede traer la ruta con el nombre de usuario de Windows): la infraestructura
        // los deja en Debug y el manejador de errores registra uno propio con la ruta visible.
        builder.Services.AgregarInfraestructura(builder.Configuration);

        // Casos de uso de acceso.
        builder.Services.AddScoped<ConsultarEstadoAcceso>();
        builder.Services.AddScoped<DefinirContrasena>();
        builder.Services.AddScoped<IngresarConContrasena>();
        builder.Services.AddScoped<CambiarContrasena>();
        builder.Services.AddScoped<CerrarSesion>();
        builder.Services.AddScoped<RestablecerContrasena>();

        // Marca (colores y logo), anónima: la de Optica:Marca:Directorio o la de ejemplo.
        builder.Services.AddScoped<ObtenerMarca>();

        // Configuración de negocio, con sesión: se lee de la base en cada pedido (sin caché) y cada grabación queda
        // en el log (categoría Optica.Application.Configuracion, que appsettings.json envía también al Registro de
        // eventos de Windows).
        builder.Services.AddScoped<ObtenerConfiguracionNegocio>();
        builder.Services.AddScoped<GrabarConfiguracionNegocio>();

        // Catálogo de artículos, con sesión: el alta lee la configuración vigente en cada pedido (FR-08) y calcula el
        // precio de venta; la consulta devuelve costos y márgenes, por eso nunca es anónima (R-01).
        builder.Services.AddScoped<AltaArticulo>();
        builder.Services.AddScoped<ObtenerArticulo>();

        // Data Protection (cifra la cookie): claves en IDatosLocales.DirectorioClaves (<directorio de datos>/claves),
        // fuera del repo, y en Windows protegidas con DPAPI. El restablecimiento vacía esa misma carpeta. Fuera de
        // Windows solo se usa en pruebas, con el directorio de datos temporal.
        var proteccion = builder.Services.AddDataProtection().SetApplicationName("OpticaSistema");
        builder.Services.AddOptions<KeyManagementOptions>()
            .Configure<IDatosLocales, ILoggerFactory>((opciones, datos, registro) =>
                opciones.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(datos.DirectorioClaves), registro));
        if (OperatingSystem.IsWindows())
        {
            proteccion.ProtectKeysWithDpapi();
        }

        // Sesión: cookie HttpOnly, Secure y SameSite=Strict que vence a los 60 minutos del último pedido. Sin sesión,
        // 401 sin cuerpo, nunca una redirección.
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(opciones =>
            {
                opciones.Cookie.Name = "OpticaSesion";
                opciones.Cookie.HttpOnly = true;
                opciones.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                opciones.Cookie.SameSite = SameSiteMode.Strict;
                opciones.Cookie.Path = "/";
                opciones.ExpireTimeSpan = RenovacionSesion.Duracion;
                opciones.SlidingExpiration = false;
                opciones.Events.OnValidatePrincipal = RenovacionSesion.AlValidarAsync;
                opciones.Events.OnRedirectToLogin = contexto =>
                {
                    contexto.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                opciones.Events.OnRedirectToAccessDenied = contexto =>
                {
                    contexto.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            });

        // Todo endpoint exige sesión salvo los marcados [AllowAnonymous], que deben ser los de RutasAnonimas (R-01).
        var conSesion = new AuthorizationPolicyBuilder(CookieAuthenticationDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .Build();
        builder.Services.AddAuthorizationBuilder()
            .SetDefaultPolicy(conSesion)
            .SetFallbackPolicy(conSesion);

        // definir, ingresar y cambiar calculan PBKDF2 y comparten este limitador: 2 en paralelo, 10 en cola, el resto
        // 429 (R-05). cambiar exige sesión, así que un pedido sin sesión recibe 401 antes de ocupar un lugar.
        builder.Services.AddRateLimiter(opciones =>
        {
            opciones.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            opciones.AddConcurrencyLimiter(AccesoController.LimitadorAcceso, limitador =>
            {
                limitador.PermitLimit = 2;
                limitador.QueueLimit = 10;
                limitador.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            });
        });

        // Los casos de uso validan los datos y la Api traduce sus errores a campo y acción; la validación automática de
        // MVC no aplica.
        builder.Services.AddControllers()
            .ConfigureApiBehaviorOptions(opciones => opciones.SuppressModelStateInvalidFilter = true);

        // Errores como ProblemDetails sin traza; el manejador registra su propio error en lugar de la excepción tal
        // cual, y el middleware no agrega el suyo (que adjuntaría la excepción con su texto y sus rutas).
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<ManejadorErroresNoControlados>();
        builder.Services.Configure<ExceptionHandlerOptions>(opciones => opciones.SuppressDiagnosticsCallback = _ => true);
    }

    /// <summary>
    /// La rama del comando completa: arma el host con <paramref name="construir"/> (configuración y servicios, sin
    /// inicializar la base), ejecuta <see cref="EjecutarRestablecimientoAsync"/> y lo descarta. Si armar el host falla
    /// (por ejemplo, un appsettings ilegible), devuelve 2 con un mensaje con solo el tipo de la falla y la clave a
    /// revisar: nunca su texto ni su traza, que pueden traer la ruta con el usuario de Windows.
    /// </summary>
    public static async Task<int> EjecutarComandoAsync(
        Func<WebApplication> construir, IReadOnlyList<string> argumentos, TextReader entrada, TextWriter salida)
    {
        WebApplication app;
        try
        {
            app = construir();
        }
        catch (Exception error)
        {
            await salida.WriteLineAsync(
                $"No se pudo preparar el comando ({error.GetType().Name}). Revisá la configuración de la aplicación " +
                $"(appsettings.json, appsettings.Production.json y la clave '{FabricaConexion.ClaveDirectorio}'). " +
                "No se cambió nada.");
            return ComandoRestablecerContrasena.CodigoFallo;
        }

        await using (app)
        {
            return await EjecutarRestablecimientoAsync(app.Services, argumentos, entrada, salida);
        }
    }

    /// <summary>
    /// La rama del comando, sobre los servicios ya construidos y sin inicializar la base: arma
    /// <see cref="ComandoRestablecerContrasena"/> en un alcance propio y devuelve su código de salida (0 restableció, 1
    /// canceló, 2 falló). Con argumentos de más no hace nada y devuelve 2.
    /// </summary>
    public static async Task<int> EjecutarRestablecimientoAsync(
        IServiceProvider servicios, IReadOnlyList<string> argumentos, TextReader entrada, TextWriter salida)
    {
        if (argumentos.Count != 1)
        {
            await salida.WriteLineAsync(
                $"Uso: Optica.Api {ComandoRestablecerContrasena.Nombre}, sin otros argumentos. No se cambió nada.");
            return ComandoRestablecerContrasena.CodigoFallo;
        }

        await using var alcance = servicios.CreateAsyncScope();
        var proveedor = alcance.ServiceProvider;
        ComandoRestablecerContrasena comando;
        try
        {
            comando = new ComandoRestablecerContrasena(
                proveedor.GetRequiredService<RestablecerContrasena>(),
                proveedor.GetRequiredService<IDatosLocales>(),
                proveedor.GetRequiredService<ICandadoInstancia>(),
                entrada,
                salida);
        }
        catch (DirectorioDatosInvalidoException error)
        {
            // Su Ruta ya es la visible; su texto no se copia y no se muestra la traza.
            await salida.WriteLineAsync(
                $"No se puede usar el directorio de datos '{error.Ruta}'. Corregí la clave de configuración " +
                $"'{error.Clave}' con una ruta absoluta. No se cambió nada.");
            return ComandoRestablecerContrasena.CodigoFallo;
        }
        catch (Exception error)
        {
            await salida.WriteLineAsync(
                $"No se pudo preparar el comando ({error.GetType().Name}). Revisá la clave de configuración " +
                $"'{FabricaConexion.ClaveDirectorio}'. No se cambió nada.");
            return ComandoRestablecerContrasena.CodigoFallo;
        }

        return await comando.EjecutarAsync();
    }
}
