using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Optica.Application.Comun;
using Optica.Infrastructure.Persistencia;

namespace Optica.Infrastructure.Tests.Persistencia;

public class DatosLocalesTests
{
    private const string RutaPrivada = "/home/usuario-privado/AppData/optica.db";

    [Fact]
    public void DescribirFalla_ErrorDeLaBaseEnLaCadena_DescribeSinElTextoOriginal()
    {
        var directorio = BaseTemporal.CrearDirectorioTemporal();
        try
        {
            var datos = SinServicios(new FabricaConexion(directorio));
            var error = new InvalidOperationException(
                "envoltorio", new SqliteException($"SQLite Error 14: 'unable to open database file' ({RutaPrivada}).", 14));

            var falla = datos.DescribirFalla(error);

            Assert.NotNull(falla);
            Assert.Equal(FabricaConexion.NombreArchivo, falla.Archivo);
            Assert.Equal(RutasPrivadas.Ocultar(directorio), falla.Directorio);
            Assert.Equal(nameof(SqliteException), falla.TipoError);
            Assert.Equal(14, falla.CodigoError);
            Assert.Equal(FabricaConexion.ClaveDirectorio, falla.Clave);
            Assert.DoesNotContain("usuario-privado", falla.ToString());
            Assert.Equal(directorio, datos.Directorio);
        }
        finally
        {
            BaseTemporal.BorrarDirectorio(directorio);
        }
    }

    [Fact]
    public void DescribirFalla_ErrorDeBaseQueNoEsDeSqlite_SinCodigo()
    {
        var datos = SinServicios(new FabricaConexion("/opt/optica-datos"));

        var falla = datos.DescribirFalla(new OtraFallaDeBase());

        Assert.NotNull(falla);
        Assert.Equal(nameof(OtraFallaDeBase), falla.TipoError);
        Assert.Null(falla.CodigoError);
    }

    [Fact]
    public void DescribirFalla_ErrorQueNoEsDeLaBase_Null()
    {
        var datos = SinServicios(new FabricaConexion("/opt/optica-datos"));

        Assert.Null(datos.DescribirFalla(new InvalidOperationException("otra cosa", new IOException("disco"))));
    }

    [Fact]
    public void AgregarInfraestructura_RegistraLosDatosLocales()
    {
        var directorio = BaseTemporal.CrearDirectorioTemporal();
        try
        {
            using var servicios = BaseTemporal.CrearServicios(directorio);

            var datos = servicios.GetRequiredService<IDatosLocales>();

            Assert.Equal(directorio, datos.Directorio);
        }
        finally
        {
            BaseTemporal.BorrarDirectorio(directorio);
        }
    }

    [Fact]
    public void DatosLocales_DirectorioClavesYRutaVisible_DeLaFabrica()
    {
        var fabrica = new FabricaConexion("/opt/optica-datos");
        var datos = SinServicios(fabrica);

        Assert.Equal(Path.Combine(fabrica.Directorio, "claves"), datos.DirectorioClaves);
        Assert.Equal(fabrica.RutaVisible, datos.RutaVisible);
    }

    [Fact]
    public void BaseExiste_SinBase_FalsoSinCrearNada()
    {
        var raiz = BaseTemporal.CrearDirectorioTemporal();
        try
        {
            var inexistente = Path.Combine(raiz, "no-existe");
            var vacio = Path.Combine(raiz, "vacio");
            Directory.CreateDirectory(vacio);
            using var servicios = BaseTemporal.CrearServicios(inexistente);
            using var serviciosVacio = BaseTemporal.CrearServicios(vacio);

            Assert.False(servicios.GetRequiredService<IDatosLocales>().BaseExiste);
            Assert.False(serviciosVacio.GetRequiredService<IDatosLocales>().BaseExiste);

            Assert.False(Directory.Exists(inexistente));
            Assert.Empty(Directory.EnumerateFileSystemEntries(vacio));
        }
        finally
        {
            BaseTemporal.BorrarDirectorio(raiz);
        }
    }

    [Fact]
    public void BaseExiste_BaseInicializada_Verdadero()
    {
        using var baseTemporal = new BaseTemporal();

        Assert.True(baseTemporal.Servicios.GetRequiredService<IDatosLocales>().BaseExiste);
    }

    [Fact]
    public void MigracionesPendientes_BaseMigrada_Falso()
    {
        using var baseTemporal = new BaseTemporal();

        Assert.False(baseTemporal.Servicios.GetRequiredService<IDatosLocales>().MigracionesPendientes());
    }

