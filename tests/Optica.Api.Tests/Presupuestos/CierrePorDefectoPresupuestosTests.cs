using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Optica.Api.Controllers;
using Optica.Api.Errores;
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
        Assert.Throws<InvalidOperationException>(() => controller.ResponderModificacion(null));
        Assert.Throws<InvalidOperationException>(() => controller.ResponderPasoAFinal(null));
    }

    /// <summary>
    /// FEAT-003c: los traductores de la modificación y del paso a Final responden cada resultado previsto: inválido 400,
    /// no encontrado 404, final y cambiado 409 con su <c>motivo</c> y el texto fijo de la aplicación.
    /// </summary>
    [Fact]
    public void ResponderEdicion_ResultadosPrevistos_SuCodigo()
    {
        var controller = Crear();
        var traductores = new Func<ResultadoEdicionPresupuesto?, IActionResult>[]
        {
            controller.ResponderModificacion, controller.ResponderPasoAFinal,
        };

        foreach (var traducir in traductores)
        {
            var invalido = Assert.IsType<ObjectResult>(traducir(ResultadoEdicionPresupuesto.Invalido(
                [new ErrorValidacion(ValidacionPresupuesto.CampoVersion, ValidacionPresupuesto.AccionVersionInvalida)])));
            Assert.Equal(StatusCodes.Status400BadRequest, invalido.StatusCode);

            var noEncontrado = Assert.IsType<ObjectResult>(traducir(ResultadoEdicionPresupuesto.NoEncontrado));
            Assert.Equal(StatusCodes.Status404NotFound, noEncontrado.StatusCode);

            foreach (var (resultado, motivo, detalle) in new[]
            {
                (ResultadoEdicionPresupuesto.Final, "final", ValidacionPresupuesto.DetalleFinal),
                (ResultadoEdicionPresupuesto.Cambiado, "cambiado", ValidacionPresupuesto.DetalleCambiado),
            })
            {
                var conflicto = Assert.IsType<ObjectResult>(traducir(resultado));
                Assert.Equal(StatusCodes.Status409Conflict, conflicto.StatusCode);
                var problema = Assert.IsType<ProblemDetails>(conflicto.Value);
                Assert.Equal("No se pudo grabar el presupuesto.", problema.Title);
                Assert.Equal(detalle, problema.Detail);
                Assert.Equal(motivo, problema.Extensions["motivo"]);
            }
        }

        Assert.Equal("El presupuesto cambió mientras lo editabas. Volvé a abrirlo.", ValidacionPresupuesto.DetalleCambiado);
        Assert.Equal("El presupuesto está en Final y no se puede modificar.", ValidacionPresupuesto.DetalleFinal);
    }

    /// <summary>El helper del 409 de presupuestos solo acepta sus dos motivos; el de artículos sigue con los suyos.</summary>
    [Fact]
    public void ConflictoPresupuesto_MotivoAjeno_Lanza()
    {
        var controller = Crear();

        Assert.Throws<ArgumentException>(() => controller.ConflictoPresupuesto("inactivo", "Texto."));
        Assert.Throws<ArgumentException>(() => controller.ConflictoArticulo(RespuestasValidacion.MotivoFinal, "Texto."));
        Assert.Throws<ArgumentException>(() => controller.ConflictoPresupuesto(RespuestasValidacion.MotivoFinal, " "));
    }

    /// <summary>FEAT-003c: <c>version</c> y <c>lineas[i].orden</c> tienen su propio campo en un error de lectura.</summary>
    [Theory]
    [InlineData("$.version", "version")]
    [InlineData("Version", "version")]
    [InlineData("$.lineas[2].orden", "lineas[2].orden")]
    [InlineData("$.Lineas[0].Orden", "lineas[0].orden")]
    [InlineData("$.lineas[100].orden", "lineas")]
    [InlineData("$.versionX", null)]
    public void CampoDeLaClave_VersionYOrden(string clave, string? campo)
    {
        Assert.Equal(campo, PresupuestosController.CampoDeLaClave(clave));
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
    /// D-09: la cantidad y el descuento de la línea i tienen su propio campo, sin distinguir mayúsculas en la clave; el
    /// código de artículo sigue en <c>lineas[i]</c>, y una línea desde la 100 sigue en <c>lineas</c>.
    /// </summary>
    [Theory]
    [InlineData("$.lineas[1].cantidad", "lineas[1].cantidad")]
    [InlineData("lineas[0].cantidad", "lineas[0].cantidad")]
    [InlineData("$.Lineas[2].Cantidad", "lineas[2].cantidad")]
    [InlineData("$.lineas[99].porcentajeDescuento", "lineas[99].porcentajeDescuento")]
    [InlineData("$.lineas[3].PorcentajeDescuento", "lineas[3].porcentajeDescuento")]
    [InlineData("$.lineas[1].codigoArticulo", "lineas[1]")]
    [InlineData("$.lineas[1].cantidadX", "lineas[1]")]
    [InlineData("$.lineas[100].cantidad", "lineas")]
    [InlineData("$.lineas[100].porcentajeDescuento", "lineas")]
    public void CampoDeLaClave_CamposDeLinea(string clave, string campo)
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
            new SolicitudLinea(987654321, 7531, 12.34m).ToString(),
            new SolicitudPresupuesto(cliente, [new SolicitudLinea(987654321, 7531, 12.34m)]).ToString(),
            new DatosLinea(987654321, 7531, 12.34m).ToString(),
            new DatosPresupuesto(null, [new DatosLinea(987654321, 7531, 12.34m)]).ToString(),
            // FEAT-003c: la línea con orden y las solicitudes de modificación y de estado.
            new SolicitudLinea(987654321, 7531, 12.34m, 7531).ToString(),
            new SolicitudModificacionPresupuesto(987654321, cliente, [new SolicitudLinea(987654321, 7531, 12.34m, 7531)])
                .ToString(),
            new SolicitudEstadoPresupuesto("Zq7-estado", 987654321).ToString(),
            new DatosLinea(987654321, 7531, 12.34m, 7531).ToString(),
            // FEAT-006: la solicitud de la búsqueda (ADR-019).
            new SolicitudBusquedaPresupuestos("Zq7Apellido", "Zq7Nombre", "30111222", "2026-03-10", "2026-03-20", 7531)
                .ToString(),
        };

        foreach (var texto in textos)
        {
            Assert.DoesNotContain("Zq7", texto, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("30111222", texto, StringComparison.Ordinal);
            Assert.DoesNotContain("5555", texto, StringComparison.Ordinal);
            Assert.DoesNotContain("987654321", texto, StringComparison.Ordinal);
            Assert.DoesNotContain("7531", texto, StringComparison.Ordinal);
            Assert.DoesNotContain("12.34", texto, StringComparison.Ordinal);
            Assert.DoesNotContain("12,34", texto, StringComparison.Ordinal);
            Assert.DoesNotContain("=", texto, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// FEAT-006 (ADR-019, Ley 25.326): la solicitud de la búsqueda y la respuesta de la página no muestran Apellido,
    /// Nombre, DNI, fechas, página ni importes en <see cref="object.ToString"/>; MVC registra en Trace los argumentos de la
    /// acción. Datos ficticios.
    /// </summary>
    [Fact]
    public void SolicitudBusqueda_ToString_SinValores()
    {
        var textos = new[]
        {
            new SolicitudBusquedaPresupuestos("Zq7Apellido", "Zq7Nombre", "30111222", "2026-03-10", "2026-03-20", 7531)
                .ToString(),
            new ListadoPresupuestos(
                [new PresupuestoListado(
                    987654321, new DateOnly(2026, 3, 10), "Zq7Apellido", "Zq7Nombre", "30111222", "final", 12.34m)],
                7531,
                50,
                7531).ToString(),
        };

        foreach (var texto in textos)
        {
            Assert.DoesNotContain("Zq7", texto, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("30111222", texto, StringComparison.Ordinal);
            Assert.DoesNotContain("2026", texto, StringComparison.Ordinal);
            Assert.DoesNotContain("987654321", texto, StringComparison.Ordinal);
            Assert.DoesNotContain("7531", texto, StringComparison.Ordinal);
            Assert.DoesNotContain("12.34", texto, StringComparison.Ordinal);
            Assert.DoesNotContain("12,34", texto, StringComparison.Ordinal);
            Assert.DoesNotContain("=", texto, StringComparison.Ordinal);
        }
    }

    /// <summary>El traductor de la búsqueda es cerrado por defecto: un resultado no previsto lanza; inválido, 400.</summary>
    [Fact]
    public void ResponderBusqueda_Cerrado()
    {
        var controller = Crear();

        Assert.Throws<InvalidOperationException>(() => controller.ResponderBusqueda(null));
        var invalido = Assert.IsType<ObjectResult>(controller.ResponderBusqueda(ResultadoBusquedaPresupuestos.Invalido(
            [new ErrorValidacion(ValidacionPresupuesto.CampoPagina, ValidacionPresupuesto.AccionPaginaFueraDeRango)])));
        Assert.Equal(StatusCodes.Status400BadRequest, invalido.StatusCode);
    }

    private static PresupuestosController Crear()
    {
        // MVC aporta la fábrica de ProblemDetails que usan las respuestas de error.
        var servicios = new ServiceCollection().AddLogging().AddMvcCore().Services.BuildServiceProvider();
        // Los casos de uso no se usan: la prueba ejercita solo la traducción del resultado.
        return new PresupuestosController(null!, null!, null!, null!, null!)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { RequestServices = servicios } },
        };
    }
}
