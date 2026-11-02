using System.Text;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Optica.Application.Comun;

namespace Optica.Api.Errores;

/// <summary>
/// Respuesta 500 genérica (ProblemDetails sin detalle de la excepción) para todo error no controlado. Nunca adjunta la
/// excepción original al log: su texto puede traer rutas con el nombre de usuario de Windows. Un error de la base se
/// registra con la descripción de <see cref="IDatosLocales.DescribirFalla"/> (directorio visible, código de error y
/// clave a revisar);
/// cualquier otro, con el tipo, el mensaje y la traza de cada causa, con la carpeta del usuario reemplazada.
/// </summary>
public sealed partial class ManejadorErroresNoControlados(
    IProblemDetailsService problemas,
    IDatosLocales datos,
    ILogger<ManejadorErroresNoControlados> registro) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext contexto, Exception error, CancellationToken cancelacion)
    {
        // Un pedido mal formado que corta el servidor (por ejemplo, un cuerpo de más de 4 KB sin Content-Length)
        // no es un error del sistema: se responde con su código (413, 400) y no se registra.
        if (error is BadHttpRequestException pedidoInvalido)
        {
            contexto.Response.StatusCode = pedidoInvalido.StatusCode;
            return await problemas.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = contexto,
                ProblemDetails = new ProblemDetails { Status = pedidoInvalido.StatusCode },
            });
        }

        // Se registra la ruta del pedido, no la URL completa ni la consulta, escapada (sin saltos de línea que falseen
        // el log). Las rutas de la Api no llevan datos personales (nombres, documentos, correos) y no deben llevarlos:
        // van en el cuerpo, nunca en la ruta.
        var metodo = contexto.Request.Method;
        var ruta = contexto.Request.Path.ToUriComponent();
        var falla = datos.DescribirFalla(error);
        if (falla is not null)
        {
            RegistrarErrorDeBase(metodo, ruta, falla.Archivo, falla.Directorio, falla.TipoError, falla.CodigoError, falla.Clave);
        }
        else
        {
            RegistrarErrorNoControlado(error.GetType().Name, metodo, ruta, RutasPrivadas.Ocultar(Describir(error)));
        }

        contexto.Response.StatusCode = StatusCodes.Status500InternalServerError;
        return await problemas.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = contexto,
            Exception = null,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "No se pudo completar el pedido.",
                Detail = "Ocurrió un error en el sistema. Reintentá en unos minutos.",
            },
        });
    }

    /// <summary>Tipo, mensaje y traza de la excepción y de cada causa, como texto.</summary>
    private static string Describir(Exception error)
    {
        var texto = new StringBuilder();
        for (Exception? actual = error; actual is not null; actual = actual.InnerException)
        {
            texto.AppendLine($"{actual.GetType().FullName}: {actual.Message}");
            texto.AppendLine(actual.StackTrace);
        }

        return texto.ToString();
    }

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "No se pudo usar la base {Archivo} del directorio de datos {Directorio} ({TipoError}, código de " +
            "error {CodigoError}) al atender {Metodo} {Ruta}. Revisá ese archivo o la clave de configuración {Clave}.")]
    private partial void RegistrarErrorDeBase(
        string metodo, string ruta, string archivo, string directorio, string tipoError, int? codigoError, string clave);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error no controlado ({TipoError}) al atender {Metodo} {Ruta}.\n{Detalle}")]
    private partial void RegistrarErrorNoControlado(string tipoError, string metodo, string ruta, string detalle);
}
