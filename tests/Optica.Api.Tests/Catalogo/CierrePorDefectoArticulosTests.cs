using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Optica.Api.Controllers;
using Optica.Api.Errores;
using Optica.Application.Catalogo;
using Optica.Application.Comun;

namespace Optica.Api.Tests.Catalogo;

/// <summary>
/// Los traductores de <see cref="ArticulosController"/> son cerrados por defecto: responden solo ante los resultados
/// previstos y cualquier otro lanza, y el manejador de errores responde el 500 genérico. Los dos 409 de la modificación
/// se distinguen por la extensión <c>motivo</c> del ProblemDetails, con un <c>title</c> fijo (D-19).
/// </summary>
public sealed class CierrePorDefectoArticulosTests
{
    private static readonly ArticuloGrabado Articulo = new(7, "ABC-1", "Armazón", 1210m, 50m, 1815m, null, true, 0);

    [Fact]
    public async Task ResponderAlta_ResultadoNoPrevisto_Error500()
    {
        var controller = Crear();

        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.ResponderAltaAsync(null));
    }

    [Fact]
    public async Task ResponderAlta_CadaResultadoPrevisto_SuCodigo()
    {
        var controller = Crear();

        var grabado = Assert.IsType<CreatedAtActionResult>(
            await controller.ResponderAltaAsync(ResultadoAlta.Grabado(Articulo)));
        var invalido = Assert.IsType<ObjectResult>(await controller.ResponderAltaAsync(
            ResultadoAlta.Invalido([new ErrorValidacion(ValidacionArticulo.CampoDescripcion, "accion")])));

        Assert.Equal(StatusCodes.Status201Created, grabado.StatusCode);
        Assert.Same(Articulo, grabado.Value);
        Assert.Equal(7L, grabado.RouteValues?["codigo"]);
        Assert.Equal(StatusCodes.Status400BadRequest, invalido.StatusCode);
    }

    /// <summary>
    /// Un resultado que el traductor no prevé lanza: la búsqueda, la modificación y el cambio de estado sin resultado, y
    /// el cambio de estado con <see cref="ResultadoEdicionArticulo.Inactivo"/>, que su caso de uso nunca devuelve.
    /// </summary>
    [Fact]
    public void Traductores_ResultadoNoPrevisto_Lanzan()
    {
        var controller = Crear();

        Assert.Throws<InvalidOperationException>(() => controller.ResponderBusqueda(null));
        Assert.Throws<InvalidOperationException>(() => controller.ResponderModificacion(null));
        Assert.Throws<InvalidOperationException>(() => controller.ResponderCambioEstado(null));
        Assert.Throws<InvalidOperationException>(
            () => controller.ResponderCambioEstado(ResultadoEdicionArticulo.Inactivo));
    }

    [Fact]
    public void ResponderModificacion_CadaResultadoPrevisto_SuCodigo()
    {
        var controller = Crear();

        var grabado = Assert.IsType<OkObjectResult>(
            controller.ResponderModificacion(ResultadoEdicionArticulo.Grabado(Articulo)));
        var invalido = Assert.IsType<ObjectResult>(controller.ResponderModificacion(
            ResultadoEdicionArticulo.Invalido([new ErrorValidacion(ValidacionArticulo.CampoVersion, "accion")])));
        var inexistente = Assert.IsType<ObjectResult>(
            controller.ResponderModificacion(ResultadoEdicionArticulo.NoEncontrado));

        Assert.Same(Articulo, grabado.Value);
        Assert.Equal(StatusCodes.Status400BadRequest, invalido.StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, inexistente.StatusCode);
    }

    /// <summary>Los dos motivos del 409 (D-19): <c>inactivo</c> y <c>cambiado</c>, con el texto del caso de uso.</summary>
    [Fact]
    public void ResponderModificacion_InactivoYCambiado_409ConSuMotivo()
    {
        var controller = Crear();

        AfirmarConflicto(
            controller.ResponderModificacion(ResultadoEdicionArticulo.Inactivo),
            "inactivo",
            ValidacionArticulo.DetalleInactivo);
        AfirmarConflicto(
            controller.ResponderModificacion(ResultadoEdicionArticulo.CambiadoAlEditar),
            "cambiado",
            ValidacionArticulo.DetalleCambiadoAlEditar);
    }

    [Fact]
    public void ResponderCambioEstado_CadaResultadoPrevisto_SuCodigo()
    {
        var controller = Crear();

        var grabado = Assert.IsType<OkObjectResult>(
            controller.ResponderCambioEstado(ResultadoEdicionArticulo.Grabado(Articulo)));
        var invalido = Assert.IsType<ObjectResult>(controller.ResponderCambioEstado(
            ResultadoEdicionArticulo.Invalido([ValidacionArticulo.ActivoFaltante])));
        var inexistente = Assert.IsType<ObjectResult>(
            controller.ResponderCambioEstado(ResultadoEdicionArticulo.NoEncontrado));

        Assert.Same(Articulo, grabado.Value);
        Assert.Equal(StatusCodes.Status400BadRequest, invalido.StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, inexistente.StatusCode);
        AfirmarConflicto(
            controller.ResponderCambioEstado(ResultadoEdicionArticulo.CambiadoAlCambiarEstado),
            "cambiado",
            ValidacionArticulo.DetalleCambiadoAlCambiarEstado);
    }

    /// <summary>H1: un error de enlace en <c>pagina</c> es el 400 de D-07, sin llegar al caso de uso.</summary>
    [Fact]
    public async Task Listar_ErrorDeEnlaceEnPagina_400EnPagina()
    {
        var controller = Crear();
        controller.ModelState.AddModelError(ValidacionArticulo.CampoPagina, "no se pudo leer");

        var respuesta = Assert.IsType<ObjectResult>(await controller.Listar("armaz", null, CancellationToken.None));

        Assert.Equal(StatusCodes.Status400BadRequest, respuesta.StatusCode);
        var problema = Assert.IsType<ValidationProblemDetails>(respuesta.Value);
        Assert.Equal(
            [ValidacionArticulo.AccionPaginaFueraDeRango], problema.Errors[ValidacionArticulo.CampoPagina]);
        Assert.Single(problema.Errors);
    }

    /// <summary>
    /// H1: un error de enlace en otra clave no se disfraza de error de página: lanza, y el manejador responde el 500
    /// genérico. El caso de uso no se usa (es null): lanzar antes es la única salida posible.
    /// </summary>
    [Theory]
    [InlineData("texto")]
    [InlineData("")]
    [InlineData("otra")]
    public async Task Listar_ErrorDeEnlaceFueraDePagina_Lanza(string clave)
    {
        var controller = Crear();
        controller.ModelState.AddModelError(clave, "no se pudo leer");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => controller.Listar("armaz", null, CancellationToken.None));
    }

    [Fact]
    public async Task Listar_ErrorEnPaginaYEnOtraClave_Lanza()
    {
        var controller = Crear();
        controller.ModelState.AddModelError(ValidacionArticulo.CampoPagina, "no se pudo leer");
        controller.ModelState.AddModelError("texto", "no se pudo leer");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => controller.Listar("armaz", null, CancellationToken.None));
    }

    /// <summary>H3: las guardas de <see cref="RespuestasValidacion.ConflictoArticulo"/>.</summary>
    [Theory]
    [InlineData("otro", "Un texto.")]
    [InlineData("Inactivo", "Un texto.")]
    [InlineData("", "Un texto.")]
    [InlineData(RespuestasValidacion.MotivoInactivo, "")]
    [InlineData(RespuestasValidacion.MotivoCambiado, "   ")]
    public void ConflictoArticulo_MotivoDesconocidoODetalleEnBlanco_Lanza(string motivo, string detalle)
    {
        var controller = Crear();

        Assert.ThrowsAny<ArgumentException>(() => controller.ConflictoArticulo(motivo, detalle));
    }

    [Fact]
    public void ConflictoArticulo_DetalleNulo_Lanza()
    {
        var controller = Crear();

        Assert.ThrowsAny<ArgumentException>(() => controller.ConflictoArticulo(RespuestasValidacion.MotivoCambiado, null!));
    }

    /// <summary>
    /// Alerta de datos: MVC registra en Trace los argumentos de la acción con su <c>ToString()</c>, y el de un record
    /// muestra todos sus valores. Las solicitudes no muestran ninguno, sin depender del nivel del log.
    /// </summary>
    [Fact]
    public void Solicitudes_ToString_NoMuestraDatos()
    {
        var textos = new[]
        {
            new SolicitudArticulo("Zq7-codigo", "Zq7-descripcion", 7654.31m, 43.21m).ToString(),
            new SolicitudModificacionArticulo("Zq7-codigo", "Zq7-descripcion", 7654.31m, 43.21m, 987654321).ToString(),
            new SolicitudEstadoArticulo(false).ToString(),
        };

        foreach (var texto in textos)
        {
            Assert.DoesNotContain("Zq7", texto, StringComparison.Ordinal);
            Assert.DoesNotContain("7654", texto, StringComparison.Ordinal);
            Assert.DoesNotContain("43", texto, StringComparison.Ordinal);
            Assert.DoesNotContain("987654321", texto, StringComparison.Ordinal);
            Assert.DoesNotContain("False", texto, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("=", texto, StringComparison.Ordinal);
        }
    }

    private static void AfirmarConflicto(IActionResult respuesta, string motivo, string detalle)
    {
        var resultado = Assert.IsType<ObjectResult>(respuesta);
        Assert.Equal(StatusCodes.Status409Conflict, resultado.StatusCode);
        Assert.Contains("application/problem+json", resultado.ContentTypes);
        var problema = Assert.IsType<ProblemDetails>(resultado.Value);
        Assert.Equal(StatusCodes.Status409Conflict, problema.Status);
        Assert.Equal("No se pudo grabar el artículo.", problema.Title);
        Assert.Equal(RespuestasValidacion.TituloConflictoArticulo, problema.Title);
        Assert.Equal(detalle, problema.Detail);
        Assert.Equal(motivo, problema.Extensions["motivo"]);
    }

    private static ArticulosController Crear()
    {
        // MVC aporta la fábrica de ProblemDetails que usan las respuestas de error.
        var servicios = new ServiceCollection().AddLogging().AddMvcCore().Services.BuildServiceProvider();
        // Los casos de uso no se usan: la prueba ejercita solo la traducción del resultado.
        return new ArticulosController(null!, null!, null!, null!, null!)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { RequestServices = servicios } },
        };
    }
}
