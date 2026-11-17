using System.Diagnostics.CodeAnalysis;

namespace Optica.Domain.Catalogo;

/// <summary>
/// El resultado de <see cref="Articulo.ConCostoNuevo"/> (FR-06 y FR-11 de FEAT-011): <see cref="EsAplicado"/> con la
/// copia que lleva el costo y el precio de venta nuevos, y <see cref="Cambia"/> falso si los dos son iguales a los
/// guardados (D-13); o <see cref="FueraDeRango"/> si el costo nuevo o el precio de venta recalculado quedan fuera del
/// rango de importes (D-09). El rechazo por rango no lleva el artículo: quien lo pide ya lo tiene.
/// </summary>
public sealed class ResultadoCostoNuevo
{
    private ResultadoCostoNuevo(Articulo? articulo, bool cambia)
    {
        Articulo = articulo;
        Cambia = cambia;
    }

    /// <summary>
    /// El costo nuevo o el precio de venta recalculado quedan fuera del rango de importes (D-09): el cambio no se puede
    /// grabar.
    /// </summary>
    public static ResultadoCostoNuevo FueraDeRango { get; } = new(null, cambia: false);

    /// <summary>La copia con el costo y el precio de venta nuevos; null si quedó fuera de rango.</summary>
    public Articulo? Articulo { get; }

    /// <summary>
    /// Verdadero si el costo o el precio de venta difieren de los guardados; falso si los dos son iguales, aunque
    /// lleguen con otra escala, y entonces no hay nada que grabar (D-13).
    /// </summary>
    public bool Cambia { get; }

    /// <summary>Verdadero si el costo nuevo se puede aplicar: <see cref="Articulo"/> tiene la copia.</summary>
    [MemberNotNullWhen(true, nameof(Articulo))]
    public bool EsAplicado => Articulo is not null;

    /// <summary>
    /// Verdadero si el costo nuevo o el precio de venta recalculado quedan fuera del rango de importes (D-09).
    /// </summary>
    public bool EsFueraDeRango => !EsAplicado;

    internal static ResultadoCostoNuevo Aplicado(Articulo articulo, bool cambia)
    {
        ArgumentNullException.ThrowIfNull(articulo);
        return new ResultadoCostoNuevo(articulo, cambia);
    }
}
