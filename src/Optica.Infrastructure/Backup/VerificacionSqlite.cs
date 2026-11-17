using Microsoft.Data.Sqlite;

namespace Optica.Infrastructure.Backup;

/// <summary>
/// La verificación de una copia de la base, una sola para el backup (<see cref="CopiadorBackupSqlite"/>) y la restauración
/// (<see cref="RestauradorSqlite"/>): conexiones propias sin pool, <c>PRAGMA trusted_schema = OFF</c>, modo del diario y
/// <c>PRAGMA integrity_check</c> (ADR-029, D-09, D-23, D-28).
/// </summary>
/// <remarks>
/// <para>
/// Una copia completa está en modo de diario de reversión (<c>journal_mode=DELETE</c>): así la deja el backup, sin
/// <c>-wal</c> ni <c>-shm</c>. El modo se lee primero del encabezado del archivo, sin abrirlo con SQLite: una base en WAL
/// abierta en solo lectura dejaría un <c>-wal</c> y un <c>-shm</c> junto a la copia, que una conexión de solo lectura no
/// borra. Después se abre en solo lectura y se exige <c>journal_mode</c> <c>delete</c> y una única fila <c>ok</c> de
/// <c>integrity_check</c>.
/// </para>
/// <para>
/// <c>trusted_schema = OFF</c>: una copia es un archivo que vino de afuera de la base en uso (un pendrive, la nube), y su
/// esquema (vistas, triggers, índices de expresiones) no tiene que poder llamar funciones con efectos al leerla.
/// </para>
/// </remarks>
internal static class VerificacionSqlite
{
    /// <summary>El modo de diario de una copia del backup.</summary>
    public const string ModoDiarioReversion = "delete";

    /// <summary>El modo de diario de toda base en uso por la Api.</summary>
    public const string ModoWal = "wal";

    private const string IntegridadCorrecta = "ok";

    /// <summary>Los 16 bytes con que empieza todo archivo de SQLite 3.</summary>
    private static readonly byte[] Firma = "SQLite format 3\0"u8.ToArray();

    /// <summary>Una conexión propia: sin pool (cerrarla cierra el archivo) y sin pasar por <c>FabricaConexion.Configurar</c>.</summary>
    public static string CadenaPropia(string ruta, SqliteOpenMode modo) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = ruta,
            Mode = modo,
            Pooling = false,
        }.ToString();

    /// <summary>Abre una conexión propia a una copia o un temporal, con <c>trusted_schema = OFF</c>.</summary>
    /// <exception cref="SqliteException">No se pudo abrir.</exception>
    public static SqliteConnection AbrirPropia(string ruta, SqliteOpenMode modo)
    {
        var conexion = new SqliteConnection(CadenaPropia(ruta, modo));
        try
        {
            conexion.Open();
            using var comando = conexion.CreateCommand();
            comando.CommandText = "PRAGMA trusted_schema = OFF;";
            comando.ExecuteNonQuery();
            return conexion;
        }
        catch
        {
            conexion.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Verdadero si la copia de <paramref name="ruta"/> existe, es una base de SQLite en modo de diario de reversión y pasa
    /// la comprobación de integridad completa. Falso si no existe, no es una base, está en WAL o está dañada: no lanza por
    /// eso. No modifica el archivo ni deja auxiliares.
    /// </summary>
    public static bool EsCopiaCompleta(string ruta)
    {
        if (!File.Exists(ruta) || !EncabezadoEnDiarioDeReversion(ruta))
        {
            return false;
        }

        try
        {
            using var copia = AbrirPropia(ruta, SqliteOpenMode.ReadOnly);
            try
            {
                return string.Equals(ModoDiario(copia), ModoDiarioReversion, StringComparison.OrdinalIgnoreCase)
                    && Integra(copia);
            }
            finally
            {
                copia.Close();
            }
        }
        catch (SqliteException)
        {
            // Intencional: no es una base o está dañada ("file is not a database", "malformed"). No cuenta (FR-07).
            return false;
        }
    }

    /// <summary>El <c>journal_mode</c> de la conexión (<c>delete</c>, <c>wal</c>…).</summary>
    public static string? ModoDiario(SqliteConnection conexion)
    {
        using var comando = conexion.CreateCommand();
        comando.CommandText = "PRAGMA journal_mode;";
        return comando.ExecuteScalar() as string;
    }

    /// <summary>Cambia el <c>journal_mode</c> y devuelve verdadero si quedó el pedido.</summary>
    public static bool CambiarModoDiario(SqliteConnection conexion, string modo)
    {
        using var comando = conexion.CreateCommand();
        comando.CommandText = modo == ModoWal ? "PRAGMA journal_mode=WAL;" : "PRAGMA journal_mode=DELETE;";
        return string.Equals(comando.ExecuteScalar() as string, modo, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// <c>PRAGMA integrity_check</c> devuelve una sola fila "ok", o una fila por problema encontrado (lee hasta dos).
    /// </summary>
    public static bool Integra(SqliteConnection conexion)
    {
        using var comando = conexion.CreateCommand();
        comando.CommandText = "PRAGMA integrity_check;";
        using var lector = comando.ExecuteReader();
        var filas = new List<string>();
        while (lector.Read() && filas.Count < 2)
        {
            filas.Add(lector.IsDBNull(0) ? string.Empty : lector.GetString(0));
        }

        return filas is [IntegridadCorrecta];
    }

    /// <summary>
    /// Lee el encabezado (100 bytes) sin abrir la base: la firma de SQLite 3 y los bytes 18 y 19 (versión de escritura y
    /// de lectura) en 1, el modo de diario de reversión; 2 es WAL.
    /// </summary>
    private static bool EncabezadoEnDiarioDeReversion(string ruta)
    {
        var encabezado = new byte[100];
        using var archivo = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (archivo.ReadAtLeast(encabezado, encabezado.Length, throwOnEndOfStream: false) < encabezado.Length)
        {
            return false;
        }

        return encabezado.AsSpan(0, Firma.Length).SequenceEqual(Firma) && encabezado[18] == 1 && encabezado[19] == 1;
    }
}
