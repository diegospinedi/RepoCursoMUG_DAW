using System.Globalization;
using Optica.Application.Comun;
using Optica.Application.Presupuestos;
using Optica.Domain.Catalogo;
using Optica.Domain.Presupuestos;

namespace Optica.Application.Tests.Presupuestos;

/// <summary>
/// Cantidad y descuento de cada línea (FR-02, FR-03; D-01, D-02, D-06) y topes de importe del contrato con la web
/// (D-04, D-11, ADR-016). Datos ficticios: ningún valor pertenece a una persona real.
/// </summary>
public class ValidacionPresupuestoTests
{
    private const string TextoCantidadInvalida = "La cantidad debe ser un número entero mayor a 0";
    private const string TextoCantidadMaxima = "Usá hasta 9.999 unidades.";
    private const string TextoDescuentoFueraDeRango = "El descuento debe estar entre 0 y 100";
    private const string TextoHastaDosDecimales = "Ingresá hasta 2 decimales.";
    private const string TextoImporteLinea = "El precio final de la línea es demasiado alto.";
    private const string TextoTotal = "El total del presupuesto es demasiado alto.";
    private const string TextoArticuloNoDisponible = "Ese artículo ya no está disponible. Quitá la línea y buscalo de nuevo";

    /// <summary><c>Number.MAX_SAFE_INTEGER / 100</c>: el importe más alto que la web lee exacto (D-04).</summary>
    private const decimal TopeContrato = 90_071_992_547_409.91m;

    private static readonly DatosCliente Cliente = new("Ficticiez", "Prueboncia", "99999999", null, null, null);

    private static IReadOnlyList<ErrorValidacion> Validar(params DatosLinea?[] lineas) =>
        ValidacionPresupuesto.Validar(new DatosPresupuesto(Cliente, lineas));

    [Fact]
    public void Textos_SonLosDelPrdYDeLasDecisiones()
    {
        Assert.Equal(TextoCantidadInvalida, ValidacionPresupuesto.AccionCantidadInvalida);
        Assert.Equal(TextoCantidadMaxima, ValidacionPresupuesto.AccionCantidadMaxima);
        Assert.Equal(TextoDescuentoFueraDeRango, ValidacionPresupuesto.AccionDescuentoFueraDeRango);
        Assert.Equal(TextoImporteLinea, ValidacionPresupuesto.AccionImporteLineaExcedido);
        Assert.Equal(TextoTotal, ValidacionPresupuesto.AccionTotalExcedido);
        Assert.Equal(TopeContrato, ValidacionPresupuesto.ImporteMaximoContrato);
        // El tope del contrato es el de la web: 2^53 − 1 centésimos.
        Assert.Equal(9_007_199_254_740_991m, ValidacionPresupuesto.ImporteMaximoContrato * 100m);
    }

    /// <summary>El "9.999" del texto de AC-09 es <see cref="CalculoLinea.CantidadMaxima"/> escrito en es-AR (D-01).</summary>
    [Fact]
    public void AccionCantidadMaxima_NombraLaCantidadMaximaDelDominio()
    {
        var maxima = CalculoLinea.CantidadMaxima.ToString("N0", CultureInfo.GetCultureInfo("es-AR"));

        Assert.Equal(ValidacionPresupuesto.AccionCantidadMaxima, $"Usá hasta {maxima} unidades.");
    }

