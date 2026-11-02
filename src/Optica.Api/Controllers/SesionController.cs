using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Optica.Api.Controllers;

/// <summary>Sonda de sesión: 204 si la cookie es válida (y la renueva), 401 si no.</summary>
[ApiController]
[Route("api/sesion")]
[Authorize]
public sealed class SesionController : ControllerBase
{
    [HttpGet]
    public IActionResult Sondear() => NoContent();
}
