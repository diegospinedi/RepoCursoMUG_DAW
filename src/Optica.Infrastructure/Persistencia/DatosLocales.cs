using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Optica.Application.Comun;

namespace Optica.Infrastructure.Persistencia;

/// <summary>
/// Implementa <see cref="IDatosLocales"/> sobre la <see cref="FabricaConexion"/>: el directorio de datos, las claves de
/// sesión, la existencia de la base y sus migraciones pendientes (sin crearla ni migrarla), y la descripción de las
/// fallas de la base con <see cref="FabricaConexion.RutaVisible"/>, nunca con el texto original de la excepción (puede
/// traer la ruta con el nombre de usuario de la PC).
/// </summary>
/// <param name="fabrica">Dónde está la base.</param>
/// <param name="alcances">
/// Para obtener la lista de migraciones del ensamblado desde un <see cref="OpticaDbContext"/> de un alcance propio, sin
/// abrir su conexión (que es <c>ReadWriteCreate</c> y crearía la base).
/// </param>
public sealed class DatosLocales(FabricaConexion fabrica, IServiceScopeFactory alcances) : IDatosLocales
{
    /// <summary>Carpeta de las claves de Data Protection dentro del directorio de datos.</summary>
    public const string CarpetaClaves = "claves";

    private const string TablaHistorial = "__EFMigrationsHistory";

    public string Directorio => fabrica.Directorio;

    public string DirectorioClaves => Path.Combine(fabrica.Directorio, CarpetaClaves);

    public string RutaVisible => fabrica.RutaVisible;

    public bool BaseExiste =>
        Directory.Exists(fabrica.Directorio) && File.Exists(Path.Combine(fabrica.Directorio, FabricaConexion.NombreArchivo));

    /// <summary>La cadena de la conexión propia de <see cref="MigracionesPendientes"/>.</summary>
    internal string CadenaSinCrear =>
        new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(fabrica.Directorio, FabricaConexion.NombreArchivo),
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
            ForeignKeys = true,
        }.ToString();

    /// <summary>
    /// Abre su propia conexión <c>Mode=ReadWrite</c> (si la base no existe, SQLite falla en lugar de crearla) y sin
    /// pool (no deja el archivo abierto), lee el historial de EF Core y lo compara con las migraciones del ensamblado.
    /// Sin la tabla de historial, todas están pendientes.
    /// </summary>
    public bool MigracionesPendientes()
    {
        List<string> delEnsamblado;
        using (var alcance = alcances.CreateScope())
        {
            // GetMigrations lee las migraciones del ensamblado; no abre la conexión.
            delEnsamblado = alcance.ServiceProvider.GetRequiredService<OpticaDbContext>().Database.GetMigrations().ToList();
        }

        using var conexion = new SqliteConnection(CadenaSinCrear);
        conexion.Open();

        using (var existe = conexion.CreateCommand())
        {
            existe.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $tabla";
            existe.Parameters.AddWithValue("$tabla", TablaHistorial);
            if (Convert.ToInt64(existe.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) == 0)
            {
                return delEnsamblado.Count > 0;
            }
        }

        var aplicadas = new HashSet<string>(StringComparer.Ordinal);
        using (var historial = conexion.CreateCommand())
        {
            historial.CommandText = $"SELECT MigrationId FROM \"{TablaHistorial}\"";
            using var lector = historial.ExecuteReader();
            while (lector.Read())
            {
                aplicadas.Add(lector.GetString(0));
            }
        }

        return delEnsamblado.Any(migracion => !aplicadas.Contains(migracion));
    }

    /// <summary>
    /// Borra todo lo que hay dentro de <see cref="DirectorioClaves"/> (la carpeta queda, vacía). Las excepciones de
    /// archivos se propagan tal cual: quien llama informa solo su tipo, porque su texto trae la ruta expandida.
    /// </summary>
    public void RenovarClaves()
    {
        var claves = new DirectoryInfo(DirectorioClaves);
        if (!claves.Exists)
        {
            return;
        }

        foreach (var entrada in claves.EnumerateFileSystemInfos())
        {
            if (entrada is DirectoryInfo carpeta)
            {
                carpeta.Delete(recursive: true);
            }
            else
            {
                entrada.Delete();
            }
        }
    }

    public FallaDatos? DescribirFalla(Exception error)
    {
        for (Exception? actual = error; actual is not null; actual = actual.InnerException)
        {
            if (actual is DbException)
            {
                return new FallaDatos(
                    FabricaConexion.NombreArchivo,
                    fabrica.RutaVisible,
                    actual.GetType().Name,
                    // El código numérico de SQLite no lleva datos personales; el texto de la excepción, sí puede.
                    actual is SqliteException sqlite ? sqlite.SqliteErrorCode : null,
                    FabricaConexion.ClaveDirectorio);
            }
        }

        return null;
    }
}
