using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Optica.Application.Backup;
using Optica.Infrastructure.Persistencia;

namespace Optica.Infrastructure.Backup;

/// <summary>
/// La restauración de la base desde una copia con la API de backup de SQLite (FR-13 de FEAT-010, D-23, D-28, D-29,
/// ADR-029): nunca una copia de archivo común de la base en uso (AGENTS.md). Solo vale con la Api detenida: quien la usa
/// (el comando <c>restaurar-backup</c>) tiene tomado el candado de instancia.
/// </summary>
/// <remarks>
/// <para>
/// Todas las conexiones son propias: sin pool, cerradas explícitamente y sin pasar por
/// <see cref="FabricaConexion.Configurar"/> (sus PRAGMA pondrían el temporal en WAL). La copia se abre siempre en
/// <see cref="SqliteOpenMode.ReadOnly"/>: no se crea si no existe, no se modifica y no se le dejan auxiliares.
/// </para>
/// <para>
/// La restauración copia la copia elegida a <see cref="NombreTemporal"/> en la carpeta de datos con
/// <see cref="SqliteConnection.BackupDatabase(SqliteConnection)"/>, lo pasa a <c>journal_mode=DELETE</c>, lo verifica en
/// solo lectura con <c>PRAGMA integrity_check</c> y lo deja en modo WAL, como toda base de la Api (sin <c>-wal</c> ni
/// <c>-shm</c> al cerrarlo; ver <see cref="PonerEnWal"/>). Recién entonces el intercambio mueve los
/// auxiliares de la base actual (<c>-wal</c>, <c>-shm</c> y <c>-journal</c>, los que haya) y después <c>optica.db</c> a una
/// carpeta nueva <see cref="CarpetaAnterior"/>/<c>AAAAMMDD-HHMMSS</c> (hora local del <see cref="TimeProvider"/>), y
/// renombra el temporal a <c>optica.db</c>. Los auxiliares van primero y juntos con la base: uno viejo junto a la base
/// restaurada sería aplicado por SQLite sobre ella, y la base, sin el suyo, perdería lo que todavía estaba en el
/// <c>-wal</c>. Si cualquier paso del intercambio falla, devuelve a la carpeta de datos, en orden inverso, todo lo que ya
/// había movido, borra la carpeta anterior vacía y relanza: la carpeta de datos nunca queda sin <c>optica.db</c>.
/// </para>
/// <para>
/// No migra: la Api migra al arrancar (D-28). No registra nada en el log; las excepciones de SQLite o del sistema de
/// archivos se propagan tal cual (su texto puede traer la ruta) y quien las atrapa informa solo su tipo.
/// </para>
/// </remarks>
public sealed class RestauradorSqlite : IRestauradorBase
{
    /// <summary>El temporal de la restauración, en la carpeta de datos.</summary>
    public const string NombreTemporal = FabricaConexion.NombreArchivo + ".restaurando";

    /// <summary>La carpeta, dentro de la carpeta de datos, donde se apartan las bases reemplazadas.</summary>
    public const string CarpetaAnterior = "anterior";

    /// <summary>Lo que se antepone al nombre de un archivo en el aviso de la vuelta atrás (solo para pruebas).</summary>
    internal const string PrefijoVuelta = "volver:";

    private const string TablaHistorial = "__EFMigrationsHistory";
    private const string TablaFactura = "Factura";
    private const string FormatoCarpeta = "yyyyMMdd-HHmmss";

    /// <summary>Los auxiliares de una base SQLite, en el orden en que se apartan, antes que la base.</summary>
    private static readonly string[] SufijosAuxiliares = ["-wal", "-shm", "-journal"];

    private readonly FabricaConexion _fabrica;
    private readonly IServiceScopeFactory _alcances;
    private readonly TimeProvider _reloj;
    private readonly Action<string>? _antesDeMover;

    /// <param name="fabrica">Dónde está la carpeta de datos.</param>
    /// <param name="alcances">
    /// Para obtener las migraciones del ensamblado desde un <see cref="OpticaDbContext"/> de un alcance propio, sin abrir
    /// su conexión (que crearía la base).
    /// </param>
    /// <param name="reloj">La hora local que nombra la carpeta anterior.</param>
    public RestauradorSqlite(FabricaConexion fabrica, IServiceScopeFactory alcances, TimeProvider reloj)
        : this(fabrica, alcances, reloj, antesDeMover: null)
    {
    }

    /// <param name="antesDeMover">
    /// Para pruebas: se llama con el nombre de cada archivo antes de moverlo (con <see cref="PrefijoVuelta"/> en la vuelta
    /// atrás), y puede lanzar para simular una falla en ese paso.
    /// </param>
    internal RestauradorSqlite(
        FabricaConexion fabrica, IServiceScopeFactory alcances, TimeProvider reloj, Action<string>? antesDeMover)
    {
        _fabrica = fabrica ?? throw new ArgumentNullException(nameof(fabrica));
        _alcances = alcances ?? throw new ArgumentNullException(nameof(alcances));
        _reloj = reloj ?? throw new ArgumentNullException(nameof(reloj));
        _antesDeMover = antesDeMover;
    }

