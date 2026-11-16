using Optica.Domain.Facturacion;

namespace Optica.Application.Facturacion;

/// <summary>
/// Los números de los presupuestos con una emisión pendiente de verificación, de menor a mayor, para el aviso al iniciar
/// sesión (FR-03, AC-03, AC-04). Solo números: ningún dato del cliente ni de la pendiente.
/// </summary>
public sealed class ListarEmisionesPendientes(IEmisionPendienteRepository pendientes)
{
    public async Task<IReadOnlyList<long>> EjecutarAsync(CancellationToken cancelacion = default)
    {
        var numeros = await pendientes.ListarNumerosAsync(cancelacion);
        // El repositorio ya los devuelve ascendentes; se ordena igual para que el contrato no dependa de él.
        return [.. numeros.Order()];
    }
}