    /// <summary>
    /// La regla de cantidad y descuento de <see cref="ValidacionPresupuesto.Validar"/> es la del dominio: lo que acepta,
    /// <see cref="LineaPresupuesto.DesdeArticulo"/> también, y lo que rechaza, el dominio también. Si divergieran, un
    /// pedido aceptado terminaría en una excepción (500) dentro del alta.
    /// </summary>
    [Theory]
    [InlineData("0")]
    [InlineData("0.00")]
    [InlineData("0.01")]
    [InlineData("0.001")]
    [InlineData("-0.01")]
    [InlineData("-0.001")]
    [InlineData("-0")]
    [InlineData("10")]
    [InlineData("10.5")]
    [InlineData("10.55")]
    [InlineData("10.550")]
    [InlineData("10.555")]
    [InlineData("99.99")]
    [InlineData("99.999")]
    [InlineData("100")]
    [InlineData("100.00")]
    [InlineData("100.000")]
    [InlineData("100.001")]
    [InlineData("100.01")]
    [InlineData("79228162514264337593543950335")]
    [InlineData("-79228162514264337593543950335")]
    [InlineData("0.0000000000000000000000000001")]
    public void Validar_DescuentoEquivalenteAlDominio(string texto)
    {
        var descuento = decimal.Parse(texto, NumberStyles.Float, CultureInfo.InvariantCulture);

        var aceptaValidar = !Validar(new DatosLinea(15, 1, descuento))
            .Any(error => error.Campo == "lineas[0].porcentajeDescuento");

        Assert.Equal(aceptaValidar, AceptaElDominio(1, descuento));
    }

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(9_998)]
    [InlineData(9_999)]
    [InlineData(10_000)]
    [InlineData(int.MaxValue)]
    public void Validar_CantidadEquivalenteAlDominio(int cantidad)
    {
        var aceptaValidar = !Validar(new DatosLinea(15, cantidad, 0m))
            .Any(error => error.Campo == "lineas[0].cantidad");

        Assert.Equal(aceptaValidar, AceptaElDominio(cantidad, 0m));
    }

    /// <summary>Verdadero si el dominio arma la línea; falso si la rechaza por la cantidad o el descuento.</summary>
    private static bool AceptaElDominio(int cantidad, decimal descuento)
    {
        try
        {
            return Resultado(1000m, cantidad, descuento).EsValida;
        }
        catch (ArgumentException error) when (error.ParamName is "cantidad" or "porcentajeDescuento")
        {
            return false;
        }
    }

    [Theory]
    [InlineData(0, "lineas[0].cantidad", "lineas[0].porcentajeDescuento")]
    [InlineData(99, "lineas[99].cantidad", "lineas[99].porcentajeDescuento")]
    public void CamposDeLaLinea_NombranElCampoDeLaApi(int indice, string cantidad, string descuento)
    {
        Assert.Equal(cantidad, ValidacionPresupuesto.CampoCantidad(indice));
        Assert.Equal(descuento, ValidacionPresupuesto.CampoDescuento(indice));
    }

    /// <summary>AC-02: cero, negativa o ausente, en <c>lineas[i].cantidad</c> con el texto del PRD.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [InlineData(null)]
    public void Validar_CantidadInvalida_ErrorEnLaCantidad(int? cantidad)
    {
        var errores = Validar(new DatosLinea(15, 1, 0m), new DatosLinea(15, cantidad, 0m));

        Assert.Equal([new ErrorValidacion("lineas[1].cantidad", TextoCantidadInvalida)], errores);
    }

    /// <summary>AC-09 (D-01): más de 9.999, con su propio texto; 1 y 9.999 se aceptan.</summary>
    [Fact]
    public void Validar_CantidadMayorAlMaximo_UsaHasta9999()
    {
        Assert.Equal(
            [new ErrorValidacion("lineas[0].cantidad", TextoCantidadMaxima)],
            Validar(new DatosLinea(15, 10_000, 0m)));
        Assert.Equal(
            [new ErrorValidacion("lineas[0].cantidad", TextoCantidadMaxima)],
            Validar(new DatosLinea(15, int.MaxValue, 0m)));
        Assert.Empty(Validar(new DatosLinea(15, CalculoLinea.CantidadMinima, 0m)));
        Assert.Empty(Validar(new DatosLinea(15, CalculoLinea.CantidadMaxima, 0m)));
    }

    /// <summary>AC-03 (D-02): fuera de 0..100 o ausente, en <c>lineas[i].porcentajeDescuento</c>.</summary>
    [Theory]
    [InlineData("-1")]
    [InlineData("-0.01")]
    [InlineData("100.01")]
    [InlineData("-0.001")]
    [InlineData("100.555")]
    [InlineData(null)]
    public void Validar_DescuentoFueraDeRango_ErrorEnElDescuento(string? descuento)
    {
        var valor = descuento is null ? (decimal?)null : decimal.Parse(descuento, CultureInfo.InvariantCulture);

        var errores = Validar(new DatosLinea(15, 1, valor));

        // Primero el rango, después la escala (D-02): -0,001 y 100,555 están fuera de rango.
        Assert.Equal([new ErrorValidacion("lineas[0].porcentajeDescuento", TextoDescuentoFueraDeRango)], errores);
    }

    [Theory]
    [InlineData("10.555")]
    [InlineData("0.001")]
    public void Validar_DescuentoConTresDecimales_HastaDosDecimales(string descuento)
    {
        var errores = Validar(new DatosLinea(15, 1, decimal.Parse(descuento, CultureInfo.InvariantCulture)));

        Assert.Equal([new ErrorValidacion("lineas[0].porcentajeDescuento", TextoHastaDosDecimales)], errores);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("100")]
    [InlineData("10.55")]
    [InlineData("10.550")]
    public void Validar_DescuentoValido_SinErrores(string descuento)
    {
        Assert.Empty(Validar(new DatosLinea(15, 1, decimal.Parse(descuento, CultureInfo.InvariantCulture))));
    }

    /// <summary>
    /// Los errores de cada línea van en el orden artículo, cantidad, descuento; una línea ausente cuenta como sus tres
    /// datos ausentes, igual que un cliente ausente cuenta como seis.
    /// </summary>
    [Fact]
    public void Validar_VariosErroresYLineaAusente_UnoPorCampoEnOrden()
    {
        var errores = Validar(new DatosLinea(0, 0, 101m), null);

        Assert.Equal(
            [
                new ErrorValidacion("lineas[0]", TextoArticuloNoDisponible),
                new ErrorValidacion("lineas[0].cantidad", TextoCantidadInvalida),
                new ErrorValidacion("lineas[0].porcentajeDescuento", TextoDescuentoFueraDeRango),
                new ErrorValidacion("lineas[1]", TextoArticuloNoDisponible),
                new ErrorValidacion("lineas[1].cantidad", TextoCantidadInvalida),
                new ErrorValidacion("lineas[1].porcentajeDescuento", TextoDescuentoFueraDeRango),
            ],
            errores);
    }

    /// <summary>R-04: las acciones son texto fijo; no repiten la cantidad ni el descuento recibidos.</summary>
    [Fact]
    public void Validar_Errores_NoRepitenElValor()
    {
        var errores = Validar(new DatosLinea(15, 98_765, 12.345m), new DatosLinea(15, -4321, 543.21m));

        Assert.Equal(4, errores.Count);
        foreach (var valor in new[] { "98765", "98.765", "12.345", "12,345", "4321", "543.21", "543,21" })
        {
            Assert.DoesNotContain(errores, error => error.Accion.Contains(valor, StringComparison.Ordinal));
        }
    }

    // Topes de importe (D-04, D-11).

    private static ResultadoLinea Resultado(decimal precioVenta, int cantidad = 1, decimal descuento = 0m) =>
        LineaPresupuesto.DesdeArticulo(
            new Articulo(15, "PRV-15", "Armazón", 1000m, 85m, precioVenta, activo: true, version: 0), cantidad, descuento);

    [Fact]
    public void ControlarTopes_DentroDelTope_SinErrores()
    {
        // Una línea justo en el tope; y varias cuyo total queda justo en el tope.
        Assert.Empty(ValidacionPresupuesto.ControlarTopes([Resultado(TopeContrato)]));
        Assert.Empty(ValidacionPresupuesto.ControlarTopes(
            [Resultado(TopeContrato - 0.01m), Resultado(0.01m), Resultado(0m)]));
    }

    [Fact]
    public void ControlarTopes_PrecioFinalPorEncimaDelContrato_ErrorEnLaCantidad()
    {
        // 10.000.000.000 × 9.999 entra en el tipo del dominio, pero no en el contrato con la web.
        var porEncima = Resultado(10_000_000_000m, 9_999);
        Assert.True(porEncima.EsValida);

        var errores = ValidacionPresupuesto.ControlarTopes([Resultado(1000m), porEncima, Resultado(TopeContrato + 0.01m)]);

        Assert.Equal(
            [
                new ErrorValidacion("lineas[1].cantidad", TextoImporteLinea),
                new ErrorValidacion("lineas[2].cantidad", TextoImporteLinea),
            ],
            errores);
    }

    [Fact]
    public void ControlarTopes_ImporteExcedidoDelDominio_ErrorEnLaCantidad()
    {
        // 10^13 × 9.999 supera el límite del tipo: el dominio no arma la línea.
        var excedido = Resultado(10_000_000_000_000m, 9_999);
        Assert.True(excedido.EsImporteExcedido);

        var errores = ValidacionPresupuesto.ControlarTopes([excedido]);

        Assert.Equal([new ErrorValidacion("lineas[0].cantidad", TextoImporteLinea)], errores);
    }

    [Fact]
    public void ControlarTopes_TotalPorEncimaDelContrato_ErrorEnLineas()
    {
        var errores = ValidacionPresupuesto.ControlarTopes([Resultado(TopeContrato), Resultado(0.01m)]);

        Assert.Equal([new ErrorValidacion("lineas", TextoTotal)], errores);
    }

    /// <summary>Con una línea por encima, el total no se informa: ya no se puede grabar y su suma no se lee igual.</summary>
    [Fact]
    public void ControlarTopes_LineaYTotalPorEncima_SoloLaLinea()
    {
        var errores = ValidacionPresupuesto.ControlarTopes([Resultado(TopeContrato), Resultado(TopeContrato + 0.01m)]);

        Assert.Equal([new ErrorValidacion("lineas[1].cantidad", TextoImporteLinea)], errores);
    }

    [Fact]
    public void ControlarTopes_RechazoQueNoEsDeImporte_Lanza()
    {
        // Los rechazos del catálogo los traduce el alta antes; llegar acá con uno es un error de programación.
        var inactivo = LineaPresupuesto.DesdeArticulo(
            new Articulo(16, "PRV-16", "Estuche", 1000m, 85m, 300m, activo: false, version: 0), 1, 0m);

        Assert.Throws<ArgumentException>(() => ValidacionPresupuesto.ControlarTopes([inactivo]));
        Assert.Throws<ArgumentNullException>(() => ValidacionPresupuesto.ControlarTopes(null!));
    }
}
