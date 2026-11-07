using Optica.Domain.Catalogo;
using Optica.Domain.Comun;
using Optica.Domain.Presupuestos;

namespace Optica.Domain.Tests.Presupuestos;

public class LineaPresupuestoTests
{
    private const string Descripcion = "Armazón metálico";
    private const decimal ImporteMaximo = ReglasImporte.ImporteMaximo;

    private static Articulo ArticuloCon(decimal precioVenta, bool activo = true, long? codigo = 7) =>
        new(codigo, "ABC-1", Descripcion, precioCosto: 1000m, margenUtilidad: 85m, precioVenta, activo, version: 3);

    [Fact]
    public void DesdeArticulo_PrecioMenos150_Rechazado()
    {
        var resultado = LineaPresupuesto.DesdeArticulo(ArticuloCon(-150m));

        Assert.True(resultado.EsPrecioNegativo);
        Assert.False(resultado.EsValida);
        Assert.False(resultado.EsInactivo);
        Assert.Null(resultado.Linea);
    }

    [Fact]
    public void DesdeArticulo_Inactivo_Rechazado()
    {
        var resultado = LineaPresupuesto.DesdeArticulo(ArticuloCon(1850m, activo: false));

        Assert.True(resultado.EsInactivo);
        Assert.False(resultado.EsValida);
        Assert.False(resultado.EsPrecioNegativo);
        Assert.Null(resultado.Linea);

        // Un artículo sin código no salió del catálogo: es un error de programación, no un rechazo.
        var error = Assert.Throws<ArgumentException>(() => LineaPresupuesto.DesdeArticulo(ArticuloCon(1850m, codigo: null)));
        Assert.Equal("articulo", error.ParamName);
        Assert.Throws<ArgumentNullException>(() => LineaPresupuesto.DesdeArticulo(null!));
    }

    [Fact]
    public void DesdeArticulo_1850_CopiaCodigoDescripcionYPrecio()
    {
        var resultado = LineaPresupuesto.DesdeArticulo(ArticuloCon(1850m));

        Assert.True(resultado.EsValida);
        Assert.Equal(7, resultado.Linea.CodigoArticulo);
        Assert.Equal(Descripcion, resultado.Linea.Descripcion);
        Assert.Equal(1850.00m, resultado.Linea.PrecioUnitario);
    }

    [Fact]
    public void DesdeArticulo_1850_Cantidad1Descuento0YPreciosIguales()
    {
        var resultado = LineaPresupuesto.DesdeArticulo(ArticuloCon(1850m));

        Assert.True(resultado.EsValida);
        var linea = resultado.Linea;
        Assert.Equal(7, linea.CodigoArticulo);
        Assert.Equal(Descripcion, linea.Descripcion);
        Assert.Equal(1850m, linea.PrecioUnitario);
        Assert.Equal(1, linea.Cantidad);
        Assert.Equal(0m, linea.PorcentajeDescuento);
        Assert.Equal(1850m, linea.PrecioConDescuento);
        Assert.Equal(1850m, linea.PrecioFinal);
    }

    [Fact]
    public void DesdeArticulo_PrecioCero_SeAdmite()
    {
        var resultado = LineaPresupuesto.DesdeArticulo(ArticuloCon(0m));

        Assert.True(resultado.EsValida);
        Assert.Equal(7, resultado.Linea.CodigoArticulo);
        Assert.Equal(0m, resultado.Linea.PrecioUnitario);
        Assert.Equal(0m, resultado.Linea.PrecioConDescuento);
        Assert.Equal(0m, resultado.Linea.PrecioFinal);
        Assert.Equal(1, resultado.Linea.Cantidad);
    }

