using System.Diagnostics.CodeAnalysis;

namespace Optica.Domain.Catalogo;

/// <summary>
/// El resultado de <see cref="Articulo.ConPrecioRecalculado"/> (FR-01 de FEAT-002c): <see cref="SinCambio"/> si el
/// precio de venta recalculado es igual al guardado, <see cref="Cambiado"/> con la copia con el precio nuevo, o
/// <see cref="FueraDeRango"/> si con la configuración vigente el precio no entra en el rango de importes (D-01). El
/// rechazo por rango no lleva el artículo: quien recalcula ya lo tiene.
/// </summary>
public sealed class ResultadoRecalculo
{
    private ResultadoRecalculo(Articulo? articulo, bool esFueraDeRango)
    {
        Articulo = articulo;
        EsFueraDeRango = esFueraDeRango;
    }

    /// <summary>El precio recalculado es igual al guardado: no hay nada que grabar (D-04).</summary>
    public static ResultadoRecalculo SinCambio { get; } = new(null, esFueraDeRango: false);

    /// <summary>El precio recalculado quedaría fuera del rango de importes: el cambio no se puede grabar (D-01).</summary>
    public static ResultadoRecalculo FueraDeRango { get; } = new(null, esFueraDeRango: true);

    /// <summary>La copia con el precio nuevo; null si no cambió o quedó fuera de rango.</summary>
    public Articulo? Articulo { get; }

    /// <summary>Verdadero si el precio recalculado quedaría fuera de rango.</summary>
    public bool EsFueraDeRango { get; }

    /// <summary>Verdadero si el precio cambió: <see cref="Articulo"/> tiene la copia con el precio nuevo.</summary>
    [MemberNotNullWhen(true, nameof(Articulo))]
    public bool EsCambiado => Articulo is not null;

    /// <summary>Verdadero si el precio recalculado es igual al guardado.</summary>
    public bool EsSinCambio => !EsCambiado && !EsFueraDeRango;

    internal static ResultadoRecalculo Cambiado(Articulo articulo)
    {
        ArgumentNullException.ThrowIfNull(articulo);
        return new ResultadoRecalculo(articulo, esFueraDeRango: false);
    }
}
