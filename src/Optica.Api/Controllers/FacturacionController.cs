using System.Globalization;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Optica.Api.Acceso;
using Optica.Api.Errores;
using Optica.Application.Comun;
using Optica.Application.Facturacion;

namespace Optica.Api.Controllers;

/// <summary>
/// La respuesta 200 de <c>GET /api/facturacion/emisiones-pendientes</c>: <c>{ "presupuestos": [155, 160] }</c>, solo los
/// números de los presupuestos con una emisión pendiente de verificación, de menor a mayor (FEAT-008, FR-03).
/// </summary>
public sealed record EmisionesPendientes(IReadOnlyList<long> Presupuestos);

/// <summary>
/// Cuerpo de <c>POST /api/facturacion/busqueda</c> (FEAT-009, ADR-019, D-28): los filtros viajan en el cuerpo, nunca en
/// la URL. Todos son opcionales: Apellido, Nombre, DNI y número de comprobante como texto, Desde y Hasta como texto
/// <c>yyyy-MM-dd</c> y la página como entero; ausente vale 1. Números JSON estrictos: una página como texto o decimal, o
/// un texto enviado como número, es un cuerpo ilegible en su campo.
/// </summary>
/// <remarks>
/// Datos personales (Ley 25.326): <see cref="object.ToString"/> no muestra ningún valor. MVC registra en Trace los
/// argumentos de la acción con su <c>ToString()</c> (R-01).
/// </remarks>
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed record SolicitudBusquedaFacturas(
    string? Apellido, string? Nombre, string? Dni, string? Numero, string? Desde, string? Hasta, int? Pagina)
{
    private bool PrintMembers(System.Text.StringBuilder constructor) => false;
}

/// <summary>
/// Respuesta 200 de la búsqueda de facturas: <c>{ facturas, pagina, tamanoPagina, total }</c>. Cada factura lleva el
/// comprobante como texto, sin clave de punto de venta (D-15). Una página posterior a la última trae la lista vacía y el
/// total.
/// </summary>
/// <remarks>Lleva Apellido, Nombre y DNI en los resultados: <see cref="object.ToString"/> no muestra ningún valor.</remarks>
public sealed record ListadoFacturas(IReadOnlyList<FacturaListada> Facturas, int Pagina, int TamanoPagina, int Total)
{
    private bool PrintMembers(System.Text.StringBuilder constructor) => false;
}

