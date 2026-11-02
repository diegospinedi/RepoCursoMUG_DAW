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
using Optica.Application.Acceso;
using Optica.Application.Comun;
using Optica.Application.Marca;
using Optica.Infrastructure;

// Raíz de composición única. HTTPS: Kestrel escucha solo en el endpoint Kestrel:Endpoints:Https de appsettings.json
// y toma el certificado del almacén de Windows con Kestrel:Certificates:Default:Store=My, :Location=CurrentUser y
// :Subject=OpticaSistema, que se fijan al instalar (variables de entorno o un appsettings.Production.json local,
// ignorado por git); sin esas claves, en desarrollo, usa el certificado de desarrollo de .NET. Ni el certificado ni
// ninguna contraseña van en appsettings ni en el repo.
// Sin CORS: la SPA se sirve desde el mismo origen. Nunca se registra el cuerpo de los pedidos (definir e ingresar
// traen la contraseña en claro): no agregar HttpLogging de cuerpos.
var builder = WebApplication.CreateBuilder(args);

// Infraestructura: base, repositorios, hasher y datos locales. Los errores de la base no se registran tal cual (su
// texto puede traer la ruta con el nombre de usuario de Windows): la infraestructura los deja en Debug y el manejador
// de errores registra uno propio con la ruta visible.
builder.Services.AgregarInfraestructura(builder.Configuration);

// Casos de uso de acceso.
builder.Services.AddScoped<ConsultarEstadoAcceso>();
builder.Services.AddScoped<DefinirContrasena>();
builder.Services.AddScoped<IngresarConContrasena>();
builder.Services.AddScoped<CerrarSesion>();

// Marca (colores y logo), anónima: la de Optica:Marca:Directorio o la de ejemplo.
builder.Services.AddScoped<ObtenerMarca>();

// Data Protection (cifra la cookie): claves en <directorio de datos>/claves, fuera del repo, y en Windows
// protegidas con DPAPI. Fuera de Windows solo se usa en pruebas, con el directorio de datos temporal.
var proteccion = builder.Services.AddDataProtection().SetApplicationName("OpticaSistema");
builder.Services.AddOptions<KeyManagementOptions>()
    .Configure<IDatosLocales, ILoggerFactory>((opciones, datos, registro) =>
        opciones.XmlRepository = new FileSystemXmlRepository(
            new DirectoryInfo(Path.Combine(datos.Directorio, "claves")), registro));
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

// definir e ingresar calculan PBKDF2: 2 en paralelo, 10 en cola, el resto 429 (R-05).
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

// Los casos de uso validan los datos y la Api traduce sus errores a campo y acción; la validación automática de MVC
// no aplica.
builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(opciones => opciones.SuppressModelStateInvalidFilter = true);

// Errores como ProblemDetails sin traza; el manejador registra su propio error en lugar de la excepción tal cual, y el
// middleware no agrega el suyo (que adjuntaría la excepción con su texto y sus rutas).
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ManejadorErroresNoControlados>();
builder.Services.Configure<ExceptionHandlerOptions>(opciones => opciones.SuppressDiagnosticsCallback = _ => true);

var app = builder.Build();

// Aplica las migraciones pendientes. Si el directorio de datos no se puede crear o escribir, registra
// el error con la ruta y la clave Optica:Datos:Directorio, y la Api no arranca.
app.Services.InicializarBaseDeDatos();

app.UseExceptionHandler();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

// Content-Type JSON en todo POST (415) y tamaño máximo del cuerpo (413), antes del limitador (R-04, R-05).
app.Use(LimitesDelCuerpo.AplicarAsync);

app.UseRateLimiter();

app.MapControllers();

app.Run();

/// <summary>Punto de entrada; parcial y público para que las pruebas levanten la Api.</summary>
public partial class Program;
