using Optica.Application.Comun;
using Optica.Domain.Presupuestos;

namespace Optica.Application.Presupuestos;

/// <summary>
/// Un resultado de la búsqueda de presupuestos (FR-09 de FEAT-006): número, fecha, Apellido, Nombre, DNI, estado como
/// texto (<see cref="PresupuestoGrabado.EstadoBorrador"/> o <see cref="PresupuestoGrabado.EstadoFinal"/>) y total, la
/// suma de los Precio Final de sus líneas. El estado es texto y no el enum del dominio, así la Api lo devuelve sin
/// compilar contra <c>Optica.Domain</c>.
/// </summary>
/// <remarks>Datos personales (Ley 25.326): <see cref="object.ToString"/> no muestra ningún valor (D-17 de FEAT-003a).</remarks>
public sealed record PresupuestoListado(
    long Numero, DateOnly Fecha, string Apellido, string Nombre, string Dni, string Estado, decimal Total)
{
    private bool PrintMembers(System.Text.StringBuilder constructor) => false;

    internal static PresupuestoListado Desde(ResumenPresupuesto resumen) => new(
        resumen.Numero,
        resumen.Fecha,
        resumen.Apellido,
        resumen.Nombre,
        resumen.Dni,
        PresupuestoGrabado.TextoDelEstado(resumen.Estado),
        resumen.Total);
}

/// <summary>
/// Cómo terminó una búsqueda de presupuestos: una página de resultados o los errores de los parámetros. Es propio de
/// Presupuestos: no usa los tipos de la búsqueda de artículos (D-05 de FEAT-003a).
/// </summary>
/// <remarks>Lleva datos personales en los resultados: <see cref="object.ToString"/> no muestra ningún valor.</remarks>
public sealed record ResultadoBusquedaPresupuestos
{
    private ResultadoBusquedaPresupuestos(
        IReadOnlyList<PresupuestoListado> presupuestos, int pagina, int total, IReadOnlyList<ErrorValidacion> errores)
    {
        Presupuestos = presupuestos;
        Pagina = pagina;
        Total = total;
        Errores = errores;
    }

    /// <summary>Los presupuestos de la página, por número de mayor a menor; vacío si la búsqueda fue inválida.</summary>
    public IReadOnlyList<PresupuestoListado> Presupuestos { get; }

    /// <summary>La página devuelta, desde 1; 0 si la búsqueda fue inválida.</summary>
    public int Pagina { get; }

    /// <summary>El tamaño de página fijo de la búsqueda (FR-08).</summary>
    public int TamanoPagina => BuscarPresupuestos.TamanoPagina;

    /// <summary>Cuántos presupuestos coinciden en todas las páginas; 0 si la búsqueda fue inválida.</summary>
    public int Total { get; }

    /// <summary>Errores de los parámetros; vacío si la búsqueda se hizo.</summary>
    public IReadOnlyList<ErrorValidacion> Errores { get; }

    public bool EsValido => Errores.Count == 0;

    private bool PrintMembers(System.Text.StringBuilder constructor) => false;

    /// <exception cref="ArgumentException">La página es menor que 1.</exception>
    public static ResultadoBusquedaPresupuestos Valido(PaginaPresupuestos pagina, int numero)
    {
        ArgumentNullException.ThrowIfNull(pagina);
        if (numero < 1)
        {
            throw new ArgumentException("La página no es válida.", nameof(numero));
        }

        return new ResultadoBusquedaPresupuestos(
            [.. pagina.Presupuestos.Select(PresupuestoListado.Desde)], numero, pagina.Total, []);
    }

    /// <exception cref="ArgumentException">No hay errores.</exception>
    public static ResultadoBusquedaPresupuestos Invalido(IReadOnlyList<ErrorValidacion> errores)
    {
        if (errores is null || errores.Count == 0)
        {
            throw new ArgumentException("Una búsqueda inválida necesita al menos un error.", nameof(errores));
        }

        return new ResultadoBusquedaPresupuestos([], 0, 0, [.. errores]);
    }
}

/// <summary>
/// Busca presupuestos por Apellido, Nombre, DNI y rango de fechas, de a <see cref="TamanoPagina"/> por página, por
/// número de mayor a menor (FR-01 a FR-09 de FEAT-006). Valida los parámetros con
/// <see cref="ValidacionPresupuesto.ValidarBusqueda"/> y, si alguno es inválido, devuelve
/// <see cref="ResultadoBusquedaPresupuestos.Invalido"/> sin buscar. Si no, arma <see cref="FiltroPresupuestos"/> (que
/// pliega Apellido y Nombre y normaliza el DNI) y pide la página al repositorio; una página ausente vale 1.
/// </summary>
/// <remarks>
/// <see cref="FiltroInvalidoException"/> queda como segunda barrera del dominio y no se atiende: la validación ya
/// rechazó Desde posterior a Hasta. Las fallas del repositorio se propagan. No recibe un registrador: no registra los
/// datos del cliente en el log (ADR-019).
/// </remarks>
public sealed class BuscarPresupuestos(IPresupuestoRepository presupuestos)
{
    /// <summary>Presupuestos por página de la búsqueda (FR-08).</summary>
    public const int TamanoPagina = 50;

    public async Task<ResultadoBusquedaPresupuestos> EjecutarAsync(
        string? apellido,
        string? nombre,
        string? dni,
        string? desde,
        string? hasta,
        int? pagina,
        CancellationToken cancelacion = default)
    {
        var errores = ValidacionPresupuesto.ValidarBusqueda(apellido, nombre, dni, desde, hasta, pagina);
        if (errores.Count > 0)
        {
            return ResultadoBusquedaPresupuestos.Invalido(errores);
        }

        ValidacionPresupuesto.LeerFechaBusqueda(desde, out var inicio);
        ValidacionPresupuesto.LeerFechaBusqueda(hasta, out var fin);
        var filtro = FiltroPresupuestos.Crear(apellido, nombre, dni, inicio, fin);
        var numero = pagina ?? 1;
        var encontrados = await presupuestos.BuscarAsync(filtro, numero, TamanoPagina, cancelacion);
        return ResultadoBusquedaPresupuestos.Valido(encontrados, numero);
    }
}
