using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Optica.Api.Controllers;
using Optica.Application.Comun;
using Optica.Application.Presupuestos;

namespace Optica.Api.Tests.Presupuestos;

/// <summary>
/// El traductor del alta de <see cref="PresupuestosController"/> es cerrado por defecto: responde solo ante los
/// resultados previstos y cualquier otro lanza, y el manejador de errores responde el 500 genérico. Las solicitudes no
/// muestran los datos del cliente en <see cref="object.ToString"/> (D-17).
/// </summary>
public sealed class CierrePorDefectoPresupuestosTests
{
    [Fact]
    public void Traductores_ResultadoNoPrevisto_Lanzan()
    {
        var controller = Crear();

        Assert.Throws<InvalidOperationException>(() => controller.ResponderAlta(null));
    }

    [Fact]
    public void ResponderAlta_Invalido_400ConLosErrores()
    {
        var controller = Crear();

        var invalido = Assert.IsType<ObjectResult>(controller.ResponderAlta(ResultadoAltaPresupuesto.Invalido(
            [new ErrorValidacion(ValidacionPresupuesto.CampoDni, ValidacionPresupuesto.AccionDniFaltante)])));

        Assert.Equal(StatusCodes.Status400BadRequest, invalido.StatusCode);
        var problema = Assert.IsType<ValidationProblemDetails>(invalido.Value);
        Assert.Equal([ValidacionPresupuesto.AccionDniFaltante], problema.Errors[ValidacionPresupuesto.CampoDni]);
    }

    /// <summary>
    /// El campo de D-20 que nombra la clave de un error del lector de JSON, o null si no nombra ninguno (entonces el
    /// cuerpo ilegible va en <c>apellido</c>). Casos:
    /// <list type="bullet">
    /// <item>la línea i, con o sin la propiedad de adentro y con o sin el prefijo <c>$.</c>, es <c>lineas[i]</c> de 0 a
    /// 99;</item>
    /// <item>una línea desde la 100 (fuera de las que se aceptan) es <c>lineas</c>, como la lista entera;</item>
    /// <item>un índice vacío, negativo o con texto después del corchete, y otra clave que empieza con "lineas", no nombran
    /// nada;</item>
    /// <item>un dato del cliente es su campo, sin distinguir mayúsculas; un dato que no existe, o el cliente entero, no
    /// nombran nada; la raíz y la clave vacía, tampoco.</item>
    /// </list>
    /// </summary>
    [Theory]
    [InlineData("$.lineas[0]", "lineas[0]")]
    [InlineData("$.lineas[0].codigoArticulo", "lineas[0]")]
    [InlineData("lineas[1]", "lineas[1]")]
    [InlineData("$.lineas[99]", "lineas[99]")]
    [InlineData("$.lineas[100]", "lineas")]
    [InlineData("$.lineas[100].codigoArticulo", "lineas")]
    [InlineData("$.lineas", "lineas")]
    [InlineData("$.lineas[]", null)]
    [InlineData("$.lineas[-1]", null)]
    [InlineData("$.lineas[0]x", null)]
    [InlineData("$.lineas[x]", null)]
    [InlineData("$.lineasX", null)]
    [InlineData("$.cliente.dni", "dni")]
    [InlineData("$.Cliente.DNI", "dni")]
    [InlineData("cliente.telefono", "telefono")]
    [InlineData("$.cliente.otro", null)]
    [InlineData("$.cliente", null)]
    [InlineData("$", null)]
    [InlineData("", null)]
    public void CampoDeLaClave_CadaClave_SuCampo(string clave, string? campo)
    {
        Assert.Equal(campo, PresupuestosController.CampoDeLaClave(clave));
    }

    /// <summary>
    /// Alerta de datos (D-17, Ley 25.326): MVC registra en Trace los argumentos de la acción con su <c>ToString()</c>, y
    /// el de un record muestra todos sus valores. Las tres solicitudes no muestran ninguno, sin depender del nivel del log.
    /// Datos ficticios.
    /// </summary>
    [Fact]
    public void Solicitudes_ToString_SinValores()
    {
        var cliente = new SolicitudCliente(
            "Zq7Apellido", "Zq7Nombre", "30111222", "Zq7 Calle Ficticia 123", "zq7@correo-ficticio.com", "011-5555-0199");
        var textos = new[]
        {
            cliente.ToString(),
            new SolicitudLinea(987654321).ToString(),
            new SolicitudPresupuesto(cliente, [new SolicitudLinea(987654321)]).ToString(),
        };

        foreach (var texto in textos)
        {
            Assert.DoesNotContain("Zq7", texto, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("30111222", texto, StringComparison.Ordinal);
            Assert.DoesNotContain("5555", texto, StringComparison.Ordinal);
            Assert.DoesNotContain("987654321", texto, StringComparison.Ordinal);
            Assert.DoesNotContain("=", texto, StringComparison.Ordinal);
        }
    }

    private static PresupuestosController Crear()
    {
        // MVC aporta la fábrica de ProblemDetails que usan las respuestas de error.
        var servicios = new ServiceCollection().AddLogging().AddMvcCore().Services.BuildServiceProvider();
        // Los casos de uso no se usan: la prueba ejercita solo la traducción del resultado.
        return new PresupuestosController(null!, null!)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { RequestServices = servicios } },
        };
    }
}
