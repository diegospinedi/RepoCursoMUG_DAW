using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Optica.Api.Acceso;
using Optica.Api.Errores;
using Optica.Application.Comun;
using Optica.Application.Configuracion;

namespace Optica.Api.Controllers;

/// <summary>
/// Cuerpo de <c>POST /api/configuracion</c>. Los tres importes se leen como números JSON estrictos (un texto, aunque
/// tenga dígitos, no se acepta) y la condición fiscal como texto, no como enum: un texto desconocido es un error del
/// campo que informa el caso de uso, no un cuerpo ilegible.
/// </summary>
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed record SolicitudConfiguracion(
    decimal? AlicuotaIva, string? CondicionFiscal, decimal? MultiploRedondeo, decimal? TopeIdentificacion);

/// <summary>
/// Consulta y graba los parámetros de negocio (FEAT-001c). Solo traduce HTTP: delega en
/// <see cref="ObtenerConfiguracionNegocio"/> y <see cref="GrabarConfiguracionNegocio"/>, que validan y registran cada
/// grabación. Exige sesión por la política por defecto (no está en <see cref="RutasAnonimas"/>, R-01): sin sesión, 401
/// antes del 415 y del 413. El Content-Type y el tamaño del cuerpo los controla <see cref="LimitesDelCuerpo"/> (R-02,
/// R-05). Sin fila de configuración (D-04), la excepción termina en el 500 genérico de
/// <see cref="ManejadorErroresNoControlados"/>.
/// </summary>
[ApiController]
[Route("api/configuracion")]
public sealed class ConfiguracionController(
    ObtenerConfiguracionNegocio obtenerConfiguracion,
    GrabarConfiguracionNegocio grabarConfiguracion) : ControllerBase
{
    /// <summary>Tamaño máximo del cuerpo del POST; más grande, 413.</summary>
    public const long CuerpoMaximo = 1024;

    /// <summary>Campos del cuerpo, en el orden en que se busca el que nombra un error de lectura.</summary>
    private static readonly IReadOnlyList<string> Campos =
    [
        ValidacionConfiguracion.CampoAlicuotaIva,
        ValidacionConfiguracion.CampoCondicionFiscal,
        ValidacionConfiguracion.CampoMultiploRedondeo,
        ValidacionConfiguracion.CampoTopeIdentificacion,
    ];

    [HttpGet]
    public async Task<ActionResult<ConfiguracionVigente>> Obtener(CancellationToken cancelacion) =>
        await obtenerConfiguracion.EjecutarAsync(cancelacion);

    /// <summary>
    /// Graba los cuatro valores juntos y responde 200 con la configuración leída después de grabar (D-03). Un cuerpo
    /// ilegible responde 400 con un solo error y una acción fija, sin los mensajes del lector de JSON ni el valor (R-07);
    /// valores inválidos, 400 con todos los campos.
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(CuerpoMaximo)]
    public async Task<IActionResult> Grabar([FromBody] SolicitudConfiguracion? solicitud, CancellationToken cancelacion)
    {
        if (!ModelState.IsValid)
        {
            return this.CuerpoIlegible(
                RespuestasValidacion.AccionCuerpoIlegibleConfiguracion, ValidacionConfiguracion.CampoAlicuotaIva, Campos);
        }

        var resultado = await grabarConfiguracion.EjecutarAsync(
            new DatosConfiguracion(
                solicitud?.AlicuotaIva, solicitud?.CondicionFiscal, solicitud?.MultiploRedondeo, solicitud?.TopeIdentificacion),
            cancelacion);
        return await ResponderGrabacionAsync(resultado.Tipo, resultado.Errores, cancelacion);
    }

    /// <summary>
    /// Traduce el resultado de grabar. Cerrado por defecto: solo <see cref="TipoResultado.Exito"/> lee y devuelve la
    /// configuración; cualquier otro valor lanza, y el manejador de errores responde 500 genérico.
    /// </summary>
    internal async Task<IActionResult> ResponderGrabacionAsync(
        TipoResultado tipo, IReadOnlyList<ErrorValidacion> errores, CancellationToken cancelacion)
    {
        switch (tipo)
        {
            case TipoResultado.Exito:
                return Ok(await obtenerConfiguracion.EjecutarAsync(cancelacion));
            case TipoResultado.Invalido:
                return this.Invalido([.. errores]);
            default:
                throw new InvalidOperationException($"Resultado de grabar la configuración no previsto: {(int)tipo}.");
        }
    }
}
