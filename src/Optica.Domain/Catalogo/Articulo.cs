using System.Diagnostics;
using Optica.Domain.Comun;
using Optica.Domain.Configuracion;

namespace Optica.Domain.Catalogo;

/// <summary>
/// Un artículo del catálogo con su precio de venta ya calculado y redondeado (FR-01, FR-10). Siempre cumple
/// <see cref="PoliticaArticulo"/>.
/// </summary>
public sealed class Articulo
{
    /// <summary>Reconstruye un artículo; el alta usa <see cref="Nuevo"/> (D-10).</summary>
    /// <param name="codigo">El código autonumérico; null antes de grabar.</param>
    /// <param name="activo">Si el artículo está activo (FR-05, FR-06); obligatorio (D-10).</param>
    /// <param name="version">La versión para la concurrencia optimista, desde 0; la incrementa la persistencia.</param>
    /// <exception cref="ArgumentException">
    /// Algún valor incumple la política, el precio de venta tiene más de 2 decimales o no entra en el tipo, o la
    /// versión es negativa. El mensaje nombra el campo y no incluye el valor recibido.
    /// </exception>
    public Articulo(
        long? codigo, string codigoProveedor, string descripcion, decimal precioCosto, decimal margenUtilidad,
        decimal precioVenta, bool activo, long version)
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

        if (version < 0)
        {
            throw new ArgumentException("La versión no es válida.", nameof(version));
        }

