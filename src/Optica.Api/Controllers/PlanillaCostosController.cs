using System.Buffers.Text;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Optica.Api.Acceso;
using Optica.Api.Errores;
using Optica.Application.Catalogo;
using Optica.Application.Comun;

namespace Optica.Api.Controllers;

/// <summary>
/// Cuerpo de <c>POST /api/articulos/planilla-costos</c> (D-06 de FEAT-011): la planilla .xlsx en base64 como texto. Un
/// número, un arreglo o un objeto en su lugar no se convierte: el cuerpo es ilegible.
/// </summary>
/// <remarks>
/// <see cref="object.ToString"/> no muestra ningún valor (<c>SolicitudPlanillaCostos { }</c>): MVC registra en Trace
/// los argumentos de la acción con su <c>ToString()</c>, y la planilla son hasta 14 MB de códigos y costos de los
/// proveedores (R-05).
/// </remarks>
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed record SolicitudPlanillaCostos(string? Planilla)
{
    private bool PrintMembers(StringBuilder constructor) => false;
}

/// <summary>Una fila no aplicada en la respuesta: fila, código acotado (D-10) y razón (FR-13).</summary>
/// <remarks><see cref="object.ToString"/> no muestra ningún valor: el código es un dato del proveedor (R-05).</remarks>
public sealed record FilaNoAplicadaRespuesta(int Fila, string Codigo, string Razon)
{
    private bool PrintMembers(StringBuilder constructor) => false;
}

/// <summary>
/// Una fila aplicada para revisar en la respuesta: fila, código acotado (D-10), precio de costo aplicado e indicaciones
/// (FR-14, FR-15).
/// </summary>
/// <remarks><see cref="object.ToString"/> no muestra ningún valor: código y costo son datos comerciales (R-05).</remarks>
public sealed record FilaParaRevisarRespuesta(int Fila, string Codigo, decimal PrecioCosto, IReadOnlyList<string> Indicaciones)
{
    private bool PrintMembers(StringBuilder constructor) => false;
}

/// <summary>
/// Respuesta 200 de <c>POST /api/articulos/planilla-costos</c>: filas leídas, aplicadas, no aplicadas con su razón y
/// aplicadas para revisar. Es el DTO propio que pide el <c>remarks</c> de <see cref="ResultadoPlanillaCostos.Aplicada"/>:
/// nunca lleva margen, precio de venta, versión, descripción ni estado del artículo, y el código va acotado (R-05, D-10).
/// </summary>
/// <remarks><see cref="object.ToString"/> no muestra ningún valor (R-05).</remarks>
public sealed record RespuestaPlanillaCostos(
    int FilasLeidas,
    int Aplicadas,
    IReadOnlyList<FilaNoAplicadaRespuesta> NoAplicadas,
    IReadOnlyList<FilaParaRevisarRespuesta> Revisar)
{
    /// <summary>Largo máximo del código en la respuesta, con el "…" incluido (D-10).</summary>
    public const int LargoMaximoCodigo = 60;

    private const char Reemplazo = '�';

    private const string PuntosSuspensivos = "…";

    /// <exception cref="ArgumentNullException"><paramref name="aplicada"/> es null.</exception>
    public static RespuestaPlanillaCostos Desde(ResultadoPlanillaCostos.Aplicada aplicada)
    {
        ArgumentNullException.ThrowIfNull(aplicada);
        return new(
            aplicada.CantidadFilasLeidas,
            aplicada.CantidadAplicadas,
            [.. aplicada.FilasNoAplicadas().Select(fila => new FilaNoAplicadaRespuesta(
                fila.Fila, AcotarCodigo(fila.Codigo), fila.Razon))],
            [.. aplicada.FilasParaRevisar().Select(fila => new FilaParaRevisarRespuesta(
                fila.Fila, AcotarCodigo(fila.Codigo), fila.PrecioCosto, fila.Indicaciones))]);
    }

    /// <summary>
    /// El código como se muestra (D-10): cada sustituto UTF-16 suelto se reemplaza por U+FFFD (un código que no es
    /// UTF-16 válido no llega así al JSON) y, si queda más largo que <see cref="LargoMaximoCodigo"/>, se corta con "…"
    /// hasta ese largo; uno menos si el corte cae dentro de un par de sustitutos, que se deja afuera entero. Solo
    /// recorre el comienzo del código: una celda puede tener 64 KB.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="codigo"/> es null.</exception>
    public static string AcotarCodigo(string codigo)
    {
        ArgumentNullException.ThrowIfNull(codigo);
        var salida = new StringBuilder(LargoMaximoCodigo + 2);
        for (var i = 0; i < codigo.Length && salida.Length <= LargoMaximoCodigo; i++)
        {
            var caracter = codigo[i];
            if (char.IsHighSurrogate(caracter) && i + 1 < codigo.Length && char.IsLowSurrogate(codigo[i + 1]))
            {
                salida.Append(caracter).Append(codigo[++i]);
            }
            else
            {
                salida.Append(char.IsSurrogate(caracter) ? Reemplazo : caracter);
            }
        }

        if (salida.Length <= LargoMaximoCodigo)
        {
            return salida.ToString();
        }

        // Ya sin sueltos: un sustituto alto al final del corte es la mitad de un par válido, y se deja afuera entero.
        var corte = LargoMaximoCodigo - PuntosSuspensivos.Length;
        if (char.IsHighSurrogate(salida[corte - 1]))
        {
            corte--;
        }

        return salida.ToString(0, corte) + PuntosSuspensivos;
    }

    private bool PrintMembers(StringBuilder constructor) => false;
}

