using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Optica.Application.Comun;
using Optica.Infrastructure.Persistencia;

namespace Optica.Infrastructure.Tests.Persistencia;

public class PersistenciaTests
{
    private const int SqliteRestriccion = 19; // SQLITE_CONSTRAINT

    [Fact]
    public void Migracion_CreaTablasConRestricciones()
    {
        using var baseTemporal = new BaseTemporal();
        using var contexto = baseTemporal.CrearContexto();
        contexto.Database.OpenConnection();
        var conexion = contexto.Database.GetDbConnection();

        // La base vive en el directorio configurado y la migración versionada quedó aplicada.
        Assert.True(File.Exists(Path.Combine(baseTemporal.Directorio, FabricaConexion.NombreArchivo)));
        Assert.Contains(contexto.Database.GetAppliedMigrations(), m => m.EndsWith("_Inicial", StringComparison.Ordinal));
        Assert.Empty(contexto.Database.GetPendingMigrations());

        // Ambas tablas existen.
        var tablas = Lista(conexion, "SELECT name FROM sqlite_master WHERE type = 'table'");
        Assert.Contains("CredencialAcceso", tablas);
        Assert.Contains("EventoAcceso", tablas);

        // CredencialAcceso: a lo sumo una fila (CHECK Id = 1), Hash y DefinidaEn obligatorios.
        var sqlCredencial = Escalar(conexion, "SELECT sql FROM sqlite_master WHERE name = 'CredencialAcceso'");
        Assert.Matches(@"CHECK\s*\(\s*""?Id""?\s*=\s*1\s*\)", sqlCredencial);
        var noNulasCredencial = ColumnasNoNulas(conexion, "CredencialAcceso");
        Assert.Contains("Hash", noNulasCredencial);
        Assert.Contains("DefinidaEn", noNulasCredencial);
        Assert.Equal("TEXT", TipoColumna(conexion, "CredencialAcceso", "Hash"));
        Assert.Equal("TEXT", TipoColumna(conexion, "CredencialAcceso", "DefinidaEn"));

        // EventoAcceso: Id autoincremental, columnas obligatorias e índice sobre FechaHora.
        var sqlEvento = Escalar(conexion, "SELECT sql FROM sqlite_master WHERE name = 'EventoAcceso'");
        Assert.Contains("AUTOINCREMENT", sqlEvento, StringComparison.OrdinalIgnoreCase);
        var noNulasEvento = ColumnasNoNulas(conexion, "EventoAcceso");
        Assert.Contains("FechaHora", noNulasEvento);
        Assert.Contains("Tipo", noNulasEvento);
        Assert.Contains("Resultado", noNulasEvento);
        Assert.Equal("TEXT", TipoColumna(conexion, "EventoAcceso", "FechaHora"));
        var columnasIndexadas = Lista(conexion,
            "SELECT ii.name FROM pragma_index_list('EventoAcceso') AS il " +
            "JOIN pragma_index_info(il.name) AS ii");
        Assert.Contains("FechaHora", columnasIndexadas);

        // Claves foráneas activas y modo WAL en la conexión que usa la aplicación.
        Assert.Equal("1", Escalar(conexion, "PRAGMA foreign_keys"));
        Assert.Equal("wal", Escalar(conexion, "PRAGMA journal_mode"), ignoreCase: true);
    }

    [Fact]
    public void CredencialAcceso_SegundaFila_RechazadaComoDuplicado()
    {
        using var baseTemporal = new BaseTemporal();
        using var contexto = baseTemporal.CrearContexto();
        const string hash = "PBKDF2-SHA256$600000$c2Fs$aGFzaA==";
        const string definidaEn = "2026-10-30T12:00:00.0000000Z";

        contexto.Database.ExecuteSql(
            $"INSERT INTO CredencialAcceso (Id, Hash, DefinidaEn) VALUES ({1}, {hash}, {definidaEn})");

        var conIdDos = Assert.Throws<SqliteException>(() => contexto.Database.ExecuteSql(
            $"INSERT INTO CredencialAcceso (Id, Hash, DefinidaEn) VALUES ({2}, {hash}, {definidaEn})"));
        Assert.Equal(SqliteRestriccion, conIdDos.SqliteErrorCode);
        Assert.Contains("CHECK", conIdDos.Message, StringComparison.OrdinalIgnoreCase);

        var repitiendoIdUno = Assert.Throws<SqliteException>(() => contexto.Database.ExecuteSql(
            $"INSERT INTO CredencialAcceso (Id, Hash, DefinidaEn) VALUES ({1}, {hash}, {definidaEn})"));
        Assert.Equal(SqliteRestriccion, repitiendoIdUno.SqliteErrorCode);
        Assert.Contains("UNIQUE", repitiendoIdUno.Message, StringComparison.OrdinalIgnoreCase);

        contexto.Database.OpenConnection();
        Assert.Equal("1", Escalar(contexto.Database.GetDbConnection(), "SELECT COUNT(*) FROM CredencialAcceso"));
    }

