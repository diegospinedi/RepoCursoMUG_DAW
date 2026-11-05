using System.Diagnostics.CodeAnalysis;

namespace Optica.Domain.Catalogo;

/// <summary>
/// El resultado de <see cref="Articulo.Modificar"/> (D-20): <see cref="Modificado"/> con el artículo ya modificado,
/// <see cref="Invalido"/> con todas las infracciones, una por campo, o <see cref="Inactivo"/> si el artículo está
/// inactivo y no se evaluaron los datos (D-02). El rechazo por inactivo no es una <see cref="InfraccionArticulo"/>,
/// cuyos valores pertenecen cada uno a un campo.
/// </summary>
public sealed class ResultadoModificacion
{
    private ResultadoModificacion(Articulo? articulo, IReadOnlyList<InfraccionArticulo> infracciones, bool esInactivo)
    {
        Articulo = articulo;
        Infracciones = infracciones;
        EsInactivo = esInactivo;
    }

    /// <summary>El rechazo de un artículo inactivo: sin artículo y sin infracciones.</summary>
    public static ResultadoModificacion Inactivo { get; } = new(null, [], esInactivo: true);

    /// <summary>El artículo modificado; null si la modificación fue inválida o el artículo está inactivo.</summary>
    public Articulo? Articulo { get; }

    /// <summary>Las infracciones de los datos; vacío si se modificó o si el artículo está inactivo.</summary>
    public IReadOnlyList<InfraccionArticulo> Infracciones { get; }

    /// <summary>Verdadero si el artículo está inactivo y no se modificó (D-02).</summary>
    public bool EsInactivo { get; }

    /// <summary>Verdadero si el artículo se modificó: <see cref="Articulo"/> tiene la copia con los datos nuevos.</summary>
    [MemberNotNullWhen(true, nameof(Articulo))]
    public bool EsModificado => Articulo is not null;

    /// <summary>Verdadero si algún dato incumple las reglas: <see cref="Infracciones"/> las tiene todas.</summary>
    public bool EsInvalido => Infracciones.Count > 0;

    internal static ResultadoModificacion Modificado(Articulo articulo)
    {
        ArgumentNullException.ThrowIfNull(articulo);
        return new ResultadoModificacion(articulo, [], esInactivo: false);
    }

    /// <exception cref="ArgumentException">La lista está vacía: un resultado inválido necesita una infracción.</exception>
    internal static ResultadoModificacion Invalido(IReadOnlyList<InfraccionArticulo> infracciones)
    {
        if (infracciones is null || infracciones.Count == 0)
        {
            throw new ArgumentException("Una modificación inválida necesita al menos una infracción.", nameof(infracciones));
        }

        return new ResultadoModificacion(null, [.. infracciones], esInactivo: false);
    }
}
