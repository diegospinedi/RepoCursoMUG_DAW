using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Optica.Api.Controllers;
using Optica.Application.Catalogo;
using Optica.Application.Comun;

namespace Optica.Api.Tests.Catalogo;

/// <summary>
/// <c>ResponderAltaAsync</c> es cerrado por defecto: responde 201 solo ante un alta grabada y 400 solo ante una
/// inválida con errores; cualquier otro resultado lanza, y el manejador de errores responde el 500 genérico.
/// </summary>
public sealed class CierrePorDefectoArticulosTests
{
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
        var articulo = new ArticuloGrabado(7, "ABC-1", "Armazón", 1210m, 50m, 1815m, null, true, 0);

        var grabado = Assert.IsType<CreatedAtActionResult>(
            await controller.ResponderAltaAsync(ResultadoAlta.Grabado(articulo)));
        var invalido = Assert.IsType<ObjectResult>(await controller.ResponderAltaAsync(
            ResultadoAlta.Invalido([new ErrorValidacion(ValidacionArticulo.CampoDescripcion, "accion")])));

        Assert.Equal(StatusCodes.Status201Created, grabado.StatusCode);
        Assert.Same(articulo, grabado.Value);
        Assert.Equal(7L, grabado.RouteValues?["codigo"]);
        Assert.Equal(StatusCodes.Status400BadRequest, invalido.StatusCode);
    }

    private static ArticulosController Crear()
    {
        // MVC aporta la fábrica de ProblemDetails que usan las respuestas de error.
        var servicios = new ServiceCollection().AddLogging().AddMvcCore().Services.BuildServiceProvider();
        // Los casos de uso no se usan: la prueba ejercita solo la traducción del resultado.
        return new ArticulosController(null!, null!)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { RequestServices = servicios } },
        };
    }
}
