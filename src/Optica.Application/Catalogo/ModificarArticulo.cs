using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Optica.Application.Comun;
using Optica.Domain.Catalogo;
using Optica.Domain.Configuracion;

namespace Optica.Application.Catalogo;

/// <summary>Cómo terminó una modificación o un cambio de estado de un artículo.</summary>
public enum TipoResultadoEdicion
{
    Grabado,
    Invalido,
    NoEncontrado,
    Inactivo,
    Cambiado,
}

/// <summary>
/// El resultado de <see cref="ModificarArticulo"/> y de <see cref="CambiarEstadoArticulo"/>: grabado, con el artículo;
/// inválido, con un error por campo; no encontrado; inactivo (D-02) o cambiado por otra escritura (D-08, D-16), con el
/// texto fijo de <see cref="Detalle"/>.
/// </summary>
public sealed class ResultadoEdicionArticulo
{
    private ResultadoEdicionArticulo(
        TipoResultadoEdicion tipo, ArticuloGrabado? articulo, IReadOnlyList<ErrorValidacion> errores, string? detalle)
    {
        Tipo = tipo;
        Articulo = articulo;
        Errores = errores;
        Detalle = detalle;
    }

    /// <summary>El artículo no existe.</summary>
    public static ResultadoEdicionArticulo NoEncontrado { get; } =
        new(TipoResultadoEdicion.NoEncontrado, null, [], null);

    /// <summary>El artículo está inactivo y no se modificó (D-02).</summary>
    public static ResultadoEdicionArticulo Inactivo { get; } =
        new(TipoResultadoEdicion.Inactivo, null, [], ValidacionArticulo.DetalleInactivo);

    /// <summary>Otra pestaña modificó el artículo entre la lectura y la grabación (D-08).</summary>
    public static ResultadoEdicionArticulo CambiadoAlEditar { get; } =
        new(TipoResultadoEdicion.Cambiado, null, [], ValidacionArticulo.DetalleCambiadoAlEditar);

    /// <summary>Otra escritura cambió el artículo entre la lectura y el cambio de estado (D-16).</summary>
    public static ResultadoEdicionArticulo CambiadoAlCambiarEstado { get; } =
        new(TipoResultadoEdicion.Cambiado, null, [], ValidacionArticulo.DetalleCambiadoAlCambiarEstado);

    public TipoResultadoEdicion Tipo { get; }

    /// <summary>El artículo grabado; null salvo cuando <see cref="Tipo"/> es <see cref="TipoResultadoEdicion.Grabado"/>.</summary>
    public ArticuloGrabado? Articulo { get; }

    /// <summary>Errores de validación; vacío salvo cuando <see cref="Tipo"/> es <see cref="TipoResultadoEdicion.Invalido"/>.</summary>
    public IReadOnlyList<ErrorValidacion> Errores { get; }

    /// <summary>
    /// El texto fijo de un rechazo por inactivo o por cambio; null en los demás resultados. No repite datos recibidos.
    /// </summary>
    public string? Detalle { get; }

    [MemberNotNullWhen(true, nameof(Articulo))]
    public bool EsGrabado => Tipo == TipoResultadoEdicion.Grabado && Articulo is not null;

    public static ResultadoEdicionArticulo Grabado(ArticuloGrabado articulo)
    {
        ArgumentNullException.ThrowIfNull(articulo);
        return new ResultadoEdicionArticulo(TipoResultadoEdicion.Grabado, articulo, [], null);
    }

    public static ResultadoEdicionArticulo Invalido(IReadOnlyList<ErrorValidacion> errores)
    {
        if (errores is null || errores.Count == 0)
        {
            throw new ArgumentException("Una edición inválida necesita al menos un error.", nameof(errores));
        }

        return new ResultadoEdicionArticulo(TipoResultadoEdicion.Invalido, null, [.. errores], null);
    }

}

