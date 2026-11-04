using Optica.Domain.Catalogo;

namespace Optica.Application.Catalogo;

/// <summary>Consulta un artículo por su código autonumérico; null si no existe.</summary>
public sealed class ObtenerArticulo(IArticuloRepository articulos)
{
    public async Task<ArticuloGrabado?> EjecutarAsync(long codigo, CancellationToken cancelacion = default) =>
        await articulos.ObtenerAsync(codigo, cancelacion) is { } articulo ? ArticuloGrabado.Desde(articulo) : null;
}
