using System.Globalization;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Optica.Api.Acceso;
using Optica.Api.Errores;
using Optica.Application.Comun;
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
/// Presupuestos: alta en Borrador y consulta por número (FEAT-003a); modificación en Borrador y paso a Final
/// (FEAT-003c), con 409 <c>cambiado</c> o <c>final</c>. Solo traduce HTTP: delega en los casos de uso, que validan, leen
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
    PasarPresupuestoAFinal pasarPresupuestoAFinal) : ControllerBase
{
    /// <summary>Tamaño máximo del cuerpo del POST y de los dos PUT (D-16); más grande, 413.</summary>
    public const long CuerpoMaximo = 16 * 1024;

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

    /// <summary>El presupuesto con ese número: 200, o 404 con ProblemDetails de título fijo y sin detalles.</summary>
    [HttpGet("{numero:long}")]
    public async Task<IActionResult> Obtener(long numero, CancellationToken cancelacion)
    {
        if (await obtenerPresupuesto.EjecutarAsync(numero, cancelacion) is { } presupuesto)
        {
            return Ok(presupuesto);
        }

        return Problem(statusCode: StatusCodes.Status404NotFound, title: TituloNoEncontrado);
    }

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
