using Optica.Domain.Facturacion;

namespace Optica.Infrastructure.Tests.Arca;

/// <summary>Solicitudes de prueba para los autorizadores de ARCA, sin datos de personas reales.</summary>
internal static class SolicitudesArca
{
    public static readonly DateOnly Fecha = new(2026, 10, 30);

    public static SolicitudComprobante FacturaB(int puntoVenta = 3) => new(
        TipoComprobante.FacturaB,
        puntoVenta,
        Fecha,
        ConceptoFactura.Productos,
        ReceptorFactura.SinIdentificar,
        ImportesFactura.ParaFacturaB(1210m, 21m));

    public static SolicitudComprobante FacturaC(int puntoVenta = 3) => new(
        TipoComprobante.FacturaC,
        puntoVenta,
        Fecha,
        ConceptoFactura.Productos,
        ReceptorFactura.SinIdentificar,
        ImportesFactura.ParaFacturaC(1000m));
}
