using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Optica.Api.Controllers;
using Optica.Application.Acceso;
using Optica.Application.Comun;

namespace Optica.Api.Tests.Acceso;

/// <summary>
/// El controller emite la cookie de sesión solo ante un éxito explícito: un resultado no previsto del caso de uso falla
/// cerrado (excepción, que el manejador de errores responde con un 500 genérico) y nunca abre sesión.
/// </summary>
public sealed class CierrePorDefectoTests
{
    private const TipoResultado TipoNoPrevisto = (TipoResultado)99;
    private const ResultadoIngreso IngresoNoPrevisto = (ResultadoIngreso)99;
    private const ResultadoCambio CambioNoPrevisto = (ResultadoCambio)99;

    [Fact]
    public async Task Definir_ResultadoNoPrevisto_FallaSinIniciarSesion()
    {
        var (controller, autenticacion) = Crear();

        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.ResponderDefinicionAsync(TipoNoPrevisto, []));

        Assert.Equal(0, autenticacion.Inicios);
        Assert.Equal(0, controller.Response.Headers.SetCookie.Count);
    }

    [Fact]
    public async Task Ingresar_ResultadoNoPrevisto_FallaSinIniciarSesion()
    {
        var (controller, autenticacion) = Crear();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => controller.ResponderIngresoAsync(new IntentoIngreso(IngresoNoPrevisto, [])));

        Assert.Equal(0, autenticacion.Inicios);
        Assert.Equal(0, controller.Response.Headers.SetCookie.Count);
    }

    [Fact]
    public async Task Ingresar_ResultadoBloqueado_NoEmiteCookie()
    {
        var (controller, autenticacion) = Crear();

        var respuesta = await controller.ResponderIngresoAsync(IntentoIngreso.Bloqueado);

        var resultado = Assert.IsType<ObjectResult>(respuesta);
        Assert.Equal(StatusCodes.Status423Locked, resultado.StatusCode);
        var problema = Assert.IsType<ProblemDetails>(resultado.Value);
        Assert.Equal(AccesoController.MensajeBloqueado, problema.Detail);
        Assert.Equal(0, autenticacion.Inicios);
        Assert.Equal(0, controller.Response.Headers.SetCookie.Count);
    }

    [Fact]
    public void Cambiar_ResultadoNoPrevisto_FallaSinIniciarSesion()
    {
        var (controller, autenticacion) = Crear();

        Assert.Throws<InvalidOperationException>(() => controller.ResponderCambio(new IntentoCambio(CambioNoPrevisto, [])));

        Assert.Equal(0, autenticacion.Inicios);
        Assert.Equal(0, controller.Response.Headers.SetCookie.Count);
    }

    [Fact]
    public void Cambiar_CadaResultadoPrevisto_SuCodigoSinIniciarSesion()
    {
        var (controller, autenticacion) = Crear();

        Assert.IsType<NoContentResult>(controller.ResponderCambio(IntentoCambio.Exito));
        var invalido = Assert.IsType<ObjectResult>(controller.ResponderCambio(
            IntentoCambio.Invalido([new ErrorValidacion(ValidacionContrasena.CampoNueva, "accion")])));
        Assert.Equal(StatusCodes.Status400BadRequest, invalido.StatusCode);
        var sinCredencial = Assert.IsType<ObjectResult>(controller.ResponderCambio(IntentoCambio.SinCredencial));
        Assert.Equal(StatusCodes.Status409Conflict, sinCredencial.StatusCode);
        var conflicto = Assert.IsType<ObjectResult>(controller.ResponderCambio(IntentoCambio.Conflicto));
        Assert.Equal(StatusCodes.Status409Conflict, conflicto.StatusCode);
        Assert.Equal(AccesoController.MensajeCambioEnConflicto, Assert.IsType<ProblemDetails>(conflicto.Value).Detail);

        // Cambiar la contraseña no abre otra sesión: la cookie es la que ya traía el pedido.
        Assert.Equal(0, autenticacion.Inicios);
        Assert.Equal(0, controller.Response.Headers.SetCookie.Count);
    }

    [Fact]
    public async Task ExitoExplicito_IniciaSesion()
    {
        var (controller, autenticacion) = Crear();

        var definicion = await controller.ResponderDefinicionAsync(TipoResultado.Exito, []);
        var ingreso = await controller.ResponderIngresoAsync(IntentoIngreso.Exito);

        Assert.Equal(StatusCodes.Status201Created, Assert.IsType<StatusCodeResult>(definicion).StatusCode);
        Assert.IsType<NoContentResult>(ingreso);
        Assert.Equal(2, autenticacion.Inicios);
    }

    private static (AccesoController Controller, AutenticacionContada Autenticacion) Crear()
    {
        var autenticacion = new AutenticacionContada();
        // MVC aporta la fábrica de ProblemDetails que usan las respuestas de error.
        var servicios = new ServiceCollection()
            .AddSingleton<IAuthenticationService>(autenticacion)
            .AddLogging()
            .AddMvcCore().Services
            .BuildServiceProvider();
        // Los casos de uso no se usan: la prueba ejercita solo la traducción del resultado.
        var controller = new AccesoController(null!, null!, null!, null!, null!)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { RequestServices = servicios } },
        };
        return (controller, autenticacion);
    }

    /// <summary>Cuenta las sesiones iniciadas; el resto no se usa.</summary>
    private sealed class AutenticacionContada : IAuthenticationService
    {
        public int Inicios { get; private set; }

        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties)
        {
            Inicios++;
            return Task.CompletedTask;
        }

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) => throw new NotSupportedException();

        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            throw new NotSupportedException();

        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            throw new NotSupportedException();

        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            throw new NotSupportedException();
    }
}
