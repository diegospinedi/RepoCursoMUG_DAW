using Optica.Application.Comun;
using Optica.Domain.Facturacion;
using Optica.Domain.Presupuestos;

namespace Optica.Application.Facturacion;

/// <summary>
/// Un resultado de la búsqueda de facturas (FR-23 de FEAT-009, AC-23): el número del presupuesto de origen, el
/// comprobante como texto ("Factura B 0003-00000125", sin punto de venta como dato aparte, D-15), la fecha, Apellido,
/// Nombre y DNI del cliente del presupuesto (FR-25) y el total.
/// </summary>
/// <remarks>Datos personales (Ley 25.326): <see cref="object.ToString"/> no muestra ningún valor (R-01).</remarks>
public sealed record FacturaListada(
    long Presupuesto, string Comprobante, DateOnly Fecha, string Apellido, string Nombre, string Dni, decimal Total)
{
    private bool PrintMembers(System.Text.StringBuilder constructor) => false;

    internal static FacturaListada Desde(ResumenFactura resumen) => new(
        resumen.PresupuestoNumero,
        resumen.Comprobante.ToString(),
        resumen.Fecha,
        resumen.Apellido,
        resumen.Nombre,
        resumen.Dni,
        resumen.Total);
}

/// <summary>
/// Cómo terminó una búsqueda de facturas: una página de resultados o el error de Desde posterior a Hasta (AC-22).
/// </summary>
/// <remarks>Lleva datos personales en los resultados: <see cref="object.ToString"/> no muestra ningún valor (R-01).</remarks>
public sealed record ResultadoBusquedaFacturas
{
    private ResultadoBusquedaFacturas(
        IReadOnlyList<FacturaListada> facturas, int pagina, int total, IReadOnlyList<ErrorValidacion> errores)
    {
        Facturas = facturas;
        Pagina = pagina;
        Total = total;
        Errores = errores;
    }

    /// <summary>Las facturas de la página, de la más reciente a la más antigua; vacío si la búsqueda fue inválida.</summary>
    public IReadOnlyList<FacturaListada> Facturas { get; }

    /// <summary>La página devuelta, desde 1; 0 si la búsqueda fue inválida.</summary>
    public int Pagina { get; }

    /// <summary>El tamaño de página fijo de la búsqueda (FR-22).</summary>
    public int TamanoPagina => BuscarFacturas.TamanoPagina;

    /// <summary>Cuántas facturas coinciden en todas las páginas; 0 si la búsqueda fue inválida.</summary>
    public int Total { get; }

    /// <summary>Errores de los parámetros; vacío si la búsqueda se hizo.</summary>
    public IReadOnlyList<ErrorValidacion> Errores { get; }

    public bool EsValido => Errores.Count == 0;

    private bool PrintMembers(System.Text.StringBuilder constructor) => false;

    /// <exception cref="ArgumentNullException">La página es null.</exception>
    /// <exception cref="ArgumentException">El número de página es menor que 1.</exception>
    public static ResultadoBusquedaFacturas Valido(PaginaFacturas pagina, int numero)
    {
        ArgumentNullException.ThrowIfNull(pagina);
        if (numero < 1)
        {
            throw new ArgumentException("La página no es válida.", nameof(numero));
        }

        return new ResultadoBusquedaFacturas([.. pagina.Facturas.Select(FacturaListada.Desde)], numero, pagina.Total, []);
    }

    /// <exception cref="ArgumentException">No hay errores.</exception>
    public static ResultadoBusquedaFacturas Invalido(IReadOnlyList<ErrorValidacion> errores)
    {
        if (errores is null || errores.Count == 0)
        {
            throw new ArgumentException("Una búsqueda inválida necesita al menos un error.", nameof(errores));
        }

        return new ResultadoBusquedaFacturas([], 0, 0, [.. errores]);
    }
}

