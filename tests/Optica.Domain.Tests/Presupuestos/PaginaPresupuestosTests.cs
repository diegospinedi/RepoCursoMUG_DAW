using Optica.Domain.Presupuestos;

namespace Optica.Domain.Tests.Presupuestos;

/// <summary>
/// La página de la búsqueda de presupuestos y sus resúmenes (Block 1 de FEAT-006). Los datos de clientes son
/// ficticios.
/// </summary>
public class PaginaPresupuestosTests
{
    [Fact]
    public void PaginaPresupuestos_Valida_ExponeResumenesYTotal()
    {
        var pagina = new PaginaPresupuestos([Resumen(120), Resumen(119)], 120);

        Assert.Equal([120L, 119L], pagina.Presupuestos.Select(resumen => resumen.Numero));
        Assert.Equal(120, pagina.Total);
        Assert.Empty(new PaginaPresupuestos([], 0).Presupuestos);
    }

    [Fact]
    public void PaginaPresupuestos_TotalInvalido_Rechazado()
    {
        var negativo = Assert.Throws<ArgumentException>(() => new PaginaPresupuestos([], -1));
        var menor = Assert.Throws<ArgumentException>(() => new PaginaPresupuestos([Resumen(7), Resumen(8)], 1));

        // Texto fijo, sin los valores recibidos.
        Assert.Equal("total", negativo.ParamName);
        Assert.Equal("total", menor.ParamName);
        Assert.DoesNotContain("-1", negativo.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("1", menor.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("2", menor.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resumen_ToString_SinDatosDelCliente()
    {
        var resumen = new ResumenPresupuesto(
            155, new DateOnly(2026, 3, 10), "Ficticiez", "Prueboncia", "23456789", EstadoPresupuesto.Final, 2710.02m);
        var pagina = new PaginaPresupuestos([resumen], 1);

        Assert.Equal(155, resumen.Numero);
        Assert.Equal("Ficticiez", resumen.Apellido);
        Assert.Equal("Prueboncia", resumen.Nombre);
        Assert.Equal("23456789", resumen.Dni);
        Assert.Equal(EstadoPresupuesto.Final, resumen.Estado);
        Assert.Equal(2710.02m, resumen.Total);

        // Ley 25.326: ni el resumen ni la página muestran Apellido, Nombre o DNI.
        foreach (var texto in new[] { resumen.ToString(), pagina.ToString() })
        {
            Assert.NotNull(texto);
            foreach (var dato in new[] { "Ficticiez", "Prueboncia", "23456789" })
            {
                Assert.DoesNotContain(dato, texto, StringComparison.Ordinal);
            }
        }
    }

    private static ResumenPresupuesto Resumen(long numero) =>
        new(numero, new DateOnly(2026, 1, 5), "Gómez", "Luis", "30111222", EstadoPresupuesto.Borrador, 100m);
}
