using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Optica.Infrastructure.Tests.Arca;

/// <summary>Una entrada del log capturada: nivel, mensaje con sus propiedades y excepción adjunta.</summary>
internal sealed record EntradaRegistroArca(LogLevel Nivel, string Texto, Exception? Excepcion);

/// <summary>
/// Captura el log de las pruebas de ARCA (mensaje formateado y propiedades estructuradas), para afirmar qué se registra y
/// que nunca aparece un valor de configuración, el contenido de un archivo ni un dato del cliente.
/// </summary>
internal sealed class RegistroCapturadoArca : ILoggerProvider
{
    private readonly ConcurrentQueue<EntradaRegistroArca> _entradas = new();

    public IReadOnlyList<EntradaRegistroArca> Entradas => _entradas.ToArray();

    public ILogger CreateLogger(string categoryName) => new Registrador(_entradas);

    public ILogger<T> Para<T>() => new RegistradorDe<T>(CreateLogger(typeof(T).FullName ?? typeof(T).Name));

    public void Dispose()
    {
    }

    private sealed class Registrador(ConcurrentQueue<EntradaRegistroArca> entradas) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var propiedades = state is IEnumerable<KeyValuePair<string, object?>> pares
                ? string.Join("; ", pares.Select(p => $"{p.Key}={p.Value}"))
                : string.Empty;
            entradas.Enqueue(new EntradaRegistroArca(logLevel, formatter(state, exception) + "\n" + propiedades, exception));
        }
    }

    private sealed class RegistradorDe<T>(ILogger interno) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => interno.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => interno.IsEnabled(logLevel);

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            interno.Log(logLevel, eventId, state, exception, formatter);
    }
}
