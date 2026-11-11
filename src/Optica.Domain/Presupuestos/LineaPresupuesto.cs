using System.Diagnostics.CodeAnalysis;
using Optica.Domain.Catalogo;
using Optica.Domain.Comun;

namespace Optica.Domain.Presupuestos;

/// <summary>
/// Una línea del presupuesto tomada de un artículo del catálogo (FR-10, FR-13, FR-14 de FEAT-003a), con la Cantidad
/// y el Porcentaje de Descuento que carga la operadora y los precios que calcula <see cref="CalculoLinea"/> (FR-01 a
/// FR-05 de FEAT-003b). Los importes se guardan en centésimos (ADR-008).
/// </summary>
public sealed class LineaPresupuesto
{
    /// <summary>Reconstruye una línea guardada; el alta usa <see cref="DesdeArticulo"/>.</summary>
    /// <exception cref="ArgumentException">
    /// El código no es mayor que 0; la descripción falta, no está recortada, tiene UTF-16 inválido o más de 200
    /// caracteres; algún importe
    /// es negativo, tiene más de 2 decimales o no entra en el tipo; la cantidad no está entre 1 y 9.999 (D-01); o el
    /// descuento no está entre 0 y 100 con hasta 2 decimales. El mensaje nombra el campo y no incluye el valor
    /// recibido.
    /// </exception>
    public LineaPresupuesto(
        long codigoArticulo, string descripcion, decimal precioUnitario, int cantidad, decimal porcentajeDescuento,
        decimal precioConDescuento, decimal precioFinal)
    {
        if (codigoArticulo <= 0)
        {
            throw new ArgumentException("El código de artículo no es válido.", nameof(codigoArticulo));
        }

        if (!EsDescripcionValida(descripcion))
        {
            throw new ArgumentException("La descripción no es válida.", nameof(descripcion));
        }

        CalculoLinea.ValidarImporte(
            precioUnitario, nameof(precioUnitario), CalculoLinea.MensajePrecioUnitarioInvalido);

        CalculoLinea.ValidarCantidad(cantidad);
        CalculoLinea.ValidarDescuento(porcentajeDescuento);

        CalculoLinea.ValidarImporte(
            precioConDescuento, nameof(precioConDescuento), CalculoLinea.MensajePrecioConDescuentoInvalido);
        CalculoLinea.ValidarImporte(precioFinal, nameof(precioFinal), CalculoLinea.MensajePrecioFinalInvalido);

        CodigoArticulo = codigoArticulo;
        Descripcion = descripcion;
        PrecioUnitario = precioUnitario;
        Cantidad = cantidad;
        PorcentajeDescuento = porcentajeDescuento;
        PrecioConDescuento = precioConDescuento;
        PrecioFinal = precioFinal;
    }

    /// <summary>El código autonumérico del artículo en el catálogo (D-13).</summary>
    public long CodigoArticulo { get; }

    /// <summary>La descripción del artículo al momento de agregarlo.</summary>
    public string Descripcion { get; }

    /// <summary>El precio de venta guardado del artículo, sin recalcular (FR-10).</summary>
    public decimal PrecioUnitario { get; }

    /// <summary>De 1 a 9.999 (D-01).</summary>
    public int Cantidad { get; }

    /// <summary>Porcentaje de 0 a 100, con hasta 2 decimales.</summary>
    public decimal PorcentajeDescuento { get; }

    public decimal PrecioConDescuento { get; }

    public decimal PrecioFinal { get; }

