using Microsoft.Data.Sqlite;
using Optica.Application.Backup;
using Optica.Infrastructure.Persistencia;

namespace Optica.Infrastructure.Backup;

/// <summary>
/// La copia de la base con la API de backup de SQLite (ADR-029, D-09, D-23): nunca una copia de archivo común
/// (AGENTS.md), porque los cambios recientes pueden estar todavía en <c>optica.db-wal</c>.
/// </summary>
/// <remarks>
/// <para>
/// Todas las conexiones son propias: sin pool (<c>Pooling=False</c>), cerradas explícitamente al terminar, y sin pasar
/// por <see cref="FabricaConexion.Configurar"/> (sus PRAGMA pondrían la copia en WAL). El origen se abre en
/// <see cref="SqliteOpenMode.ReadWrite"/>: si la base no existe, falla en lugar de crear una vacía. La copia se hace en un
/// solo paso (<see cref="SqliteConnection.BackupDatabase(SqliteConnection)"/>): una foto consistente de lo confirmado al
/// empezar, que en modo WAL no frena a quien graba (FR-05). La copia hereda el WAL del encabezado y se pasa a
/// <c>journal_mode=DELETE</c> para que no deje <c>-wal</c> ni <c>-shm</c> en la carpeta del backup.
/// </para>
/// <para>
/// La verificación abre la copia en solo lectura y exige <c>journal_mode</c> <c>delete</c> y
/// <c>PRAGMA integrity_check</c> igual a <c>ok</c> (FR-07). Lo que libera el archivo para el renombre y el borrado (en
/// Windows, un archivo abierto no se puede renombrar) es el cierre explícito de cada conexión: sin pool, cerrar cierra el
/// archivo de verdad, y no queda una conexión agrupada que lo retenga.
/// </para>
/// <para>
/// <see cref="SqliteConnection.BackupDatabase(SqliteConnection)"/> no reintenta ante <c>SQLITE_BUSY</c> (por ejemplo, si
/// otra conexión tiene un bloqueo exclusivo del origen en ese momento): la <see cref="SqliteException"/> se propaga, el
/// backup del día falla con <see cref="FallaBackup.Copia"/> sin anotarse, y el programador (spec FEAT-010, Block 5) lo
/// vuelve a intentar en su próxima vuelta.
/// </para>
/// <para>
/// No registra nada en el log. Las excepciones de SQLite o del sistema de archivos se propagan tal cual: su texto puede
/// traer la ruta, y quien las atrapa (<see cref="HacerBackup"/>) registra solo el tipo.
/// </para>
/// </remarks>
public sealed class CopiadorBackupSqlite(FabricaConexion fabrica) : ICopiadorBackup
{
    private const string ModoDiarioReversion = "delete";
    private const string IntegridadCorrecta = "ok";

    private readonly FabricaConexion _fabrica = fabrica ?? throw new ArgumentNullException(nameof(fabrica));

    public Task CopiarAsync(string rutaTemporal, CancellationToken cancelacion = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(rutaTemporal);
        cancelacion.ThrowIfCancellationRequested();
        // La API de backup es sincrónica: corre fuera del hilo de quien la pide. Un paso: no se interrumpe a la mitad.
        return Task.Run(() => Copiar(rutaTemporal), cancelacion);
    }

    public Task<bool> VerificarAsync(string rutaTemporal, CancellationToken cancelacion = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(rutaTemporal);
        cancelacion.ThrowIfCancellationRequested();
        return Task.Run(() => Verificar(rutaTemporal), cancelacion);
    }

    private void Copiar(string rutaTemporal)
    {
        var cadenaOrigen = new SqliteConnectionStringBuilder(_fabrica.CadenaConexion)
        {
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ToString();
        var cadenaDestino = CadenaPropia(rutaTemporal, SqliteOpenMode.ReadWriteCreate);

        using var origen = new SqliteConnection(cadenaOrigen);
        using var destino = new SqliteConnection(cadenaDestino);
        try
        {
            origen.Open();
            destino.Open();
            origen.BackupDatabase(destino);

            using var comando = destino.CreateCommand();
            comando.CommandText = "PRAGMA journal_mode=DELETE;";
            if (!string.Equals(comando.ExecuteScalar() as string, ModoDiarioReversion, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("La copia del backup no quedó en modo de diario de reversión.");
            }
        }
        finally
        {
            Cerrar(destino);
            Cerrar(origen);
        }
    }

    private static bool Verificar(string rutaTemporal)
    {
        if (!File.Exists(rutaTemporal))
        {
            return false;
        }

        using var copia = new SqliteConnection(CadenaPropia(rutaTemporal, SqliteOpenMode.ReadOnly));
        try
        {
            copia.Open();
            using var modo = copia.CreateCommand();
            modo.CommandText = "PRAGMA journal_mode;";
            if (!string.Equals(modo.ExecuteScalar() as string, ModoDiarioReversion, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // integrity_check devuelve una sola fila "ok", o una fila por problema encontrado.
            using var integridad = copia.CreateCommand();
            integridad.CommandText = "PRAGMA integrity_check;";
            using var lector = integridad.ExecuteReader();
            var filas = new List<string>();
            while (lector.Read() && filas.Count < 2)
            {
                filas.Add(lector.IsDBNull(0) ? string.Empty : lector.GetString(0));
            }

            return filas is [IntegridadCorrecta];
        }
        catch (SqliteException)
        {
            // Intencional: no es una base o está dañada ("file is not a database", "malformed"). No cuenta (FR-07).
            return false;
        }
        finally
        {
            Cerrar(copia);
        }
    }

    private static string CadenaPropia(string ruta, SqliteOpenMode modo) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = ruta,
            Mode = modo,
            Pooling = false,
        }.ToString();

    // Sin pool, Close cierra el archivo: es lo que lo libera antes del renombre (y del borrado de un temporal fallido).
    private static void Cerrar(SqliteConnection conexion) => conexion.Close();
}
