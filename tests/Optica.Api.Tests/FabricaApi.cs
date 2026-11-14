using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Optica.Infrastructure.Arca;
using Optica.Infrastructure.Persistencia;

namespace Optica.Api.Tests;

/// <summary>
/// La Api completa sobre un servidor de pruebas: base SQLite en un directorio temporal propio (también las claves
/// de Data Protection y el candado de instancia), reloj controlable y un registro en memoria para inspeccionar el log.
/// Al descartarla se libera el candado y se borra el directorio.
/// </summary>
public sealed class FabricaApi : WebApplicationFactory<Program>
{
    /// <summary>Hora inicial del reloj: segundos enteros, para que los vencimientos de la cookie sean exactos.</summary>
    public static readonly DateTimeOffset Inicio = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    /// <summary>El simulador autoriza (por defecto).</summary>
    public const string RespuestaArcaAutorizar = "autorizar";

    /// <summary>El simulador rechaza con el código 10016 (AC-20).</summary>
    public const string RespuestaArcaRechazar = "rechazar";

    /// <summary>El simulador espera hasta que vence el plazo de la emisión (AC-21).</summary>
    public const string RespuestaArcaNoResponder = "no_responder";

    /// <summary>
    /// El tiempo máximo de ARCA de las pruebas: el de producción (NFR-01). Lo mide el reloj controlado de la fábrica, así
    /// que una prueba de "sin respuesta" adelanta <see cref="Reloj"/> en lugar de esperar.
    /// </summary>
    public const int TiempoMaximoArcaSegundos = 30;

    private readonly Action<IServiceCollection>? _reemplazos;
    private readonly bool _nivelesDeConfiguracion;
    private readonly string _respuestaArca;

    /// <param name="reemplazos">Servicios que la prueba reemplaza después de la raíz de composición.</param>
    /// <param name="directorioDatos">
    /// Valor de <c>Optica:Datos:Directorio</c>; si es nulo, un directorio temporal nuevo que se borra al final.
    /// </param>
    /// <param name="nivelesDeConfiguracion">
    /// Si es verdadero, el registro de captura respeta los niveles de <c>Logging:LogLevel</c> de appsettings.json (ve
    /// lo que se escribiría en producción); si no, ve todos los niveles.
    /// </param>
    /// <param name="respuestaArca">
    /// Lo que responde el simulador de ARCA (<c>Arca:Simulado:Respuesta</c>, D-55): <c>autorizar</c> (por defecto),
    /// <c>rechazar</c> o <c>no_responder</c>. Va en la misma colección en memoria que fija el entorno simulado, por
    /// encima de toda otra fuente, junto con <c>Arca:TiempoMaximoSegundos</c> = <see cref="TiempoMaximoArcaSegundos"/>.
    /// </param>
    public FabricaApi(
        Action<IServiceCollection>? reemplazos = null,
        string? directorioDatos = null,
        bool nivelesDeConfiguracion = false,
        string respuestaArca = RespuestaArcaAutorizar)
    {
        _reemplazos = reemplazos;
        _nivelesDeConfiguracion = nivelesDeConfiguracion;
        _respuestaArca = respuestaArca;
        DirectorioPropio = directorioDatos is null;
        // Cada instancia tiene su propio directorio de datos, y con él su candado de instancia (optica.instancia): las
        // pruebas en paralelo no chocan. Solo comparten directorio las que lo pasan a propósito (reinicio, comando).
        DirectorioDatos = directorioDatos ?? RaizTemporal();
        if (DirectorioPropio)
        {
            Directory.CreateDirectory(DirectorioDatos);
        }

        // Los cookies Secure solo viajan por HTTPS.
        ClientOptions.BaseAddress = new Uri("https://localhost");
    }

    public string DirectorioDatos { get; }

    private bool DirectorioPropio { get; }

    /// <summary>
    /// Un directorio de datos vacío y único bajo la carpeta temporal, para las pruebas que reinician la Api o ejecutan
    /// el comando sobre el mismo directorio; lo borra la prueba.
    /// </summary>
    public static string NuevoDirectorioTemporal()
    {
        var directorio = RaizTemporal();
        Directory.CreateDirectory(directorio);
        return directorio;
    }

    /// <summary>
    /// <c>&lt;temp&gt;/optica-pruebas-api/&lt;guid&gt;</c>. Nunca en una unidad de Windows montada en WSL (<c>/mnt/…</c>):
    /// ahí <c>FileShare.None</c> no es un candado confiable y el candado de instancia no protegería nada.
    /// </summary>
    private static string RaizTemporal()
    {
        var directorio = Path.Combine(Path.GetTempPath(), "optica-pruebas-api", Guid.NewGuid().ToString("N"));
        ComprobarFueraDeMnt(directorio);
        return directorio;
    }

    /// <summary>Antigüedad a partir de la cual un directorio de prueba del perfil se considera abandonado.</summary>
    private static readonly TimeSpan AntiguedadAbandonado = TimeSpan.FromHours(1);

