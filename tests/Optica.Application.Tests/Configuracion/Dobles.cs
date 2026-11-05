using Microsoft.Extensions.Logging;
using Optica.Application.Comun;
using Optica.Domain.Configuracion;

namespace Optica.Application.Tests.Configuracion;

/// <summary>
/// Almacén en memoria que imita la unidad de trabajo: <see cref="Reemplazar"/> deja el cambio pendiente y solo
/// <see cref="ConfirmarAsync"/> lo pasa a lo guardado. Como el repositorio real, sin fila lanza
/// <see cref="InvalidOperationException"/> (D-04).
/// </summary>
internal sealed class ConfiguracionEnMemoria : IConfiguracionNegocioRepository, IUnidadDeTrabajo
{
    /// <summary>Los valores iniciales de la migración (FR-07, FR-08).</summary>
    public static ConfiguracionNegocio Inicial() =>
        new(21m, CondicionFiscal.ResponsableInscripto, 0.01m, null);

    private ConfiguracionNegocio? _pendiente;
    private bool _enOperacion;

    public ConfiguracionNegocio? Guardada { get; set; } = Inicial();

    public int Lecturas { get; private set; }

    public int Reemplazos { get; private set; }

    public int Confirmaciones { get; private set; }

    /// <summary>Si tiene valor, <see cref="ConfirmarAsync"/> lo lanza y no guarda nada.</summary>
    public Exception? ErrorAlConfirmar { get; set; }

    public Task<ConfiguracionNegocio> ObtenerAsync(CancellationToken cancelacion = default)
    {
        Lecturas++;
        return Guardada is null
            ? throw new InvalidOperationException("No hay fila de configuración de negocio.")
            : Task.FromResult(Guardada);
    }

    public void Reemplazar(ConfiguracionNegocio nueva)
    {
        ArgumentNullException.ThrowIfNull(nueva);
        if (Guardada is null)
        {
            throw new InvalidOperationException("No hay fila de configuración de negocio.");
        }

        Reemplazos++;
        _pendiente = nueva;
    }

    /// <summary>
    /// Ejecuta la operación; si lanza, lo guardado vuelve a como estaba al entrar y lo pendiente se descarta, como la
    /// transacción real que se revierte (ADR-013). Como la real, rechaza el anidamiento con
    /// <see cref="InvalidOperationException"/> (D-19).
    /// </summary>
    public async Task<T> EjecutarEnTransaccionAsync<T>(
        Func<CancellationToken, Task<T>> operacion, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(operacion);
        if (_enOperacion)
        {
            throw new InvalidOperationException("La operación en transacción no se anida.");
        }

        var guardadaAlEntrar = Guardada;
        _enOperacion = true;
        try
        {
            return await operacion(cancelacion);
        }
        catch
        {
            Guardada = guardadaAlEntrar;
            _pendiente = null;
            throw;
        }
        finally
        {
            _enOperacion = false;
        }
    }

    public Task ConfirmarAsync(CancellationToken cancelacion = default)
    {
        Confirmaciones++;
        if (ErrorAlConfirmar is { } error)
        {
            _pendiente = null;
            throw error;
        }

        if (_pendiente is not null)
        {
            Guardada = _pendiente;
            _pendiente = null;
        }

        return Task.CompletedTask;
    }
}

/// <summary>Una entrada del registro: nivel, mensaje ya formateado y los valores de sus marcadores.</summary>
internal sealed record EntradaRegistro(
    LogLevel Nivel, string Mensaje, IReadOnlyDictionary<string, object?> Valores, Exception? Error);

/// <summary>Registro de prueba que guarda cada entrada.</summary>
internal sealed class RegistroFalso<T> : ILogger<T>
{
    public List<EntradaRegistro> Entradas { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var valores = state is IEnumerable<KeyValuePair<string, object?>> pares
            ? pares.ToDictionary(par => par.Key, par => par.Value)
            : new Dictionary<string, object?>();
        Entradas.Add(new EntradaRegistro(logLevel, formatter(state, exception), valores, exception));
    }
}
