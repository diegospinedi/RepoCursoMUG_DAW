namespace Optica.Application.Facturacion;

/// <summary>
/// El plazo de una emisión (D-32 de FEAT-007, D-10): un <see cref="CancellationTokenSource"/> sobre el
/// <see cref="TimeProvider"/> que vence a <see cref="TiempoMaximo"/> de crearse, y lo que le queda. Lo crea quien toma el
/// turno del candado, antes de pedirlo (la emisión) o al empezar a emitir (la verificación, FR-06), y lo descarta al
/// terminar.
/// </summary>
public sealed class PlazoEmision : IDisposable
{
    private readonly CancellationTokenSource _fuente;
    private readonly TimeProvider _reloj;
    private readonly long _inicio;

    private PlazoEmision(TimeSpan tiempoMaximo, TimeProvider reloj)
    {
        TiempoMaximo = tiempoMaximo;
        _reloj = reloj;
        _inicio = reloj.GetTimestamp();
        _fuente = new CancellationTokenSource(tiempoMaximo, reloj);
    }

    /// <summary>La duración total del plazo.</summary>
    public TimeSpan TiempoMaximo { get; }

    /// <summary>Se cancela al vencer el plazo.</summary>
    public CancellationToken Token => _fuente.Token;

    /// <summary>Lo que queda del plazo (negativo si ya venció).</summary>
    public TimeSpan Restante => TiempoMaximo - _reloj.GetElapsedTime(_inicio);

    /// <summary>Empieza a contar el plazo ahora.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="reloj"/> es null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">El tiempo máximo no es positivo.</exception>
    public static PlazoEmision Iniciar(TimeSpan tiempoMaximo, TimeProvider reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(tiempoMaximo, TimeSpan.Zero);
        return new PlazoEmision(tiempoMaximo, reloj);
    }

    public void Dispose() => _fuente.Dispose();
}
