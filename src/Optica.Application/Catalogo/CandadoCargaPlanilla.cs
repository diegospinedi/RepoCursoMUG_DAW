namespace Optica.Application.Catalogo;

/// <summary>
/// Una carga de planilla de costos por vez en todo el proceso (D-23, D-25): <see cref="ActualizarCostosPorPlanilla"/>
/// lo toma antes de leer la planilla y lo libera después de la transacción, así la lectura y la transacción de una carga
/// no se solapan con las de otra. Acota la memoria transitoria de la lectura (D-20, R-02) y coincide con el único
/// escritor de SQLite (R-04). La Api lo registra como singleton; es la segunda barrera detrás del limitador de
/// concurrencia del endpoint (D-25).
/// </summary>
public sealed class CandadoCargaPlanilla : IDisposable
{
    private readonly SemaphoreSlim _semaforo = new(1, 1);

    /// <summary>
    /// Se dispara cuando una carga encuentra el candado tomado y empieza a esperar su turno, justo antes de esperar.
    /// Permite observar la espera sin depender del tiempo (pruebas, diagnóstico); no lleva datos de la carga.
    /// </summary>
    public event EventHandler? EsperaIniciada;

    /// <summary>
    /// Toma el candado; si otra carga lo tiene, avisa con <see cref="EsperaIniciada"/> y espera en fila. Quien lo toma
    /// tiene que llamar a <see cref="Liberar"/> en un <c>finally</c>.
    /// </summary>
    /// <exception cref="OperationCanceledException">Se canceló antes de tomarlo; no queda tomado.</exception>
    public async Task TomarAsync(CancellationToken cancelacion)
    {
        if (_semaforo.Wait(0, cancelacion))
        {
            return;
        }

        EsperaIniciada?.Invoke(this, EventArgs.Empty);
        await _semaforo.WaitAsync(cancelacion);
    }

    /// <summary>Libera el candado tomado con <see cref="TomarAsync"/>; la siguiente carga en fila entra.</summary>
    public void Liberar() => _semaforo.Release();

    public void Dispose() => _semaforo.Dispose();
}
