using Optica.Application.Backup;

namespace Optica.Infrastructure.Backup;

/// <summary>
/// Un backup a la vez en el proceso (D-22 de FEAT-010), sin esperar: el segundo pedido no hace nada. Singleton de la raíz
/// de composición, como <see cref="Facturacion.CandadoEmision"/>: la aplicación es de instancia única (FEAT-001b), así que
/// no hace falta un candado entre procesos. Sin recursos que descartar: no es <see cref="IDisposable"/>.
/// </summary>
public sealed class CandadoBackup : ICandadoBackup
{
    private int _tomado;

    public IDisposable? IntentarTomar() =>
        Interlocked.CompareExchange(ref _tomado, 1, 0) == 0 ? new Turno(this) : null;

    /// <summary>Libera el turno una sola vez: descartarlo de nuevo no libera el turno de otro backup.</summary>
    private sealed class Turno(CandadoBackup candado) : IDisposable
    {
        private int _liberado;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _liberado, 1) == 0)
            {
                Volatile.Write(ref candado._tomado, 0);
            }
        }
    }
}
