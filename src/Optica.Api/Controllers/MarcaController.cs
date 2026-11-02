using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Optica.Application.Marca;

namespace Optica.Api.Controllers;

/// <summary>
/// La marca para la pantalla de ingreso y el encabezado (FR-06): colores y logo, sin sesión (están en
/// <see cref="Acceso.RutasAnonimas"/>; la marca no contiene datos protegidos). Solo delega en
/// <see cref="ObtenerMarca"/>; si tampoco la marca de ejemplo se puede leer, el error termina en el 500 genérico del
/// manejador de errores.
/// </summary>
[ApiController]
[Route("api/marca")]
[AllowAnonymous]
public sealed class MarcaController(ObtenerMarca obtenerMarca) : ControllerBase
{
    [HttpGet]
    public ActionResult<MarcaVisible> Obtener() => obtenerMarca.Ejecutar();

    /// <summary>
    /// El archivo del logo con su tipo de contenido. Nunca se deja adivinar el tipo (<c>nosniff</c>) y un SVG se sirve
    /// con <c>Content-Security-Policy: default-src 'none'; style-src 'unsafe-inline'; sandbox</c>: si se abre directo,
    /// sus scripts no se ejecutan y no carga nada externo, solo sus estilos propios (R-10).
    /// </summary>
    [HttpGet("logo")]
    public IActionResult Logo()
    {
        var logo = obtenerMarca.ObtenerLogo();
        Response.Headers.XContentTypeOptions = "nosniff";
        if (logo.EsSvg)
        {
            Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; sandbox";
        }

        return File(logo.Contenido, logo.TipoContenido);
    }
}
