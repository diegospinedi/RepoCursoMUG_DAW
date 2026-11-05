using Optica.Application.Comun;
using Optica.Domain.Catalogo;

namespace Optica.Application.Catalogo;

/// <summary>Un artículo del listado: sin costo ni margen (D-12).</summary>
public sealed record ArticuloListado(
    long Codigo, string CodigoProveedor, string Descripcion, decimal PrecioVenta, bool Activo);

/// <summary>Cómo terminó una búsqueda: una página de artículos o los errores de los parámetros.</summary>
public sealed class ResultadoBusqueda
{
    private ResultadoBusqueda(
        IReadOnlyList<ArticuloListado> articulos, int pagina, int total, IReadOnlyList<ErrorValidacion> errores)
    {
        Articulos = articulos;
        Pagina = pagina;
        Total = total;
        Errores = errores;
    }

    /// <summary>Los artículos de la página, en el orden por descripción; vacío si la búsqueda fue inválida.</summary>
    public IReadOnlyList<ArticuloListado> Articulos { get; }

    /// <summary>La página devuelta, desde 1; 0 si la búsqueda fue inválida.</summary>
    public int Pagina { get; }

    /// <summary>El tamaño de página fijo del listado (D-01).</summary>
    public int TamanoPagina => BuscarArticulos.TamanoPagina;

    /// <summary>Cuántos artículos coinciden en todas las páginas; 0 si la búsqueda fue inválida.</summary>
    public int Total { get; }

    /// <summary>Errores de los parámetros; vacío si la búsqueda se hizo.</summary>
    public IReadOnlyList<ErrorValidacion> Errores { get; }

    public bool EsValido => Errores.Count == 0;

    /// <exception cref="ArgumentException">La página es menor que 1.</exception>
    public static ResultadoBusqueda Valido(PaginaArticulos pagina, int numero)
    {
        ArgumentNullException.ThrowIfNull(pagina);
        if (numero < 1)
        {
            throw new ArgumentException("La página no es válida.", nameof(numero));
        }

        return new ResultadoBusqueda([.. pagina.Articulos.Select(Listado)], numero, pagina.Total, []);
    }

    public static ResultadoBusqueda Invalido(IReadOnlyList<ErrorValidacion> errores)
    {
        if (errores is null || errores.Count == 0)
        {
            throw new ArgumentException("Una búsqueda inválida necesita al menos un error.", nameof(errores));
        }

        return new ResultadoBusqueda([], 0, 0, [.. errores]);
    }

    private static ArticuloListado Listado(Articulo articulo) => new(
        articulo.Codigo ?? throw new InvalidOperationException("El artículo leído no tiene código."),
        articulo.CodigoProveedor,
        articulo.Descripcion,
        articulo.PrecioVenta,
        articulo.Activo);
}

/// <summary>
/// Busca artículos por texto, de a <see cref="TamanoPagina"/> por página ordenados por descripción (FR-04, D-01). Valida
/// el texto y la página sin buscar si alguno es inválido; un texto vacío o ausente lista todo el catálogo (D-06), una
/// página ausente vale 1 (D-07), y el texto se pliega con <see cref="PlegadoBusqueda"/> para ignorar mayúsculas y
/// acentos (D-04). No registra el texto en el log (D-12).
/// </summary>
/// <remarks>
/// Resultados posibles: válido, con la página (vacía si es posterior a la última, con el total, D-07), o inválido, con
/// los errores en <c>texto</c> y en <c>pagina</c>. Las fallas del repositorio se propagan.
/// </remarks>
public sealed class BuscarArticulos(IArticuloRepository articulos)
{
    /// <summary>Artículos por página del listado (D-01).</summary>
    public const int TamanoPagina = 50;

    public async Task<ResultadoBusqueda> EjecutarAsync(
        string? texto, int? pagina, CancellationToken cancelacion = default)
    {
        var errores = ValidacionArticulo.ValidarBusqueda(texto, pagina);
        if (errores.Count > 0)
        {
            return ResultadoBusqueda.Invalido(errores);
        }

        // Un texto hecho solo de espacios o de marcas de acento queda vacío al plegarlo: sin filtro.
        var plegado = texto is null ? null : PlegadoBusqueda.Plegar(texto);
        var numero = pagina ?? 1;
        var encontrados = await articulos.BuscarAsync(
            string.IsNullOrEmpty(plegado) ? null : plegado, numero, TamanoPagina, cancelacion);
        return ResultadoBusqueda.Valido(encontrados, numero);
    }
}
