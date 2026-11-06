using System.Diagnostics.CodeAnalysis;
using Optica.Domain.Catalogo;
using Optica.Domain.Comun;

namespace Optica.Domain.Presupuestos;

/// <summary>
/// Una línea del presupuesto tomada de un artículo del catálogo (FR-10, FR-13, FR-14). Los importes se guardan en
/// centésimos (ADR-008). Mientras no exista FEAT-003b, una línea nueva lleva Cantidad 1 y Descuento 0 (D-03).
/// </summary>
public sealed class LineaPresupuesto
{
    public const decimal DescuentoMinimo = 0m;
    public const decimal DescuentoMaximo = 100m;

    /// <summary>Reconstruye una línea guardada; el alta usa <see cref="DesdeArticulo"/>.</summary>
    /// <exception cref="ArgumentException">
    /// El código no es mayor que 0; la descripción falta, no está recortada, tiene UTF-16 inválido o más de 200
    /// caracteres; algún importe
    /// es negativo, tiene más de 2 decimales o no entra en el tipo; la cantidad es menor que 1; o el descuento no está
    /// entre 0 y 100 con hasta 2 decimales. El mensaje nombra el campo y no incluye el valor recibido.
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

        ValidarImporte(precioUnitario, nameof(precioUnitario), "El precio unitario no es válido.");

        if (cantidad < 1)
        {
            throw new ArgumentException("La cantidad no es válida.", nameof(cantidad));
        }

        if (porcentajeDescuento < DescuentoMinimo || porcentajeDescuento > DescuentoMaximo
            || ReglasImporte.TieneMasDeDosDecimales(porcentajeDescuento))
        {
            throw new ArgumentException("El porcentaje de descuento no es válido.", nameof(porcentajeDescuento));
        }

        ValidarImporte(precioConDescuento, nameof(precioConDescuento), "El precio con descuento no es válido.");
        ValidarImporte(precioFinal, nameof(precioFinal), "El precio final no es válido.");

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

    /// <summary>Desde 1.</summary>
    public int Cantidad { get; }

    /// <summary>Porcentaje de 0 a 100, con hasta 2 decimales.</summary>
    public decimal PorcentajeDescuento { get; }

    public decimal PrecioConDescuento { get; }

    public decimal PrecioFinal { get; }

    /// <summary>
    /// La línea para un artículo del catálogo. Inactivo: <see cref="ResultadoLinea.Inactivo"/> (FR-09, D-14). Precio
    /// de venta negativo: <see cref="ResultadoLinea.PrecioNegativo"/> (FR-12). Si no, una línea válida que copia el
    /// código, la descripción y el precio de venta como Precio Unitario sin recalcularlo (FR-10), con Cantidad 1,
    /// Descuento 0 y Precio con Descuento y Precio Final iguales al Precio Unitario (FR-14, D-03). Precio 0 se admite.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="articulo"/> es null.</exception>
    /// <exception cref="ArgumentException">El artículo no tiene código: no salió del catálogo.</exception>
    public static ResultadoLinea DesdeArticulo(Articulo articulo)
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

        var precio = articulo.PrecioVenta;
        return ResultadoLinea.Valida(new LineaPresupuesto(
            codigo, articulo.Descripcion, precio, cantidad: 1, porcentajeDescuento: 0m, precioConDescuento: precio,
            precioFinal: precio));
    }

    // Ya recortada, así el largo se mide sobre el texto guardado.
    private static bool EsDescripcionValida(string? descripcion) =>
        descripcion is not null
        && descripcion.Length > 0
        && NormalizacionCodigo.Recortar(descripcion) == descripcion
        && descripcion.Length <= PoliticaArticulo.LargoMaximoDescripcion
        && NormalizacionCodigo.EsUtf16Valido(descripcion);

    private static void ValidarImporte(decimal importe, string campo, string mensaje)
    {
        if (importe < 0m || importe > ReglasImporte.ImporteMaximo || ReglasImporte.TieneMasDeDosDecimales(importe))
        {
            throw new ArgumentException(mensaje, campo);
        }
    }
}

/// <summary>
/// El resultado de <see cref="LineaPresupuesto.DesdeArticulo"/>: <see cref="EsValida"/> con la línea,
/// <see cref="Inactivo"/> si el artículo está dado de baja (FR-09, D-14) o <see cref="PrecioNegativo"/> si su precio
/// de venta es negativo (FR-12). Los rechazos no llevan línea.
/// </summary>
public sealed class ResultadoLinea
{
    private ResultadoLinea(LineaPresupuesto? linea, bool esInactivo, bool esPrecioNegativo)
    {
        Linea = linea;
        EsInactivo = esInactivo;
        EsPrecioNegativo = esPrecioNegativo;
    }

    /// <summary>El artículo está inactivo: no se agrega la línea al presupuesto.</summary>
    public static ResultadoLinea Inactivo { get; } = new(null, esInactivo: true, esPrecioNegativo: false);

    /// <summary>El precio de venta del artículo es negativo: no se agrega la línea al presupuesto.</summary>
    public static ResultadoLinea PrecioNegativo { get; } = new(null, esInactivo: false, esPrecioNegativo: true);

    /// <summary>La línea; null si el artículo se rechazó.</summary>
    public LineaPresupuesto? Linea { get; }

    /// <summary>Verdadero si el artículo está inactivo.</summary>
    public bool EsInactivo { get; }

    /// <summary>Verdadero si el precio de venta del artículo es negativo.</summary>
    public bool EsPrecioNegativo { get; }

    /// <summary>Verdadero si el artículo se aceptó: <see cref="Linea"/> tiene la línea.</summary>
    [MemberNotNullWhen(true, nameof(Linea))]
    public bool EsValida => Linea is not null;

    internal static ResultadoLinea Valida(LineaPresupuesto linea)
    {
        ArgumentNullException.ThrowIfNull(linea);
        return new ResultadoLinea(linea, esInactivo: false, esPrecioNegativo: false);
    }
}
