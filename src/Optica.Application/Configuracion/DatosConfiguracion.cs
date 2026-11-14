namespace Optica.Application.Configuracion;

/// <summary>
/// Los parámetros tal como llegan; la condición fiscal en su texto de la Api. Un tope ausente es válido y deja la
/// configuración sin tope (D-02 de FEAT-001c). Un punto de venta ausente es válido y conserva el guardado (D-27 de
/// FEAT-007). Lo graba <c>GrabarConfiguracionConRecalculo</c>, en Catálogo (D-10 y D-16 de FEAT-002c): Configuración no
/// depende de Catálogo.
/// </summary>
public sealed record DatosConfiguracion(
    decimal? AlicuotaIva, string? CondicionFiscal, decimal? MultiploRedondeo, decimal? TopeIdentificacion,
    int? PuntoVenta = null);
