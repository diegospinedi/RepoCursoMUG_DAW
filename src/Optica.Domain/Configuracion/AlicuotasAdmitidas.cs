namespace Optica.Domain.Configuracion;

/// <summary>
/// Las alícuotas de IVA que ARCA acepta en la Factura B: 0, 2,5, 5, 10,5, 21 y 27 (FR-26 de FEAT-007). Es la regla de
/// escritura de la configuración; al leer se tolera cualquier alícuota de 0 a 100 (D-06). Los códigos de WSFEv1 de cada
/// una no viven acá sino en la integración con ARCA (D-20).
/// </summary>
public static class AlicuotasAdmitidas
{
    /// <summary>Las 6 alícuotas, de menor a mayor. La lista no se puede modificar.</summary>
    public static IReadOnlyList<decimal> Todas { get; } = Array.AsReadOnly([0m, 2.5m, 5m, 10.5m, 21m, 27m]);

    /// <summary>
    /// Verdadero si <paramref name="alicuota"/> es una de <see cref="Todas"/>. Compara el valor, no la escala: 21,00 es
    /// admitida.
    /// </summary>
    public static bool EsAdmitida(decimal alicuota) => Todas.Contains(alicuota);
}
