using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Optica.Api.Acceso;
using Optica.Api.Errores;
using Optica.Application.Catalogo;
using Optica.Application.Comun;

namespace Optica.Api.Controllers;

/// <summary>
/// Cuerpo de <c>POST /api/articulos</c>. El costo y el margen se leen como números JSON estrictos (un texto, aunque
/// tenga dígitos, no se acepta); los textos, como <c>string?</c>, para que uno faltante sea un error del campo que
/// informa el caso de uso y no un cuerpo ilegible.
/// </summary>
/// <remarks>
/// <see cref="object.ToString"/> no muestra ningún valor (<c>SolicitudArticulo { }</c>): MVC registra en Trace los
/// argumentos de la acción con su <c>ToString()</c>, y el de un record mostraría el costo y el margen (R-06). Vale para
/// las tres solicitudes de este archivo.
/// </remarks>
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed record SolicitudArticulo(
    string? CodigoProveedor, string? Descripcion, decimal? PrecioCosto, decimal? MargenUtilidad)
{
    private bool PrintMembers(System.Text.StringBuilder constructor) => false;
}

/// <summary>
/// Cuerpo de <c>PUT /api/articulos/{codigo}</c>: los datos del alta y la <see cref="Version"/> que la pantalla leyó
/// (D-08, D-17). Números JSON estrictos; <see cref="Version"/> es <c>long?</c> para que una faltante sea un error en
/// <c>version</c> que informa el caso de uso, y una ilegible, un cuerpo ilegible en ese campo.
/// </summary>
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed record SolicitudModificacionArticulo(
    string? CodigoProveedor, string? Descripcion, decimal? PrecioCosto, decimal? MargenUtilidad, long? Version)
{
    /// <summary>Sin valores en <see cref="object.ToString"/>, como <see cref="SolicitudArticulo"/> (R-06).</summary>
    private bool PrintMembers(System.Text.StringBuilder constructor) => false;
}

/// <summary>
/// Cuerpo de <c>PUT /api/articulos/{codigo}/estado</c>: <c>{ "activo": true|false }</c> (D-11). <c>bool?</c>: uno
/// faltante es un error en <c>activo</c> que informa el caso de uso; uno que no es booleano, un cuerpo ilegible.
/// </summary>
public sealed record SolicitudEstadoArticulo(bool? Activo)
{
    /// <summary>Sin valores en <see cref="object.ToString"/>, como <see cref="SolicitudArticulo"/> (R-06).</summary>
    private bool PrintMembers(System.Text.StringBuilder constructor) => false;
}

/// <summary>
/// Una página del listado de <c>GET /api/articulos</c>: los artículos sin costo ni margen (D-12), la página devuelta,
/// el tamaño fijo de página (D-01) y cuántos coinciden en total.
/// </summary>
public sealed record ListadoArticulos(IReadOnlyList<ArticuloListado> Articulos, int Pagina, int TamanoPagina, int Total);

