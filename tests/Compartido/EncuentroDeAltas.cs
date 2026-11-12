using System.Collections.Concurrent;
using System.Data.Common;
using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Optica.Domain.Catalogo;
using Optica.Infrastructure.Persistencia;

namespace Optica.Pruebas.Compartido;

/// <summary>
/// Coordinador del encuentro forzado de dos altas de presupuesto (FEAT-003d, D-04 y D-06). Optica.Infrastructure.Tests
/// y Optica.Api.Tests enlazan este archivo. Armado con <see cref="Armar"/>, la primera llamada a
/// <see cref="ArticulosConEncuentro.ObtenerAsync"/> (ya dentro de la transacción del alta) queda retenida hasta que una
/// conexión con <see cref="BaseOcupadaAlAbrir"/> choca con la base ocupada, o hasta que vence el tope. La señal de base
/// ocupada queda marcada: si llega antes de que la primera alta pida un artículo, no se retiene a nadie. Las pruebas lo
/// desarman en un <c>finally</c>.
/// </summary>
/// <remarks>Es de pruebas: no se registra en la composición de la aplicación.</remarks>
public sealed class EncuentroDeAltas
{
    /// <summary>El mensaje fijo con el que falla el alta retenida si el encuentro no se produce dentro del tope.</summary>
    public const string MensajeTopeVencido = "El encuentro de las altas no se produjo dentro del tope.";

    private const string MensajeSinArmar = "El encuentro de las altas no está armado.";

    private Ronda? _ronda;
    private long _marcas;

    /// <summary>Arma una ronda nueva: la próxima alta que pida un artículo queda retenida hasta la señal o el tope.</summary>
    /// <exception cref="ArgumentOutOfRangeException">El tope es menor o igual a cero.</exception>
    /// <exception cref="InvalidOperationException">El encuentro ya está armado.</exception>
    public void Armar(TimeSpan tope)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(tope, TimeSpan.Zero);
        if (Interlocked.CompareExchange(ref _ronda, new Ronda(tope), null) is not null)
        {
            throw new InvalidOperationException("El encuentro de las altas ya está armado.");
        }
    }

    /// <summary>Desarma la ronda: el decorador y el busy handler dejan de intervenir.</summary>
    public void Desarmar() => Volatile.Write(ref _ronda, null);

    /// <summary>Se completa cuando la primera alta quedó retenida en <see cref="ArticulosConEncuentro.ObtenerAsync"/>.</summary>
    public Task PrimeraRetenida => RondaArmada().Retenida.Task;

    /// <summary>Se completa cuando alguna conexión chocó con la base ocupada; queda marcada.</summary>
    public Task BaseOcupada => RondaArmada().BaseOcupada.Task;

    /// <summary>La marca de orden de la señal de base ocupada, o null si todavía no hubo.</summary>
    public long? MarcaBaseOcupada => Volatile.Read(ref RondaArmada().Marca) is var marca and > 0 ? marca : null;

    /// <summary>El origen (el nombre de la conexión) que dio la señal de base ocupada, o null si todavía no hubo.</summary>
    public string? OrigenBaseOcupada => Volatile.Read(ref RondaArmada().Origen);

    /// <summary>Una marca de orden nueva, creciente, compartida por la señal y las transacciones.</summary>
    public long SiguienteMarca() => Interlocked.Increment(ref _marcas);

    /// <summary>
    /// Con el encuentro armado, retiene solo la primera llamada posterior a <see cref="Armar"/> hasta la señal de base
    /// ocupada. Vencido el tope, lanza <see cref="TimeoutException"/> con <see cref="MensajeTopeVencido"/>.
    /// </summary>
    internal async Task RetenerSiCorrespondeAsync(CancellationToken cancelacion)
    {
        var ronda = Volatile.Read(ref _ronda);
        if (ronda is null || Interlocked.Exchange(ref ronda.RetencionTomada, 1) != 0)
        {
            return;
        }

        ronda.Retenida.TrySetResult();
        try
        {
            await ronda.BaseOcupada.Task.WaitAsync(ronda.Tope, cancelacion);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException(MensajeTopeVencido);
        }
    }

    /// <summary>La señal de base ocupada: solo la primera vez por ronda, con su marca y su origen.</summary>
    internal void SenalarBaseOcupada(string origen)
    {
        var ronda = Volatile.Read(ref _ronda);
        if (ronda is null || Interlocked.Exchange(ref ronda.SenalTomada, 1) != 0)
        {
            return;
        }

        Volatile.Write(ref ronda.Origen, origen);
        Interlocked.Exchange(ref ronda.Marca, SiguienteMarca());
        ronda.BaseOcupada.TrySetResult();
    }

    private Ronda RondaArmada() => Volatile.Read(ref _ronda) ?? throw new InvalidOperationException(MensajeSinArmar);

    /// <summary>El estado de un armado: las dos señales, el tope y quién tomó cada cosa.</summary>
    private sealed class Ronda(TimeSpan tope)
    {
        public readonly TimeSpan Tope = tope;

        // Las continuaciones corren aparte: la señal se da desde el busy handler, dentro del paso nativo de SQLite.
        public readonly TaskCompletionSource Retenida = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public readonly TaskCompletionSource BaseOcupada = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int RetencionTomada;

        public int SenalTomada;

        public long Marca;

        public string? Origen;
    }
}