    [Fact]
    public void MigracionesPendientes_SuConexion_ConClavesForaneasSinCrearNiPool()
    {
        var cadena = new SqliteConnectionStringBuilder(SinServicios(new FabricaConexion("/opt/optica-datos")).CadenaSinCrear);

        Assert.Equal(true, cadena.ForeignKeys);
        Assert.Equal(SqliteOpenMode.ReadWrite, cadena.Mode);
        Assert.False(cadena.Pooling);
        Assert.Equal(Path.Combine("/opt/optica-datos", FabricaConexion.NombreArchivo), cadena.DataSource);
    }

    [Fact]
    public void MigracionesPendientes_SinHistorial_VerdaderoSinMigrar()
    {
        var directorio = BaseTemporal.CrearDirectorioTemporal();
        try
        {
            // Una base SQLite cualquiera, sin la tabla de historial de EF Core.
            var archivo = Path.Combine(directorio, FabricaConexion.NombreArchivo);
            using (var conexion = new SqliteConnection($"Data Source={archivo};Pooling=False"))
            {
                conexion.Open();
                using var comando = conexion.CreateCommand();
                comando.CommandText = "CREATE TABLE Otra (Id INTEGER PRIMARY KEY);";
                comando.ExecuteNonQuery();
            }

            using var servicios = BaseTemporal.CrearServicios(directorio);

            Assert.True(servicios.GetRequiredService<IDatosLocales>().MigracionesPendientes());
            Assert.Equal(["Otra"], Tablas(archivo));
        }
        finally
        {
            BaseTemporal.BorrarDirectorio(directorio);
        }
    }

    [Fact]
    public void MigracionesPendientes_SoloLaPrimeraAplicada_VerdaderoSinMigrar()
    {
        var directorio = BaseTemporal.CrearDirectorioTemporal();
        try
        {
            using var servicios = BaseTemporal.CrearServicios(directorio);
            using (var alcance = servicios.CreateScope())
            {
                var contexto = alcance.ServiceProvider.GetRequiredService<OpticaDbContext>();
                contexto.GetService<IMigrator>().Migrate(PrimeraMigracion);
            }

            Assert.True(servicios.GetRequiredService<IDatosLocales>().MigracionesPendientes());
            Assert.Equal([PrimeraMigracion], Aplicadas(Path.Combine(directorio, FabricaConexion.NombreArchivo)));
        }
        finally
        {
            BaseTemporal.BorrarDirectorio(directorio);
        }
    }

    [Fact]
    public void MigracionesPendientes_SinBase_FallaSinCrearla()
    {
        var directorio = BaseTemporal.CrearDirectorioTemporal();
        try
        {
            using var servicios = BaseTemporal.CrearServicios(directorio);

            // Mode=ReadWrite: no crea la base; SQLite informa que no la puede abrir.
            var error = Assert.Throws<SqliteException>(() => servicios.GetRequiredService<IDatosLocales>().MigracionesPendientes());

            Assert.Equal(14, error.SqliteErrorCode);
            Assert.Empty(Directory.EnumerateFileSystemEntries(directorio));
        }
        finally
        {
            BaseTemporal.BorrarDirectorio(directorio);
        }
    }

    [Fact]
    public void RenovarClaves_VaciaElDirectorioDeClaves()
    {
        var directorio = BaseTemporal.CrearDirectorioTemporal();
        try
        {
            var datos = SinServicios(new FabricaConexion(directorio));
            Directory.CreateDirectory(Path.Combine(datos.DirectorioClaves, "sub"));
            File.WriteAllText(Path.Combine(datos.DirectorioClaves, "key-1.xml"), "<key/>");
            File.WriteAllText(Path.Combine(datos.DirectorioClaves, "sub", "otra.xml"), "<key/>");
            var ajeno = Path.Combine(directorio, "ajeno.txt");
            File.WriteAllText(ajeno, "no es una clave");

            datos.RenovarClaves();

            Assert.True(Directory.Exists(datos.DirectorioClaves));
            Assert.Empty(Directory.EnumerateFileSystemEntries(datos.DirectorioClaves));
            // Solo las claves: lo demás del directorio de datos queda.
            Assert.True(File.Exists(ajeno));

            // Sin directorio de claves tampoco falla.
            Directory.Delete(datos.DirectorioClaves);
            datos.RenovarClaves();
        }
        finally
        {
            BaseTemporal.BorrarDirectorio(directorio);
        }
    }

    [Fact]
    public void CandadoInstancia_SegundaInstancia_RechazadaHastaQueLaPrimeraLoLibere()
    {
        var directorio = BaseTemporal.CrearDirectorioTemporal();
        try
        {
            var primero = new CandadoInstanciaArchivo(directorio);
            var segundo = new CandadoInstanciaArchivo(directorio);

            var tomado = primero.IntentarAdquirir();
            Assert.NotNull(tomado);
            // Lo que cuenta es el handle abierto, no la existencia del archivo.
            Assert.Null(segundo.IntentarAdquirir());
            Assert.Null(primero.IntentarAdquirir());

            tomado.Dispose();
            var archivo = Path.Combine(directorio, CandadoInstanciaArchivo.NombreArchivo);
            // El archivo queda (sin DeleteOnClose) y vacío: sin PID, usuario ni equipo.
            Assert.True(File.Exists(archivo));
            Assert.Equal(0, new FileInfo(archivo).Length);

            using var otraVez = segundo.IntentarAdquirir();
            Assert.NotNull(otraVez);
        }
        finally
        {
            BaseTemporal.BorrarDirectorio(directorio);
        }
    }

