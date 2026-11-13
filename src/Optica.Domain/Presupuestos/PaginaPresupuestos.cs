namespace Optica.Domain.Presupuestos;

/// <summary>
/// Una página de la búsqueda de presupuestos (FR-08 de FEAT-006), como <see cref="Catalogo.PaginaArticulos"/>: los
/// resúmenes de la página, por número de mayor a menor, y cuántos coinciden en total con el filtro.
/// </summary>
/// <remarks>Lleva datos personales a través de los resúmenes: no sobrescribe <see cref="object.ToString"/>.</remarks>
public sealed class PaginaPresupuestos
{
    /// <exception cref="ArgumentException">
    /// El total es negativo o menor que la cantidad de resúmenes de la página. El mensaje no incluye los valores.
    /// </exception>
    public PaginaPresupuestos(IReadOnlyList<ResumenPresupuesto> presupuestos, int total)
    {
        ArgumentNullException.ThrowIfNull(presupuestos);
        if (total < 0 || total < presupuestos.Count)
        {
            throw new ArgumentException("El total de la página no es válido.", nameof(total));
        }

        Presupuestos = [.. presupuestos];
        Total = total;
    }

    /// <summary>Los resúmenes de la página, en el orden de la búsqueda.</summary>
    public IReadOnlyList<ResumenPresupuesto> Presupuestos { get; }

    /// <summary>Cuántos presupuestos coinciden con el filtro, en todas las páginas.</summary>
    public int Total { get; }
}