/// <summary>
/// Actualiza los costos del catálogo con una planilla .xlsx (FEAT-011, D-19). Solo traduce HTTP: decodifica el base64,
/// delega en <see cref="ActualizarCostosPorPlanilla"/>, que lee, valida, aplica y registra la carga, y traduce su
/// resultado. Exige sesión por la política por defecto (no está en <see cref="RutasAnonimas"/>, R-01): sin sesión, 401
/// antes del 415 y del 413. El Content-Type JSON y el tamaño del cuerpo los controla <see cref="LimitesDelCuerpo"/>
/// (R-01, R-02): multipart o un formulario, 415. La respuesta lleva <c>no-store</c> como todo <c>/api</c>.
/// </summary>
[ApiController]
[Route("api/articulos/planilla-costos")]
public sealed class PlanillaCostosController(ActualizarCostosPorPlanilla actualizarCostos) : ControllerBase
{
    /// <summary>
    /// Tamaño máximo del cuerpo: 14 MiB (D-14). El archivo de <see cref="ArchivoMaximo"/> en base64 ocupa
    /// 4 × ⌈10.485.760 / 3⌉ = 13.981.016 caracteres; con <c>{"planilla":""}</c> son 13.981.031 bytes, y quedan unos
    /// 680 KB de margen para espacios o claves de más. Más grande, 413 sin leer el cuerpo (R-02).
    /// </summary>
    public const long CuerpoMaximo = 14 * 1024 * 1024;

    /// <summary>
    /// Tamaño máximo del archivo decodificado: 10 MiB (D-14). Un base64 válido que decodifica más (todavía entra en
    /// <see cref="CuerpoMaximo"/> hasta unos 10,5 MB) se rechaza con <see cref="MensajeArchivoGrande"/> sin
    /// decodificarlo (D-25).
    /// </summary>
    public const int ArchivoMaximo = 10 * 1024 * 1024;

    /// <summary>
    /// Política del limitador de concurrencia de este endpoint (D-25): 1 carga en curso y 1 en fila; el resto, 429 sin
    /// leer el cuerpo. Cada pedido retiene hasta unos 50 MB (cuerpo UTF-8, texto base64 y archivo decodificado), así que
    /// el pico queda acotado a dos pedidos.
    /// </summary>
    public const string LimitadorPlanilla = "planilla";

    /// <summary>El archivo decodificado pasa <see cref="ArchivoMaximo"/> (D-25); el mismo texto que el 413 en la pantalla.</summary>
    public const string MensajeArchivoGrande = "La planilla supera los 10 MB.";

    /// <summary>El campo de <c>errors</c> de todos los rechazos 400.</summary>
    public const string CampoPlanilla = "planilla";

    /// <summary>Planilla faltante, null, vacía o con solo espacios.</summary>
    public const string MensajeFaltante = "Elegí una planilla .xlsx.";

    /// <summary>
    /// Base64 inválido, un cuerpo ilegible o un archivo que no es .xlsx (FR-05, AC-07, R-09). No repite lo recibido.
    /// </summary>
    public const string MensajeNoEsXlsx =
        "El archivo no es una planilla .xlsx. Guardala como Libro de Excel (.xlsx) y volvé a cargarla.";

