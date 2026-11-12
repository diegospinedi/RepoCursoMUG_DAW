using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Optica.Application.Comun;

namespace Optica.Infrastructure.Persistencia;

/// <summary>
/// Resuelve dónde vive la base SQLite a partir de <c>Optica:Datos:Directorio</c> y arma las conexiones:
/// cada conexión abierta activa las claves foráneas y el modo WAL y registra las funciones de búsqueda (ADR-011).
/// </summary>
public sealed class FabricaConexion
{
    public const string ClaveDirectorio = "Optica:Datos:Directorio";
    public const string NombreArchivo = "optica.db";
    public const string CarpetaPorDefecto = "OpticaSistema";

    public FabricaConexion(string? directorioConfigurado)
        : this(directorioConfigurado, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))
    {
    }

    /// <summary>
    /// Permite indicar la carpeta de datos locales del usuario en lugar de tomarla del sistema (para pruebas).
    /// </summary>
    internal FabricaConexion(string? directorioConfigurado, string carpetaDatosLocales)
    {
        EsPorDefecto = string.IsNullOrWhiteSpace(directorioConfigurado);
        Directorio = ResolverDirectorio(directorioConfigurado, carpetaDatosLocales);
        RutaVisible = EsPorDefecto ? MostrarRutaPorDefecto(carpetaDatosLocales) : RutasPrivadas.Ocultar(Directorio);
        CadenaConexion = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(Directorio, NombreArchivo),
            Mode = SqliteOpenMode.ReadWriteCreate,
            // Las FK se piden en la cadena para cualquier conexión, pase o no por EF Core.
            ForeignKeys = true,
            // ADR-013 y NFR-01 de FEAT-003d: la escritura se toma con BEGIN IMMEDIATE y, si la base está ocupada,
            // Microsoft.Data.Sqlite lo reintenta hasta este tiempo (en segundos). Es su valor por defecto, declarado
            // explícito; vencido, el SQLITE_BUSY se propaga y no se graba nada.
            DefaultTimeout = 30,
        }.ToString();
    }

    /// <summary>Ruta absoluta del directorio de datos, fuera del árbol del código.</summary>
    public string Directorio { get; }

    /// <summary>
    /// La ruta como se muestra en mensajes y en el log, sin el nombre de usuario de la PC. Si se configuró, es la
    /// ruta con la carpeta del usuario reemplazada (<see cref="RutasPrivadas"/>); si es la de por defecto, va sin
    /// expandir la carpeta del usuario (por ejemplo <c>%LOCALAPPDATA%\OpticaSistema</c>).
    /// </summary>
    public string RutaVisible { get; }

    /// <summary>Verdadero si <c>Optica:Datos:Directorio</c> está vacío o ausente.</summary>
    public bool EsPorDefecto { get; }

    public string CadenaConexion { get; }

    /// <summary>
    /// Vacío o ausente: <c>LocalApplicationData/OpticaSistema</c>. Configurado: debe ser una ruta absoluta. En los
    /// errores, la ruta pasa por <see cref="RutasPrivadas"/>: llega al log y puede contener la carpeta del usuario.
    /// </summary>
    private static string ResolverDirectorio(string? directorioConfigurado, string datosLocales)
    {
        if (string.IsNullOrWhiteSpace(directorioConfigurado))
        {
            if (string.IsNullOrEmpty(datosLocales) || !Path.IsPathFullyQualified(datosLocales))
            {
                throw new DirectorioDatosInvalidoException(
                    RutasPrivadas.Ocultar(datosLocales),
                    $"No se pudo determinar la carpeta de datos local del usuario. Configurá la clave '{ClaveDirectorio}' " +
                    "con una ruta absoluta.");
            }

            return Path.Combine(datosLocales, CarpetaPorDefecto);
        }

        if (!Path.IsPathFullyQualified(directorioConfigurado))
        {
            var visible = RutasPrivadas.Ocultar(directorioConfigurado);
            throw new DirectorioDatosInvalidoException(
                visible,
                $"El directorio de datos '{visible}' no es una ruta absoluta. Corregí la clave de " +
                $"configuración '{ClaveDirectorio}' con una ruta absoluta, o dejala vacía para usar la ubicación por defecto.");
        }

        return Path.GetFullPath(directorioConfigurado);
    }

    private static string MostrarRutaPorDefecto(string datosLocales)
    {
        if (OperatingSystem.IsWindows())
        {
            return @"%LOCALAPPDATA%\" + CarpetaPorDefecto;
        }

        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrEmpty(xdg) && MismaRuta(xdg, datosLocales))
        {
            return "$XDG_DATA_HOME/" + CarpetaPorDefecto;
        }

        var perfil = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(perfil) && Path.IsPathFullyQualified(datosLocales))
        {
            var relativa = Path.GetRelativePath(perfil, datosLocales);
            if (relativa == ".")
            {
                return "~/" + CarpetaPorDefecto;
            }

            if (!relativa.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relativa))
            {
                return $"~/{relativa}/{CarpetaPorDefecto}";
            }
        }

        return "<carpeta de datos local del usuario>/" + CarpetaPorDefecto;
    }

    private static bool MismaRuta(string una, string otra) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(una),
            Path.TrimEndingDirectorySeparator(otra),
            StringComparison.Ordinal);

    /// <summary>
    /// Crea el directorio si no existe y comprueba que se pueda escribir en él. Si no se puede, lanza
    /// <see cref="DirectorioDatosInvalidoException"/> nombrando la ruta y la clave de configuración.
    /// </summary>
    public void PrepararDirectorio()
    {
        try
        {
            Directory.CreateDirectory(Directorio);
            var sonda = Path.Combine(Directorio, $".sonda-escritura-{Guid.NewGuid():N}.tmp");
            using (File.Create(sonda, 1, FileOptions.DeleteOnClose))
            {
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // El mensaje de la excepción original trae la ruta expandida: no se copia al mensaje propio ni se adjunta
            // como causa (llegaría al log con el nombre de usuario), tampoco para un directorio configurado.
            throw new DirectorioDatosInvalidoException(
                RutaVisible,
                $"No se puede crear o escribir en el directorio de datos '{RutaVisible}' ({error.GetType().Name}). " +
                $"Corregí la clave de configuración '{ClaveDirectorio}' o los permisos de esa carpeta.");
        }
    }

    /// <summary>
    /// Configura EF Core con SQLite y el interceptor que fija los PRAGMA de cada conexión.
    /// Los eventos de error de EF Core se bajan a Debug: <c>ConnectionError</c> lleva el DataSource completo, que en la
    /// ruta por defecto incluye el nombre de usuario de la PC, y los demás adjuntan la excepción de SQLite tal cual. El
    /// diagnóstico queda en el error propio, con <see cref="RutaVisible"/> y la clave de configuración (ver
    /// <see cref="DatosLocales"/>), y en la excepción que se propaga. Nunca activar <c>EnableSensitiveDataLogging</c>
    /// ni <c>EnableDetailedErrors</c>.
    /// </summary>
    public void Configurar(DbContextOptionsBuilder opciones) =>
        opciones
            .UseSqlite(CadenaConexion)
            .AddInterceptors(PragmasAlAbrir.Instancia)
            .ConfigureWarnings(avisos => avisos.Log(
                (RelationalEventId.ConnectionError, LogLevel.Debug),
                (RelationalEventId.CommandError, LogLevel.Debug),
                (RelationalEventId.TransactionError, LogLevel.Debug),
                (CoreEventId.SaveChangesFailed, LogLevel.Debug),
                (CoreEventId.QueryIterationFailed, LogLevel.Debug)));

    /// <summary>
    /// Activa <c>foreign_keys</c> y <c>journal_mode = WAL</c> en cada conexión que se abre, y registra las funciones
    /// <c>plegar</c> y <c>orden_articulo</c> de la búsqueda de artículos (<see cref="FuncionesSqlite"/>, ADR-011).
    /// </summary>
    private sealed class PragmasAlAbrir : DbConnectionInterceptor
    {
        public static readonly PragmasAlAbrir Instancia = new();

        private const string Pragmas = "PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL;";

        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        {
            RegistrarFunciones(connection);
            using var comando = connection.CreateCommand();
            comando.CommandText = Pragmas;
            comando.ExecuteNonQuery();
        }

        public override async Task ConnectionOpenedAsync(
            DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            RegistrarFunciones(connection);
            await using var comando = connection.CreateCommand();
            comando.CommandText = Pragmas;
            await comando.ExecuteNonQueryAsync(cancellationToken);
        }

        // EF Core con UseSqlite siempre abre una SqliteConnection; otra cosa es un error de configuración.
        private static void RegistrarFunciones(DbConnection connection) =>
            FuncionesSqlite.Registrar(connection as SqliteConnection
                ?? throw new InvalidOperationException("La conexión de la base no es de SQLite."));
    }
}

/// <summary>El directorio de datos configurado no se puede usar; la Api no debe arrancar.</summary>
public sealed class DirectorioDatosInvalidoException : Exception
{
    public DirectorioDatosInvalidoException(string ruta, string mensaje, Exception? causa = null)
        : base(mensaje, causa)
    {
        Ruta = ruta;
    }

    /// <summary>La ruta que no se pudo usar.</summary>
    public string Ruta { get; }

    /// <summary>La clave de configuración que hay que corregir.</summary>
    public string Clave => FabricaConexion.ClaveDirectorio;
}
