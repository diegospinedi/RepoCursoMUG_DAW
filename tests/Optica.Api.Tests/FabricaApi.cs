using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Optica.Infrastructure.Persistencia;

namespace Optica.Api.Tests;

/// <summary>
/// La Api completa sobre un servidor de pruebas: base SQLite en un directorio temporal propio (también las claves
/// de Data Protection), reloj controlable y un registro en memoria para inspeccionar el log. Al descartarla se
/// borra el directorio.
/// </summary>
public sealed class FabricaApi : WebApplicationFactory<Program>
{
    /// <summary>Hora inicial del reloj: segundos enteros, para que los vencimientos de la cookie sean exactos.</summary>
    public static readonly DateTimeOffset Inicio = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    private readonly Action<IServiceCollection>? _reemplazos;
    private readonly bool _nivelesDeConfiguracion;

    /// <param name="reemplazos">Servicios que la prueba reemplaza después de la raíz de composición.</param>
    /// <param name="directorioDatos">
    /// Valor de <c>Optica:Datos:Directorio</c>; si es nulo, un directorio temporal nuevo que se borra al final.
    /// </param>
    /// <param name="nivelesDeConfiguracion">
    /// Si es verdadero, el registro de captura respeta los niveles de <c>Logging:LogLevel</c> de appsettings.json (ve
    /// lo que se escribiría en producción); si no, ve todos los niveles.
    /// </param>
    public FabricaApi(
        Action<IServiceCollection>? reemplazos = null, string? directorioDatos = null, bool nivelesDeConfiguracion = false)
    {
        _reemplazos = reemplazos;
        _nivelesDeConfiguracion = nivelesDeConfiguracion;
        DirectorioPropio = directorioDatos is null;
        DirectorioDatos = directorioDatos
            ?? Path.Combine(Path.GetTempPath(), "optica-pruebas-api", Guid.NewGuid().ToString("N"));
        if (DirectorioPropio)
        {
            Directory.CreateDirectory(DirectorioDatos);
        }

        // Los cookies Secure solo viajan por HTTPS.
        ClientOptions.BaseAddress = new Uri("https://localhost");
    }

    public string DirectorioDatos { get; }

    private bool DirectorioPropio { get; }

    public FakeTimeProvider Reloj { get; } = new(Inicio);

    public RegistroEnMemoria Registro { get; } = new();

    /// <summary>Cliente que no guarda cookies: la prueba las maneja a mano.</summary>
    public HttpClient CrearClienteSinCookies() =>
        CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
            AllowAutoRedirect = false,
        });

    /// <summary>Lee la tabla <c>EventoAcceso</c> en un alcance propio.</summary>
    public List<EventoAccesoFila> Eventos()
    {
        using var alcance = Services.CreateScope();
        return alcance.ServiceProvider.GetRequiredService<OpticaDbContext>().EventosAcceso
            .OrderBy(evento => evento.Id)
            .ToList();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting(FabricaConexion.ClaveDirectorio, DirectorioDatos);
        // Salvo que se pidan los niveles de la configuración, el registro de captura ve todos los niveles, incluido
        // Debug, sin importar los filtros de appsettings: así las pruebas detectan datos sensibles también en los
        // niveles que hoy no se escriben.
        builder.ConfigureLogging(log =>
        {
            log.AddProvider(Registro);
            if (!_nivelesDeConfiguracion)
            {
                log.AddFilter<RegistroEnMemoria>(null, LogLevel.Trace);
            }
        });
        builder.ConfigureTestServices(servicios =>
        {
            servicios.RemoveAll<TimeProvider>();
            servicios.AddSingleton<TimeProvider>(Reloj);
            _reemplazos?.Invoke(servicios);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing || !DirectorioPropio)
        {
            return;
        }

        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(DirectorioDatos))
            {
                Directory.Delete(DirectorioDatos, recursive: true);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(
                $"[FabricaApi] No se pudo borrar el directorio de prueba '{DirectorioDatos}': {error.GetType().Name}");
        }
    }
}

/// <summary>
/// Una línea del log capturada: categoría, nivel, mensaje ya formateado, la excepción adjunta, si hubo, y el evento.
/// </summary>
public sealed record LineaRegistro(
    string Categoria, LogLevel Nivel, string Mensaje, Exception? Excepcion, EventId Evento, string Propiedades)
{
    /// <summary>
    /// El mensaje, las propiedades estructuradas (lo que escribe un destino de log estructurado) y la excepción
    /// completa (con sus causas y su traza), como texto.
    /// </summary>
    public string TextoCompleto =>
        Mensaje + Environment.NewLine + Propiedades + (Excepcion is null ? string.Empty : Environment.NewLine + Excepcion);
}

/// <summary>Proveedor de log que guarda todo en memoria.</summary>
public sealed class RegistroEnMemoria : ILoggerProvider
{
    private readonly ConcurrentQueue<LineaRegistro> _lineas = new();

    public IReadOnlyList<LineaRegistro> Lineas => _lineas.ToArray();

    public ILogger CreateLogger(string categoryName) => new Registrador(categoryName, _lineas);

    public void Dispose()
    {
    }

    private sealed class Registrador(string categoria, ConcurrentQueue<LineaRegistro> lineas) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            lineas.Enqueue(new LineaRegistro(
                categoria, logLevel, formatter(state, exception), exception, eventId, Propiedades(state)));

        private static string Propiedades<TState>(TState state) =>
            state is IEnumerable<KeyValuePair<string, object?>> propiedades
                ? string.Join("; ", propiedades.Select(p => $"{p.Key}={p.Value}"))
                : string.Empty;
    }
}
