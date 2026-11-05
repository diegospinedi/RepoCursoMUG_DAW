using Optica.Application.Comun;
using Optica.Domain.Catalogo;

namespace Optica.Application.Catalogo;

/// <summary>
/// Desactiva o reactiva un artículo (FR-05, FR-06). Un estado faltante es un error en <c>activo</c>; un artículo
/// inexistente, <see cref="ResultadoEdicionArticulo.NoEncontrado"/>. Pedir el estado que ya tiene responde grabado sin
/// escribir (D-09). Si no, registra el cambio de estado, que escribe solo el estado y la versión, confirma y responde
/// con el artículo releído, con el estado y la versión nuevos (D-26); si la relectura no lo encuentra, el estado está
/// corrupto y lanza <see cref="InvalidOperationException"/>. Un conflicto de
/// concurrencia relee: inexistente, no encontrado; ya con el estado pedido, grabado con el artículo releído; si no,
/// cambiado (D-16). Cualquier otra falla se propaga. No registra nada en el log (D-12).
/// </summary>
/// <remarks>
/// Resultados posibles: <see cref="TipoResultadoEdicion.Grabado"/>, <see cref="TipoResultadoEdicion.Invalido"/> (solo
/// <c>activo</c> faltante), <see cref="TipoResultadoEdicion.NoEncontrado"/> y <see cref="TipoResultadoEdicion.Cambiado"/>,
/// siempre como <see cref="ResultadoEdicionArticulo.CambiadoAlCambiarEstado"/>. Nunca devuelve
/// <see cref="TipoResultadoEdicion.Inactivo"/>: desactivar y reactivar no dependen del estado. Lanza
/// <see cref="InvalidOperationException"/> si el artículo confirmado no se relee (estado corrupto) y propaga un
/// <see cref="ConflictoAlConfirmarException"/> con motivo <see cref="MotivoConflicto.Unicidad"/>.
/// </remarks>
public sealed class CambiarEstadoArticulo(IArticuloRepository articulos, IUnidadDeTrabajo unidadDeTrabajo)
{
    public async Task<ResultadoEdicionArticulo> EjecutarAsync(
        long codigo, bool? activo, CancellationToken cancelacion = default)
    {
        if (activo is not { } pedido)
        {
            return ResultadoEdicionArticulo.Invalido([ValidacionArticulo.ActivoFaltante]);
        }

        var leido = await articulos.ObtenerAsync(codigo, cancelacion);
        if (leido is null)
        {
            return ResultadoEdicionArticulo.NoEncontrado;
        }

        if (leido.Activo == pedido)
        {
            // D-09: CambiarEstado siempre incrementa la versión, así que no se llama.
            return ResultadoEdicionArticulo.Grabado(ArticuloGrabado.Desde(leido));
        }

        articulos.CambiarEstado(codigo, pedido);

        try
        {
            await unidadDeTrabajo.ConfirmarAsync(cancelacion);
        }
        catch (ConflictoAlConfirmarException conflicto) when (conflicto.Motivo == MotivoConflicto.Concurrencia)
        {
            // D-16: no se guardó nada; si otra escritura ya dejó el estado pedido, el pedido está cumplido.
            var releido = await articulos.ObtenerAsync(codigo, cancelacion);
            return releido switch
            {
                null => ResultadoEdicionArticulo.NoEncontrado,
                _ when releido.Activo == pedido => ResultadoEdicionArticulo.Grabado(ArticuloGrabado.Desde(releido)),
                _ => ResultadoEdicionArticulo.CambiadoAlCambiarEstado,
            };
        }

        // D-26: relee para devolver el estado y la versión ya incrementada. Sin cancelación a propósito, como el alta:
        // el cambio ya quedó confirmado.
        var grabado = await articulos.ObtenerAsync(codigo, CancellationToken.None)
            ?? throw new InvalidOperationException("El artículo confirmado no se encontró por su código: estado corrupto.");
        return ResultadoEdicionArticulo.Grabado(ArticuloGrabado.Desde(grabado));
    }
}