    /// <summary>Encabezados distintos o columnas de más (FR-03, FR-04, FR-05, AC-04, AC-05).</summary>
    public const string MensajeFormatoInesperado =
        "El formato de la planilla no es el esperado. Tiene que tener solo dos columnas, con los encabezados " +
        "\"Codigo en el proveedor\" y \"Precio de Costo Nuevo\" en la fila 1.";

    /// <summary>
    /// La planilla pasa un límite del lector (D-14, D-20); con la causa más probable en una planilla legítima (D-24).
    /// </summary>
    public const string MensajeExcedeLimites =
        "La planilla supera las 50.000 filas o tiene celdas demasiado largas. Dividila y cargala por partes. " +
        "Si tiene formato en celdas vacías, quitáselo.";

    /// <summary>
    /// El <c>detail</c> del 409 "cambiado" (D-15): otra escritura cambió un artículo entre la lectura y la confirmación,
    /// y no se aplicó nada. No nombra el artículo.
    /// </summary>
    public const string DetalleCambiado =
        "Un artículo cambió mientras se cargaba la planilla. No se actualizó ningún costo. Volvé a cargarla.";

    /// <summary>
    /// Carga la planilla y responde 200 con <see cref="RespuestaPlanillaCostos"/>. Rechazos 400 con
    /// <c>errors.planilla</c>: faltante (<see cref="MensajeFaltante"/>), base64 inválido, cuerpo ilegible o no .xlsx
    /// (<see cref="MensajeNoEsXlsx"/>), formato (<see cref="MensajeFormatoInesperado"/>) y límites
    /// (<see cref="MensajeExcedeLimites"/>) y archivo de más de 10 MB (<see cref="MensajeArchivoGrande"/>); ninguno toca
    /// la base. Un conflicto de concurrencia, 409 con el motivo "cambiado", <see cref="DetalleCambiado"/> y nada aplicado
    /// (D-15, D-25). Con el limitador <see cref="LimitadorPlanilla"/> lleno, 429 antes del model binding (D-25). Cualquier
    /// otra falla termina en el 500 genérico de <see cref="ManejadorErroresNoControlados"/>.
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(CuerpoMaximo)]
    [EnableRateLimiting(LimitadorPlanilla)]
    public async Task<IActionResult> Actualizar(
        [FromBody] SolicitudPlanillaCostos? solicitud, CancellationToken cancelacion)
    {
        if (!ModelState.IsValid)
        {
            return Rechazo(MensajeNoEsXlsx);
        }

        var texto = solicitud?.Planilla;
        if (texto is null)
        {
            return Rechazo(MensajeFaltante);
        }

        // Valida y mide sin decodificar: ignora los mismos espacios que Convert.TryFromBase64String.
        if (!Base64.IsValid(texto, out var largo))
        {
            return Rechazo(MensajeNoEsXlsx);
        }

        if (largo == 0)
        {
            return Rechazo(MensajeFaltante);
        }

        if (largo > ArchivoMaximo)
        {
            return Rechazo(MensajeArchivoGrande);
        }

        // Un búfer del largo exacto, nunca más de ArchivoMaximo.
        var archivo = new byte[largo];
        if (!Convert.TryFromBase64String(texto, archivo, out var escritos) || escritos != largo)
        {
            return Rechazo(MensajeNoEsXlsx);
        }

        ResultadoPlanillaCostos resultado;
        try
        {
            resultado = await actualizarCostos.EjecutarAsync(archivo, cancelacion);
        }
        catch (ConflictoAlConfirmarException conflicto) when (conflicto.Motivo == MotivoConflicto.Concurrencia)
        {
            return this.ConflictoArticulo(RespuestasValidacion.MotivoCambiado, DetalleCambiado);
        }

        // Cerrado por defecto: una forma no prevista lanza y el manejador responde 500 genérico.
        return resultado switch
        {
            ResultadoPlanillaCostos.Aplicada aplicada => Ok(RespuestaPlanillaCostos.Desde(aplicada)),
            ResultadoPlanillaCostos.NoEsXlsx => Rechazo(MensajeNoEsXlsx),
            ResultadoPlanillaCostos.FormatoInesperado => Rechazo(MensajeFormatoInesperado),
            ResultadoPlanillaCostos.ExcedeLimites => Rechazo(MensajeExcedeLimites),
            _ => throw new InvalidOperationException("Resultado de la planilla de costos no previsto."),
        };
    }

    private ActionResult Rechazo(string mensaje) => this.Invalido(new ErrorValidacion(CampoPlanilla, mensaje));
}