/// <summary>
/// Decorador scoped de <see cref="IArticuloRepository"/> sobre <see cref="ArticuloRepository"/>: con el encuentro armado,
/// la primera llamada a <see cref="ObtenerAsync"/> queda retenida (ver <see cref="EncuentroDeAltas"/>); lo demás pasa
/// directo al repositorio real.
/// </summary>
public sealed class ArticulosConEncuentro(OpticaDbContext contexto, EncuentroDeAltas encuentro) : IArticuloRepository
{
    private readonly ArticuloRepository _real = new(contexto);

    public async Task<Articulo?> ObtenerAsync(long codigo, CancellationToken cancelacion = default)
    {
        await encuentro.RetenerSiCorrespondeAsync(cancelacion);
        return await _real.ObtenerAsync(codigo, cancelacion);
    }

    public Task<bool> ExisteClaveAsync(string clave, CancellationToken cancelacion = default) =>
        _real.ExisteClaveAsync(clave, cancelacion);

    public void Agregar(Articulo articulo) => _real.Agregar(articulo);

    public Task<Articulo?> ObtenerPorClaveAsync(string clave, CancellationToken cancelacion = default) =>
        _real.ObtenerPorClaveAsync(clave, cancelacion);

    public Task<PaginaArticulos> BuscarAsync(
        string? textoPlegado, int pagina, int tamano, bool soloActivos = false, CancellationToken cancelacion = default) =>
        _real.BuscarAsync(textoPlegado, pagina, tamano, soloActivos, cancelacion);

    public Task<bool> ExisteClaveDeOtroAsync(string clave, long codigo, CancellationToken cancelacion = default) =>
        _real.ExisteClaveDeOtroAsync(clave, codigo, cancelacion);

    public void Actualizar(Articulo articulo, long versionLeida) => _real.Actualizar(articulo, versionLeida);

    public void CambiarEstado(long codigo, bool activo) => _real.CambiarEstado(codigo, activo);

    public Task<IReadOnlyList<Articulo>> ListarParaRecalculoAsync(CancellationToken cancelacion = default) =>
        _real.ListarParaRecalculoAsync(cancelacion);

    public void RegistrarPrecioRecalculado(Articulo articulo) => _real.RegistrarPrecioRecalculado(articulo);
}

/// <summary>
/// Al abrir cada conexión registra un busy handler de SQLite que devuelve 0: SQLite devuelve SQLITE_BUSY igual que sin
/// handler y Microsoft.Data.Sqlite reintenta igual que en producción (D-04; Microsoft.Data.Sqlite no registra handler
/// propio, reintenta en código administrado). Con el encuentro armado, la primera vez que se invoca da la señal de base
/// ocupada con <paramref name="origen"/>. Al cerrar la conexión se quita el handler, así el handle vuelve al grupo sin
/// él.
/// </summary>
/// <remarks>
/// SQLitePCLRaw 2.1.12 no expone <c>sqlite3_busy_handler</c> en <c>SQLitePCL.raw</c>: se llama a la misma función de la
/// biblioteca nativa <c>e_sqlite3</c> que usa el proveedor de SQLitePCLRaw, con el puntero del handle de la conexión.
/// </remarks>
public sealed class BaseOcupadaAlAbrir(EncuentroDeAltas encuentro, string origen) : DbConnectionInterceptor
{
    private const int SqliteOk = 0;