    private string RutaBase => Path.Combine(_fabrica.Directorio, FabricaConexion.NombreArchivo);

    private string RutaTemporal => Path.Combine(_fabrica.Directorio, NombreTemporal);

    public IReadOnlyList<string> MigracionesConocidas()
    {
        using var alcance = _alcances.CreateScope();
        // GetMigrations lee las migraciones del ensamblado, en orden; no abre la conexión.
        return alcance.ServiceProvider.GetRequiredService<OpticaDbContext>().Database.GetMigrations().ToList();
    }

    public Task<InspeccionCopia> InspeccionarCopiaAsync(string rutaCopia, CancellationToken cancelacion = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(rutaCopia);
        cancelacion.ThrowIfCancellationRequested();
        return Task.Run(() => Inspeccionar(rutaCopia), cancelacion);
    }

    public Task<int?> ContarFacturasPosterioresAsync(long ultimaFacturaDeLaCopia, CancellationToken cancelacion = default)
    {
        cancelacion.ThrowIfCancellationRequested();
        return Task.Run(() => Contar(ultimaFacturaDeLaCopia), cancelacion);
    }

    public Task<InspeccionCopia> RestaurarATemporalAsync(string rutaCopia, CancellationToken cancelacion = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(rutaCopia);
        cancelacion.ThrowIfCancellationRequested();
        // La API de backup es sincrónica: corre fuera del hilo de quien la pide, en un solo paso.
        return Task.Run(() => RestaurarATemporal(rutaCopia), cancelacion);
    }

    public string ApartarYPonerRestaurada()
    {
        var (relativa, carpeta) = CrearCarpetaAnterior();
        var movidos = new List<(string Desde, string Hasta, string Nombre)>();
        try
        {
            foreach (var nombre in SufijosAuxiliares.Select(s => FabricaConexion.NombreArchivo + s)
                .Append(FabricaConexion.NombreArchivo))
            {
                var desde = Path.Combine(_fabrica.Directorio, nombre);
                if (!File.Exists(desde))
                {
                    continue;
                }

                var hasta = Path.Combine(carpeta, nombre);
                Mover(nombre, desde, hasta);
                movidos.Add((desde, hasta, nombre));
            }

            Mover(NombreTemporal, RutaTemporal, RutaBase);
            return relativa;
        }
        catch (Exception error)
        {
            VolverAtras(movidos, carpeta, relativa, error);
            throw;
        }
    }

    public void DescartarTemporal()
    {
        foreach (var archivo in ArchivosDelTemporal())
        {
            if (File.Exists(archivo))
            {
                File.Delete(archivo);
            }
        }
    }

    /// <summary>
    /// La inspección de una copia o del temporal, en solo lectura: primero <see cref="VerificacionSqlite.EsCopiaCompleta"/>
    /// (modo de diario de reversión e integridad, la misma verificación que el backup); después el historial de
    /// migraciones, los triggers de protección de ese historial (<see cref="TriggersDeProteccion"/>) y la última factura.
    /// </summary>
    private static InspeccionCopia Inspeccionar(string ruta)
    {
        if (!File.Exists(ruta))
        {
            return new InspeccionCopia(false, false, null, 0);
        }

        if (!VerificacionSqlite.EsCopiaCompleta(ruta))
        {
            return new InspeccionCopia(true, false, null, 0);
        }

        try
        {
            using var copia = VerificacionSqlite.AbrirPropia(ruta, SqliteOpenMode.ReadOnly);
            try
            {
                var migraciones = ExisteTabla(copia, TablaHistorial) ? LeerMigraciones(copia) : null;
                var triggers = migraciones is not null && TriggersCompletos(copia, migraciones);
                var ultima = ExisteTabla(copia, TablaFactura)
                    ? Convert.ToInt64(Escalar(copia, $"SELECT COALESCE(MAX(\"Id\"), 0) FROM \"{TablaFactura}\""), CultureInfo.InvariantCulture)
                    : 0L;
                return new InspeccionCopia(true, true, migraciones, ultima, triggers);
            }
            finally
            {
                copia.Close();
            }
        }
        catch (SqliteException)
        {
            // Intencional: pasó la integridad pero su esquema no se puede leer como el de la óptica. No se restaura.
            return new InspeccionCopia(true, false, null, 0);
        }
    }