    [Fact]
    public void DirectorioDatos_Invalido_ImpideArrancar()
    {
        var raiz = BaseTemporal.CrearDirectorioTemporal();
        var sinEscritura = Path.Combine(raiz, "sin-escritura");
        try
        {
            // Una ruta cuyo padre es un archivo: no se puede crear en ningún sistema operativo.
            var archivo = Path.Combine(raiz, "soy-un-archivo");
            File.WriteAllText(archivo, "x");
            var casos = new List<string> { Path.Combine(archivo, "datos"), Path.Combine("relativa", "datos") };

            // Un directorio existente sin permiso de escritura (solo donde los permisos Unix aplican y
            // el usuario no los saltea, como root).
            if (!OperatingSystem.IsWindows())
            {
                Directory.CreateDirectory(sinEscritura);
                File.SetUnixFileMode(sinEscritura, UnixFileMode.UserRead | UnixFileMode.UserExecute);
                if (!SePuedeEscribir(sinEscritura))
                {
                    casos.Add(sinEscritura);
                }
            }

            foreach (var ruta in casos)
            {
                var registro = new RegistroCapturado();
                using var servicios = BaseTemporal.CrearServicios(ruta, registro);

                var error = Assert.Throws<DirectorioDatosInvalidoException>(() => servicios.InicializarBaseDeDatos());

                // La ruta como se muestra: en Windows, la carpeta temporal está dentro de la del usuario.
                var visible = RutasPrivadas.Ocultar(ruta);
                Assert.Contains(visible, error.Message, StringComparison.Ordinal);
                Assert.Contains(FabricaConexion.ClaveDirectorio, error.Message, StringComparison.Ordinal);
                Assert.Equal(visible, error.Ruta);
                Assert.Contains(registro.Entradas, e =>
                    e.Nivel >= LogLevel.Error
                    && e.Mensaje.Contains(visible, StringComparison.Ordinal)
                    && e.Mensaje.Contains(FabricaConexion.ClaveDirectorio, StringComparison.Ordinal));
            }
        }
        finally
        {
            if (!OperatingSystem.IsWindows() && Directory.Exists(sinEscritura))
            {
                File.SetUnixFileMode(sinEscritura,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            BaseTemporal.BorrarDirectorio(raiz);
        }
    }

    [Fact]
    public void DirectorioDatos_PorDefecto_RutaAbsolutaFueraDelRepositorio()
    {
        // Sin configuración: la clave ausente y la clave vacía (como en appsettings.json).
        foreach (var configurado in new string?[] { null, "" })
        {
            using var servicios = BaseTemporal.CrearServicios(configurado);
            var ruta = servicios.GetRequiredService<FabricaConexion>().Directorio;

            Assert.True(Path.IsPathFullyQualified(ruta), $"La ruta '{ruta}' no es absoluta.");
            var esperada = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpticaSistema");
            Assert.Equal(esperada, ruta);

            var repositorio = RaizDelRepositorio();
            var relativa = Path.GetRelativePath(repositorio, ruta);
            Assert.True(
                relativa.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relativa),
                $"La ruta por defecto '{ruta}' está dentro del repositorio '{repositorio}'.");
        }
    }

    [Fact]
    public void CadenaConexion_ConexionDirecta_ClavesForaneasActivas()
    {
        var directorio = BaseTemporal.CrearDirectorioTemporal();
        try
        {
            var fabrica = new FabricaConexion(directorio);
            fabrica.PrepararDirectorio();

            // La cadena pide las FK de forma explícita: no depende de cómo se compiló la biblioteca nativa
            // de SQLite (la que trae SQLitePCLRaw ya las activa por defecto, otras no).
            Assert.Equal(true, new SqliteConnectionStringBuilder(fabrica.CadenaConexion).ForeignKeys);

            // Una conexión abierta con la cadena, sin pasar por EF Core ni su interceptor.
            using (var conexion = new SqliteConnection(fabrica.CadenaConexion))
            {
                conexion.Open();
                Assert.Equal("1", Escalar(conexion, "PRAGMA foreign_keys"));
            }
        }
        finally
        {
            BaseTemporal.BorrarDirectorio(directorio);
        }
    }

    [Fact]
    public void DirectorioDatos_PorDefectoInvalido_NoExponeRutaExpandida()
    {
        // La ruta por defecto, tal como se muestra, no lleva la carpeta del usuario expandida.
        var visiblePorDefecto = new FabricaConexion(null).RutaVisible;
        Assert.EndsWith(FabricaConexion.CarpetaPorDefecto, visiblePorDefecto, StringComparison.Ordinal);
        Assert.DoesNotContain(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), visiblePorDefecto, StringComparison.OrdinalIgnoreCase);
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(@"%LOCALAPPDATA%\OpticaSistema", visiblePorDefecto);
        }