    /// <summary>
    /// La línea para un artículo del catálogo. Inactivo: <see cref="ResultadoLinea.Inactivo"/> (FR-09, D-14). Precio
    /// de venta negativo: <see cref="ResultadoLinea.PrecioNegativo"/> (FR-12). Esos dos rechazos de FEAT-003a se
    /// deciden antes de mirar la Cantidad y el descuento: no dependen de ellos. Precio Final por encima de
    /// <see cref="ReglasImporte.ImporteMaximo"/>: <see cref="ResultadoLinea.ImporteExcedido"/>. Si no, una línea
    /// válida que copia el código, la descripción y el precio de venta como Precio Unitario sin recalcularlo (FR-10),
    /// con la Cantidad y el descuento recibidos y los precios de <see cref="CalculoLinea"/> (FR-04, FR-05). Precio 0
    /// se admite.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="articulo"/> es null.</exception>
    /// <exception cref="ArgumentException">
    /// El artículo no tiene código: no salió del catálogo. O, con un artículo activo de precio no negativo, la cantidad
    /// no está entre 1 y 9.999 o el descuento no está entre 0 y 100 con hasta 2 decimales: la capa de aplicación los
    /// valida antes. El mensaje nombra el campo y no incluye el valor recibido.
    /// </exception>
    public static ResultadoLinea DesdeArticulo(Articulo articulo, int cantidad, decimal porcentajeDescuento)
    {
        ArgumentNullException.ThrowIfNull(articulo);
        if (articulo.Codigo is not { } codigo)
        {
            throw new ArgumentException("El artículo no tiene código.", nameof(articulo));
        }

        if (!articulo.Activo)
        {
            return ResultadoLinea.Inactivo;
        }

        if (articulo.PrecioVenta < 0m)
        {
            return ResultadoLinea.PrecioNegativo;
        }

        // La cantidad y el descuento se validan antes de calcular; el constructor los vuelve a validar como invariante.
        // El precio de venta ya cumple las reglas de importe por ser de un Articulo (2 decimales, dentro del tipo) y
        // acá no es negativo.
        CalculoLinea.ValidarCantidad(cantidad);
        CalculoLinea.ValidarDescuento(porcentajeDescuento);

        var precio = articulo.PrecioVenta;
        var precioConDescuento = CalculoLinea.CalcularPrecioConDescuento(precio, porcentajeDescuento);
        var precioFinal = CalculoLinea.CalcularPrecioFinal(precioConDescuento, cantidad);
        if (precioFinal > ReglasImporte.ImporteMaximo)
        {
            return ResultadoLinea.ImporteExcedido;
        }

        return ResultadoLinea.Valida(new LineaPresupuesto(
            codigo, articulo.Descripcion, precio, cantidad, porcentajeDescuento, precioConDescuento, precioFinal));
    }

    // Ya recortada, así el largo se mide sobre el texto guardado.
    private static bool EsDescripcionValida(string? descripcion) =>
        descripcion is not null
        && descripcion.Length > 0
        && NormalizacionCodigo.Recortar(descripcion) == descripcion
        && descripcion.Length <= PoliticaArticulo.LargoMaximoDescripcion
        && NormalizacionCodigo.EsUtf16Valido(descripcion);
}

/// <summary>
/// El resultado de <see cref="LineaPresupuesto.DesdeArticulo"/>: <see cref="EsValida"/> con la línea,
/// <see cref="Inactivo"/> si el artículo está dado de baja (FR-09, D-14), <see cref="PrecioNegativo"/> si su precio
/// de venta es negativo (FR-12) o <see cref="ImporteExcedido"/> si el Precio Final no entra en el tipo. Los rechazos
/// no llevan línea.
/// </summary>
public sealed class ResultadoLinea
{
    private ResultadoLinea(
        LineaPresupuesto? linea, bool esInactivo = false, bool esPrecioNegativo = false, bool esImporteExcedido = false)
    {
        Linea = linea;
        EsInactivo = esInactivo;
        EsPrecioNegativo = esPrecioNegativo;
        EsImporteExcedido = esImporteExcedido;
    }

    /// <summary>El artículo está inactivo: no se agrega la línea al presupuesto.</summary>
    public static ResultadoLinea Inactivo { get; } = new(null, esInactivo: true);

    /// <summary>El precio de venta del artículo es negativo: no se agrega la línea al presupuesto.</summary>
    public static ResultadoLinea PrecioNegativo { get; } = new(null, esPrecioNegativo: true);

    /// <summary>
    /// El Precio Final supera <see cref="ReglasImporte.ImporteMaximo"/>, el límite del tipo: no se agrega la
    /// línea al presupuesto.
    /// </summary>
    public static ResultadoLinea ImporteExcedido { get; } = new(null, esImporteExcedido: true);

    /// <summary>La línea; null si el artículo se rechazó.</summary>
    public LineaPresupuesto? Linea { get; }

    /// <summary>Verdadero si el artículo está inactivo.</summary>
    public bool EsInactivo { get; }

    /// <summary>Verdadero si el precio de venta del artículo es negativo.</summary>
    public bool EsPrecioNegativo { get; }

    /// <summary>Verdadero si el Precio Final de la línea supera el límite del tipo.</summary>
    public bool EsImporteExcedido { get; }

    /// <summary>Verdadero si el artículo se aceptó: <see cref="Linea"/> tiene la línea.</summary>
    [MemberNotNullWhen(true, nameof(Linea))]
    public bool EsValida => Linea is not null;

    internal static ResultadoLinea Valida(LineaPresupuesto linea)
    {
        ArgumentNullException.ThrowIfNull(linea);
        return new ResultadoLinea(linea);
    }
}
