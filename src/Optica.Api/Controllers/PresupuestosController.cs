using System.Globalization;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Optica.Api.Acceso;
using Optica.Api.Errores;
using Optica.Application.Comun;
using Optica.Application.Facturacion;
using Optica.Application.Presupuestos;

namespace Optica.Api.Controllers;

/// <summary>
/// El cliente del cuerpo de <c>POST /api/presupuestos</c>: los seis datos como <c>string?</c>, para que uno faltante
/// sea un error del campo que informa el caso de uso y uno que no es texto (por ejemplo, un DNI enviado como número),
/// un cuerpo ilegible en ese campo.
/// </summary>
/// <remarks>
/// Datos personales (Ley 25.326): <see cref="object.ToString"/> no muestra ningún valor (<c>SolicitudCliente { }</c>).
/// MVC registra en Trace los argumentos de la acción con su <c>ToString()</c>, y el de un record mostraría el apellido,
/// el DNI o el email (D-17). Vale para las tres solicitudes de este archivo.
/// </remarks>
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed record SolicitudCliente(
    string? Apellido, string? Nombre, string? Dni, string? Domicilio, string? Email, string? Telefono)
{
    private bool PrintMembers(System.Text.StringBuilder constructor) => false;
}

/// <summary>
/// Una línea del cuerpo: el código autonumérico del artículo (D-12, D-13), la Cantidad (entero) y el Porcentaje de
/// Descuento (FR-01 a FR-03), como números JSON estrictos: un texto, aunque tenga dígitos, o un 2.5 en la cantidad no se
/// leen (D-07). Los tres son nullables para que uno ausente sea un error del campo que informa el caso de uso. No tiene
/// precios ni descripción: los pone el servidor, y si llegan en el cuerpo no se leen (FR-11, R-01). La cantidad la lee
/// <see cref="LectorCantidadJson"/>: un entero que no entra en un <c>int</c> se rechaza como cantidad fuera de rango
/// (D-01), no como cuerpo ilegible. En la modificación (FEAT-003c, D-03), <see cref="Orden"/> opcional identifica la
/// línea grabada que se conserva; el alta no lo usa.
/// </summary>
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed record SolicitudLinea(
    long? CodigoArticulo,
    [property: JsonConverter(typeof(LectorCantidadJson))] int? Cantidad,
    decimal? PorcentajeDescuento,
    int? Orden = null)
{
    /// <summary>Sin valores en <see cref="object.ToString"/>, como <see cref="SolicitudCliente"/> (D-17).</summary>
    private bool PrintMembers(System.Text.StringBuilder constructor) => false;
}

/// <summary>Cuerpo de <c>POST /api/presupuestos</c>: el cliente y las líneas en el orden cargado.</summary>
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed record SolicitudPresupuesto(SolicitudCliente? Cliente, IReadOnlyList<SolicitudLinea?>? Lineas)
{
    /// <summary>Lleva el cliente: sin valores en <see cref="object.ToString"/> (D-17).</summary>
    private bool PrintMembers(System.Text.StringBuilder constructor) => false;
}

/// <summary>
/// Cuerpo de <c>PUT /api/presupuestos/{numero}</c> (FEAT-003c, D-05): la <see cref="Version"/> que leyó la pantalla, el
/// cliente y las líneas en el orden en que quedan. Números JSON estrictos; <see cref="Version"/> es <c>long?</c> para que
/// una faltante sea un error en <c>version</c> que informa el caso de uso, y una ilegible, un cuerpo ilegible en ese campo.
/// </summary>
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed record SolicitudModificacionPresupuesto(
    long? Version, SolicitudCliente? Cliente, IReadOnlyList<SolicitudLinea?>? Lineas)
{
    /// <summary>Lleva el cliente: sin valores en <see cref="object.ToString"/> (D-17 de FEAT-003a).</summary>
    private bool PrintMembers(System.Text.StringBuilder constructor) => false;
}

/// <summary>
/// Cuerpo de <c>PUT /api/presupuestos/{numero}/estado</c> (FEAT-003c, D-05): <c>{ "estado": "final", "version": n }</c>.
/// Un <c>estado</c> que no es texto, o una versión que no es un entero, es un cuerpo ilegible en su campo.
/// </summary>
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed record SolicitudEstadoPresupuesto(string? Estado, long? Version)
{
    /// <summary>Sin valores en <see cref="object.ToString"/>, como las demás solicitudes (D-17 de FEAT-003a).</summary>
    private bool PrintMembers(System.Text.StringBuilder constructor) => false;
}

