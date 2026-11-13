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

    /// <summary>
    /// Acción para un cuerpo de configuración que no se pudo leer: JSON roto, un texto donde va un número o un número
    /// que no entra en <see cref="decimal"/>.
    /// </summary>
    public const string AccionCuerpoIlegibleConfiguracion =
        "No se pudieron leer los valores enviados. Revisalos y grabá de nuevo.";

    /// <summary>
    /// Acción para un cuerpo de la búsqueda de presupuestos que no se pudo leer (FEAT-006): JSON roto o vacío, un tipo
    /// incorrecto en un filtro o una página que no es un entero. La búsqueda no graba: no dice "grabá".
    /// </summary>
    public const string AccionCuerpoIlegibleBusqueda =
        "No se pudieron leer los filtros enviados. Revisalos y buscá de nuevo.";

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
    /// 400 para un cuerpo ilegible con varias contraseñas: el error va en el primero de <paramref name="campos"/> que
    /// nombra una clave con error de ModelState (por ejemplo <c>$.contrasenaNueva</c>, sin distinguir mayúsculas) y, si
    /// ninguna lo nombra (JSON roto al empezar, un cuerpo que no es un objeto), en <paramref name="campoPorDefecto"/>.
    /// Solo se miran las claves, nunca los mensajes ni los valores.
    /// </summary>
    public static ActionResult CuerpoIlegible(
        this ControllerBase controller, string campoPorDefecto, params string[] campos) =>
        controller.CuerpoIlegible(AccionCuerpoIlegible, campoPorDefecto, (IReadOnlyList<string>)campos);

    /// <summary>
    /// Lo mismo que <see cref="CuerpoIlegible(ControllerBase, string, string[])"/> con otra acción fija (por ejemplo
    /// <see cref="AccionCuerpoIlegibleConfiguracion"/>). Sin <c>params</c> a propósito: con él, una llamada con tres
    /// textos elegiría esta sobrecarga en lugar de la anterior.
    /// </summary>
    public static ActionResult CuerpoIlegible(
        this ControllerBase controller, string accion, string campoPorDefecto, IReadOnlyList<string> campos)
    {
        var claves = controller.ModelState
            .Where(entrada => entrada.Value is { Errors.Count: > 0 })
            .Select(entrada => entrada.Key)
            .ToList();
        var campo = claves
            .Select(clave => campos.FirstOrDefault(candidato => NombraElCampo(clave, candidato)))
            .FirstOrDefault(candidato => candidato is not null)
            ?? campoPorDefecto;
        return controller.Invalido(new ErrorValidacion(campo, accion));
    }

    /// <summary>
    /// La clave es el campo, con o sin el prefijo <c>$.</c> de JSON. No hace falta mirar rutas dentro del campo
    /// (<c>$.campo[0]</c>, <c>$.campo.x</c>): <see cref="Acceso.LectorContrasenaJson"/> rechaza un arreglo o un objeto
    /// al empezar su token, y el error queda en la clave del campo.
    /// </summary>
    private static bool NombraElCampo(string clave, string campo)
    {
        var ruta = clave.StartsWith("$.", StringComparison.Ordinal) ? clave[2..] : clave;
        return string.Equals(ruta, campo, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// La misma forma con otro código, para un rechazo que se muestra junto al campo (por ejemplo, 401 por
    /// contraseña incorrecta); la acción va también en <c>detail</c>.
    /// </summary>
    public static ActionResult Rechazo(this ControllerBase controller, int codigo, ErrorValidacion error) =>
        controller.ConErrores(codigo, error.Accion, error);

    /// <summary>
    /// 423 con ProblemDetails para un ingreso bloqueado por intentos fallidos: el mensaje va en <c>detail</c> y no hay
    /// <c>errors</c>, porque no es un error del campo sino del acceso.
    /// </summary>
    public static ActionResult Bloqueado(this ControllerBase controller, string mensaje)
    {
        var problema = controller.ProblemDetailsFactory.CreateProblemDetails(
            controller.HttpContext,
            statusCode: StatusCodes.Status423Locked,
            title: "El acceso está bloqueado.",
            detail: mensaje);
        return new ObjectResult(problema)
        {
            StatusCode = StatusCodes.Status423Locked,
            ContentTypes = { "application/problem+json" },
        };
    }

    // El 409 de los artículos (D-19): su título fijo, los dos valores de la extensión motivo y quien los arma.

    /// <summary>El <c>title</c> fijo de los 409 de la modificación y del cambio de estado de un artículo (D-19).</summary>
    public const string TituloConflictoArticulo = "No se pudo grabar el artículo.";

    /// <summary>Valor de la extensión <c>motivo</c> de un 409: el artículo está inactivo (D-02).</summary>
    public const string MotivoInactivo = "inactivo";

    /// <summary>Valor de la extensión <c>motivo</c> de un 409: otra escritura cambió el artículo (D-08, D-16).</summary>
    public const string MotivoCambiado = "cambiado";

    /// <summary>
    /// 409 con ProblemDetails para un artículo que no se pudo grabar (D-19): <c>title</c> fijo
    /// (<see cref="TituloConflictoArticulo"/>), <paramref name="detalle"/> en <c>detail</c> (el texto fijo del caso de
    /// uso, que no repite datos recibidos) y <paramref name="motivo"/> en la extensión <c>motivo</c>
    /// (<see cref="MotivoInactivo"/> o <see cref="MotivoCambiado"/>), que la pantalla traduce a su propio texto. Sin
    /// <c>errors</c>: no es un error de un campo.
    /// </summary>
    /// <exception cref="ArgumentException">El motivo no es uno de los dos previstos, o el detalle está vacío.</exception>
    public static ActionResult ConflictoArticulo(this ControllerBase controller, string motivo, string detalle)
    {
        if (motivo is not (MotivoInactivo or MotivoCambiado))
        {
            throw new ArgumentException("El motivo del conflicto no es válido.", nameof(motivo));
        }

        return controller.Conflicto409(TituloConflictoArticulo, motivo, detalle);
    }

    // El 409 de los presupuestos (D-05 de FEAT-003c): su título fijo y su propio motivo; "cambiado" se comparte.

    /// <summary>El <c>title</c> fijo de los 409 de la modificación y del paso a Final de un presupuesto (D-05).</summary>
    public const string TituloConflictoPresupuesto = "No se pudo grabar el presupuesto.";

    /// <summary>Valor de la extensión <c>motivo</c> de un 409: el presupuesto está en Final (FR-05, FR-06).</summary>
    public const string MotivoFinal = "final";

    /// <summary>
    /// 409 con ProblemDetails para un presupuesto que no se pudo grabar (D-05): <c>title</c> fijo
    /// (<see cref="TituloConflictoPresupuesto"/>), <paramref name="detalle"/> en <c>detail</c> (el texto fijo de la
    /// aplicación, que no repite datos recibidos) y <paramref name="motivo"/> en la extensión <c>motivo</c>
    /// (<see cref="MotivoFinal"/> o <see cref="MotivoCambiado"/>). Sin <c>errors</c>: no es un error de un campo.
    /// </summary>
    /// <exception cref="ArgumentException">El motivo no es uno de los dos previstos, o el detalle está vacío.</exception>
    public static ActionResult ConflictoPresupuesto(this ControllerBase controller, string motivo, string detalle)
    {
        if (motivo is not (MotivoFinal or MotivoCambiado))
        {
            throw new ArgumentException("El motivo del conflicto no es válido.", nameof(motivo));
        }

        return controller.Conflicto409(TituloConflictoPresupuesto, motivo, detalle);
    }

    /// <summary>
    /// El 409 que comparten artículos y presupuestos: <paramref name="titulo"/> fijo, <paramref name="detalle"/> en
    /// <c>detail</c> y <paramref name="motivo"/> en la extensión <c>motivo</c>. Cada llamador valida su motivo.
    /// </summary>
    /// <exception cref="ArgumentException">El detalle está vacío.</exception>
    private static ObjectResult Conflicto409(this ControllerBase controller, string titulo, string motivo, string detalle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(detalle);
        var problema = controller.ProblemDetailsFactory.CreateProblemDetails(
            controller.HttpContext,
            statusCode: StatusCodes.Status409Conflict,
            title: titulo,
            detail: detalle);
        problema.Extensions["motivo"] = motivo;
        return new ObjectResult(problema)
        {
            StatusCode = StatusCodes.Status409Conflict,
            ContentTypes = { "application/problem+json" },
        };
    }

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
