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
/// Una línea del cuerpo: solo el código autonumérico del artículo (D-12, D-13), como número JSON estricto (un texto,
/// aunque tenga dígitos, no se acepta). El precio, la descripción, la cantidad y el descuento los pone el servidor; si
/// llegan en el cuerpo se ignoran (FR-11).
/// </summary>
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed record SolicitudLinea(long? CodigoArticulo)
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
/// Presupuestos: alta en Borrador y consulta por número (FEAT-003a). Solo traduce HTTP: delega en los casos de uso, que
/// validan, leen los artículos, numeran y graban. Exige sesión por la política por defecto (no está en
/// <see cref="RutasAnonimas"/>): las respuestas traen datos personales del cliente (D-17). Sin sesión, 401 antes del 415
/// y del 413; el Content-Type y el tamaño del cuerpo los controla <see cref="LimitesDelCuerpo"/> (D-16). No registra el
/// cuerpo ni la query string; los errores son textos fijos que no repiten lo enviado, y ningún dato personal viaja en la
/// ruta: el GET es por número. El traductor del alta es cerrado por defecto: un resultado no previsto lanza y el
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
    AltaPresupuesto altaPresupuesto, ObtenerPresupuesto obtenerPresupuesto) : ControllerBase
{
    /// <summary>Tamaño máximo del cuerpo del POST (D-16); más grande, 413.</summary>
    public const long CuerpoMaximo = 16 * 1024;

    /// <summary>El <c>title</c> fijo del 404 de un presupuesto inexistente (D-24).</summary>
    private const string TituloNoEncontrado = "No se encontró el presupuesto.";

    /// <summary>El prefijo del cliente en la clave de un error de lectura (<c>$.cliente.dni</c>).</summary>
    private const string PrefijoCliente = "cliente.";

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

        var datosCliente = solicitud?.Cliente is { } cliente
            ? new DatosCliente(cliente.Apellido, cliente.Nombre, cliente.Dni, cliente.Domicilio, cliente.Email, cliente.Telefono)
            : null;
        IReadOnlyList<long?>? codigos = solicitud?.Lineas is { } lineas
            ? [.. lineas.Select(linea => linea?.CodigoArticulo)]
            : null;
        return ResponderAlta(await altaPresupuesto.EjecutarAsync(new DatosPresupuesto(datosCliente, codigos), cancelacion));
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
    /// 400 para un cuerpo ilegible, con la acción fija de los cuerpos con valores
    /// (<see cref="RespuestasValidacion.AccionCuerpoIlegibleConfiguracion"/>) en el campo que nombra la primera clave
    /// con error: <c>$.cliente.dni</c> en <c>dni</c>, <c>$.lineas[1].codigoArticulo</c> en <c>lineas[1]</c>,
    /// <c>$.lineas</c> en <c>lineas</c>; si ninguna nombra un campo (JSON roto al empezar, un cuerpo que no es un
    /// objeto), en <c>apellido</c>. Solo se miran las claves, nunca los mensajes ni los valores.
    /// </summary>
    /// <remarks>
    /// Se llama distinto de la extensión <see cref="RespuestasValidacion.CuerpoIlegible(ControllerBase, string, string[])"/>
    /// para no taparla: esa compara la ruta completa de la clave y no entra en <c>cliente</c> ni en <c>lineas[i]</c>.
    /// </remarks>
    private ActionResult CuerpoIlegiblePresupuesto()
    {
        var campo = ModelState
            .Where(entrada => entrada.Value is { Errors.Count: > 0 })
            .Select(entrada => CampoDeLaClave(entrada.Key))
            .FirstOrDefault(candidato => candidato is not null)
            ?? ValidacionPresupuesto.CampoApellido;
        return this.Invalido(new ErrorValidacion(campo, RespuestasValidacion.AccionCuerpoIlegibleConfiguracion));
    }

    /// <summary>El campo de D-20 que nombra la clave de un error de lectura, o null si no nombra ninguno.</summary>
    internal static string? CampoDeLaClave(string clave)
    {
        var ruta = clave.StartsWith("$.", StringComparison.Ordinal) ? clave[2..] : clave;
        if (ruta.StartsWith(PrefijoCliente, StringComparison.OrdinalIgnoreCase))
        {
            var dato = ruta[PrefijoCliente.Length..];
            return CamposCliente.FirstOrDefault(candidato =>
                string.Equals(dato, candidato, StringComparison.OrdinalIgnoreCase));
        }

        var lineas = ValidacionPresupuesto.CampoLineas;
        if (!ruta.StartsWith(lineas, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var resto = ruta[lineas.Length..];
        if (resto.Length == 0)
        {
            return lineas;
        }

        // lineas[i] o lineas[i].codigoArticulo: el campo de la línea i, si es una de las que se aceptan.
        var cierre = resto.IndexOf(']', StringComparison.Ordinal);
        if (resto[0] != '['
            || cierre < 2
            || (cierre + 1 < resto.Length && resto[cierre + 1] != '.')
            || !int.TryParse(resto.AsSpan(1, cierre - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var indice))
        {
            return null;
        }

        return indice < ValidacionPresupuesto.LineasMaximas ? ValidacionPresupuesto.CampoLinea(indice) : lineas;
    }
}
