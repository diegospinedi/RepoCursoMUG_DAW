namespace Optica.Application.Configuracion;

/// <summary>
/// Los cuatro parámetros tal como llegan; la condición fiscal en su texto de la Api. Un tope ausente es válido y deja
/// la configuración sin tope (D-02 de FEAT-001c). Lo graba <c>GrabarConfiguracionConRecalculo</c>, en Catálogo (D-10
/// y D-16 de FEAT-002c): Configuración no depende de Catálogo.
/// </summary>
public sealed record DatosConfiguracion(
    decimal? AlicuotaIva, string? CondicionFiscal, decimal? MultiploRedondeo, decimal? TopeIdentificacion);