/// <summary>
/// Modifica los datos de un artículo (FR-01, FR-03). Una versión faltante o fuera de rango es un error en
/// <c>version</c> (D-17). Lee el artículo: inexistente, <see cref="ResultadoEdicionArticulo.NoEncontrado"/>; con una
/// versión distinta de la enviada, inactivo o cambiado sin escribir (D-17). Si no, lee la configuración vigente y aplica
/// <see cref="Articulo.Modificar"/>: inactivo (D-02), inválido con todos los errores o, si el código en el proveedor ya
/// lo tiene otro artículo, inválido con el duplicado. Registra la modificación con la versión enviada y confirma: un
/// conflicto de unicidad es el duplicado; uno de concurrencia relee y responde inactivo, no encontrado o cambiado
/// (D-08). Grabado devuelve el artículo releído. Cualquier otra falla se propaga. No registra nada en el log (D-12).
/// </summary>
/// <remarks>
/// Resultados posibles: <see cref="TipoResultadoEdicion.Grabado"/>, <see cref="TipoResultadoEdicion.Invalido"/> (datos,
/// código duplicado o versión), <see cref="TipoResultadoEdicion.NoEncontrado"/>,
/// <see cref="TipoResultadoEdicion.Inactivo"/> y <see cref="TipoResultadoEdicion.Cambiado"/>, siempre como
/// <see cref="ResultadoEdicionArticulo.CambiadoAlEditar"/>. Lanza <see cref="ArgumentNullException"/> sin datos,
/// <see cref="InvalidOperationException"/> si el artículo confirmado no se relee (estado corrupto) y
/// <see cref="UnreachableException"/> ante un resultado del dominio no previsto.
/// </remarks>
public sealed class ModificarArticulo(
    IArticuloRepository articulos,
    IConfiguracionNegocioRepository configuracion,
    IUnidadDeTrabajo unidadDeTrabajo)
{
    public async Task<ResultadoEdicionArticulo> EjecutarAsync(
        long codigo, DatosArticulo datos, long? version, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(datos);
        if (ValidacionArticulo.ValidarVersion(version) is { } errorVersion)
        {
            return ResultadoEdicionArticulo.Invalido([errorVersion]);
        }

        var versionEnviada = version!.Value;
        var leido = await articulos.ObtenerAsync(codigo, cancelacion);
        if (leido is null)
        {
            return ResultadoEdicionArticulo.NoEncontrado;
        }

        if (leido.Version != versionEnviada)
        {
            // Misma precedencia que la relectura tras un conflicto: inactivo antes que cambiado.
            return leido.Activo ? ResultadoEdicionArticulo.CambiadoAlEditar : ResultadoEdicionArticulo.Inactivo;
        }

        var vigente = await configuracion.ObtenerAsync(cancelacion);
        var modificacion = leido.Modificar(
            datos.CodigoProveedor, datos.Descripcion, datos.PrecioCosto, datos.MargenUtilidad, vigente);
        if (modificacion.EsInactivo)
        {
            return ResultadoEdicionArticulo.Inactivo;
        }

        if (modificacion.EsInvalido)
        {
            return ResultadoEdicionArticulo.Invalido([.. modificacion.Infracciones.Select(ValidacionArticulo.Traducir)]);
        }

        if (!modificacion.EsModificado)
        {
            throw new UnreachableException("Resultado de la modificación del artículo no previsto.");
        }

        var modificado = modificacion.Articulo;
        if (await articulos.ExisteClaveDeOtroAsync(modificado.CodigoProveedorClave, codigo, cancelacion))
        {
            return ResultadoEdicionArticulo.Invalido([ValidacionArticulo.CodigoDuplicado]);
        }

        articulos.Actualizar(modificado, versionEnviada);

        try
        {
            await unidadDeTrabajo.ConfirmarAsync(cancelacion);
        }
        catch (ConflictoAlConfirmarException conflicto) when (conflicto.Motivo == MotivoConflicto.Unicidad)
        {
            // El índice único de la clave: otro artículo la tomó entre el chequeo y la grabación.
            return ResultadoEdicionArticulo.Invalido([ValidacionArticulo.CodigoDuplicado]);
        }
        catch (ConflictoAlConfirmarException)
        {
            // Concurrencia (D-08): no se guardó nada; la relectura dice por qué.
            var releido = await articulos.ObtenerAsync(codigo, cancelacion);
            return releido switch
            {
                null => ResultadoEdicionArticulo.NoEncontrado,
                { Activo: false } => ResultadoEdicionArticulo.Inactivo,
                _ => ResultadoEdicionArticulo.CambiadoAlEditar,
            };
        }

        // Sin cancelación a propósito, como el alta: la modificación ya quedó confirmada.
        var grabado = await articulos.ObtenerAsync(codigo, CancellationToken.None)
            ?? throw new InvalidOperationException("El artículo confirmado no se encontró por su código: estado corrupto.");
        return ResultadoEdicionArticulo.Grabado(ArticuloGrabado.Desde(grabado));
    }
}