/// <summary>
/// Cuerpo de <c>POST /api/presupuestos/busqueda</c> (FEAT-006, ADR-019): los filtros viajan en el cuerpo, nunca en la
/// URL. Todos son opcionales: Apellido, Nombre y DNI como texto, Desde y Hasta como texto <c>yyyy-MM-dd</c> (los valida
/// el caso de uso) y la página como entero; ausente vale 1. Números JSON estrictos: una página como texto o decimal, o un
/// texto enviado como número, es un cuerpo ilegible en su campo.
/// </summary>
/// <remarks>Datos personales (Ley 25.326): <see cref="object.ToString"/> no muestra ningún valor, como las demás.</remarks>
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed record SolicitudBusquedaPresupuestos(
    string? Apellido, string? Nombre, string? Dni, string? Desde, string? Hasta, int? Pagina)
{
    private bool PrintMembers(System.Text.StringBuilder constructor) => false;
}

/// <summary>
/// Cuerpo de <c>POST /api/presupuestos/{numero}/factura</c> (FEAT-007, D-18): <c>{ "concepto": "productos" |
/// "productos_y_servicios" }</c>. Es lo único que manda la pantalla: tipo, importes, receptor y fecha los arma el
/// servidor (R-06). Un concepto que no es texto es un cuerpo ilegible; uno faltante o desconocido lo informa el caso de
/// uso.
/// </summary>
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed record SolicitudFactura(string? Concepto)
{
    /// <summary>Sin valores en <see cref="object.ToString"/>, como las demás solicitudes (D-17 de FEAT-003a).</summary>
    private bool PrintMembers(System.Text.StringBuilder constructor) => false;
}

/// <summary>
/// La factura de un presupuesto en su GET (FR-20, D-19): solo el comprobante, "Factura B 0003-00000125".
/// </summary>
public sealed record FacturaDelPresupuesto(string Comprobante);

/// <summary>
/// La respuesta del GET de un presupuesto (D-28): los mismos datos que <see cref="PresupuestoGrabado"/>, en el mismo
/// orden, más <see cref="Factura"/> (null sin factura) y <see cref="EmisionPendiente"/> (FR-20, FR-24). La combina la
/// Api: Presupuestos no conoce Facturación. El alta y los PUT siguen respondiendo <see cref="PresupuestoGrabado"/>.
/// </summary>
/// <remarks>Lleva los datos del cliente: <see cref="object.ToString"/> no muestra ningún valor (D-17).</remarks>
public sealed record PresupuestoConFacturacion(
    long Numero,
    DateOnly Fecha,
    string Estado,
    ClienteGrabado Cliente,
    IReadOnlyList<LineaGrabada> Lineas,
    decimal Total,
    long Version,
    FacturaDelPresupuesto? Factura,
    bool EmisionPendiente)
{
    private bool PrintMembers(System.Text.StringBuilder constructor) => false;

    /// <summary>El presupuesto con su estado de facturación.</summary>
    public static PresupuestoConFacturacion Desde(PresupuestoGrabado presupuesto, EstadoFacturacion estado) => new(
        presupuesto.Numero,
        presupuesto.Fecha,
        presupuesto.Estado,
        presupuesto.Cliente,
        presupuesto.Lineas,
        presupuesto.Total,
        presupuesto.Version,
        estado.Comprobante is { } comprobante ? new FacturaDelPresupuesto(comprobante) : null,
        estado.EmisionPendiente);
}

/// <summary>
/// Respuesta 200 de la búsqueda de presupuestos: <c>{ presupuestos, pagina, tamanoPagina, total }</c>. Una página
/// posterior a la última trae la lista vacía y el total.
/// </summary>
/// <remarks>Lleva Apellido, Nombre y DNI en los resultados: <see cref="object.ToString"/> no muestra ningún valor.</remarks>
public sealed record ListadoPresupuestos(
    IReadOnlyList<PresupuestoListado> Presupuestos, int Pagina, int TamanoPagina, int Total)
{
    private bool PrintMembers(System.Text.StringBuilder constructor) => false;
}

