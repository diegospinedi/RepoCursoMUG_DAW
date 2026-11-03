using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Optica.Api.Acceso;
using Optica.Api.Errores;
using Optica.Application.Acceso;
using Optica.Application.Comun;

namespace Optica.Api.Controllers;

/// <summary>Cuerpo de <c>definir</c> e <c>ingresar</c>. Nunca se registra ni se devuelve.</summary>
public sealed record SolicitudContrasena(
    [property: JsonConverter(typeof(LectorContrasenaJson))] string? Contrasena)
{
    /// <summary>
    /// Sin la contraseña: MVC registra en nivel Trace los argumentos de la acción con su <c>ToString()</c>, y el de un
    /// record muestra todas sus propiedades.
    /// </summary>
    public override string ToString() => nameof(SolicitudContrasena) + " { Contrasena = *** }";
}

/// <summary>
/// Estado del acceso, definición de la contraseña, ingreso y salida. Solo traduce HTTP: delega en los casos de uso
/// (que validan la contraseña y registran los eventos de acceso), traduce sus resultados y emite o borra la cookie de
/// sesión. El Content-Type y el tamaño del cuerpo los controla <see cref="LimitesDelCuerpo"/>.
/// </summary>
[ApiController]
[Route("api/acceso")]
public sealed class AccesoController(
    ConsultarEstadoAcceso consultarEstado,
    DefinirContrasena definirContrasena,
    IngresarConContrasena ingresarConContrasena,
    CerrarSesion cerrarSesion) : ControllerBase
{
    /// <summary>Política del limitador de concurrencia compartida por <c>definir</c> e <c>ingresar</c> (R-05).</summary>
    public const string LimitadorAcceso = "acceso";

    /// <summary>Tamaño máximo del cuerpo de los POST de acceso; más grande, 413.</summary>
    public const long CuerpoMaximo = 4 * 1024;

    public const string MensajeContrasenaIncorrecta = "La contraseña no es correcta. Volvé a ingresarla.";
    public const string AccionSinContrasena = "Definí la contraseña primero";
    public const string AccionYaDefinida = "Ya hay una contraseña definida. Ingresá con ella.";
    public const string MensajeBloqueado = "El acceso está bloqueado temporalmente. Volvé a intentar en unos minutos.";

    [HttpGet("estado")]
    [AllowAnonymous]
    public async Task<ActionResult<EstadoAcceso>> Estado(CancellationToken cancelacion) =>
        await consultarEstado.EjecutarAsync(cancelacion);

    [HttpPost("definir")]
    [AllowAnonymous]
    [RequestSizeLimit(CuerpoMaximo)]
    [EnableRateLimiting(LimitadorAcceso)]
    public async Task<IActionResult> Definir([FromBody] SolicitudContrasena? solicitud, CancellationToken cancelacion)
    {
        if (!ModelState.IsValid)
        {
            return this.ContrasenaIlegible();
        }

        var resultado = await definirContrasena.EjecutarAsync(solicitud?.Contrasena, cancelacion);
        return await ResponderDefinicionAsync(resultado.Tipo, resultado.Errores);
    }

    /// <summary>
    /// Traduce el resultado de <c>definir</c>. Cerrado por defecto: la cookie se emite solo ante
    /// <see cref="TipoResultado.Exito"/>; cualquier otro valor lanza, y el manejador de errores responde 500 genérico.
    /// </summary>
    internal async Task<IActionResult> ResponderDefinicionAsync(TipoResultado tipo, IReadOnlyList<ErrorValidacion> errores)
    {
        switch (tipo)
        {
            case TipoResultado.Exito:
                await IniciarSesionAsync();
                return StatusCode(StatusCodes.Status201Created);
            case TipoResultado.Invalido:
                return this.Invalido([.. errores]);
            case TipoResultado.Conflicto:
                return Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "La contraseña ya está definida.",
                    detail: AccionYaDefinida);
            default:
                throw new InvalidOperationException($"Resultado de definir no previsto: {(int)tipo}.");
        }
    }

    [HttpPost("ingresar")]
    [AllowAnonymous]
    [RequestSizeLimit(CuerpoMaximo)]
    [EnableRateLimiting(LimitadorAcceso)]
    public async Task<IActionResult> Ingresar([FromBody] SolicitudContrasena? solicitud, CancellationToken cancelacion)
    {
        if (!ModelState.IsValid)
        {
            return this.ContrasenaIlegible();
        }

        var intento = await ingresarConContrasena.EjecutarAsync(solicitud?.Contrasena, cancelacion);
        return await ResponderIngresoAsync(intento);
    }

    /// <summary>
    /// Traduce el resultado de <c>ingresar</c>. Cerrado por defecto: la cookie se emite solo ante
    /// <see cref="ResultadoIngreso.Exito"/>; cualquier otro valor lanza, y el manejador de errores responde 500 genérico.
    /// </summary>
    internal async Task<IActionResult> ResponderIngresoAsync(IntentoIngreso intento)
    {
        switch (intento.Tipo)
        {
            case ResultadoIngreso.Exito:
                await IniciarSesionAsync();
                return NoContent();
            case ResultadoIngreso.Invalido:
                return this.Invalido([.. intento.Errores]);
            case ResultadoIngreso.SinCredencial:
                return Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Todavía no hay una contraseña definida.",
                    detail: AccionSinContrasena);
            case ResultadoIngreso.Rechazo:
                return this.Rechazo(
                    StatusCodes.Status401Unauthorized,
                    new ErrorValidacion(ValidacionContrasena.Campo, MensajeContrasenaIncorrecta));
            case ResultadoIngreso.Bloqueado:
                return this.Bloqueado(MensajeBloqueado);
            default:
                throw new InvalidOperationException($"Resultado de ingresar no previsto: {(int)intento.Tipo}.");
        }
    }

    /// <summary>
    /// Registra la salida y borra la cookie. Exige sesión por la política por defecto y, como todo POST,
    /// <c>Content-Type: application/json</c>; el cuerpo puede ir vacío y no se lee.
    /// </summary>
    [HttpPost("salir")]
    [RequestSizeLimit(CuerpoMaximo)]
    public async Task<IActionResult> Salir(CancellationToken cancelacion)
    {
        await cerrarSesion.EjecutarAsync(cancelacion);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    /// <summary>Emite siempre una cookie nueva, sin reutilizar una anterior (sin fijación de sesión).</summary>
    private Task IniciarSesionAsync() =>
        HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(CookieAuthenticationDefaults.AuthenticationScheme)),
            new AuthenticationProperties { IsPersistent = false });
}
