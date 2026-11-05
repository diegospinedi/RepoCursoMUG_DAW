using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Optica.Application.Comun;
using Optica.Infrastructure.Persistencia;

namespace Optica.Infrastructure.Tests.Persistencia;

public class FabricaConexionTests
{
    /// <summary>Eventos de error de EF Core que llevan la ruta de la base o la excepción de SQLite tal cual.</summary>
    private static readonly Dictionary<string, EventId> EventosDeError = new()
    {
        [nameof(RelationalEventId.ConnectionError)] = RelationalEventId.ConnectionError,
        [nameof(RelationalEventId.CommandError)] = RelationalEventId.CommandError,
        [nameof(RelationalEventId.TransactionError)] = RelationalEventId.TransactionError,
        [nameof(CoreEventId.SaveChangesFailed)] = CoreEventId.SaveChangesFailed,
        [nameof(CoreEventId.QueryIterationFailed)] = CoreEventId.QueryIterationFailed,
    };

    [Theory]
    [InlineData(nameof(RelationalEventId.ConnectionError))]
    [InlineData(nameof(RelationalEventId.CommandError))]
    [InlineData(nameof(RelationalEventId.TransactionError))]
    [InlineData(nameof(CoreEventId.SaveChangesFailed))]
    [InlineData(nameof(CoreEventId.QueryIterationFailed))]
    public void Configurar_EventoDeErrorDeEf_SeRegistraEnDebug(string evento)
    {
        var opciones = new DbContextOptionsBuilder<OpticaDbContext>();

        new FabricaConexion("/opt/optica-datos").Configurar(opciones);

        var avisos = opciones.Options.FindExtension<CoreOptionsExtension>()!.WarningsConfiguration;
        Assert.Equal(LogLevel.Debug, avisos.GetLevel(EventosDeError[evento]));
    }

