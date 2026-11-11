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
        var resultado = LineaPresupuesto.DesdeArticulo(ArticuloCon(-150m), 1, 0m);

        Assert.True(resultado.EsPrecioNegativo);
        Assert.False(resultado.EsValida);
        Assert.False(resultado.EsInactivo);
        Assert.Null(resultado.Linea);
    }

    [Fact]
    public void DesdeArticulo_Inactivo_Rechazado()
    {
        var resultado = LineaPresupuesto.DesdeArticulo(ArticuloCon(1850m, activo: false), 1, 0m);

        Assert.True(resultado.EsInactivo);
        Assert.False(resultado.EsValida);
        Assert.False(resultado.EsPrecioNegativo);
        Assert.Null(resultado.Linea);

        // Un artículo sin código no salió del catálogo: es un error de programación, no un rechazo.
        var error = Assert.Throws<ArgumentException>(() => LineaPresupuesto.DesdeArticulo(ArticuloCon(1850m, codigo: null), 1, 0m));
        Assert.Equal("articulo", error.ParamName);
        Assert.Throws<ArgumentNullException>(() => LineaPresupuesto.DesdeArticulo(null!, 1, 0m));
    }

    [Fact]
    public void DesdeArticulo_1850_CopiaCodigoDescripcionYPrecio()
    {
        var resultado = LineaPresupuesto.DesdeArticulo(ArticuloCon(1850m), 1, 0m);

        Assert.True(resultado.EsValida);
        Assert.Equal(7, resultado.Linea.CodigoArticulo);
        Assert.Equal(Descripcion, resultado.Linea.Descripcion);
        Assert.Equal(1850.00m, resultado.Linea.PrecioUnitario);
    }

    [Fact]
    public void DesdeArticulo_1850_Cantidad1Descuento0YPreciosIguales()
    {
        var resultado = LineaPresupuesto.DesdeArticulo(ArticuloCon(1850m), 1, 0m);

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
        var resultado = LineaPresupuesto.DesdeArticulo(ArticuloCon(0m), 1, 0m);

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
            ("cantidad", "10000", () => new LineaPresupuesto(7, Descripcion, 1850m, 10_000, 0m, 1850m, 1850m)),
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

    [Fact]
    public void DesdeArticulo_RechazosDeFeat003a_AntesQueCantidadYDescuento()
    {
        // Un artículo inactivo se rechaza como inactivo, sean cuales sean la cantidad y el descuento (FR-09, D-14).
        foreach (var (cantidad, descuento) in new[] { (0, 0m), (10_000, 0m), (1, -0.01m), (1, 100.01m), (0, 10.555m) })
        {
            Assert.True(LineaPresupuesto.DesdeArticulo(ArticuloCon(1850m, activo: false), cantidad, descuento).EsInactivo);
            Assert.True(LineaPresupuesto.DesdeArticulo(ArticuloCon(-150m), cantidad, descuento).EsPrecioNegativo);
        }
    }

    [Fact]
    public void DesdeArticulo_CantidadYDescuento_CalculaPrecios()
    {
        // AC-01: Precio Unitario 1.000,00, Cantidad 3 y Descuento 10 dan 900,00 y 2.700,00.
        var resultado = LineaPresupuesto.DesdeArticulo(ArticuloCon(1000m), 3, 10m);

        Assert.True(resultado.EsValida);
        var linea = resultado.Linea;
        Assert.Equal(1000.00m, linea.PrecioUnitario);
        Assert.Equal(3, linea.Cantidad);
        Assert.Equal(10m, linea.PorcentajeDescuento);
        Assert.Equal(900.00m, linea.PrecioConDescuento);
        Assert.Equal(2700.00m, linea.PrecioFinal);

        // AC-06: el Precio Final usa el Precio con Descuento ya redondeado.
        var ac06 = LineaPresupuesto.DesdeArticulo(ArticuloCon(6.67m), 3, 50m);
        Assert.True(ac06.EsValida);
        Assert.Equal(3.34m, ac06.Linea.PrecioConDescuento);
        Assert.Equal(10.02m, ac06.Linea.PrecioFinal);

        // Los extremos de la cantidad (D-01) y del descuento (AC-04).
        var maxima = LineaPresupuesto.DesdeArticulo(ArticuloCon(1000m), 9_999, 100m);
        Assert.True(maxima.EsValida);
        Assert.Equal(9_999, maxima.Linea.Cantidad);
        Assert.Equal(0m, maxima.Linea.PrecioConDescuento);
        Assert.Equal(0m, maxima.Linea.PrecioFinal);
    }

    [Fact]
    public void Constructor_CantidadInvalida_Rechazada()
    {
        // AC-09: 10.000 supera el tope de D-01; 0 no es mayor que 0 (AC-02).
        _ = new LineaPresupuesto(7, Descripcion, 1850m, 9_999, 0m, 1850m, 1850m * 9_999);

        foreach (var cantidad in new[] { 0, 10_000, int.MaxValue, int.MinValue })
        {
            var error = Assert.Throws<ArgumentException>(
                () => new LineaPresupuesto(7, Descripcion, 1850m, cantidad, 0m, 1850m, 1850m));
            Assert.Equal("cantidad", error.ParamName);

            var errorAlta = Assert.Throws<ArgumentException>(
                () => LineaPresupuesto.DesdeArticulo(ArticuloCon(1850m), cantidad, 0m));
            Assert.Equal("cantidad", errorAlta.ParamName);
        }
    }

    [Fact]
    public void Constructor_DescuentoInvalido_Rechazado()
    {
        foreach (var descuento in new[] { -0.01m, 100.01m, 10.555m })
        {
            var error = Assert.Throws<ArgumentException>(
                () => new LineaPresupuesto(7, Descripcion, 1850m, 1, descuento, 1850m, 1850m));
            Assert.Equal("porcentajeDescuento", error.ParamName);

            var errorAlta = Assert.Throws<ArgumentException>(
                () => LineaPresupuesto.DesdeArticulo(ArticuloCon(1850m), 1, descuento));
            Assert.Equal("porcentajeDescuento", errorAlta.ParamName);
        }
    }

    [Fact]
    public void DesdeArticulo_ImporteExcedido_Rechazado()
    {
        // Justo en el límite del tipo: se acepta.
        var enElLimite = LineaPresupuesto.DesdeArticulo(ArticuloCon(ImporteMaximo), 1, 0m);
        Assert.True(enElLimite.EsValida);
        Assert.False(enElLimite.EsImporteExcedido);
        Assert.Equal(ImporteMaximo, enElLimite.Linea.PrecioFinal);

        // ImporteMaximo × 50 % = …879,035 → …879,04; × 2 = …758,08: un centésimo por encima del límite.
        foreach (var (cantidad, descuento) in new[] { (2, 0m), (2, 50m), (9_999, 0m) })
        {
            var resultado = LineaPresupuesto.DesdeArticulo(ArticuloCon(ImporteMaximo), cantidad, descuento);

            Assert.True(resultado.EsImporteExcedido);
            Assert.False(resultado.EsValida);
            Assert.False(resultado.EsInactivo);
            Assert.False(resultado.EsPrecioNegativo);
            Assert.Null(resultado.Linea);
        }

        // Los rechazos de FEAT-003a no cambian: inactivo y precio negativo antes que el importe.
        Assert.False(LineaPresupuesto.DesdeArticulo(ArticuloCon(-150m), 1, 0m).EsImporteExcedido);
        Assert.False(ResultadoLinea.Inactivo.EsImporteExcedido);
        Assert.True(ResultadoLinea.ImporteExcedido.EsImporteExcedido);
    }

    [Fact]
    public void Excepciones_NoRepitenElValor()
    {
        var casos = new (string Valor, Action Crear)[]
        {
            ("10000", () => _ = new LineaPresupuesto(7, Descripcion, 1850m, 10_000, 0m, 1850m, 1850m)),
            ("4321", () => _ = new LineaPresupuesto(7, Descripcion, 1850m, -4321, 0m, 1850m, 1850m)),
            ("10.555", () => _ = new LineaPresupuesto(7, Descripcion, 1850m, 1, 10.555m, 1850m, 1850m)),
            ("100.01", () => _ = new LineaPresupuesto(7, Descripcion, 1850m, 1, 100.01m, 1850m, 1850m)),
            ("10000", () => LineaPresupuesto.DesdeArticulo(ArticuloCon(1850m), 10_000, 0m)),
            ("4321", () => LineaPresupuesto.DesdeArticulo(ArticuloCon(1850m), -4321, 0m)),
            ("10.555", () => LineaPresupuesto.DesdeArticulo(ArticuloCon(1850m), 1, 10.555m)),
            ("100.01", () => LineaPresupuesto.DesdeArticulo(ArticuloCon(1850m), 1, 100.01m)),
        };

        foreach (var (valor, crear) in casos)
        {
            var error = Assert.Throws<ArgumentException>(crear);
            Assert.DoesNotContain(valor, error.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(valor.Replace('.', ','), error.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// AC-09, D-02: la línea grabada con el artículo a $1.815,00 conserva ese Precio Unitario aunque el catálogo ya
    /// lo tenga a $1.850,00; la nueva Cantidad y el descuento se calculan sobre el precio grabado.
    /// </summary>
    [Fact]
    public void ConCantidadYDescuento_ConservaPrecioUnitarioGrabado()
    {
        var grabada = LineaPresupuesto.DesdeArticulo(ArticuloCon(1815m), 1, 0m).Linea!;

        // Aunque el catálogo pase a $1.850,00, ConCantidadYDescuento no recibe el artículo: solo puede usar el Precio
        // Unitario grabado en la línea.
        var resultado = grabada.ConCantidadYDescuento(3, 10m);

        Assert.True(resultado.EsValida);
        var linea = resultado.Linea;
        Assert.Equal(7, linea.CodigoArticulo);
        Assert.Equal(Descripcion, linea.Descripcion);
        Assert.Equal(1815.00m, linea.PrecioUnitario);
        Assert.Equal(3, linea.Cantidad);
        Assert.Equal(10m, linea.PorcentajeDescuento);

        // 1.815,00 × 0,90 = 1.633,50; × 3 = 4.900,50.
        Assert.Equal(1633.50m, linea.PrecioConDescuento);
        Assert.Equal(4900.50m, linea.PrecioFinal);

        // La línea original no cambia.
        Assert.Equal(1, grabada.Cantidad);
        Assert.Equal(0m, grabada.PorcentajeDescuento);
        Assert.Equal(1815.00m, grabada.PrecioFinal);

        // La cantidad y el descuento siguen las reglas de CalculoLinea.
        Assert.Equal("cantidad", Assert.Throws<ArgumentException>(() => grabada.ConCantidadYDescuento(0, 0m)).ParamName);
        Assert.Equal(
            "cantidad", Assert.Throws<ArgumentException>(() => grabada.ConCantidadYDescuento(10_000, 0m)).ParamName);

        // Cada caso de descuento rompe una sola regla: 100,01 solo el rango; 10,555 solo la escala.
        Assert.Equal(
            "porcentajeDescuento",
            Assert.Throws<ArgumentException>(() => grabada.ConCantidadYDescuento(1, 100.01m)).ParamName);
        Assert.Equal(
            "porcentajeDescuento",
            Assert.Throws<ArgumentException>(() => grabada.ConCantidadYDescuento(1, 10.555m)).ParamName);
    }

    [Fact]
    public void ConCantidadYDescuento_ImporteExcedido_Rechazado()
    {
        var grabada = new LineaPresupuesto(7, Descripcion, ImporteMaximo, 1, 0m, ImporteMaximo, ImporteMaximo);

        // Justo en el límite del tipo: se acepta.
        var enElLimite = grabada.ConCantidadYDescuento(1, 0m);
        Assert.True(enElLimite.EsValida);
        Assert.Equal(ImporteMaximo, enElLimite.Linea.PrecioFinal);

        var resultado = grabada.ConCantidadYDescuento(2, 0m);

        Assert.True(resultado.EsImporteExcedido);
        Assert.False(resultado.EsValida);
        Assert.False(resultado.EsInactivo);
        Assert.False(resultado.EsPrecioNegativo);
        Assert.Null(resultado.Linea);
    }
}
