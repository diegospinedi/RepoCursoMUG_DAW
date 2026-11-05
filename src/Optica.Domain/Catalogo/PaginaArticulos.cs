namespace Optica.Domain.Catalogo;

/// <summary>
/// Una página de una búsqueda de artículos (FR-04, D-01): los artículos de la página, en el orden por descripción, y
/// cuántos coinciden en total con el filtro.
/// </summary>
public sealed class PaginaArticulos
{
    /// <exception cref="ArgumentException">
    /// El total es negativo o menor que la cantidad de artículos de la página. El mensaje no incluye los valores.
    /// </exception>
    public PaginaArticulos(IReadOnlyList<Articulo> articulos, int total)
    {
        ArgumentNullException.ThrowIfNull(articulos);
        if (total < 0 || total < articulos.Count)
        {
            throw new ArgumentException("El total de la página no es válido.", nameof(total));
        }

        Articulos = [.. articulos];
        Total = total;
    }

    /// <summary>Los artículos de la página, en el orden de la búsqueda.</summary>
    public IReadOnlyList<Articulo> Articulos { get; }

    /// <summary>Cuántos artículos coinciden con el filtro, en todas las páginas.</summary>
    public int Total { get; }
}
