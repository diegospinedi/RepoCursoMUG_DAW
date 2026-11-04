namespace Optica.Domain.Comun;

/// <summary>
/// Reglas numéricas que comparten los módulos que guardan importes o porcentajes en centésimos enteros (ADR-008,
/// D-12 de FEAT-002a).
/// </summary>
public static class ReglasImporte
{
    /// <summary>
    /// El mayor importe cuyo valor en centavos entra en un entero de 64 bits (<c>long.MaxValue / 100</c>, truncado a
    /// 2 decimales), que es como el modelo representa los importes (ADR-008). Es el límite del tipo, no un tope de
    /// negocio.
    /// </summary>
    public const decimal ImporteMaximo = 92_233_720_368_547_758.07m;

    /// <summary>
    /// Verdadero si el valor tiene más de 2 decimales significativos. Se mide por el valor, no por
    /// <c>decimal.Scale</c>: 21.500 y 0.010 llegan con escala 3 y tienen 2 decimales o menos.
    /// </summary>
    public static bool TieneMasDeDosDecimales(decimal valor) => valor != decimal.Round(valor, 2);
}