    [Fact]
    public void RutaVisible_DirectorioConfiguradoEnLaCarpetaDelUsuario_SinElNombreDeUsuario()
    {
        var perfil = CarpetaDelUsuario();
        var configurado = Path.Combine(perfil, "OpticaDatos");

        var fabrica = new FabricaConexion(configurado);

        Assert.Equal(configurado, fabrica.Directorio);
        Assert.DoesNotContain(perfil, fabrica.RutaVisible, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(RutasPrivadas.Ocultar(configurado), fabrica.RutaVisible);
        Assert.EndsWith("OpticaDatos", fabrica.RutaVisible, StringComparison.Ordinal);
    }

    [Fact]
    public void RutaVisible_DirectorioConfiguradoFueraDeLaCarpetaDelUsuario_TalCual()
    {
        var configurado = Path.Combine(Path.GetPathRoot(Path.GetTempPath())!, "optica-datos-de-prueba");

        Assert.Equal(configurado, new FabricaConexion(configurado).RutaVisible);
    }

    [Fact]
    public void PrepararDirectorio_ConfiguradoEnLaCarpetaDelUsuarioQueNoSePuedeCrear_ErrorSinElNombreDeUsuario()
    {
        // Un nombre de más de 255 caracteres no se puede crear en ningún sistema: falla sin escribir nada.
        var perfil = CarpetaDelUsuario();
        var fabrica = new FabricaConexion(Path.Combine(perfil, new string('x', 300), "datos"));

        var error = Assert.Throws<DirectorioDatosInvalidoException>(fabrica.PrepararDirectorio);

        // Ni el mensaje ni una causa adjunta (cuyo texto trae la ruta expandida) llevan la carpeta del usuario.
        Assert.Null(error.InnerException);
        Assert.DoesNotContain(perfil, error.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(fabrica.RutaVisible, error.Ruta);
    }

    [Fact]
    public void ResolverDirectorio_ConfiguradoRelativoConLaCarpetaDelUsuario_ErrorSinElNombreDeUsuario()
    {
        // Una ruta relativa que contiene la carpeta del usuario (por ejemplo, un error de tipeo al instalar): no es
        // absoluta, así que se rechaza, y ni la ruta del error ni su mensaje deben llevar el nombre de usuario.
        var perfil = CarpetaDelUsuario();
        var configurado = "relativa" + perfil + Path.DirectorySeparatorChar + "datos";
        Assert.False(Path.IsPathFullyQualified(configurado));

        var error = Assert.Throws<DirectorioDatosInvalidoException>(() => new FabricaConexion(configurado));

        Assert.Null(error.InnerException);
        Assert.Equal(RutasPrivadas.Ocultar(configurado), error.Ruta);
        Assert.Contains(RutasPrivadas.Ocultar(configurado), error.Message, StringComparison.Ordinal);
        Assert.Contains(FabricaConexion.ClaveDirectorio, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(perfil, error.Ruta, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(perfil, error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolverDirectorio_CarpetaLocalNoCalificadaConLaCarpetaDelUsuario_ErrorSinElNombreDeUsuario()
    {
        // La carpeta de datos locales que informa el sistema no es absoluta (caso anómalo) y contiene la del usuario.
        var perfil = CarpetaDelUsuario();
        var datosLocales = "relativa" + perfil;
        Assert.False(Path.IsPathFullyQualified(datosLocales));

        var error = Assert.Throws<DirectorioDatosInvalidoException>(() => new FabricaConexion(null, datosLocales));

        Assert.Null(error.InnerException);
        Assert.Equal(RutasPrivadas.Ocultar(datosLocales), error.Ruta);
        Assert.Contains(FabricaConexion.ClaveDirectorio, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(perfil, error.Ruta, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(perfil, error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Plegar_RegistradaEnCadaConexion()
    {
        using var baseTemporal = new BaseTemporal();
        const string consulta =
            "SELECT plegar('ARMAZ\U000000D3N'), orden_articulo('\U000000D1and\U000000FA'), plegar(NULL) IS NULL, " +
            "orden_articulo(NULL) IS NULL";

        // Las conexiones que abre EF Core, sincrónica y asincrónicamente, tienen las dos funciones (ADR-011).
        var sincronico = baseTemporal.CrearContexto();
        sincronico.Database.OpenConnection();
        try
        {
            Assert.Equal(["armazon", "n{andu", "1", "1"], Leer(sincronico.Database.GetDbConnection(), consulta));
        }
        finally
        {
            sincronico.Database.CloseConnection();
        }

        var asincronico = baseTemporal.CrearContexto();
        await asincronico.Database.OpenConnectionAsync();
        try
        {
            var conexion = asincronico.Database.GetDbConnection();
            Assert.Equal(["armazon", "n{andu", "1", "1"], Leer(conexion, consulta));

            // Deterministas, como pide ADR-011.
            var deterministas = Leer(conexion,
                "SELECT count(*) FROM pragma_function_list WHERE name IN ('plegar', 'orden_articulo') AND (flags & 0x800) <> 0");
            Assert.Equal(["2"], deterministas);
        }
        finally
        {
            await asincronico.Database.CloseConnectionAsync();
        }
    }

    [Fact]
    public void Esquema_NingunObjetoUsaPlegar()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = new SqliteConnectionStringBuilder(baseTemporal.Servicios.GetRequiredService<FabricaConexion>().CadenaConexion)
        {
            // Sin grupo: una conexión del grupo podría traer las funciones que registró EF Core.
            Pooling = false,
        }.ToString();

        // La conexión de la aplicación sí las tiene: la prueba distingue las dos.
        var contexto = baseTemporal.CrearContexto();
        contexto.Database.OpenConnection();
        try
        {
            Assert.Equal(["a"], Leer(contexto.Database.GetDbConnection(), "SELECT plegar('A')"));
        }
        finally
        {
            contexto.Database.CloseConnection();
        }

        using var cruda = new SqliteConnection(cadena);
        cruda.Open();

        // La conexión cruda no conoce las funciones, como sqlite3 o un backup con VACUUM INTO.
        Assert.Throws<SqliteException>(() => Leer(cruda, "SELECT plegar('a')"));
        Assert.Throws<SqliteException>(() => Leer(cruda, "SELECT orden_articulo('a')"));

        // Y aun así lee el esquema completo y la base está íntegra: ningún índice, CHECK, trigger ni vista las usa (R-07).
        var objetos = new List<string>();
        using (var comando = cruda.CreateCommand())
        {
            comando.CommandText = "SELECT name, coalesce(sql, '') FROM sqlite_master";
            using var lector = comando.ExecuteReader();
            while (lector.Read())
            {
                objetos.Add(lector.GetString(0) + ": " + lector.GetString(1));
            }
        }

        // El esquema revisado es el de FEAT-002b, con las columnas nuevas.
        Assert.Contains(objetos, o => o.StartsWith("Articulo: ", StringComparison.Ordinal)
            && o.Contains("\"Activo\"", StringComparison.Ordinal) && o.Contains("\"Version\"", StringComparison.Ordinal));
        Assert.DoesNotContain(objetos, o => o.Contains("plegar", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(objetos, o => o.Contains("orden_articulo", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(["ok"], Leer(cruda, "PRAGMA integrity_check"));
    }

    private static List<string> Leer(System.Data.Common.DbConnection conexion, string consulta)
    {
        using var comando = conexion.CreateCommand();
        comando.CommandText = consulta;
        using var lector = comando.ExecuteReader();
        Assert.True(lector.Read());
        var valores = new List<string>();
        for (var i = 0; i < lector.FieldCount; i++)
        {
            valores.Add(Convert.ToString(lector.GetValue(i), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
        }

        return valores;
    }

    private static string CarpetaDelUsuario()
    {
        var perfil = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.False(string.IsNullOrEmpty(perfil), "La prueba necesita la carpeta del usuario.");
        return perfil;
    }
}