/// <summary>
/// Presupuestos: alta en Borrador y consulta por número (FEAT-003a); modificación en Borrador y paso a Final
/// (FEAT-003c), con 409 <c>cambiado</c> o <c>final</c>; búsqueda y PDF de un Final (FEAT-006), con 409 <c>borrador</c>
/// para el PDF de un Borrador; emisión de la factura y estado de facturación en el GET (FEAT-007), con 409 y su
/// <c>motivo</c> para los rechazos y los desenlaces de ARCA. Solo traduce HTTP: delega en los casos de uso, que validan, leen
/// los artículos, numeran y graban. Exige sesión por la política por defecto (no está en
/// <see cref="RutasAnonimas"/>): las respuestas traen datos personales del cliente (D-17). Sin sesión, 401 antes del 415
/// y del 413; el Content-Type y el tamaño del cuerpo los controla <see cref="LimitesDelCuerpo"/> (D-16). No registra el
/// cuerpo ni la query string; los errores son textos fijos que no repiten lo enviado, y ningún dato personal viaja en la
/// ruta: el GET y los PUT son por número. Los traductores son cerrados por defecto: un resultado no previsto lanza y el
/// manejador responde 500 genérico, sin datos del pedido.
/// </summary>
/// <remarks>
/// La respuesta es <see cref="PresupuestoGrabado"/> tal cual, como en Catálogo: el estado ya llega como texto en
/// minúsculas (<c>borrador</c>, <c>final</c>, traducido en Application) y la fecha se escribe como <c>yyyy-MM-dd</c>.
/// Una falla al confirmar, incluida una <c>DbUpdateException</c>, no se atiende ni se serializa: llega al manejador de
/// errores (D-30).
/// </remarks>
[ApiController]
[Route("api/presupuestos")]
public sealed class PresupuestosController(
    AltaPresupuesto altaPresupuesto,
    ObtenerPresupuesto obtenerPresupuesto,
    ModificarPresupuesto modificarPresupuesto,
    PasarPresupuestoAFinal pasarPresupuestoAFinal,
    BuscarPresupuestos buscarPresupuestos,
    GenerarPdfPresupuesto generarPdfPresupuesto,
    EmitirFactura emitirFactura,
    ObtenerEstadoFacturacion obtenerEstadoFacturacion) : ControllerBase
{
    /// <summary>Tamaño máximo del cuerpo de la emisión de la factura (FEAT-007); más grande, 413.</summary>
    public const long CuerpoMaximoFactura = 1024;

    /// <summary>
    /// Emite la factura a consumidor final del presupuesto (FEAT-007, FR-01, D-18) y responde 201 con la factura y
    /// <c>Location</c> hacia el GET del presupuesto. Un cuerpo ilegible responde 400 en <c>concepto</c> con
    /// <see cref="RespuestasValidacion.AccionCuerpoIlegibleFactura"/>; un concepto faltante o desconocido, 400 en
    /// <c>concepto</c>; inexistente, 404; Borrador, facturado, pendiente, configuración incompleta y los desenlaces de
    /// ARCA, 409 con su <c>motivo</c> (ADR-021). La emisión no se cancela si el navegador corta el pedido (D-32): el
    /// token del pedido solo cubre la primera transacción.
    /// </summary>
    [HttpPost("{numero:long}/factura")]
    [RequestSizeLimit(CuerpoMaximoFactura)]
    public async Task<IActionResult> Facturar(
        long numero, [FromBody] SolicitudFactura? solicitud, CancellationToken cancelacion)
    {
        if (!ModelState.IsValid)
        {
            return this.Invalido(new ErrorValidacion(
                EmitirFactura.CampoConcepto, RespuestasValidacion.AccionCuerpoIlegibleFactura));
        }

        return ResponderEmision(numero, await emitirFactura.EjecutarAsync(numero, solicitud?.Concepto, cancelacion));
    }

    /// <summary>
    /// Traduce el resultado de la emisión. Cerrado por defecto: emitida, 201 con la factura; inválida con errores, 400;
    /// no encontrado, 404; conflicto con su motivo, 409 (<see cref="RespuestasValidacion.ConflictoFactura"/>). Cualquier
    /// otro resultado lanza, y el manejador de errores responde 500 genérico.
    /// </summary>
    internal IActionResult ResponderEmision(long numero, ResultadoEmision? resultado) => resultado switch
    {
        { EsEmitida: true, Factura: var factura } => CreatedAtAction(nameof(Obtener), new { numero }, factura),
        { Tipo: TipoResultadoEmision.Invalido, Errores.Count: > 0 } => this.Invalido([.. resultado.Errores]),
        { Tipo: TipoResultadoEmision.NoEncontrado } =>
            Problem(statusCode: StatusCodes.Status404NotFound, title: TituloNoEncontrado),
        { Tipo: TipoResultadoEmision.Conflicto, Motivo: { } motivo } =>
            this.ConflictoFactura(TextoDelMotivo(motivo), resultado.CodigoArca, resultado.DescripcionArca),
        _ => throw new InvalidOperationException("Resultado de la emisión de una factura no previsto."),
    };

    /// <summary>El texto de la extensión <c>motivo</c> de cada motivo de la aplicación (D-18, D-31).</summary>
    private static string TextoDelMotivo(MotivoConflictoEmision motivo) => motivo switch
    {
        MotivoConflictoEmision.Borrador => RespuestasValidacion.MotivoBorrador,
        MotivoConflictoEmision.Facturado => RespuestasValidacion.MotivoFacturado,
        MotivoConflictoEmision.Pendiente => RespuestasValidacion.MotivoPendiente,
        MotivoConflictoEmision.SinTope => RespuestasValidacion.MotivoSinTope,
        MotivoConflictoEmision.SinPuntoVenta => RespuestasValidacion.MotivoSinPuntoVenta,
        MotivoConflictoEmision.AlicuotaNoAdmitida => RespuestasValidacion.MotivoAlicuotaNoAdmitida,
        MotivoConflictoEmision.Rechazada => RespuestasValidacion.MotivoRechazada,
        MotivoConflictoEmision.SinRespuesta => RespuestasValidacion.MotivoSinRespuesta,
        MotivoConflictoEmision.NoEnviada => RespuestasValidacion.MotivoNoEnviada,
        MotivoConflictoEmision.PendienteVerificacion => RespuestasValidacion.MotivoPendienteVerificacion,
        _ => throw new InvalidOperationException("Motivo del conflicto de la emisión no previsto."),
    };

    /// <summary>El tipo de contenido del PDF del presupuesto (FEAT-006).</summary>
    private const string TipoPdf = "application/pdf";

    /// <summary>Tamaño máximo del cuerpo del POST y de los dos PUT (D-16); más grande, 413.</summary>
    public const long CuerpoMaximo = 16 * 1024;

    /// <summary>Tamaño máximo del cuerpo de la búsqueda (FEAT-006, ADR-019); más grande, 413.</summary>
    public const long CuerpoMaximoBusqueda = 4 * 1024;

    /// <summary>Los campos del cuerpo de la búsqueda, en el orden en que se buscan en un error de lectura.</summary>
    private static readonly IReadOnlyList<string> CamposBusqueda =
    [
        ValidacionPresupuesto.CampoApellido,
        ValidacionPresupuesto.CampoNombre,
        ValidacionPresupuesto.CampoDni,
        ValidacionPresupuesto.CampoDesde,
        ValidacionPresupuesto.CampoHasta,
        ValidacionPresupuesto.CampoPagina,
    ];

    /// <summary>El <c>title</c> fijo del 404 de un presupuesto inexistente (D-24).</summary>
    private const string TituloNoEncontrado = "No se encontró el presupuesto.";

    /// <summary>El prefijo del cliente en la clave de un error de lectura (<c>$.cliente.dni</c>).</summary>
    private const string PrefijoCliente = "cliente.";

    /// <summary>La propiedad JSON de la cantidad en una línea (<c>$.lineas[i].cantidad</c>).</summary>
    private const string PropiedadCantidad = "cantidad";

    /// <summary>La propiedad JSON del descuento en una línea (<c>$.lineas[i].porcentajeDescuento</c>).</summary>
    private const string PropiedadDescuento = "porcentajeDescuento";

    /// <summary>La propiedad JSON del orden en una línea (<c>$.lineas[i].orden</c>, FEAT-003c).</summary>
    private const string PropiedadOrden = "orden";

    /// <summary>Los campos del cuerpo del paso a Final, en el orden en que se buscan en un error de lectura.</summary>
    private static readonly IReadOnlyList<string> CamposEstado =
        [ValidacionPresupuesto.CampoEstado, ValidacionPresupuesto.CampoVersion];

    /// <summary>Los seis campos del cliente (D-20), en el orden en que se buscan en un error de lectura.</summary>
    private static readonly IReadOnlyList<string> CamposCliente =
    [
        ValidacionPresupuesto.CampoApellido,
        ValidacionPresupuesto.CampoNombre,
        ValidacionPresupuesto.CampoDni,
        ValidacionPresupuesto.CampoDomicilio,
        ValidacionPresupuesto.CampoEmail,
        ValidacionPresupuesto.CampoTelefono,
    ];

    /// <summary>
    /// Da de alta un presupuesto en Borrador y responde 201 con el presupuesto grabado y <c>Location</c> hacia su GET.
    /// Un cuerpo ilegible responde 400 con un solo error y una acción fija, sin los mensajes del lector de JSON ni el
    /// valor; datos inválidos, sin líneas o una línea rechazada, 400 con todos los errores del caso de uso (D-20).
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(CuerpoMaximo)]
    public async Task<IActionResult> Grabar([FromBody] SolicitudPresupuesto? solicitud, CancellationToken cancelacion)
    {
        if (!ModelState.IsValid)
        {
            return CuerpoIlegiblePresupuesto();
        }

        // El alta no usa el orden de las líneas: no se pasa.
        var datos = Datos(solicitud?.Cliente, solicitud?.Lineas, conOrden: false);
        return ResponderAlta(await altaPresupuesto.EjecutarAsync(datos, cancelacion));
    }

    /// <summary>
    /// Modifica el cliente y las líneas de un presupuesto en Borrador con la versión que leyó la pantalla y responde 200
    /// con el presupuesto releído (FR-01, FR-02, FR-07 de FEAT-003c). Un cuerpo ilegible responde 400 con un solo error y
    /// la acción fija del alta; datos inválidos, una línea ajena o rechazada o una versión faltante, 400 con todos los
    /// campos; inexistente, 404; en Final o cambiado por otra sesión, 409 con su <c>motivo</c> (D-05).
    /// </summary>
    [HttpPut("{numero:long}")]
    [RequestSizeLimit(CuerpoMaximo)]
    public async Task<IActionResult> Modificar(
        long numero, [FromBody] SolicitudModificacionPresupuesto? solicitud, CancellationToken cancelacion)
    {
        if (!ModelState.IsValid)
        {
            return CuerpoIlegiblePresupuesto();
        }

        var datos = Datos(solicitud?.Cliente, solicitud?.Lineas, conOrden: true);
        return ResponderModificacion(
            await modificarPresupuesto.EjecutarAsync(numero, datos, solicitud?.Version, cancelacion));
    }

    /// <summary>
    /// Pasa un presupuesto en Borrador a Final y responde 200 con el presupuesto releído (FR-03, FR-06 de FEAT-003c). Un
    /// cuerpo ilegible, 400 en el campo que nombra el lector (<c>estado</c> si no nombra ninguno); un <c>estado</c> que
    /// no es "final" o una versión faltante, 400; inexistente, 404; en Final (también con "borrador", AC-08) o cambiado,
    /// 409 con su <c>motivo</c>.
    /// </summary>
    [HttpPut("{numero:long}/estado")]
    [RequestSizeLimit(CuerpoMaximo)]
    public async Task<IActionResult> CambiarEstado(
        long numero, [FromBody] SolicitudEstadoPresupuesto? solicitud, CancellationToken cancelacion)
    {
        if (!ModelState.IsValid)
        {
            return this.CuerpoIlegible(
                RespuestasValidacion.AccionCuerpoIlegibleConfiguracion, ValidacionPresupuesto.CampoEstado, CamposEstado);
        }

        return ResponderPasoAFinal(
            await pasarPresupuestoAFinal.EjecutarAsync(numero, solicitud?.Estado, solicitud?.Version, cancelacion));
    }

    /// <summary>
    /// Busca presupuestos por Apellido, Nombre, DNI y rango de fechas y responde 200 con la página de 50, por número de
    /// mayor a menor (FEAT-006, FR-01 a FR-09). Los filtros viajan en el cuerpo y no se registran (ADR-019). Un cuerpo
    /// ilegible (incluido uno vacío) responde 400 con un solo error y la acción fija de la búsqueda
    /// (<see cref="RespuestasValidacion.AccionCuerpoIlegibleBusqueda"/>), en el campo que nombra el
    /// lector o en <c>apellido</c>, sin el valor; fechas inválidas, Desde posterior a Hasta, textos largos o una página
    /// fuera de rango, 400 con todos los errores del caso de uso. Sin sesión, 401 antes de leer el cuerpo.
    /// </summary>
    [HttpPost("busqueda")]
    [RequestSizeLimit(CuerpoMaximoBusqueda)]
    public async Task<IActionResult> Buscar(
        [FromBody] SolicitudBusquedaPresupuestos? solicitud, CancellationToken cancelacion)
    {
        if (!ModelState.IsValid)
        {
            return this.CuerpoIlegible(
                RespuestasValidacion.AccionCuerpoIlegibleBusqueda, ValidacionPresupuesto.CampoApellido, CamposBusqueda);
        }

        return ResponderBusqueda(await buscarPresupuestos.EjecutarAsync(
            solicitud?.Apellido,
            solicitud?.Nombre,
            solicitud?.Dni,
            solicitud?.Desde,
            solicitud?.Hasta,
            solicitud?.Pagina,
            cancelacion));
    }

    /// <summary>
    /// Los datos del caso de uso: el cliente y las líneas tal como llegaron (ausentes, null). Con
    /// <paramref name="conOrden"/>, cada línea lleva su <c>orden</c>.
    /// </summary>
    private static DatosPresupuesto Datos(
        SolicitudCliente? cliente, IReadOnlyList<SolicitudLinea?>? lineas, bool conOrden)
    {
        var datosCliente = cliente is null
            ? null
            : new DatosCliente(cliente.Apellido, cliente.Nombre, cliente.Dni, cliente.Domicilio, cliente.Email, cliente.Telefono);
        IReadOnlyList<DatosLinea?>? datosLineas = lineas is null
            ? null
            : [.. lineas.Select(linea => linea is null
                ? null
                : new DatosLinea(
                    linea.CodigoArticulo, linea.Cantidad, linea.PorcentajeDescuento, conOrden ? linea.Orden : null))];
        return new DatosPresupuesto(datosCliente, datosLineas);
    }

    /// <summary>
    /// El presupuesto con ese número: 200 con su factura y su emisión pendiente (FEAT-007, FR-20, D-28), o 404 con
    /// ProblemDetails de título fijo y sin detalles.
    /// </summary>
    [HttpGet("{numero:long}")]
    public async Task<IActionResult> Obtener(long numero, CancellationToken cancelacion)
    {
        if (await obtenerPresupuesto.EjecutarAsync(numero, cancelacion) is { } presupuesto)
        {
            var estado = await obtenerEstadoFacturacion.EjecutarAsync(numero, cancelacion);
            return Ok(PresupuestoConFacturacion.Desde(presupuesto, estado));
        }

        return Problem(statusCode: StatusCodes.Status404NotFound, title: TituloNoEncontrado);
    }

    /// <summary>
    /// El PDF del presupuesto en Final (FEAT-006, FR-11): 200 <c>application/pdf</c> como adjunto
    /// <c>presupuesto-{numero}.pdf</c>, sin datos del cliente en el nombre (R-02). Inexistente, 404 con el título fijo; en
    /// Borrador, 409 <c>borrador</c> sin generar el PDF (FR-12, AC-12). Una falla del generador llega al manejador de
    /// errores, que responde el 500 genérico. Sin sesión, 401; la respuesta lleva <c>no-store</c> como todo <c>/api</c>.
    /// </summary>
    [HttpGet("{numero:long}/pdf")]
    public async Task<IActionResult> DescargarPdf(long numero, CancellationToken cancelacion) =>
        ResponderPdf(numero, await generarPdfPresupuesto.EjecutarAsync(numero, cancelacion));

    /// <summary>
    /// Traduce el resultado del PDF. Cerrado por defecto: generado, 200 con el archivo; no encontrado, 404; Borrador con
    /// su texto, 409 <c>borrador</c>. Cualquier otro resultado lanza, y el manejador de errores responde 500 genérico.
    /// </summary>
    internal IActionResult ResponderPdf(long numero, ResultadoPdfPresupuesto? resultado) => resultado switch
    {
        { EsGenerado: true, Contenido: var contenido } =>
            File(contenido, TipoPdf, string.Create(CultureInfo.InvariantCulture, $"presupuesto-{numero}.pdf")),
        { Tipo: TipoPdfPresupuesto.NoEncontrado } =>
            Problem(statusCode: StatusCodes.Status404NotFound, title: TituloNoEncontrado),
        { Tipo: TipoPdfPresupuesto.Borrador, Detalle: { } detalle } =>
            this.ConflictoPresupuesto(RespuestasValidacion.MotivoBorrador, detalle),
        _ => throw new InvalidOperationException("Resultado del PDF de un presupuesto no previsto."),
    };

    /// <summary>
    /// Traduce el resultado del alta. Cerrado por defecto: solo un alta grabada responde 201 y solo una inválida con
    /// errores, 400; cualquier otro resultado lanza, y el manejador de errores responde 500 genérico.
    /// </summary>
    internal IActionResult ResponderAlta(ResultadoAltaPresupuesto? resultado) => resultado switch
    {
        { EsGrabado: true, Presupuesto: var presupuesto } =>
            CreatedAtAction(nameof(Obtener), new { numero = presupuesto.Numero }, presupuesto),
        { EsGrabado: false, Errores.Count: > 0 } => this.Invalido([.. resultado.Errores]),
        _ => throw new InvalidOperationException("Resultado del alta de un presupuesto no previsto."),
    };

    /// <summary>
    /// Traduce el resultado de la búsqueda (FEAT-006). Cerrado por defecto: solo una búsqueda válida responde 200 con la
    /// página, y solo una inválida con errores, 400 con todos; cualquier otro resultado lanza, y el manejador de errores
    /// responde 500 genérico.
    /// </summary>
    internal IActionResult ResponderBusqueda(ResultadoBusquedaPresupuestos? resultado) => resultado switch
    {
        { EsValido: true } => Ok(new ListadoPresupuestos(
            resultado.Presupuestos, resultado.Pagina, resultado.TamanoPagina, resultado.Total)),
        { EsValido: false, Errores.Count: > 0 } => this.Invalido([.. resultado.Errores]),
        _ => throw new InvalidOperationException("Resultado de la búsqueda de presupuestos no previsto."),
    };

    /// <summary>
    /// Traduce el resultado de la modificación. Cerrado por defecto: grabado, 200; inválido con errores, 400; no
    /// encontrado, 404; final o cambiado con su texto, 409 con <c>motivo</c> <c>final</c> o <c>cambiado</c> (D-05).
    /// Cualquier otro resultado lanza.
    /// </summary>
    internal IActionResult ResponderModificacion(ResultadoEdicionPresupuesto? resultado) =>
        ResponderEdicion(resultado) ?? throw new InvalidOperationException(
            "Resultado de la modificación de un presupuesto no previsto.");

    /// <summary>
    /// Traduce el resultado del paso a Final, con las mismas respuestas que <see cref="ResponderModificacion"/>.
    /// Cualquier otro resultado lanza.
    /// </summary>
    internal IActionResult ResponderPasoAFinal(ResultadoEdicionPresupuesto? resultado) =>
        ResponderEdicion(resultado) ?? throw new InvalidOperationException(
            "Resultado del paso a Final de un presupuesto no previsto.");

    /// <summary>La respuesta de un resultado previsto de los dos PUT, o null si no es ninguno.</summary>
    private IActionResult? ResponderEdicion(ResultadoEdicionPresupuesto? resultado) => resultado switch
    {
        { EsGrabado: true, Presupuesto: { } presupuesto } => Ok(presupuesto),
        { Tipo: TipoEdicionPresupuesto.Invalido, Errores.Count: > 0 } => this.Invalido([.. resultado.Errores]),
        { Tipo: TipoEdicionPresupuesto.NoEncontrado } =>
            Problem(statusCode: StatusCodes.Status404NotFound, title: TituloNoEncontrado),
        { Tipo: TipoEdicionPresupuesto.Final, Detalle: { } detalle } =>
            this.ConflictoPresupuesto(RespuestasValidacion.MotivoFinal, detalle),
        { Tipo: TipoEdicionPresupuesto.Cambiado, Detalle: { } detalle } =>
            this.ConflictoPresupuesto(RespuestasValidacion.MotivoCambiado, detalle),
        _ => null,
    };

    /// <summary>
    /// 400 para un cuerpo ilegible, en el campo que nombra la primera clave con error: <c>$.cliente.dni</c> en
    /// <c>dni</c>, <c>$.lineas[1].codigoArticulo</c> en <c>lineas[1]</c>, <c>$.lineas[1].cantidad</c> en
    /// <c>lineas[1].cantidad</c>, <c>$.lineas</c> en <c>lineas</c>; si ninguna nombra un campo (JSON roto al empezar,
    /// un cuerpo que no es un objeto), en <c>apellido</c>. La acción es fija: la de AC-02 en la cantidad, la de AC-03 en
    /// el descuento (D-07) y, en los demás campos, la de los cuerpos con valores
    /// (<see cref="RespuestasValidacion.AccionCuerpoIlegibleConfiguracion"/>). Solo se miran las claves, nunca los
    /// mensajes ni los valores (R-04).
    /// </summary>
    /// <remarks>
    /// Se llama distinto de la extensión <see cref="RespuestasValidacion.CuerpoIlegible(ControllerBase, string, string[])"/>
    /// para no taparla: esa compara la ruta completa de la clave y no entra en <c>cliente</c> ni en <c>lineas[i]</c>.
    /// </remarks>
    private ActionResult CuerpoIlegiblePresupuesto()
    {
        var error = ModelState
            .Where(entrada => entrada.Value is { Errors.Count: > 0 })
            .Select(entrada => ErrorDeLaClave(entrada.Key))
            .FirstOrDefault(candidato => candidato is not null)
            ?? new ErrorValidacion(
                ValidacionPresupuesto.CampoApellido, RespuestasValidacion.AccionCuerpoIlegibleConfiguracion);
        return this.Invalido(error);
    }

    /// <summary>El campo de D-20 y D-09 que nombra la clave de un error de lectura, o null si no nombra ninguno.</summary>
    internal static string? CampoDeLaClave(string clave) => ErrorDeLaClave(clave)?.Campo;

    /// <summary>
    /// El campo que nombra la clave de un error de lectura y la acción fija de ese campo, o null si no nombra ninguno.
    /// </summary>
    private static ErrorValidacion? ErrorDeLaClave(string clave)
    {
        var generica = RespuestasValidacion.AccionCuerpoIlegibleConfiguracion;
        var ruta = clave.StartsWith("$.", StringComparison.Ordinal) ? clave[2..] : clave;
        if (string.Equals(ruta, ValidacionPresupuesto.CampoVersion, StringComparison.OrdinalIgnoreCase))
        {
            // FEAT-003c: una versión que no es un entero, en su campo y con la acción fija de los cuerpos con valores.
            return new ErrorValidacion(ValidacionPresupuesto.CampoVersion, generica);
        }

        if (ruta.StartsWith(PrefijoCliente, StringComparison.OrdinalIgnoreCase))
        {
            var dato = ruta[PrefijoCliente.Length..];
            var campoCliente = CamposCliente.FirstOrDefault(candidato =>
                string.Equals(dato, candidato, StringComparison.OrdinalIgnoreCase));
            return campoCliente is null ? null : new ErrorValidacion(campoCliente, generica);
        }

        var lineas = ValidacionPresupuesto.CampoLineas;
        if (!ruta.StartsWith(lineas, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var resto = ruta[lineas.Length..];
        if (resto.Length == 0)
        {
            return new ErrorValidacion(lineas, generica);
        }

        // lineas[i] o lineas[i].propiedad: el campo de la línea i, si es una de las que se aceptan.
        var cierre = resto.IndexOf(']', StringComparison.Ordinal);
        if (resto[0] != '['
            || cierre < 2
            || (cierre + 1 < resto.Length && resto[cierre + 1] != '.')
            || !int.TryParse(resto.AsSpan(1, cierre - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var indice))
        {
            return null;
        }

        if (indice >= ValidacionPresupuesto.LineasMaximas)
        {
            return new ErrorValidacion(lineas, generica);
        }

        // D-09: la cantidad y el descuento tienen su campo y el texto de AC-02 o AC-03 (D-07); lo demás, la línea.
        var propiedad = cierre + 2 <= resto.Length ? resto[(cierre + 2)..] : string.Empty;
        if (string.Equals(propiedad, PropiedadCantidad, StringComparison.OrdinalIgnoreCase))
        {
            return new ErrorValidacion(
                ValidacionPresupuesto.CampoCantidad(indice), ValidacionPresupuesto.AccionCantidadInvalida);
        }

        if (string.Equals(propiedad, PropiedadDescuento, StringComparison.OrdinalIgnoreCase))
        {
            return new ErrorValidacion(
                ValidacionPresupuesto.CampoDescuento(indice), ValidacionPresupuesto.AccionDescuentoFueraDeRango);
        }

        if (string.Equals(propiedad, PropiedadOrden, StringComparison.OrdinalIgnoreCase))
        {
            // FEAT-003c: un orden que no es un entero, en lineas[i].orden con la acción fija de los cuerpos con valores.
            return new ErrorValidacion(
                string.Create(CultureInfo.InvariantCulture, $"{ValidacionPresupuesto.CampoLinea(indice)}.{PropiedadOrden}"),
                generica);
        }

        return new ErrorValidacion(ValidacionPresupuesto.CampoLinea(indice), generica);
    }
}