    [Fact]
    public void ConstructorLinea_InvariantesRotas_Error()
    {
        // La línea válida que se rompe de a un campo; los valores rotos son fáciles de buscar en el mensaje.
        var valida = new LineaPresupuesto(7, Descripcion, 1850m, 1, 0m, 1850m, 1850m);
        Assert.Equal(7, valida.CodigoArticulo);
        _ = new LineaPresupuesto(7, new string('d', 200), 0m, 1, 100m, 0m, 0m);
        _ = new LineaPresupuesto(7, Descripcion, ImporteMaximo, 1, 12.5m, ImporteMaximo, ImporteMaximo);

        var casos = new (string Campo, string Valor, Func<LineaPresupuesto> Crear)[]
        {
            ("codigoArticulo", "-98765", () => new LineaPresupuesto(-98765, Descripcion, 1850m, 1, 0m, 1850m, 1850m)),
            ("codigoArticulo", "0", () => new LineaPresupuesto(0, Descripcion, 1850m, 1, 0m, 1850m, 1850m)),
            ("descripcion", "", () => new LineaPresupuesto(7, "", 1850m, 1, 0m, 1850m, 1850m)),
            ("descripcion", "   ", () => new LineaPresupuesto(7, "   ", 1850m, 1, 0m, 1850m, 1850m)),
            ("descripcion", new string('d', 201), () => new LineaPresupuesto(7, new string('d', 201), 1850m, 1, 0m, 1850m, 1850m)),
            ("descripcion", "a\uDC00b", () => new LineaPresupuesto(7, "a\uDC00b", 1850m, 1, 0m, 1850m, 1850m)),
            ("descripcion", "null", () => new LineaPresupuesto(7, null!, 1850m, 1, 0m, 1850m, 1850m)),

            // Sin recortar: el largo de 200 se mide sobre el texto guardado.
            ("descripcion", "  x  ", () => new LineaPresupuesto(7, "  x  ", 1850m, 1, 0m, 1850m, 1850m)),
            ("precioUnitario", "-150", () => new LineaPresupuesto(7, Descripcion, -150m, 1, 0m, 1850m, 1850m)),
            ("precioUnitario", "1850.555", () => new LineaPresupuesto(7, Descripcion, 1850.555m, 1, 0m, 1850m, 1850m)),
            ("precioUnitario", "92233720368547758.08", () => new LineaPresupuesto(7, Descripcion, ImporteMaximo + 0.01m, 1, 0m, 1850m, 1850m)),
            ("cantidad", "-4321", () => new LineaPresupuesto(7, Descripcion, 1850m, -4321, 0m, 1850m, 1850m)),
            ("cantidad", "0", () => new LineaPresupuesto(7, Descripcion, 1850m, 0, 0m, 1850m, 1850m)),
            ("porcentajeDescuento", "-1", () => new LineaPresupuesto(7, Descripcion, 1850m, 1, -1m, 1850m, 1850m)),
            ("porcentajeDescuento", "100.01", () => new LineaPresupuesto(7, Descripcion, 1850m, 1, 100.01m, 1850m, 1850m)),
            ("porcentajeDescuento", "12.345", () => new LineaPresupuesto(7, Descripcion, 1850m, 1, 12.345m, 1850m, 1850m)),
            ("precioConDescuento", "-777", () => new LineaPresupuesto(7, Descripcion, 1850m, 1, 0m, -777m, 1850m)),
            ("precioConDescuento", "1.001", () => new LineaPresupuesto(7, Descripcion, 1850m, 1, 0m, 1.001m, 1850m)),
            ("precioFinal", "-888", () => new LineaPresupuesto(7, Descripcion, 1850m, 1, 0m, 1850m, -888m)),
            ("precioFinal", "2.009", () => new LineaPresupuesto(7, Descripcion, 1850m, 1, 0m, 1850m, 2.009m)),
        };

        foreach (var (campo, valor, crear) in casos)
        {
            var error = Assert.Throws<ArgumentException>(crear);
            Assert.Equal(campo, error.ParamName);
            if (valor.Trim().Length > 0)
            {
                Assert.DoesNotContain(valor, error.Message, StringComparison.Ordinal);
            }
        }

        // Rama defensiva: el resultado válido siempre lleva una línea.
        Assert.Throws<ArgumentNullException>(() => ResultadoLinea.Valida(null!));
    }
}
