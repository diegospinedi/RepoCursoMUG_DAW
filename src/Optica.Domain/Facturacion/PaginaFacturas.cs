namespace Optica.Domain.Facturacion;

/// <summary>
/// Una página de la búsqueda de facturas (FR-22 de FEAT-009), como <see cref="Presupuestos.PaginaPresupuestos"/>: los
/// resúmenes de la página, de la más reciente a la más antigua, y cuántas coinciden en total con el filtro.
/// </summary>
/// <remarks>Lleva datos personales a través de los resúmenes: no sobrescribe <see cref="object.ToString"/>.</remarks>
public sealed class PaginaFacturas
{
    /// <exception cref="ArgumentNullException">La lista de resúmenes es null.</exception>
    /// <exception cref="ArgumentException">
    /// El total es negativo o menor que la cantidad de resúmenes de la página. El mensaje no incluye los valores.
    /// </exception>
    public PaginaFacturas(IReadOnlyList<ResumenFactura> facturas, int total)
    {
        ArgumentNullException.ThrowIfNull(facturas);
        if (total < 0 || total < facturas.Count)
        {
            throw new ArgumentException("El total de la página no es válido.", nameof(total));
        }

        Facturas = [.. facturas];
        Total = total;
    }

    /// <summary>Los resúmenes de la página, en el orden de la búsqueda.</summary>
    public IReadOnlyList<ResumenFactura> Facturas { get; }

    /// <summary>Cuántas facturas coinciden con el filtro, en todas las páginas.</summary>
    public int Total { get; }
}
