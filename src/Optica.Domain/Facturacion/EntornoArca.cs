namespace Optica.Domain.Facturacion;

/// <summary>
/// El entorno de ARCA en que se emitió una factura (FR-18, D-11). Una factura <see cref="Simulado"/> no tiene validez
/// fiscal: su CAE no existe en ARCA (AGENTS.md).
/// </summary>
public enum EntornoArca
{
    Simulado,
    Homologacion,
    Produccion,
}
