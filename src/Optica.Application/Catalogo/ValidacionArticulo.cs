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

    // FEAT-002b: búsqueda, modificación y cambio de estado.
    public const string CampoTexto = "texto";
    public const string CampoPagina = "pagina";
    public const string CampoVersion = "version";
    public const string CampoActivo = "activo";

    /// <summary>
    /// Largo máximo del texto de búsqueda tras el recorte de D-17 (spec FEAT-002b, Block 3, Input validation). Es
    /// propio de la búsqueda: no depende del largo máximo de la descripción.
    /// </summary>
    public const int LargoMaximoTexto = 200;

    /// <summary>Páginas que se pueden pedir en la búsqueda (D-07).</summary>
    public const int PaginaMaxima = 10_000;

    /// <summary>La versión más alta que se acepta: 2^53 − 1, el entero exacto más grande de JavaScript (D-17).</summary>
    public const long VersionMaxima = 9_007_199_254_740_991;

    public const string AccionTextoLargo = AccionDescripcionLarga;
    public const string AccionPaginaFueraDeRango = "Pedí una página entre 1 y 10.000.";
    public const string AccionVersionInvalida = "Volvé a abrir el artículo para editarlo.";
    public const string AccionActivoFaltante = "Indicá si el artículo queda activo o inactivo.";

    /// <summary>El rechazo de la modificación de un artículo inactivo (D-02).</summary>
    public const string DetalleInactivo = "Reactivá el artículo para editarlo.";

    /// <summary>Otra pestaña modificó el artículo entre la lectura y la grabación (D-08).</summary>
    public const string DetalleCambiadoAlEditar = "El artículo cambió mientras lo editabas. Volvé a abrirlo.";

    /// <summary>Otra escritura cambió el artículo entre la lectura y el cambio de estado (D-16).</summary>
    public const string DetalleCambiadoAlCambiarEstado = "El artículo cambió. Volvé a cargar el listado.";

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

    /// <summary>
    /// Los parámetros de la búsqueda (FR-04, D-07): el texto, opcional, hasta <see cref="LargoMaximoTexto"/> caracteres
    /// tras el recorte de D-17 y
    /// UTF-16 válido; la página, opcional, de 1 a <see cref="PaginaMaxima"/>. Devuelve todos los errores, uno por
    /// parámetro, con texto fijo que no repite el valor.
    /// </summary>
    public static IReadOnlyList<ErrorValidacion> ValidarBusqueda(string? texto, int? pagina)
    {
        var errores = new List<ErrorValidacion>(2);
        if (texto is not null)
        {
            var recortado = NormalizacionCodigo.Recortar(texto);
            if (!NormalizacionCodigo.EsUtf16Valido(recortado))
            {
                errores.Add(new ErrorValidacion(CampoTexto, AccionCaracteresInvalidos));
            }
            else if (recortado.Length > LargoMaximoTexto)
            {
                errores.Add(new ErrorValidacion(CampoTexto, AccionTextoLargo));
            }
        }

        if (pagina is < 1 or > PaginaMaxima)
        {
            errores.Add(new ErrorValidacion(CampoPagina, AccionPaginaFueraDeRango));
        }

        return errores;
    }

    /// <summary>El error de una versión faltante o fuera de 0 a <see cref="VersionMaxima"/> (D-17); null si es válida.</summary>
    public static ErrorValidacion? ValidarVersion(long? version) =>
        version is >= 0 and <= VersionMaxima ? null : new ErrorValidacion(CampoVersion, AccionVersionInvalida);

    /// <summary>El error de un estado faltante en el cambio de estado.</summary>
    public static ErrorValidacion ActivoFaltante { get; } = new(CampoActivo, AccionActivoFaltante);

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
