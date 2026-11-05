using System.Diagnostics.CodeAnalysis;
using Optica.Application.Comun;
using Optica.Domain.Catalogo;
using Optica.Domain.Configuracion;

namespace Optica.Application.Catalogo;

/// <summary>Los cuatro datos del artículo tal como llegan; la política los valida, una infracción por campo.</summary>
/// <remarks>
/// <see cref="object.ToString"/> no muestra ningún valor (<c>DatosArticulo { }</c>): el de un record mostraría el costo y
/// el margen en cualquier log que lo reciba (R-06 de FEAT-002b).
/// </remarks>
public sealed record DatosArticulo(
    string? CodigoProveedor, string? Descripcion, decimal? PrecioCosto, decimal? MargenUtilidad)
{
    private bool PrintMembers(System.Text.StringBuilder constructor) => false;
}

/// <summary>
/// Un artículo grabado: su código autonumérico, los textos recortados, los importes y el precio de venta ya
/// redondeado, <see cref="Advertencia"/> con el texto de FR-05 si el costo es cero o negativo (null si no), su estado
/// (FR-05, FR-06) y la <see cref="Version"/> que la pantalla de edición devuelve al grabar (D-08, D-17).
/// </summary>
public sealed record ArticuloGrabado(
    long Codigo,
    string CodigoProveedor,
    string Descripcion,
    decimal PrecioCosto,
    decimal MargenUtilidad,
    decimal PrecioVenta,
    string? Advertencia,
    bool Activo,
    long Version)
{
    /// <exception cref="InvalidOperationException">El artículo no tiene código: todavía no se grabó.</exception>
    internal static ArticuloGrabado Desde(Articulo articulo) => new(
        articulo.Codigo ?? throw new InvalidOperationException("El artículo leído no tiene código."),
        articulo.CodigoProveedor,
        articulo.Descripcion,
        articulo.PrecioCosto,
        articulo.MargenUtilidad,
        articulo.PrecioVenta,
        articulo.CostoNoPositivo ? ValidacionArticulo.AdvertenciaCostoNoPositivo : null,
        articulo.Activo,
        articulo.Version);
}

/// <summary>Cómo terminó un alta: grabado, con el artículo, o inválido, con un error por campo.</summary>
public sealed class ResultadoAlta
{
    private ResultadoAlta(ArticuloGrabado? articulo, IReadOnlyList<ErrorValidacion> errores)
    {
        Articulo = articulo;
        Errores = errores;
    }

    /// <summary>El artículo grabado; null si el alta fue inválida.</summary>
    public ArticuloGrabado? Articulo { get; }

    /// <summary>Errores de validación; vacío si el alta se grabó.</summary>
    public IReadOnlyList<ErrorValidacion> Errores { get; }

    [MemberNotNullWhen(true, nameof(Articulo))]
    public bool EsGrabado => Articulo is not null;

    public static ResultadoAlta Grabado(ArticuloGrabado articulo)
    {
        ArgumentNullException.ThrowIfNull(articulo);
        return new ResultadoAlta(articulo, []);
    }

    public static ResultadoAlta Invalido(IReadOnlyList<ErrorValidacion> errores)
    {
        if (errores is null || errores.Count == 0)
        {
            throw new ArgumentException("Un alta inválida necesita al menos un error.", nameof(errores));
        }

        return new ResultadoAlta(null, [.. errores]);
    }
}

/// <summary>
/// Da de alta un artículo con su precio de venta calculado (FR-01 a FR-10). Si algún campo incumple la política
/// devuelve <see cref="ResultadoAlta.Invalido"/> con todos los errores sin leer la configuración ni la base. Si no, lee
/// la configuración vigente en cada llamada (FR-08), calcula el precio (fuera de rango: error en el margen, D-06),
/// rechaza un código en el proveedor ya usado (por el chequeo previo o por el índice único al confirmar, D-07) y graba.
/// Sin fila de configuración, y ante cualquier otra falla al confirmar, la excepción se propaga. No registra en el log
/// los datos del pedido.
/// </summary>
public sealed class AltaArticulo(
    IArticuloRepository articulos,
    IConfiguracionNegocioRepository configuracion,
    IUnidadDeTrabajo unidadDeTrabajo)
{
    public async Task<ResultadoAlta> EjecutarAsync(DatosArticulo datos, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(datos);
        var errores = ValidacionArticulo.Validar(datos);
        if (errores.Count > 0)
        {
            return ResultadoAlta.Invalido(errores);
        }

        // La política ya aceptó los cuatro: los valores existen y los textos son UTF-16 válido.
        var codigoProveedor = datos.CodigoProveedor!;
        var costo = datos.PrecioCosto!.Value;
        var margen = datos.MargenUtilidad!.Value;

        var vigente = await configuracion.ObtenerAsync(cancelacion);
        var calculo = CalculadoraPrecioVenta.Calcular(costo, margen, vigente);
        if (calculo.Infraccion is { } infraccion)
        {
            return ResultadoAlta.Invalido([ValidacionArticulo.Traducir(infraccion)]);
        }

        var clave = NormalizacionCodigo.Clave(codigoProveedor);
        if (await articulos.ExisteClaveAsync(clave, cancelacion))
        {
            return ResultadoAlta.Invalido([ValidacionArticulo.CodigoDuplicado]);
        }

        articulos.Agregar(Articulo.Nuevo(
            codigoProveedor, datos.Descripcion!, costo, margen, calculo.Precio!.PrecioVenta));

        try
        {
            await unidadDeTrabajo.ConfirmarAsync(cancelacion);
        }
        catch (ConflictoAlConfirmarException)
        {
            // D-07: en el alta de un artículo solo puede venir del índice único de la clave; no se guardó nada.
            return ResultadoAlta.Invalido([ValidacionArticulo.CodigoDuplicado]);
        }

        // Sin cancelación a propósito: el alta ya quedó confirmada, y cortar esta lectura haría que la operadora, al
        // reintentar, viera "código duplicado" por su propio artículo.
        var grabado = await articulos.ObtenerPorClaveAsync(clave, CancellationToken.None)
            ?? throw new InvalidOperationException("El artículo confirmado no se encontró por su clave: estado corrupto.");
        return ResultadoAlta.Grabado(ArticuloGrabado.Desde(grabado));
    }
}