        Codigo = codigo;
        CodigoProveedor = NormalizacionCodigo.Recortar(codigoProveedor);
        CodigoProveedorClave = NormalizacionCodigo.Clave(codigoProveedor);
        Descripcion = NormalizacionCodigo.Recortar(descripcion);
        PrecioCosto = precioCosto;
        MargenUtilidad = margenUtilidad;
        PrecioVenta = precioVenta;
        Activo = activo;
        Version = version;
    }

    /// <summary>Un artículo para dar de alta: sin código, activo y con versión 0 (D-10).</summary>
    /// <exception cref="ArgumentException">Igual que el constructor.</exception>
    public static Articulo Nuevo(
        string codigoProveedor, string descripcion, decimal precioCosto, decimal margenUtilidad, decimal precioVenta) =>
        new(null, codigoProveedor, descripcion, precioCosto, margenUtilidad, precioVenta, activo: true, version: 0);

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

    /// <summary>Falso si el artículo está dado de baja: no se modifica hasta reactivarlo (FR-05, D-02).</summary>
    public bool Activo { get; }

    /// <summary>La versión para la concurrencia optimista, desde 0.</summary>
    public long Version { get; }

    /// <summary>
    /// Los datos modificados (FR-03). Inactivo: <see cref="ResultadoModificacion.Inactivo"/> sin evaluar los datos
    /// (D-02, D-20). Si no, evalúa <see cref="PoliticaArticulo"/> y, si alguno incumple, devuelve
    /// <see cref="ResultadoModificacion.Invalido"/> con todas las infracciones, una por campo. El precio de venta se
    /// conserva si el costo y el margen son iguales a los guardados, y si alguno cambió se calcula con la configuración
    /// vigente; su infracción de rango va en la misma lista (D-03, D-06 de FEAT-002a). Sin infracciones devuelve
    /// <see cref="ResultadoModificacion.Modificado"/> con una copia con el mismo código y la misma versión, que
    /// incrementa la persistencia. Nunca lanza por los datos.
    /// </summary>
    public ResultadoModificacion Modificar(
        string? codigoProveedor, string? descripcion, decimal? precioCosto, decimal? margenUtilidad,
        ConfiguracionNegocio vigente)
    {
        ArgumentNullException.ThrowIfNull(vigente);
        if (!Activo)
        {
            return ResultadoModificacion.Inactivo;
        }

        var infracciones = new List<InfraccionArticulo>(
            PoliticaArticulo.Evaluar(codigoProveedor, descripcion, precioCosto, margenUtilidad));

        decimal? precioVenta = null;
        if (precioCosto is { } costo && margenUtilidad is { } margen && !TieneInfraccionDeCostoOMargen(infracciones))
        {
            if (costo == PrecioCosto && margen == MargenUtilidad)
            {
                precioVenta = PrecioVenta;
            }
            else
            {
                var calculo = CalculadoraPrecioVenta.Calcular(costo, margen, vigente);
                if (calculo.Infraccion is { } infraccion)
                {
                    infracciones.Add(infraccion);
                }
                else
                {
                    precioVenta = calculo.Precio!.PrecioVenta;
                }
            }
        }

        if (infracciones.Count > 0)
        {
            return ResultadoModificacion.Invalido(infracciones);
        }

        // Sin infracciones, la política aceptó los cuatro y el precio quedó calculado o conservado.
        return ResultadoModificacion.Modificado(new Articulo(
            Codigo, codigoProveedor!, descripcion!, precioCosto!.Value, margenUtilidad!.Value, precioVenta!.Value,
            activo: true, Version));
    }

    /// <summary>Una copia que solo cambia <see cref="Activo"/>: los demás datos y la versión quedan igual (D-09).</summary>
    public Articulo ConEstado(bool activo) =>
        new(Codigo, CodigoProveedor, Descripcion, PrecioCosto, MargenUtilidad, PrecioVenta, activo, Version);

    /// <summary>
    /// El precio de venta recalculado con la configuración vigente sobre el costo y el margen guardados, esté activo o
    /// inactivo (FR-01 y AC-03 de FEAT-002c; ADR-010). Igual al guardado: <see cref="ResultadoRecalculo.SinCambio"/>.
    /// Distinto: <see cref="ResultadoRecalculo.Cambiado"/> con una copia con el mismo código, datos, estado y versión, y
    /// el precio nuevo; la versión la incrementa la persistencia (D-04). Fuera de rango:
    /// <see cref="ResultadoRecalculo.FueraDeRango"/>. Nunca lanza por desborde.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="vigente"/> es null.</exception>
    public ResultadoRecalculo ConPrecioRecalculado(ConfiguracionNegocio vigente)
    {
        ArgumentNullException.ThrowIfNull(vigente);

        var calculo = CalculadoraPrecioVenta.Calcular(PrecioCosto, MargenUtilidad, vigente);
        if (calculo.Precio is not { } precio)
        {
            return ResultadoRecalculo.FueraDeRango;
        }

        if (precio.PrecioVenta == PrecioVenta)
        {
            return ResultadoRecalculo.SinCambio;
        }

        return ResultadoRecalculo.Cambiado(new Articulo(
            Codigo, CodigoProveedor, Descripcion, precioCosto: PrecioCosto, margenUtilidad: MargenUtilidad,
            precioVenta: precio.PrecioVenta, activo: Activo, version: Version));
    }

    private static bool TieneInfraccionDeCostoOMargen(List<InfraccionArticulo> infracciones) =>
        infracciones.Exists(infraccion => CampoDe(infraccion) is Campo.PrecioCosto or Campo.MargenUtilidad);

    // Los mensajes no incluyen el valor recibido, como el resto de las invariantes del dominio.
    private static ArgumentException Error(InfraccionArticulo infraccion) => CampoDe(infraccion) switch
    {
        Campo.CodigoProveedor => new ArgumentException("El código en el proveedor no es válido.", "codigoProveedor"),
        Campo.Descripcion => new ArgumentException("La descripción no es válida.", "descripcion"),
        Campo.PrecioCosto => new ArgumentException("El precio de costo no es válido.", "precioCosto"),
        Campo.MargenUtilidad => new ArgumentException("El margen de utilidad no es válido.", "margenUtilidad"),
        Campo.PrecioVenta => new ArgumentException("El precio de venta no es válido.", "precioVenta"),
        _ => throw new UnreachableException("Campo de artículo sin mensaje asignado."),
    };

    // El único lugar que dice a qué campo pertenece cada infracción.
    private static Campo CampoDe(InfraccionArticulo infraccion) => infraccion switch
    {
        InfraccionArticulo.CodigoFaltante
            or InfraccionArticulo.CodigoConCaracteresInvalidos
            or InfraccionArticulo.CodigoDemasiadoLargo => Campo.CodigoProveedor,
        InfraccionArticulo.DescripcionFaltante
            or InfraccionArticulo.DescripcionConCaracteresInvalidos
            or InfraccionArticulo.DescripcionDemasiadoLarga => Campo.Descripcion,
        InfraccionArticulo.CostoFaltante
            or InfraccionArticulo.CostoConMasDeDosDecimales
            or InfraccionArticulo.CostoFueraDeRango => Campo.PrecioCosto,
        InfraccionArticulo.MargenFaltante
            or InfraccionArticulo.MargenFueraDeRango
            or InfraccionArticulo.MargenConMasDeDosDecimales => Campo.MargenUtilidad,
        InfraccionArticulo.PrecioVentaFueraDeRango => Campo.PrecioVenta,
        _ => throw new UnreachableException("Infracción de artículo sin campo asignado."),
    };

    private enum Campo
    {
        CodigoProveedor,
        Descripcion,
        PrecioCosto,
        MargenUtilidad,
        PrecioVenta,
    }
}