        // Una carpeta de datos locales donde no se puede crear OpticaSistema (es un archivo), en lugar
        // de la del usuario: la ruta expandida no debe aparecer en el error ni en el log.
        var raiz = BaseTemporal.CrearDirectorioTemporal();
        try
        {
            var datosLocales = Path.Combine(raiz, "datos-locales-del-usuario");
            File.WriteAllText(datosLocales, "x");
            var fabrica = new FabricaConexion(null, datosLocales);
            var registro = new RegistroCapturado();
            using var servicios = BaseTemporal.CrearServicios(null, registro, fabrica);

            var error = Assert.Throws<DirectorioDatosInvalidoException>(() => servicios.InicializarBaseDeDatos());

            Assert.Equal(fabrica.RutaVisible, error.Ruta);
            Assert.Contains(fabrica.RutaVisible, error.Message, StringComparison.Ordinal);
            Assert.Contains(FabricaConexion.ClaveDirectorio, error.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(datosLocales, error.ToString(), StringComparison.Ordinal);
            Assert.Contains(registro.Entradas, e =>
                e.Nivel == LogLevel.Critical
                && e.Mensaje.Contains(fabrica.RutaVisible, StringComparison.Ordinal)
                && e.Mensaje.Contains(FabricaConexion.ClaveDirectorio, StringComparison.Ordinal));
            Assert.All(registro.Entradas, e =>
            {
                Assert.DoesNotContain(datosLocales, e.Mensaje, StringComparison.Ordinal);
                Assert.DoesNotContain(datosLocales, e.Excepcion ?? "", StringComparison.Ordinal);
                // El mensaje de la excepción va con la excepción, no repetido en la plantilla.
                Assert.DoesNotContain(error.Message, e.Mensaje, StringComparison.Ordinal);
            });
        }
        finally
        {
            BaseTemporal.BorrarDirectorio(raiz);
        }
    }

    [Fact]
    public void Migracion_BaseDanadaEnRutaPorDefecto_NoExponeRutaExpandida()
    {
        // Dos fallas de la base en la ruta por defecto: un archivo que no es SQLite, y un optica.db que no se
        // puede abrir (es un directorio).
        var casos = new (string Nombre, Action<string> Preparar)[]
        {
            ("archivo dañado", archivo => File.WriteAllText(archivo, new string('x', 4096))),
            ("no se puede abrir", archivo => Directory.CreateDirectory(archivo)),
        };

        foreach (var (nombre, preparar) in casos)
        {
            var raiz = BaseTemporal.CrearDirectorioTemporal();
            try
            {
                // La carpeta de datos locales del usuario, simulada.
                var datosLocales = Path.Combine(raiz, "datos-locales-del-usuario");
                var directorioDatos = Path.Combine(datosLocales, FabricaConexion.CarpetaPorDefecto);
                Directory.CreateDirectory(directorioDatos);
                preparar(Path.Combine(directorioDatos, FabricaConexion.NombreArchivo));
                var fabrica = new FabricaConexion(null, datosLocales);

                // Captura todas las categorías (las de EF Core incluidas) desde Information, como appsettings.json.
                var registro = new RegistroCapturado();
                using var servicios = BaseTemporal.CrearServicios(null, registro, fabrica);

                Assert.Throws<SqliteException>(() => servicios.InicializarBaseDeDatos());

                Assert.Contains(registro.Entradas, e =>
                    e.Nivel == LogLevel.Critical
                    && e.Mensaje.Contains(fabrica.RutaVisible, StringComparison.Ordinal)
                    && e.Mensaje.Contains(FabricaConexion.ClaveDirectorio, StringComparison.Ordinal));

                // El mismo problema al usar la base fuera del arranque (un pedido cualquiera): EF Core no debe
                // dejar la ruta expandida en el log.
                using (var alcance = servicios.CreateScope())
                {
                    var contexto = alcance.ServiceProvider.GetRequiredService<OpticaDbContext>();
                    Assert.Throws<SqliteException>(() => contexto.Database.ExecuteSqlRaw("SELECT 1"));
                }
                Assert.All(registro.Entradas, e =>
                {
                    Assert.False(e.Mensaje.Contains(datosLocales, StringComparison.Ordinal),
                        $"[{nombre}] El log ({e.Nivel}, {e.Categoria}) expone la ruta expandida: {e.Mensaje}");
                    Assert.False((e.Excepcion ?? "").Contains(datosLocales, StringComparison.Ordinal),
                        $"[{nombre}] La excepción registrada ({e.Nivel}, {e.Categoria}) expone la ruta expandida.");
                });
            }
            finally
            {
                BaseTemporal.BorrarDirectorio(raiz);
            }
        }
    }

