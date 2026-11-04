using System.Diagnostics;
using Optica.Application.Comun;
using Optica.Domain.Catalogo;

namespace Optica.Application.Catalogo;

/// <summary>
/// Traduce las infracciones de <see cref="PoliticaArticulo"/> y de <see cref="CalculadoraPrecioVenta"/> a un
/// <see cref="ErrorValidacion"/> con el campo de la Api y la acción que lo corrige. Las acciones son texto fijo: no
/// repiten el valor recibido.
/// </summary>
public static class ValidacionArticulo
{
    public const string CampoCodigoProveedor = "codigoProveedor";
    public const string CampoDescripcion = "descripcion";
    public const string CampoPrecioCosto = "precioCosto";
    public const string CampoMargenUtilidad = "margenUtilidad";

    public const string AccionCodigoFaltante = "Completá el código en el proveedor.";
    public const string AccionCodigoLargo = "Usá hasta 50 caracteres.";
    public const string AccionDescripcionFaltante = "Completá la descripción.";
    public const string AccionDescripcionLarga = "Usá hasta 200 caracteres.";
    public const string AccionCaracteresInvalidos = "El texto tiene caracteres no válidos. Escribilo de nuevo.";
    public const string AccionCostoFaltante = "Ingresá el precio de costo.";
    public const string AccionMargenFaltante = "Ingresá el margen de utilidad.";
    public const string AccionMargenFueraDeRango = "El margen debe estar entre 0 y 1.000.";
    public const string AccionPrecioVentaFueraDeRango =
        "El precio de venta resultante es demasiado grande. Revisá el costo o el margen.";

    /// <summary>El mensaje no revela datos del otro artículo (D-07).</summary>
    public const string AccionCodigoDuplicado = "Ese código ya pertenece a otro artículo.";

    /// <summary>La advertencia de un artículo con precio de costo cero o negativo (FR-05).</summary>
    public const string AdvertenciaCostoNoPositivo = "Precio de costo negativo o cero";

    /// <summary>El error de un código en el proveedor que ya tiene otro artículo (AC-02, D-07).</summary>
    public static ErrorValidacion CodigoDuplicado { get; } = new(CampoCodigoProveedor, AccionCodigoDuplicado);

    /// <summary>
    /// Evalúa los cuatro campos con la política y devuelve todos los errores, uno por campo como máximo, en el orden
    /// código, descripción, costo, margen. Vacío si los datos cumplen la política.
    /// </summary>
    public static IReadOnlyList<ErrorValidacion> Validar(DatosArticulo datos)
    {
        ArgumentNullException.ThrowIfNull(datos);
        var infracciones = PoliticaArticulo.Evaluar(
            datos.CodigoProveedor, datos.Descripcion, datos.PrecioCosto, datos.MargenUtilidad);
        return [.. infracciones.Select(Traducir)];
    }

    /// <summary>El campo y la acción de una infracción.</summary>
    /// <exception cref="UnreachableException">
    /// La infracción no tiene traducción: el dominio sumó un valor y falta agregarlo aquí.
    /// </exception>
    public static ErrorValidacion Traducir(InfraccionArticulo infraccion) => infraccion switch
    {
        InfraccionArticulo.CodigoFaltante => new(CampoCodigoProveedor, AccionCodigoFaltante),
        InfraccionArticulo.CodigoConCaracteresInvalidos => new(CampoCodigoProveedor, AccionCaracteresInvalidos),
        InfraccionArticulo.CodigoDemasiadoLargo => new(CampoCodigoProveedor, AccionCodigoLargo),
        InfraccionArticulo.DescripcionFaltante => new(CampoDescripcion, AccionDescripcionFaltante),
        InfraccionArticulo.DescripcionConCaracteresInvalidos => new(CampoDescripcion, AccionCaracteresInvalidos),
        InfraccionArticulo.DescripcionDemasiadoLarga => new(CampoDescripcion, AccionDescripcionLarga),
        InfraccionArticulo.CostoFaltante => new(CampoPrecioCosto, AccionCostoFaltante),
        InfraccionArticulo.CostoConMasDeDosDecimales => new(CampoPrecioCosto, AccionesNumericas.HastaDosDecimales),
        InfraccionArticulo.CostoFueraDeRango => new(CampoPrecioCosto, AccionesNumericas.ValorMenor),
        InfraccionArticulo.MargenFaltante => new(CampoMargenUtilidad, AccionMargenFaltante),
        InfraccionArticulo.MargenFueraDeRango => new(CampoMargenUtilidad, AccionMargenFueraDeRango),
        InfraccionArticulo.MargenConMasDeDosDecimales => new(CampoMargenUtilidad, AccionesNumericas.HastaDosDecimales),
        // D-06: el precio de venta no es un campo de la pantalla; se corrige desde el margen (o el costo).
        InfraccionArticulo.PrecioVentaFueraDeRango => new(CampoMargenUtilidad, AccionPrecioVentaFueraDeRango),
        _ => throw new UnreachableException("Infracción de artículo sin campo ni acción asignados."),
    };
}
