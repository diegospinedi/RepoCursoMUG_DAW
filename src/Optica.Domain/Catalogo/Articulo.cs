using System.Diagnostics;
using Optica.Domain.Comun;

namespace Optica.Domain.Catalogo;

/// <summary>
/// Un artículo del catálogo con su precio de venta ya calculado y redondeado (FR-01, FR-10). Siempre cumple
/// <see cref="PoliticaArticulo"/>.
/// </summary>
public sealed class Articulo
{
    /// <param name="codigo">El código autonumérico; null antes de grabar.</param>
    /// <exception cref="ArgumentException">
    /// Algún valor incumple la política, o el precio de venta tiene más de 2 decimales o no entra en el tipo. El
    /// mensaje nombra el campo y no incluye el valor recibido.
    /// </exception>
    public Articulo(
        long? codigo, string codigoProveedor, string descripcion, decimal precioCosto, decimal margenUtilidad,
        decimal precioVenta)
    {
        var infracciones = PoliticaArticulo.Evaluar(codigoProveedor, descripcion, precioCosto, margenUtilidad);
        if (infracciones.Count > 0)
        {
            throw Error(infracciones[0]);
        }

        if (ReglasImporte.TieneMasDeDosDecimales(precioVenta) || Math.Abs(precioVenta) > ReglasImporte.ImporteMaximo)
        {
            throw new ArgumentException("El precio de venta no es válido.", nameof(precioVenta));
        }

        Codigo = codigo;
        CodigoProveedor = NormalizacionCodigo.Recortar(codigoProveedor);
        CodigoProveedorClave = NormalizacionCodigo.Clave(codigoProveedor);
        Descripcion = NormalizacionCodigo.Recortar(descripcion);
        PrecioCosto = precioCosto;
        MargenUtilidad = margenUtilidad;
        PrecioVenta = precioVenta;
    }

    /// <summary>El código autonumérico; null antes de grabar.</summary>
    public long? Codigo { get; }

    /// <summary>El código en el proveedor tal como se escribió, recortado (D-13).</summary>
    public string CodigoProveedor { get; }

    /// <summary>La clave normalizada del código en el proveedor, única en el catálogo (ADR-009).</summary>
    public string CodigoProveedorClave { get; }

    /// <summary>La descripción recortada (D-13).</summary>
    public string Descripcion { get; }

    public decimal PrecioCosto { get; }

    /// <summary>Porcentaje de 0 a 1000, con hasta 2 decimales.</summary>
    public decimal MargenUtilidad { get; }

    /// <summary>El precio de venta ya redondeado al múltiplo (FR-10).</summary>
    public decimal PrecioVenta { get; }

    /// <summary>Verdadero si el precio de costo es cero o negativo: lleva la advertencia de FR-05.</summary>
    public bool CostoNoPositivo => PrecioCosto <= 0m;

    // Los mensajes no incluyen el valor recibido, como el resto de las invariantes del dominio.
    private static ArgumentException Error(InfraccionArticulo infraccion) => infraccion switch
    {
        InfraccionArticulo.CodigoFaltante
            or InfraccionArticulo.CodigoConCaracteresInvalidos
            or InfraccionArticulo.CodigoDemasiadoLargo =>
            new ArgumentException("El código en el proveedor no es válido.", "codigoProveedor"),
        InfraccionArticulo.DescripcionFaltante
            or InfraccionArticulo.DescripcionConCaracteresInvalidos
            or InfraccionArticulo.DescripcionDemasiadoLarga =>
            new ArgumentException("La descripción no es válida.", "descripcion"),
        InfraccionArticulo.CostoFaltante
            or InfraccionArticulo.CostoConMasDeDosDecimales
            or InfraccionArticulo.CostoFueraDeRango =>
            new ArgumentException("El precio de costo no es válido.", "precioCosto"),
        InfraccionArticulo.MargenFaltante
            or InfraccionArticulo.MargenFueraDeRango
            or InfraccionArticulo.MargenConMasDeDosDecimales =>
            new ArgumentException("El margen de utilidad no es válido.", "margenUtilidad"),
        _ => throw new UnreachableException("Infracción de artículo sin campo asignado."),
    };
}
