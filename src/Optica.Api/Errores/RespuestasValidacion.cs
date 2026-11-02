using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Optica.Application.Acceso;
using Optica.Application.Comun;

namespace Optica.Api.Errores;

/// <summary>
/// Traduce <see cref="ErrorValidacion"/> a ValidationProblemDetails: <c>errors.{campo}</c> con la acción que lo
/// corrige. Nunca copia el valor recibido ni los mensajes internos del lector de JSON. La validación de la contraseña
/// la hacen los casos de uso; acá solo se traduce su resultado.
/// </summary>
public static class RespuestasValidacion
{
    /// <summary>Acción para un cuerpo que no se pudo leer: JSON roto o una contraseña que no es texto.</summary>
    public const string AccionCuerpoIlegible = "No se pudo leer la contraseña enviada. Escribila de nuevo.";

    /// <summary>400 con <c>errors.{campo}</c> y la acción de cada error.</summary>
    public static ActionResult Invalido(this ControllerBase controller, params ErrorValidacion[] errores) =>
        controller.ConErrores(StatusCodes.Status400BadRequest, detalle: null, errores);

    /// <summary>
    /// 400 para un cuerpo con la contraseña que el lector de JSON no pudo leer (JSON roto, un número, un arreglo).
    /// Es un error del pedido, no de la política: no llega al caso de uso.
    /// </summary>
    public static ActionResult ContrasenaIlegible(this ControllerBase controller) =>
        controller.Invalido(new ErrorValidacion(ValidacionContrasena.Campo, AccionCuerpoIlegible));

    /// <summary>
    /// La misma forma con otro código, para un rechazo que se muestra junto al campo (por ejemplo, 401 por
    /// contraseña incorrecta); la acción va también en <c>detail</c>.
    /// </summary>
    public static ActionResult Rechazo(this ControllerBase controller, int codigo, ErrorValidacion error) =>
        controller.ConErrores(codigo, error.Accion, error);

    private static ActionResult ConErrores(
        this ControllerBase controller, int codigo, string? detalle, params ErrorValidacion[] errores)
    {
        var modelo = new ModelStateDictionary();
        foreach (var error in errores)
        {
            modelo.AddModelError(error.Campo, error.Accion);
        }

        var problema = controller.ProblemDetailsFactory.CreateValidationProblemDetails(
            controller.HttpContext, modelo, statusCode: codigo, detail: detalle);
        return new ObjectResult(problema)
        {
            StatusCode = codigo,
            ContentTypes = { "application/problem+json" },
        };
    }
}
