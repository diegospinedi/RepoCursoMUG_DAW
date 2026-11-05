using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Optica.Api.Acceso;
using Optica.Api.Errores;
using Optica.Application.Catalogo;
using Optica.Application.Configuracion;

namespace Optica.Api.Controllers;

/// <summary>
/// Cuerpo de <c>POST /api/configuracion</c>. Los tres importes se leen como números JSON estrictos (un texto, aunque
/// tenga dígitos, no se acepta) y la condición fiscal como texto, no como enum: un texto desconocido es un error del
/// campo que informa el caso de uso, no un cuerpo ilegible.
/// </summary>
/// <remarks>
/// <see cref="object.ToString"/> no muestra ningún valor (<c>SolicitudConfiguracion { }</c>, D-28 de FEAT-002c): MVC
/// registra en Trace los argumentos de la acción con su <c>ToString()</c>, y la configuración es información comercial
/// confidencial de la empresa.
/// </remarks>
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed record SolicitudConfiguracion(
    decimal? AlicuotaIva, string? CondicionFiscal, decimal? MultiploRedondeo, decimal? TopeIdentificacion)
{
    private bool PrintMembers(System.Text.StringBuilder constructor) => false;
}

/// <summary>
/// Respuesta 200 de <c>POST /api/configuracion</c> (D-23 de FEAT-002c): la <see cref="ConfiguracionVigente"/> leída
/// después del commit, con la misma forma que el GET, más cuántos artículos cambiaron de precio de venta al recalcular
/// el catálogo (D-09). El GET sigue respondiendo <see cref="ConfiguracionVigente"/>, sin la cantidad.
/// </summary>
/// <remarks>
/// <see cref="object.ToString"/> no muestra ningún valor (<c>ConfiguracionGrabada { }</c>), como las solicitudes de
/// artículos (D-28 de FEAT-002b): la configuración comercial no llega a un log por esa vía.
/// </remarks>
public sealed record ConfiguracionGrabada(
    decimal AlicuotaIva, string CondicionFiscal, decimal MultiploRedondeo, decimal? TopeIdentificacion, bool FaltaTope,
    int PreciosCambiados)
{
    /// <exception cref="ArgumentNullException"><paramref name="vigente"/> es nula.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="preciosCambiados"/> es negativo.</exception>
    public static ConfiguracionGrabada Desde(ConfiguracionVigente vigente, int preciosCambiados)
    {
        ArgumentNullException.ThrowIfNull(vigente);
        ArgumentOutOfRangeException.ThrowIfNegative(preciosCambiados);
        return new(
            vigente.AlicuotaIva,
            vigente.CondicionFiscal,
            vigente.MultiploRedondeo,
            vigente.TopeIdentificacion,
            vigente.FaltaTope,
            preciosCambiados);
    }

    private bool PrintMembers(System.Text.StringBuilder constructor) => false;
}

/// <summary>
/// Consulta y graba los parámetros de negocio (FEAT-001c). Solo traduce HTTP: delega en
/// <see cref="ObtenerConfiguracionNegocio"/> y <see cref="GrabarConfiguracionConRecalculo"/>, que validan y registran cada
/// grabación. Exige sesión por la política por defecto (no está en <see cref="RutasAnonimas"/>, R-01): sin sesión, 401
/// antes del 415 y del 413. El Content-Type y el tamaño del cuerpo los controla <see cref="LimitesDelCuerpo"/> (R-02,
/// R-05). Sin fila de configuración (D-04), la excepción termina en el 500 genérico de
/// <see cref="ManejadorErroresNoControlados"/>.
/// </summary>
[ApiController]
[Route("api/configuracion")]
public sealed class ConfiguracionController(
    ObtenerConfiguracionNegocio obtenerConfiguracion,
    GrabarConfiguracionConRecalculo grabarConfiguracion) : ControllerBase
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
    /// Graba los cuatro valores juntos, recalculando el catálogo si corresponde (FEAT-002c), y responde 200 con
    /// <see cref="ConfiguracionGrabada"/>: la configuración leída después de grabar (D-03) y cuántos precios de venta
    /// cambiaron (D-23). Un cuerpo ilegible responde 400 con un solo error y una acción fija, sin los mensajes del lector
    /// de JSON ni el valor (R-07); valores inválidos, 400 con todos los campos, incluido el de precio fuera de rango en
    /// <c>multiploRedondeo</c> (D-01).
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
        return await ResponderGrabacionAsync(resultado, cancelacion);
    }

    /// <summary>
    /// Traduce el resultado de grabar. Cerrado por defecto: solo una grabación hecha lee la configuración después del
    /// commit (la última grabación gana, D-03 de FEAT-001c) y la devuelve con la cantidad; una inválida con errores es
    /// 400; cualquier otra forma lanza, y el manejador de errores responde 500 genérico.
    /// </summary>
    private async Task<IActionResult> ResponderGrabacionAsync(
        ResultadoGrabacionConfiguracion resultado, CancellationToken cancelacion)
    {
        if (resultado.EsGrabado)
        {
            var vigente = await obtenerConfiguracion.EjecutarAsync(cancelacion);
            return Ok(ConfiguracionGrabada.Desde(vigente, resultado.PreciosCambiados.Value));
        }

        if (resultado.Errores.Count > 0)
        {
            return this.Invalido([.. resultado.Errores]);
        }

        throw new InvalidOperationException("Resultado de grabar la configuración no previsto.");
    }
}