    private readonly EncuentroDeAltas _encuentro = encuentro;
    private readonly string _origen = origen;

    /// <summary>Quién está detrás de cada handle nativo: el último interceptor que lo abrió.</summary>
    private static readonly ConcurrentDictionary<IntPtr, BaseOcupadaAlAbrir> PorHandle = new();

    // Referencia estática: el delegado no se recolecta mientras SQLite conserve el puntero.
    private static readonly ManejadorOcupada Manejador = AlChocar;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ManejadorOcupada(IntPtr datos, int intentos);

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) =>
        Registrar(connection);

    public override Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Registrar(connection);
        return Task.CompletedTask;
    }

    private void Registrar(DbConnection connection)
    {
        var handle = (connection as SqliteConnection)?.Handle
            ?? throw new InvalidOperationException("La conexión de la base no es de SQLite o no está abierta.");
        var puntero = handle.DangerousGetHandle();
        PorHandle[puntero] = this;
        if (sqlite3_busy_handler(puntero, Manejador, puntero) != SqliteOk)
        {
            throw new InvalidOperationException("No se pudo registrar el busy handler de SQLite.");
        }
    }

    public override InterceptionResult ConnectionClosing(
        DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    {
        Desregistrar(connection);
        return result;
    }

    public override ValueTask<InterceptionResult> ConnectionClosingAsync(
        DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    {
        Desregistrar(connection);
        return ValueTask.FromResult(result);
    }

    /// <summary>
    /// Antes de cerrar (el handle vuelve al grupo): quita la entrada si sigue siendo de este interceptor y saca el busy
    /// handler del handle nativo, así el registro dura lo mismo que la conexión abierta.
    /// </summary>
    private void Desregistrar(DbConnection connection)
    {
        if ((connection as SqliteConnection)?.Handle is not { IsInvalid: false } handle)
        {
            return;
        }

        var puntero = handle.DangerousGetHandle();
        PorHandle.TryRemove(new KeyValuePair<IntPtr, BaseOcupadaAlAbrir>(puntero, this));
        if (sqlite3_busy_handler(puntero, null, IntPtr.Zero) != SqliteOk)
        {
            throw new InvalidOperationException("No se pudo quitar el busy handler de SQLite.");
        }
    }

    /// <summary>Corre dentro del paso nativo de SQLite: no lanza y siempre devuelve 0 (no reintentar acá).</summary>
    private static int AlChocar(IntPtr datos, int intentos)
    {
        if (PorHandle.TryGetValue(datos, out var interceptor))
        {
            interceptor._encuentro.SenalarBaseOcupada(interceptor._origen);
        }

        return 0;
    }

    [DllImport("e_sqlite3", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_busy_handler(IntPtr db, ManejadorOcupada? manejador, IntPtr datos);
}

/// <summary>
/// Registra, con las marcas de orden de <see cref="EncuentroDeAltas.SiguienteMarca"/>, cuándo empezó y cuándo confirmó la
/// transacción de un contexto (D-06). Una instancia por contexto.
/// </summary>
public sealed class MarcasDeTransaccion(EncuentroDeAltas encuentro) : DbTransactionInterceptor
{
    private long _inicio;
    private long _confirmacion;

    /// <summary>La marca del inicio de la transacción (después del BEGIN), o null si no empezó.</summary>
    public long? Inicio => Volatile.Read(ref _inicio) is var marca and > 0 ? marca : null;

    /// <summary>La marca de la confirmación, o null si no confirmó.</summary>
    public long? Confirmacion => Volatile.Read(ref _confirmacion) is var marca and > 0 ? marca : null;

    public override DbTransaction TransactionStarted(
        DbConnection connection, TransactionEndEventData eventData, DbTransaction result)
    {
        Interlocked.Exchange(ref _inicio, encuentro.SiguienteMarca());
        return result;
    }

    public override ValueTask<DbTransaction> TransactionStartedAsync(
        DbConnection connection, TransactionEndEventData eventData, DbTransaction result,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Exchange(ref _inicio, encuentro.SiguienteMarca());
        return ValueTask.FromResult(result);
    }

    public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) =>
        Interlocked.Exchange(ref _confirmacion, encuentro.SiguienteMarca());

    public override Task TransactionCommittedAsync(
        DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Interlocked.Exchange(ref _confirmacion, encuentro.SiguienteMarca());
        return Task.CompletedTask;
    }
}