    [Fact]
    public void Migracion_BaseDanada_RegistraErrorConClave()
    {
        var directorio = BaseTemporal.CrearDirectorioTemporal();
        try
        {
            // Un archivo que no es una base SQLite en el lugar de la base.
            File.WriteAllText(Path.Combine(directorio, FabricaConexion.NombreArchivo), new string('x', 4096));
            var registro = new RegistroCapturado();
            using var servicios = BaseTemporal.CrearServicios(directorio, registro);

            var error = Assert.Throws<SqliteException>(() => servicios.InicializarBaseDeDatos());

            // El error propio nombra el directorio visible, la clave y el código de SQLite, sin la excepción cruda
            // adjunta (su texto puede traer la ruta con el nombre de usuario).
            Assert.Contains(registro.Entradas, e =>
                e.Nivel == LogLevel.Critical
                && e.Mensaje.Contains(RutasPrivadas.Ocultar(directorio), StringComparison.Ordinal)
                && e.Mensaje.Contains(FabricaConexion.ClaveDirectorio, StringComparison.Ordinal)
                && e.Mensaje.Contains($"código de error de SQLite {error.SqliteErrorCode}", StringComparison.Ordinal));
            Assert.DoesNotContain(registro.Entradas, e => e.Nivel > LogLevel.Debug && e.Excepcion is not null);
        }
        finally
        {
            BaseTemporal.BorrarDirectorio(directorio);
        }
    }

    private static string RaizDelRepositorio()
    {
        var directorio = new DirectoryInfo(AppContext.BaseDirectory);
        while (directorio is not null && !File.Exists(Path.Combine(directorio.FullName, "Optica.sln")))
        {
            directorio = directorio.Parent;
        }

        return directorio?.FullName ?? throw new InvalidOperationException("No se encontró Optica.sln.");
    }

    private static bool SePuedeEscribir(string directorio)
    {
        try
        {
            var prueba = Path.Combine(directorio, "sonda");
            File.WriteAllText(prueba, "x");
            File.Delete(prueba);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static System.Data.Common.DbCommand Comando(
        System.Data.Common.DbConnection conexion, string sql, params (string Nombre, object Valor)[] parametros)
    {
        var comando = conexion.CreateCommand();
        comando.CommandText = sql;
        foreach (var (nombre, valor) in parametros)
        {
            var parametro = comando.CreateParameter();
            parametro.ParameterName = nombre;
            parametro.Value = valor;
            comando.Parameters.Add(parametro);
        }

        return comando;
    }

    private static string Escalar(
        System.Data.Common.DbConnection conexion, string sql, params (string Nombre, object Valor)[] parametros)
    {
        using var comando = Comando(conexion, sql, parametros);
        return Convert.ToString(comando.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) ?? "";
    }

    private static List<string> Lista(
        System.Data.Common.DbConnection conexion, string sql, params (string Nombre, object Valor)[] parametros)
    {
        using var comando = Comando(conexion, sql, parametros);
        using var lector = comando.ExecuteReader();
        var resultado = new List<string>();
        while (lector.Read())
        {
            resultado.Add(lector.GetString(0));
        }

        return resultado;
    }

    private static List<string> ColumnasNoNulas(System.Data.Common.DbConnection conexion, string tabla) =>
        Lista(conexion, "SELECT name FROM pragma_table_info(@tabla) WHERE \"notnull\" = 1", ("@tabla", tabla));

    private static string TipoColumna(System.Data.Common.DbConnection conexion, string tabla, string columna) =>
        Escalar(conexion, "SELECT type FROM pragma_table_info(@tabla) WHERE name = @columna",
            ("@tabla", tabla), ("@columna", columna));

    private sealed record EntradaRegistro(LogLevel Nivel, string Mensaje, string? Excepcion, string Categoria = "");

    /// <summary>Proveedor de registro en memoria para comprobar qué se informó al arrancar.</summary>
    private sealed class RegistroCapturado : ILoggerProvider
    {
        public ConcurrentQueue<EntradaRegistro> Entradas { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Registrador(Entradas, categoryName);

        public void Dispose()
        {
        }

        private sealed class Registrador(ConcurrentQueue<EntradaRegistro> entradas, string categoria) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                entradas.Enqueue(new EntradaRegistro(logLevel, formatter(state, exception), exception?.ToString(), categoria));
        }
    }
}