    /// <summary>Limpia una sola vez por proceso, antes de crear el primer directorio en el perfil.</summary>
    private static readonly Lazy<string> RaizLimpia = new(() =>
    {
        var raiz = RaizEnElPerfil;
        LimpiarDirectoriosViejos(raiz, AntiguedadAbandonado);
        return raiz;
    });

    /// <summary>
    /// Raíz fija de los directorios de prueba que tienen que estar dentro de la carpeta del usuario (para que la ruta
    /// visible y la expandida difieran): en Windows, la carpeta temporal si ya está bajo el perfil; si no,
    /// <c>&lt;perfil&gt;/.cache/optica-pruebas</c>. Nunca la raíz del perfil, y con la misma guarda <c>/mnt/</c>.
    /// </summary>
    public static string RaizEnElPerfil
    {
        get
        {
            var perfil = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(perfil))
            {
                throw new InvalidOperationException("Las pruebas necesitan la carpeta del usuario.");
            }

            var temporal = Path.GetTempPath();
            var raiz = OperatingSystem.IsWindows() && EstaDentro(temporal, perfil)
                ? Path.Combine(temporal, "optica-pruebas-perfil")
                : Path.Combine(perfil, ".cache", "optica-pruebas");
            ComprobarFueraDeMnt(raiz);
            return raiz;
        }
    }

    /// <summary>
    /// <c>&lt;<see cref="RaizEnElPerfil"/>&gt;/&lt;guid&gt;</c>, creado; lo borra la prueba. La primera vez en el proceso
    /// borra los directorios abandonados de esa raíz.
    /// </summary>
    public static string NuevoDirectorioEnElPerfil()
    {
        var directorio = Path.Combine(RaizLimpia.Value, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directorio);
        return directorio;
    }

    /// <summary>
    /// Borra los subdirectorios de <paramref name="raiz"/> sin cambios desde hace más de <paramref name="antiguedad"/>
    /// (los de otras pruebas en curso, recientes, quedan). Lo que no se puede borrar se informa y se deja.
    /// </summary>
    public static void LimpiarDirectoriosViejos(string raiz, TimeSpan antiguedad)
    {
        if (!Directory.Exists(raiz))
        {
            return;
        }

        var limite = DateTime.UtcNow - antiguedad;
        foreach (var directorio in Directory.EnumerateDirectories(raiz))
        {
            if (Directory.GetLastWriteTimeUtc(directorio) < limite)
            {
                BorrarDirectorioDePrueba(directorio);
            }
        }
    }

    /// <summary>
    /// Libera las conexiones agrupadas de SQLite de las bases <c>optica.db</c> que haya en <paramref name="directorio"/>
    /// o debajo, solo las de esas bases. <see cref="SqliteConnection.ClearAllPools"/> es global al proceso: con pruebas en
    /// paralelo cierra conexiones de otras bases en uso.
    /// </summary>
    public static void LiberarConexiones(string directorio)
    {
        var bases = Directory.Exists(directorio)
            ? Directory.EnumerateFiles(directorio, FabricaConexion.NombreArchivo, SearchOption.AllDirectories)
                .Select(archivo => Path.GetDirectoryName(archivo)!)
            : [];
        foreach (var carpeta in bases.Append(directorio).Distinct(StringComparer.Ordinal))
        {
            // La misma cadena que arma la Api para ese directorio: es la clave de su grupo de conexiones.
            using var conexion = new SqliteConnection(new FabricaConexion(carpeta).CadenaConexion);
            SqliteConnection.ClearPool(conexion);
        }
    }

    /// <summary>Libera las conexiones de las bases del directorio y lo borra; si no se puede, lo informa.</summary>
    public static void BorrarDirectorioDePrueba(string directorio)
    {
        LiberarConexiones(directorio);
        try
        {
            if (Directory.Exists(directorio))
            {
                Directory.Delete(directorio, recursive: true);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(
                $"[FabricaApi] No se pudo borrar un directorio de prueba ({error.GetType().Name}).");
        }
    }

    private static bool EstaDentro(string ruta, string carpeta)
    {
        var relativa = Path.GetRelativePath(carpeta, ruta);
        return relativa != "." && !relativa.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relativa);
    }

    private static void ComprobarFueraDeMnt(string directorio)
    {
        if (!OperatingSystem.IsWindows() && directorio.StartsWith("/mnt/", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "La carpeta de las pruebas está en una unidad de Windows montada; configurá TMPDIR y HOME fuera de /mnt.");
        }
    }

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
        // R-14: ARCA simulada, fijada en memoria y agregada al final (le gana a las variables de entorno Arca__*): las
        // pruebas de la Api nunca emiten contra homologación ni producción por la configuración de la PC. La respuesta
        // del simulador la elige la prueba (D-55) y el tiempo máximo queda fijo en el de producción.
        builder.ConfigureAppConfiguration(configuracion => configuracion.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                [OpcionesArca.ClaveEntorno] = "Simulado",
                [OpcionesArca.ClaveRespuestaSimulado] = _respuestaArca,
                [OpcionesArca.ClaveTiempoMaximo] =
                    TiempoMaximoArcaSegundos.ToString(System.Globalization.CultureInfo.InvariantCulture),
            }));
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

        BorrarDirectorioDePrueba(DirectorioDatos);
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