    /// <summary>Verdadero si la base tiene todos los triggers de protección que crean sus migraciones.</summary>
    private static bool TriggersCompletos(SqliteConnection conexion, IReadOnlyList<string> migraciones)
    {
        var presentes = new HashSet<string>(StringComparer.Ordinal);
        using (var comando = conexion.CreateCommand())
        {
            comando.CommandText = "SELECT name FROM sqlite_master WHERE type = 'trigger'";
            using var lector = comando.ExecuteReader();
            while (lector.Read())
            {
                presentes.Add(lector.GetString(0));
            }
        }

        return TriggersDeProteccion.Esperados(migraciones).All(presentes.Contains);
    }

    /// <summary>
    /// Abre la base actual en solo lectura y cuenta las facturas posteriores; null si no se pudo leer. Así lee también lo
    /// que está en el <c>-wal</c> de una Api cortada, sin pasarlo al archivo de la base. SQLite reescribe el índice del
    /// <c>-shm</c> al leer en WAL y, si faltaban, crea un <c>-wal</c> vacío y un <c>-shm</c> que una conexión de solo
    /// lectura no borra al cerrar: esos dos, los que creó esta lectura, se borran (la cancelación del comando dice que no se
    /// cambió nada). Nunca se borra un <c>-wal</c> con contenido.
    /// </summary>
    private int? Contar(long ultimaFacturaDeLaCopia)
    {
        if (!File.Exists(RutaBase))
        {
            return null;
        }

        var wal = RutaBase + "-wal";
        var shm = RutaBase + "-shm";
        var habiaAuxiliares = File.Exists(wal) || File.Exists(shm);
        try
        {
            return ContarEn(ultimaFacturaDeLaCopia);
        }
        finally
        {
            if (!habiaAuxiliares && File.Exists(wal) && new FileInfo(wal).Length == 0)
            {
                BorrarSinFallar(wal);
                BorrarSinFallar(shm);
            }
        }
    }

