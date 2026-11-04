using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Optica.Api.Acceso;
using Optica.Api.Errores;
using Optica.Application.Catalogo;

namespace Optica.Api.Controllers;

/// <summary>
/// Cuerpo de <c>POST /api/articulos</c>. El costo y el margen se leen como números JSON estrictos (un texto, aunque
/// tenga dígitos, no se acepta); los textos, como <c>string?</c>, para que uno faltante sea un error del campo que
/// informa el caso de uso y no un cuerpo ilegible.
/// </summary>
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed record SolicitudArticulo(
    string? CodigoProveedor, string? Descripcion, decimal? PrecioCosto, decimal? MargenUtilidad);

/// <summary>
/// Alta y consulta de artículos (FEAT-002a). Solo traduce HTTP: delega en <see cref="AltaArticulo"/> y
/// <see cref="ObtenerArticulo"/>, que validan, calculan el precio de venta y graban. Exige sesión por la política por
/// defecto (no está en <see cref="RutasAnonimas"/>, R-01): las respuestas traen costos y márgenes. Sin sesión, 401
/// antes del 415 y del 413; el Content-Type y el tamaño del cuerpo los controla <see cref="LimitesDelCuerpo"/> (R-02,
/// R-05). No registra el cuerpo de los pedidos (R-06).
/// </summary>
[ApiController]
[Route("api/articulos")]
public sealed class ArticulosController(AltaArticulo altaArticulo, ObtenerArticulo obtenerArticulo) : ControllerBase
{
    /// <summary>Tamaño máximo del cuerpo del POST; más grande, 413.</summary>
    public const long CuerpoMaximo = 1024;

    /// <summary>Campos del cuerpo, en el orden en que se busca el que nombra un error de lectura.</summary>
    private static readonly IReadOnlyList<string> Campos =
    [
        ValidacionArticulo.CampoCodigoProveedor,
        ValidacionArticulo.CampoDescripcion,
        ValidacionArticulo.CampoPrecioCosto,
        ValidacionArticulo.CampoMargenUtilidad,
    ];

    /// <summary>El artículo con ese código: 200, o 404 con ProblemDetails sin detalles (R-06).</summary>
    [HttpGet("{codigo:long}")]
    public async Task<ActionResult<ArticuloGrabado>> Obtener(long codigo, CancellationToken cancelacion)
    {
        if (await obtenerArticulo.EjecutarAsync(codigo, cancelacion) is { } articulo)
        {
            return articulo;
        }

        return Problem(statusCode: StatusCodes.Status404NotFound, title: "No se encontró el artículo.");
    }

    /// <summary>
    /// Da de alta un artículo y responde 201 con el artículo grabado y <c>Location</c> hacia su GET. Un cuerpo ilegible
    /// responde 400 con un solo error y una acción fija, sin los mensajes del lector de JSON ni el valor (R-06); datos
    /// inválidos o un código en el proveedor duplicado, 400 con todos los campos.
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(CuerpoMaximo)]
    public async Task<IActionResult> Grabar([FromBody] SolicitudArticulo? solicitud, CancellationToken cancelacion)
    {
        if (!ModelState.IsValid)
        {
            return this.CuerpoIlegible(
                RespuestasValidacion.AccionCuerpoIlegibleConfiguracion, ValidacionArticulo.CampoCodigoProveedor, Campos);
        }

        var resultado = await altaArticulo.EjecutarAsync(
            new DatosArticulo(
                solicitud?.CodigoProveedor, solicitud?.Descripcion, solicitud?.PrecioCosto, solicitud?.MargenUtilidad),
            cancelacion);
        return await ResponderAltaAsync(resultado);
    }

    /// <summary>
    /// Traduce el resultado del alta. Cerrado por defecto: solo un alta grabada responde 201 y solo una inválida con
    /// errores, 400; cualquier otro resultado lanza, y el manejador de errores responde 500 genérico.
    /// </summary>
    internal Task<IActionResult> ResponderAltaAsync(ResultadoAlta? resultado)
    {
        IActionResult respuesta = resultado switch
        {
            { EsGrabado: true, Articulo: var articulo } =>
                CreatedAtAction(nameof(Obtener), new { codigo = articulo.Codigo }, articulo),
            { EsGrabado: false, Errores.Count: > 0 } => this.Invalido([.. resultado.Errores]),
            _ => throw new InvalidOperationException("Resultado del alta de un artículo no previsto."),
        };
        return Task.FromResult(respuesta);
    }
}
