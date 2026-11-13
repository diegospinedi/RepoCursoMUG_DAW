namespace Optica.Application.Facturacion;

/// <summary>
/// Una emisión a la vez en el proceso (D-10): WSFEv1 numera con el último autorizado + 1, así que dos pedidos
/// simultáneos a ARCA pedirían el mismo número. La aplicación es de instancia única (candado de FEAT-001b), por eso
/// alcanza con un candado en memoria. La implementación vive en Infrastructure y es singleton.
/// </summary>
public interface ICandadoEmision
{
    /// <summary>
    /// Espera el turno de emitir. Devuelve lo que hay que descartar para liberarlo, o null si
    /// <paramref name="cancelacion"/> se canceló antes de conseguirlo (el plazo único de la emisión, D-32): en ese caso
    /// no se tomó nada y no hay que liberar nada. No lanza por la cancelación.
    /// </summary>
    Task<IDisposable?> TomarAsync(CancellationToken cancelacion);
}