/// <summary>
/// Busca facturas por Apellido, Nombre, DNI, número de comprobante y rango de fechas, de a <see cref="TamanoPagina"/>
/// por página, de la más reciente a la más antigua (FR-16 a FR-23 y FR-25 de FEAT-009). Arma
/// <see cref="FiltroFacturas"/>, que pliega Apellido y Nombre y quita los separadores del DNI y del número, y pide la
/// página al repositorio. Si Desde es posterior a Hasta, el dominio lo rechaza y la búsqueda responde
/// <see cref="ResultadoBusquedaFacturas.Invalido"/> con el error junto a <see cref="CampoDesde"/>, sin buscar (AC-22).
/// </summary>
/// <remarks>
/// El formato de las fechas, los textos (con <see cref="ErrorDeTexto"/>) y el rango de la página los valida la Api
/// antes de llamar (ADR-019).
/// Las fallas del repositorio se propagan. No recibe un registrador: no registra los filtros ni los datos del cliente
/// en el log (R-01).
/// </remarks>
public sealed class BuscarFacturas(IFacturaRepository facturas)
{
    /// <summary>Facturas por página de la búsqueda (FR-22).</summary>
    public const int TamanoPagina = 50;

    /// <summary>El campo de la fecha Desde, donde va el error del rango (AC-22).</summary>
    public const string CampoDesde = "desde";

    /// <summary>El texto de AC-22, junto a Desde.</summary>
    public const string AccionDesdePosteriorAHasta = "La fecha Desde no puede ser posterior a Hasta.";

    /// <summary>Largo máximo de cada texto de la búsqueda, medido después de recortar.</summary>
    public const int LargoMaximoTexto = 100;

    /// <summary>Un texto de la búsqueda de más de <see cref="LargoMaximoTexto"/> caracteres; no repite el valor.</summary>
    public const string AccionTextoLargo = "Usá hasta 100 caracteres.";

    /// <summary>Un texto con UTF-16 inválido (un sustituto suelto), con el texto del molde de presupuestos.</summary>
    public const string AccionCaracteresInvalidos = "El texto tiene caracteres no válidos. Escribilo de nuevo.";

    /// <summary>
    /// El error de un texto de la búsqueda, o null si no tiene, como en la búsqueda de presupuestos: primero UTF-16
    /// inválido (<see cref="AccionCaracteresInvalidos"/>), después el largo medido con el recorte de
    /// <see cref="PlegadoCliente.Recortar"/>, que también quita U+FEFF. Un texto null no se cargó.
    /// La Api lo usa en el borde, antes de llamar a <see cref="EjecutarAsync"/>.
    /// </summary>
    public static ErrorValidacion? ErrorDeTexto(string? texto, string campo)
    {
        if (texto is null)
        {
            return null;
        }

        var recortado = PlegadoCliente.Recortar(texto);
        if (!PlegadoCliente.EsUtf16Valido(recortado))
        {
            return new ErrorValidacion(campo, AccionCaracteresInvalidos);
        }

        return recortado.Length > LargoMaximoTexto ? new ErrorValidacion(campo, AccionTextoLargo) : null;
    }

    /// <param name="pagina">Desde 1.</param>
    /// <exception cref="ArgumentOutOfRangeException">La página es menor que 1 (la Api ya la validó).</exception>
    public async Task<ResultadoBusquedaFacturas> EjecutarAsync(
        string? apellido,
        string? nombre,
        string? dni,
        string? numero,
        DateOnly? desde,
        DateOnly? hasta,
        int pagina,
        CancellationToken cancelacion = default)
    {
        FiltroFacturas filtro;
        try
        {
            filtro = FiltroFacturas.Crear(apellido, nombre, dni, numero, desde, hasta);
        }
        catch (FiltroFacturasInvalidoException)
        {
            return ResultadoBusquedaFacturas.Invalido([new ErrorValidacion(CampoDesde, AccionDesdePosteriorAHasta)]);
        }

        var encontradas = await facturas.BuscarAsync(filtro, pagina, TamanoPagina, cancelacion);
        return ResultadoBusquedaFacturas.Valido(encontradas, pagina);
    }
}