/// <summary>
/// Facturación fuera de un presupuesto: la lista de emisiones pendientes para el aviso de la pantalla de inicio
/// (FEAT-008, FR-03, AC-03, AC-04) y la búsqueda de facturas (FEAT-009, FR-16 a FR-23, D-28). Solo traduce HTTP y valida
/// el pedido de la búsqueda en el borde. Exige sesión por la política por defecto (no está en
/// <see cref="RutasAnonimas"/>): sin sesión, 401. Todas las respuestas llevan <c>no-store</c> como todo <c>/api</c>.
/// </summary>
[ApiController]
[Route("api/facturacion")]
public sealed class FacturacionController(
    ListarEmisionesPendientes listarEmisionesPendientes, BuscarFacturas buscarFacturas) : ControllerBase
{
    /// <summary>Tamaño máximo del cuerpo de la búsqueda, como el de presupuestos (ADR-019); más grande, 413.</summary>
    public const long CuerpoMaximoBusqueda = 4 * 1024;

    /// <summary>Páginas que se pueden pedir en la búsqueda, como en la de presupuestos.</summary>
    public const int PaginaMaximaBusqueda = 1_000_000;

    private const string CampoApellido = "apellido";
    private const string CampoNombre = "nombre";
    private const string CampoDni = "dni";
    private const string CampoNumero = "numero";
    private const string CampoHasta = "hasta";
    private const string CampoPagina = "pagina";
    private const string FormatoFecha = "yyyy-MM-dd";

    private const string AccionFechaInvalida = "La fecha no es válida.";
    private const string AccionPaginaFueraDeRango = "Pedí una página entre 1 y 1.000.000.";

    /// <summary>Los campos del cuerpo de la búsqueda, en el orden en que se buscan en un error de lectura.</summary>
    private static readonly IReadOnlyList<string> CamposBusqueda =
        [CampoApellido, CampoNombre, CampoDni, CampoNumero, BuscarFacturas.CampoDesde, CampoHasta, CampoPagina];

    /// <summary>Los presupuestos con una emisión pendiente, de menor a mayor; sin pendientes, la lista vacía.</summary>
    [HttpGet("emisiones-pendientes")]
    public async Task<IActionResult> ListarPendientes(CancellationToken cancelacion) =>
        Ok(new EmisionesPendientes(await listarEmisionesPendientes.EjecutarAsync(cancelacion)));

    /// <summary>
    /// Busca facturas por Apellido, Nombre, DNI, número de comprobante y rango de fechas y responde 200 con la página de
    /// 50, de la más reciente a la más antigua (FEAT-009, FR-16 a FR-23). Los filtros viajan en el cuerpo y no se
    /// registran (R-01). Un cuerpo ilegible responde 400 con un solo error y la acción fija de la búsqueda
    /// (<see cref="RespuestasValidacion.AccionCuerpoIlegibleBusqueda"/>), en el campo que nombra el lector o en
    /// <c>apellido</c>; textos con UTF-16 inválido o de más de 100 caracteres (<see cref="BuscarFacturas.ErrorDeTexto"/>),
    /// fechas que no son <c>yyyy-MM-dd</c> reales o una página fuera de rango, 400 con todos los errores por campo. Solo
    /// si todo eso es válido, Desde posterior a Hasta da 400 junto a <c>desde</c>, sin buscar (AC-22, D-42).
    /// Sin sesión, 401 antes de leer el cuerpo; más de 4 KB, 413.
    /// </summary>
    [HttpPost("busqueda")]
    [RequestSizeLimit(CuerpoMaximoBusqueda)]
    public async Task<IActionResult> Buscar(
        [FromBody] SolicitudBusquedaFacturas? solicitud, CancellationToken cancelacion)
    {
        if (!ModelState.IsValid)
        {
            return this.CuerpoIlegible(RespuestasValidacion.AccionCuerpoIlegibleBusqueda, CampoApellido, CamposBusqueda);
        }

        var errores = new List<ErrorValidacion>();
        AgregarErrorDeTexto(errores, solicitud?.Apellido, CampoApellido);
        AgregarErrorDeTexto(errores, solicitud?.Nombre, CampoNombre);
        AgregarErrorDeTexto(errores, solicitud?.Dni, CampoDni);
        AgregarErrorDeTexto(errores, solicitud?.Numero, CampoNumero);
        var desde = LeerFecha(errores, solicitud?.Desde, BuscarFacturas.CampoDesde);
        var hasta = LeerFecha(errores, solicitud?.Hasta, CampoHasta);
        var pagina = solicitud?.Pagina ?? 1;
        if (pagina is < 1 or > PaginaMaximaBusqueda)
        {
            errores.Add(new ErrorValidacion(CampoPagina, AccionPaginaFueraDeRango));
        }

        if (errores.Count > 0)
        {
            return this.Invalido([.. errores]);
        }

        return ResponderBusqueda(await buscarFacturas.EjecutarAsync(
            solicitud?.Apellido, solicitud?.Nombre, solicitud?.Dni, solicitud?.Numero, desde, hasta, pagina, cancelacion));
    }

    /// <summary>
    /// Traduce el resultado de la búsqueda. Cerrado por defecto: solo una búsqueda válida responde 200 con la página, y
    /// solo una inválida con errores, 400 con todos; cualquier otro resultado lanza y el manejador responde 500 genérico.
    /// </summary>
    internal IActionResult ResponderBusqueda(ResultadoBusquedaFacturas? resultado) => resultado switch
    {
        { EsValido: true } => Ok(new ListadoFacturas(
            resultado.Facturas, resultado.Pagina, resultado.TamanoPagina, resultado.Total)),
        { EsValido: false, Errores.Count: > 0 } => this.Invalido([.. resultado.Errores]),
        _ => throw new InvalidOperationException("Resultado de la búsqueda de facturas no previsto."),
    };

    // La regla del texto es la del caso de uso (UTF-16 válido y largo recortado, como en presupuestos); el error no
    // repite el valor.
    private static void AgregarErrorDeTexto(List<ErrorValidacion> errores, string? texto, string campo)
    {
        if (BuscarFacturas.ErrorDeTexto(texto, campo) is { } error)
        {
            errores.Add(error);
        }
    }

    // Null es una fecha no cargada; cualquier otro texto tiene que ser exactamente yyyy-MM-dd y una fecha real.
    private static DateOnly? LeerFecha(List<ErrorValidacion> errores, string? texto, string campo)
    {
        if (texto is null)
        {
            return null;
        }

        if (DateOnly.TryParseExact(texto, FormatoFecha, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha))
        {
            return fecha;
        }

        errores.Add(new ErrorValidacion(campo, AccionFechaInvalida));
        return null;
    }
}
