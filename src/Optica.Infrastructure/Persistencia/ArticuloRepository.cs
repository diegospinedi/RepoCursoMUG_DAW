using Microsoft.EntityFrameworkCore;
using Optica.Domain.Catalogo;

namespace Optica.Infrastructure.Persistencia;

/// <summary>
/// Artículos del catálogo sobre la tabla <c>Articulo</c>. Mapea la fila a la entidad de dominio sin aritmética: la
/// conversión a centésimos vive en <see cref="OpticaDbContext"/> (ADR-008). <see cref="Agregar"/> solo deja el alta en
/// el contexto compartido, que confirma <see cref="UnidadDeTrabajoEf"/>; una clave repetida se informa ahí como
/// conflicto, por el índice UNIQUE de la clave (ADR-009).
/// </summary>
public sealed class ArticuloRepository(OpticaDbContext contexto) : IArticuloRepository
{
    private const string MensajeFilaInvalida = "El artículo guardado no es válido.";

    /// <summary>Compara la clave tal como llega, byte a byte: normalizarla es trabajo del dominio (ADR-009).</summary>
    public Task<bool> ExisteClaveAsync(string clave, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(clave);
        return contexto.Articulos.AsNoTracking().AnyAsync(a => a.CodigoProveedorClave == clave, cancelacion);
    }

    /// <exception cref="ArgumentException">El artículo ya tiene código: esto es un alta y el código lo asigna la base.</exception>
    public void Agregar(Articulo articulo)
    {
        ArgumentNullException.ThrowIfNull(articulo);
        if (articulo.Codigo is not null)
        {
            throw new ArgumentException("Un artículo nuevo no tiene código todavía.", nameof(articulo));
        }

        contexto.Articulos.Add(new ArticuloFila
        {
            CodigoProveedor = articulo.CodigoProveedor,
            CodigoProveedorClave = articulo.CodigoProveedorClave,
            Descripcion = articulo.Descripcion,
            PrecioCosto = articulo.PrecioCosto,
            MargenUtilidad = articulo.MargenUtilidad,
            PrecioVenta = articulo.PrecioVenta,
        });
    }

    /// <exception cref="InvalidOperationException">La fila no cumple la política: estado corrupto.</exception>
    public async Task<Articulo?> ObtenerAsync(long codigo, CancellationToken cancelacion = default)
    {
        var fila = await contexto.Articulos.AsNoTracking().SingleOrDefaultAsync(a => a.Id == codigo, cancelacion);
        return fila is null ? null : AEntidad(fila);
    }

    /// <exception cref="InvalidOperationException">La fila no cumple la política: estado corrupto.</exception>
    public async Task<Articulo?> ObtenerPorClaveAsync(string clave, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(clave);
        var fila = await contexto.Articulos.AsNoTracking()
            .SingleOrDefaultAsync(a => a.CodigoProveedorClave == clave, cancelacion);
        return fila is null ? null : AEntidad(fila);
    }

    /// <summary>
    /// La entidad de una fila. Una fila que eludió los CHECK, o cuya clave guardada no es la que el dominio calcula para
    /// su código (la unicidad dejaría de valer), no puede representarse: se informa como estado corrupto y no con el
    /// <see cref="ArgumentException"/> del dominio, que significa entrada inválida. El mensaje no lleva valores.
    /// </summary>
    private static Articulo AEntidad(ArticuloFila fila)
    {
        Articulo articulo;
        try
        {
            articulo = new Articulo(
                fila.Id, fila.CodigoProveedor, fila.Descripcion, fila.PrecioCosto, fila.MargenUtilidad, fila.PrecioVenta);
        }
        catch (ArgumentException error)
        {
            // La causa nombra el campo y no el valor (invariante del dominio), así que se puede adjuntar.
            throw new InvalidOperationException(MensajeFilaInvalida, error);
        }

        return string.Equals(articulo.CodigoProveedorClave, fila.CodigoProveedorClave, StringComparison.Ordinal)
            ? articulo
            : throw new InvalidOperationException(MensajeFilaInvalida);
    }
}