    private int? ContarEn(long ultimaFacturaDeLaCopia)
    {
        using var actual = new SqliteConnection(VerificacionSqlite.CadenaPropia(RutaBase, SqliteOpenMode.ReadOnly));
        try
        {
            actual.Open();
            if (!ExisteTabla(actual, TablaFactura))
            {
                return 0;
            }

            using var comando = actual.CreateCommand();
            comando.CommandText = $"SELECT COUNT(*) FROM \"{TablaFactura}\" WHERE \"Id\" > $ultima";
            comando.Parameters.AddWithValue("$ultima", ultimaFacturaDeLaCopia);
            return Convert.ToInt32(comando.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
        catch (SqliteException)
        {
            // Intencional: la base actual puede estar dañada (es una razón para restaurar); el comando lo informa.
            return null;
        }
        finally
        {
            actual.Close();
        }
    }

    /// <summary>
    /// Copia la copia al temporal, en modo de diario de reversión, y lo inspecciona con la misma verificación que la copia.
    /// Si es una copia completa, lo pasa a WAL (<see cref="PonerEnWal"/>); si eso falla, la inspección vuelve como no
    /// íntegra. Quien llama le aplica la regla de D-28 y los triggers.
    /// </summary>
    private InspeccionCopia RestaurarATemporal(string rutaCopia)
    {
        // Un temporal que quedó de una restauración anterior interrumpida no es la base: se pisa.
        DescartarTemporal();
        using (var origen = VerificacionSqlite.AbrirPropia(rutaCopia, SqliteOpenMode.ReadOnly))
        using (var destino = VerificacionSqlite.AbrirPropia(RutaTemporal, SqliteOpenMode.ReadWriteCreate))
        {
            try
            {
                origen.BackupDatabase(destino);
                if (!VerificacionSqlite.CambiarModoDiario(destino, VerificacionSqlite.ModoDiarioReversion))
                {
                    return new InspeccionCopia(true, false, null, 0);
                }
            }
            finally
            {
                destino.Close();
                origen.Close();
            }
        }

        var inspeccion = Inspeccionar(RutaTemporal);
        return !inspeccion.Integra || PonerEnWal() ? inspeccion : inspeccion with { Integra = false };
    }

    /// <summary>
    /// Deja el temporal verificado en modo WAL, como toda base de la Api, y vuelve a comprobar su integridad en la misma
    /// conexión. Hace falta: al arrancar, EF Core comprueba que la base exista con una conexión de solo lectura, y los
    /// PRAGMA de <see cref="FabricaConexion"/> piden WAL también en esa conexión; sobre una base en modo de diario de
    /// reversión (como la copia) ese cambio no se puede hacer en solo lectura y la Api no arranca. No escribe datos: solo
    /// cambia el modo del diario en el encabezado. Al cerrar la única conexión, SQLite borra el <c>-wal</c> y el <c>-shm</c>
    /// del temporal.
    /// </summary>
    private bool PonerEnWal()
    {
        using (var temporal = VerificacionSqlite.AbrirPropia(RutaTemporal, SqliteOpenMode.ReadWrite))
        {
            try
            {
                if (!VerificacionSqlite.CambiarModoDiario(temporal, VerificacionSqlite.ModoWal) || !VerificacionSqlite.Integra(temporal))
                {
                    return false;
                }
            }
            finally
            {
                temporal.Close();
            }
        }

        // Ningún auxiliar del temporal puede quedar: no acompaña al renombre y quedaría huérfano en la carpeta de datos.
        return SufijosAuxiliares.All(sufijo => !File.Exists(RutaTemporal + sufijo));
    }

    /// <summary>
    /// Crea <c>anterior/AAAAMMDD-HHMMSS</c> con la hora local; si ya existe (dos restauraciones en el mismo segundo), le
    /// agrega <c>-2</c>, <c>-3</c>…: nunca pisa una base apartada antes.
    /// </summary>
    private (string Relativa, string Absoluta) CrearCarpetaAnterior()
    {
        var momento = _reloj.GetLocalNow().ToString(FormatoCarpeta, CultureInfo.InvariantCulture);
        for (var intento = 1; ; intento++)
        {
            var nombre = intento == 1 ? momento : string.Create(CultureInfo.InvariantCulture, $"{momento}-{intento}");
            var relativa = Path.Combine(CarpetaAnterior, nombre);
            var absoluta = Path.Combine(_fabrica.Directorio, relativa);
            if (!Directory.Exists(absoluta) && !File.Exists(absoluta))
            {
                Directory.CreateDirectory(absoluta);
                return (relativa, absoluta);
            }
        }
    }

    /// <summary>
    /// Devuelve, en orden inverso, lo que ya se había apartado. Si también falla, lanza
    /// <see cref="VueltaAtrasFallidaException"/> con la carpeta anterior relativa (lo que queda allí es la base actual).
    /// </summary>
    private void VolverAtras(
        List<(string Desde, string Hasta, string Nombre)> movidos, string carpeta, string relativa, Exception causa)
    {
        try
        {
            for (var i = movidos.Count - 1; i >= 0; i--)
            {
                var (desde, hasta, nombre) = movidos[i];
                Mover(PrefijoVuelta + nombre, hasta, desde);
            }

            BorrarCarpetasVacias(carpeta);
        }
        catch (Exception error) when (error is not VueltaAtrasFallidaException)
        {
            throw new VueltaAtrasFallidaException(relativa, causa);
        }
    }

    /// <summary>Borra la carpeta anterior vacía y, si también queda vacía, la carpeta <see cref="CarpetaAnterior"/>.</summary>
    private void BorrarCarpetasVacias(string carpeta)
    {
        if (Directory.Exists(carpeta) && !Directory.EnumerateFileSystemEntries(carpeta).Any())
        {
            Directory.Delete(carpeta);
        }

        var raiz = Path.Combine(_fabrica.Directorio, CarpetaAnterior);
        if (Directory.Exists(raiz) && !Directory.EnumerateFileSystemEntries(raiz).Any())
        {
            Directory.Delete(raiz);
        }
    }

    private void Mover(string paso, string desde, string hasta)
    {
        _antesDeMover?.Invoke(paso);
        // Sin reemplazo: el destino nunca existe (carpeta nueva, o la base ya apartada), y si existiera es un error.
        File.Move(desde, hasta, overwrite: false);
    }

    /// <summary>Borra un auxiliar vacío que dejó la lectura; si no se puede, queda (SQLite lo reutiliza o lo borra después).</summary>
    private static void BorrarSinFallar(string archivo)
    {
        try
        {
            File.Delete(archivo);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Intencional: un -wal vacío y un -shm no cambian los datos de la base.
        }
    }

    private IEnumerable<string> ArchivosDelTemporal() =>
        SufijosAuxiliares.Select(sufijo => RutaTemporal + sufijo).Prepend(RutaTemporal);

    private static bool ExisteTabla(SqliteConnection conexion, string tabla)
    {
        using var comando = conexion.CreateCommand();
        comando.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $tabla";
        comando.Parameters.AddWithValue("$tabla", tabla);
        return Convert.ToInt64(comando.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
    }

    private static List<string> LeerMigraciones(SqliteConnection conexion)
    {
        using var comando = conexion.CreateCommand();
        comando.CommandText = $"SELECT \"MigrationId\" FROM \"{TablaHistorial}\" ORDER BY \"MigrationId\"";
        using var lector = comando.ExecuteReader();
        var migraciones = new List<string>();
        while (lector.Read())
        {
            migraciones.Add(lector.GetString(0));
        }

        return migraciones;
    }

    private static object? Escalar(SqliteConnection conexion, string sql)
    {
        using var comando = conexion.CreateCommand();
        comando.CommandText = sql;
        return comando.ExecuteScalar();
    }
}
