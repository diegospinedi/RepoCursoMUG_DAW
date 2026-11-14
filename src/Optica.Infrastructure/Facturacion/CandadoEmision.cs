using Optica.Application.Facturacion;

namespace Optica.Infrastructure.Facturacion;

/// <summary>
/// Una emisión a la vez en el proceso (D-10), con un <see cref="SemaphoreSlim"/> de un lugar. Singleton de la raíz de
/// composición: la aplicación es de instancia única (candado de FEAT-001b), así que no hace falta un candado entre
/// procesos. La espera la corta el token que recibe (el plazo único de la emisión, D-32); no tiene plazo propio.
/// </summary>
/// <remarks>
/// No es <see cref="IDisposable"/> a propósito (WARN-1 de la auditoría): un <see cref="SemaphoreSlim"/> que nunca usa
/// <see cref="SemaphoreSlim.AvailableWaitHandle"/> no tiene recursos que descartar, y si el contenedor lo descartara al
/// apagar la Api, la emisión que todavía tiene el turno fallaría al liberarlo.
/// </remarks>
public sealed class CandadoEmision : ICandadoEmision
{
    private readonly SemaphoreSlim _turno = new(1, 1);

    public async Task<IDisposable?> TomarAsync(CancellationToken cancelacion)
    {
        try
        {
            await _turno.WaitAsync(cancelacion).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancelacion.IsCancellationRequested)
        {
            // El plazo venció esperando: no se tomó nada.
            return null;
        }

        return new Turno(_turno);
    }

    /// <summary>
    /// Libera el turno una sola vez: descartarlo de nuevo no libera el turno de otra emisión. Tolera un semáforo ya
    /// descartado: no hay turno que devolver.
    /// </summary>
    private sealed class Turno(SemaphoreSlim turno) : IDisposable
    {
        private int _liberado;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _liberado, 1) != 0)
            {
                return;
            }

            try
            {
                turno.Release();
            }
            catch (ObjectDisposedException)
            {
                // Intencional: el candado ya no existe.
            }
        }
    }
}