    [Fact]
    public void CandadoInstancia_AccesoDenegado_SeTrataComoTomado()
    {
        var directorio = BaseTemporal.CrearDirectorioTemporal();
        var archivo = Path.Combine(directorio, CandadoInstanciaArchivo.NombreArchivo);
        try
        {
            // Como el candado de otra cuenta: el archivo existe y esta cuenta no lo puede abrir para escribir.
            File.WriteAllBytes(archivo, []);
            if (OperatingSystem.IsWindows())
            {
                File.SetAttributes(archivo, FileAttributes.ReadOnly);
            }
            else
            {
                File.SetUnixFileMode(archivo, UnixFileMode.None);
            }

            Assert.Null(new CandadoInstanciaArchivo(directorio).IntentarAdquirir());
        }
        finally
        {
            if (OperatingSystem.IsWindows())
            {
                File.SetAttributes(archivo, FileAttributes.Normal);
            }
            else
            {
                File.SetUnixFileMode(archivo, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            BaseTemporal.BorrarDirectorio(directorio);
        }
    }

    [Fact]
    public void CandadoInstancia_DirectorioInexistente_DirectoryNotFound()
    {
        var raiz = BaseTemporal.CrearDirectorioTemporal();
        try
        {
            var inexistente = Path.Combine(raiz, "no-existe");

            Assert.Throws<DirectoryNotFoundException>(() => new CandadoInstanciaArchivo(inexistente).IntentarAdquirir());
            Assert.False(Directory.Exists(inexistente));
        }
        finally
        {
            BaseTemporal.BorrarDirectorio(raiz);
        }
    }

    [Fact]
    public void InicializarBaseDeDatos_TomaElCandadoHastaDescartarLosServicios()
    {
        var directorio = BaseTemporal.CrearDirectorioTemporal();
        try
        {
            using (var primera = BaseTemporal.CrearServicios(directorio))
            {
                primera.InicializarBaseDeDatos();
                // Inicializar dos veces los mismos servicios no choca con su propio candado.
                primera.InicializarBaseDeDatos();
                Assert.Null(primera.GetRequiredService<ICandadoInstancia>().IntentarAdquirir());

                using var segunda = BaseTemporal.CrearServicios(directorio);
                var error = Assert.Throws<InstanciaEnMarchaException>(() => segunda.InicializarBaseDeDatos());
                Assert.Null(error.InnerException);
                Assert.Contains(RutasPrivadas.Ocultar(directorio), error.Message, StringComparison.Ordinal);
                // Un acceso denegado también llega como "en marcha": el mensaje nombra los permisos del archivo.
                Assert.Contains(CandadoInstanciaArchivo.NombreArchivo, error.Message, StringComparison.Ordinal);
                Assert.Contains("permisos", error.Message, StringComparison.Ordinal);
            }

            using var despues = BaseTemporal.CrearServicios(directorio);
            using var candado = despues.GetRequiredService<ICandadoInstancia>().IntentarAdquirir();
            Assert.NotNull(candado);
        }
        finally
        {
            BaseTemporal.BorrarDirectorio(directorio);
        }
    }

    private const string PrimeraMigracion = "20261030220627_Inicial";

    /// <summary>Datos locales sobre la fábrica indicada, con servicios vacíos (para lo que no consulta migraciones).</summary>
    private static DatosLocales SinServicios(FabricaConexion fabrica) =>
        new(fabrica, new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>());

    private static List<string> Tablas(string archivo) =>
        Consultar(archivo, "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name");

    private static List<string> Aplicadas(string archivo) =>
        Consultar(archivo, "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId");

    private static List<string> Consultar(string archivo, string sql)
    {
        using var conexion = new SqliteConnection($"Data Source={archivo};Mode=ReadOnly;Pooling=False");
        conexion.Open();
        using var comando = conexion.CreateCommand();
        comando.CommandText = sql;
        using var lector = comando.ExecuteReader();
        var valores = new List<string>();
        while (lector.Read())
        {
            valores.Add(lector.GetString(0));
        }

        return valores;
    }

    /// <summary>Una falla de otra base, que no informa un código de SQLite.</summary>
    private sealed class OtraFallaDeBase : System.Data.Common.DbException;
}