/// <summary>
/// Catálogo de artículos: alta y consulta (FEAT-002a); listado, modificación y cambio de estado (FEAT-002b). Solo
/// traduce HTTP: delega en los casos de uso, que validan, calculan el precio de venta y graban. Exige sesión por la
/// política por defecto (no está en <see cref="RutasAnonimas"/>, R-01): las respuestas traen costos y márgenes. Sin
/// sesión, 401 antes del 415 y del 413; el Content-Type y el tamaño del cuerpo los controla
/// <see cref="LimitesDelCuerpo"/> (R-02, R-05). No registra la query string ni el cuerpo de los pedidos (D-12, R-06).
/// Los traductores son cerrados por defecto: un resultado no previsto lanza y el manejador responde 500 genérico.
/// </summary>
[ApiController]
[Route("api/articulos")]
public sealed class ArticulosController(
    AltaArticulo altaArticulo,
    ObtenerArticulo obtenerArticulo,
    BuscarArticulos buscarArticulos,
    ModificarArticulo modificarArticulo,
    CambiarEstadoArticulo cambiarEstadoArticulo) : ControllerBase
{
    /// <summary>Tamaño máximo del cuerpo del POST y del PUT de datos; más grande, 413.</summary>
    public const long CuerpoMaximo = 1024;

    /// <summary>Tamaño máximo del cuerpo del PUT de estado; más grande, 413 (D-11).</summary>
    public const long CuerpoMaximoEstado = 64;

    private const string TituloNoEncontrado = "No se encontró el artículo.";

    /// <summary>Campos del cuerpo del alta, en el orden en que se busca el que nombra un error de lectura.</summary>
    private static readonly IReadOnlyList<string> Campos =
    [
        ValidacionArticulo.CampoCodigoProveedor,
        ValidacionArticulo.CampoDescripcion,
        ValidacionArticulo.CampoPrecioCosto,
        ValidacionArticulo.CampoMargenUtilidad,
    ];

    /// <summary>Los del alta y <c>version</c>: un <c>version</c> ilegible cae en ese campo (D-17).</summary>
    private static readonly IReadOnlyList<string> CamposModificacion = [.. Campos, ValidacionArticulo.CampoVersion];

    private static readonly IReadOnlyList<string> CamposEstado = [ValidacionArticulo.CampoActivo];

    /// <summary>
    /// Una página del catálogo, de a 50 por descripción (D-01), filtrada por <paramref name="texto"/> sin distinguir
    /// mayúsculas ni acentos (FR-04). Una página ilegible (no entera o fuera de un entero) responde 400 con el texto fijo
    /// de D-07 en <c>pagina</c>, sin el valor recibido; el texto y el rango los valida el caso de uso.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] string? texto, [FromQuery] int? pagina, CancellationToken cancelacion)
    {
        if (!ModelState.IsValid)
        {
            // El único parámetro que el enlace puede no leer es la página: un texto siempre se lee. Un error en
            // cualquier otra clave no se disfraza de error de página: lanza, y el manejador responde 500 genérico.
            var claves = ModelState
                .Where(entrada => entrada.Value is { Errors.Count: > 0 })
                .Select(entrada => entrada.Key)
                .ToList();
            if (!claves.All(clave => string.Equals(clave, ValidacionArticulo.CampoPagina, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Error de enlace del listado de artículos fuera de la página.");
            }

            return this.Invalido(
                new ErrorValidacion(ValidacionArticulo.CampoPagina, ValidacionArticulo.AccionPaginaFueraDeRango));
        }

        return ResponderBusqueda(await buscarArticulos.EjecutarAsync(texto, pagina, cancelacion: cancelacion));
    }

    /// <summary>El artículo con ese código: 200, o 404 con ProblemDetails sin detalles (R-06).</summary>
    [HttpGet("{codigo:long}")]
    public async Task<ActionResult<ArticuloGrabado>> Obtener(long codigo, CancellationToken cancelacion)
    {
        if (await obtenerArticulo.EjecutarAsync(codigo, cancelacion) is { } articulo)
        {
            return articulo;
        }

        return NoEncontrado();
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
    /// Modifica los datos de un artículo con la versión que leyó la pantalla y responde 200 con el artículo grabado
    /// (FR-01, FR-03). Un cuerpo ilegible responde 400 con un solo error y la acción fija del alta; datos inválidos, un
    /// duplicado o una versión faltante, 400 con todos los campos; inexistente, 404; inactivo o cambiado por otra
    /// pestaña, 409 con su <c>motivo</c> (D-02, D-08, D-19).
    /// </summary>
    [HttpPut("{codigo:long}")]
    [RequestSizeLimit(CuerpoMaximo)]
    public async Task<IActionResult> Modificar(
        long codigo, [FromBody] SolicitudModificacionArticulo? solicitud, CancellationToken cancelacion)
    {
        if (!ModelState.IsValid)
        {
            return this.CuerpoIlegible(
                RespuestasValidacion.AccionCuerpoIlegibleConfiguracion,
                ValidacionArticulo.CampoCodigoProveedor,
                CamposModificacion);
        }

        var resultado = await modificarArticulo.EjecutarAsync(
            codigo,
            new DatosArticulo(
                solicitud?.CodigoProveedor, solicitud?.Descripcion, solicitud?.PrecioCosto, solicitud?.MargenUtilidad),
            solicitud?.Version,
            cancelacion);
        return ResponderModificacion(resultado);
    }

    /// <summary>
    /// Desactiva o reactiva un artículo y responde 200 con el artículo, en la misma forma que el PUT de datos (FR-05,
    /// FR-06). Un cuerpo ilegible o sin <c>activo</c>, 400 en <c>activo</c>; inexistente, 404; cambiado por otra
    /// escritura durante la grabación, 409 con <c>motivo</c> <c>cambiado</c> (D-16).
    /// </summary>
    [HttpPut("{codigo:long}/estado")]
    [RequestSizeLimit(CuerpoMaximoEstado)]
    public async Task<IActionResult> CambiarEstado(
        long codigo, [FromBody] SolicitudEstadoArticulo? solicitud, CancellationToken cancelacion)
    {
        if (!ModelState.IsValid)
        {
            return this.CuerpoIlegible(
                RespuestasValidacion.AccionCuerpoIlegibleConfiguracion, ValidacionArticulo.CampoActivo, CamposEstado);
        }

        return ResponderCambioEstado(
            await cambiarEstadoArticulo.EjecutarAsync(codigo, solicitud?.Activo, cancelacion));
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

    /// <summary>
    /// Traduce el resultado de la búsqueda. Cerrado por defecto: solo una búsqueda válida responde 200 con la página y
    /// solo una inválida con errores, 400; cualquier otro resultado lanza.
    /// </summary>
    internal IActionResult ResponderBusqueda(ResultadoBusqueda? resultado) => resultado switch
    {
        { EsValido: true } =>
            Ok(new ListadoArticulos(resultado.Articulos, resultado.Pagina, resultado.TamanoPagina, resultado.Total)),
        { EsValido: false, Errores.Count: > 0 } => this.Invalido([.. resultado.Errores]),
        _ => throw new InvalidOperationException("Resultado de la búsqueda de artículos no previsto."),
    };

    /// <summary>
    /// Traduce el resultado de la modificación. Cerrado por defecto: grabado, 200; inválido con errores, 400; no
    /// encontrado, 404; inactivo o cambiado con su texto, 409 con <c>motivo</c> <c>inactivo</c> o <c>cambiado</c>
    /// (D-19). Cualquier otro resultado lanza.
    /// </summary>
    internal IActionResult ResponderModificacion(ResultadoEdicionArticulo? resultado) => resultado switch
    {
        { EsGrabado: true, Articulo: { } articulo } => Ok(articulo),
        { Tipo: TipoResultadoEdicion.Invalido, Errores.Count: > 0 } => this.Invalido([.. resultado.Errores]),
        { Tipo: TipoResultadoEdicion.NoEncontrado } => NoEncontrado(),
        { Tipo: TipoResultadoEdicion.Inactivo, Detalle: { } detalle } =>
            this.ConflictoArticulo(RespuestasValidacion.MotivoInactivo, detalle),
        { Tipo: TipoResultadoEdicion.Cambiado, Detalle: { } detalle } =>
            this.ConflictoArticulo(RespuestasValidacion.MotivoCambiado, detalle),
        _ => throw new InvalidOperationException("Resultado de la modificación de un artículo no previsto."),
    };

    /// <summary>
    /// Traduce el resultado del cambio de estado. Cerrado por defecto: grabado, 200; inválido con errores, 400; no
    /// encontrado, 404; cambiado con su texto, 409 con <c>motivo</c> <c>cambiado</c> (D-16). Inactivo no es un resultado
    /// de este caso de uso (desactivar y reactivar no dependen del estado) y, como cualquier otro, lanza.
    /// </summary>
    internal IActionResult ResponderCambioEstado(ResultadoEdicionArticulo? resultado) => resultado switch
    {
        { EsGrabado: true, Articulo: { } articulo } => Ok(articulo),
        { Tipo: TipoResultadoEdicion.Invalido, Errores.Count: > 0 } => this.Invalido([.. resultado.Errores]),
        { Tipo: TipoResultadoEdicion.NoEncontrado } => NoEncontrado(),
        { Tipo: TipoResultadoEdicion.Cambiado, Detalle: { } detalle } =>
            this.ConflictoArticulo(RespuestasValidacion.MotivoCambiado, detalle),
        _ => throw new InvalidOperationException("Resultado del cambio de estado de un artículo no previsto."),
    };

    /// <summary>404 con ProblemDetails sin <c>detail</c> ni datos del pedido (R-06).</summary>
    private ObjectResult NoEncontrado() => Problem(statusCode: StatusCodes.Status404NotFound, title: TituloNoEncontrado);
}
