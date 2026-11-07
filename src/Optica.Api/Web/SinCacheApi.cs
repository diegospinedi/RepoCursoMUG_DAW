using Microsoft.Net.Http.Headers;

namespace Optica.Api.Web;

/// <summary>
/// Ninguna respuesta de <c>/api</c> se guarda en una caché (D-37, R-08): las de presupuestos traen los datos personales
/// del cliente y las de artículos y configuración, datos comerciales; en una PC compartida el navegador no debe
/// conservarlas, ni tampoco un intermediario. Las rutas fuera de <c>/api</c> (la SPA de <see cref="SpaEstatica"/>)
/// conservan su <c>Cache-Control: no-cache</c>.
/// </summary>
public static class SinCacheApi
{
    private const string Valor = "no-store";

    private const string PrefijoApi = "/api";

    /// <summary>
    /// Si la ruta empieza con el segmento <c>/api</c> (sin distinguir mayúsculas), fija <c>Cache-Control: no-store</c>
    /// justo antes de que la respuesta empiece, de modo que también lo llevan las de error (400, 401, 404, 413, 415, 423,
    /// 429 y 500) que escriben la autorización, <see cref="Acceso.LimitesDelCuerpo"/>, el limitador o el manejador de
    /// errores. No pisa un <c>Cache-Control</c> que ya fijó otro (un controlador o el manejador de errores, que pone
    /// <c>no-cache,no-store</c>) y no cambia el código de estado ni el cuerpo. Va antes de <c>UseExceptionHandler</c>.
    /// </summary>
    public static Task AplicarAsync(HttpContext contexto, RequestDelegate siguiente)
    {
        if (contexto.Request.Path.StartsWithSegments(PrefijoApi, StringComparison.OrdinalIgnoreCase))
        {
            contexto.Response.OnStarting(
                estado =>
                {
                    var respuesta = (HttpResponse)estado;
                    if (!respuesta.Headers.ContainsKey(HeaderNames.CacheControl))
                    {
                        respuesta.Headers.CacheControl = Valor;
                    }

                    return Task.CompletedTask;
                },
                contexto.Response);
        }

        return siguiente(contexto);
    }
}
