using Optica.Domain.Configuracion;
using Optica.Domain.Facturacion;
using Optica.Domain.Presupuestos;

namespace Optica.Domain.Tests.Facturacion;

// Datos ficticios (D-17 de FEAT-003a): ninguno corresponde a una persona real.
public class ReglasEmisionTests
{
    private const string Dni = "23456789";
    private static readonly DateOnly Fecha = new(2026, 10, 30);

    private static ClientePresupuesto Cliente() =>
        new("Zabaleta", "Ernestina", "23.456.789", "Calle Falsa 123", "ernestina@example.com", "11 5555-0000");

    private static LineaPresupuesto Linea(long codigo, decimal importe) =>
        new(codigo, "Armazón metálico", importe, 1, 0m, importe, importe);

    private static Presupuesto Final(params decimal[] importes) =>
        Borrador(importes).PasarAFinal();

    private static Presupuesto Borrador(params decimal[] importes) =>
        Presupuesto.Nuevo(
            41, new DateOnly(2026, 10, 1), Cliente(), [.. importes.Select((importe, i) => Linea(i + 1, importe))]);

    private static ConfiguracionNegocio Configuracion(
        CondicionFiscal condicion = CondicionFiscal.ResponsableInscripto,
        decimal alicuota = 21m,
        decimal? tope = 100_000m,
        int? puntoVenta = 3) =>
        new(alicuota, condicion, 10m, tope, puntoVenta);

    private static SolicitudComprobante Armada(
        Presupuesto presupuesto, ConfiguracionNegocio configuracion,
        ConceptoFactura concepto = ConceptoFactura.Productos)
    {
        var resultado = ReglasEmision.Armar(presupuesto, configuracion, concepto, Fecha);
        Assert.True(resultado.EsArmada, "Se esperaba la solicitud armada.");
        Assert.Null(resultado.Motivo);
        return resultado.Solicitud;
    }

    private static void AssertRechazada(ResultadoArmadoEmision resultado, MotivoEmisionRechazada motivo)
    {
        Assert.False(resultado.EsArmada, "Se esperaba la emisión rechazada.");
        Assert.Null(resultado.Solicitud);
        Assert.Equal(motivo, resultado.Motivo);
    }

    [Fact]
    public void Armar_ResponsableInscripto_FacturaB()
    {
        var solicitud = Armada(Final(1815m), Configuracion());

        Assert.Equal(TipoComprobante.FacturaB, solicitud.Tipo);
        Assert.Equal(3, solicitud.PuntoVenta);
        Assert.Equal(21m, solicitud.Importes.Alicuota);
        Assert.True(solicitud.Importes.DiscriminaIva);
    }

    [Fact]
    public void Armar_Monotributo_FacturaC()
    {
        // Con Monotributo la alícuota no se mira, aunque no sea una de ARCA (FR-27 rige solo la B).
        var solicitud = Armada(Final(1815m), Configuracion(CondicionFiscal.Monotributo, alicuota: 19m));

        Assert.Equal(TipoComprobante.FacturaC, solicitud.Tipo);
        Assert.Equal(3, solicitud.PuntoVenta);
        Assert.Equal(1815m, solicitud.Importes.Total);
        Assert.False(solicitud.Importes.DiscriminaIva);
    }

    [Fact]
    public void Armar_TotalDelPresupuesto_3Lineas3000()
    {
        var presupuesto = Final(1000m, 1500m, 500m);

        var solicitud = Armada(presupuesto, Configuracion());

        Assert.Equal(3000m, solicitud.Importes.Total);

        // Las 3 líneas pasan a la factura sin volver a cargarlas (FR-01).
        var factura = Factura.Emitida(
            presupuesto, solicitud, 125, "75123456789012", new DateOnly(2026, 11, 9), EntornoArca.Simulado);
        Assert.Equal(3, factura.Lineas.Count);
        Assert.Equal([1000m, 1500m, 500m], factura.Lineas.Select(linea => linea.PrecioFinal));
        Assert.Equal([1, 2, 3], factura.Lineas.Select(linea => linea.Orden));
        Assert.Equal(3000m, factura.Importes.Total);
    }

    [Fact]
    public void Armar_FechaDelDia()
    {
        var solicitud = Armada(Final(1815m), Configuracion());

        Assert.Equal(Fecha, solicitud.Fecha);
    }

    [Fact]
    public void Armar_ProductosYServicios_TresFechasIguales()
    {
        var solicitud = Armada(Final(1815m), Configuracion(), ConceptoFactura.ProductosYServicios);

        Assert.Equal(ConceptoFactura.ProductosYServicios, solicitud.Concepto);
        Assert.Equal(Fecha, solicitud.FechaServicioDesde);
        Assert.Equal(Fecha, solicitud.FechaServicioHasta);
        Assert.Equal(Fecha, solicitud.FechaVencimientoPago);
    }

    [Fact]
    public void Armar_Productos_SinFechasDeServicio()
    {
        var solicitud = Armada(Final(1815m), Configuracion(), ConceptoFactura.Productos);

        Assert.Equal(ConceptoFactura.Productos, solicitud.Concepto);
        Assert.Equal(Fecha, solicitud.Fecha);
        Assert.Null(solicitud.FechaServicioDesde);
        Assert.Null(solicitud.FechaServicioHasta);
        Assert.Null(solicitud.FechaVencimientoPago);
    }

    [Fact]
    public void Armar_Total50000Tope100000ConDni_SinIdentificar()
    {
        var solicitud = Armada(Final(50_000m), Configuracion(tope: 100_000m));

        Assert.Equal(50_000m, solicitud.Importes.Total);
        Assert.False(solicitud.Receptor.EstaIdentificado);
        Assert.Null(solicitud.Receptor.Dni);
    }

    [Fact]
    public void Armar_TotalIgualAlTope_SinIdentificar()
    {
        var solicitud = Armada(Final(100_000m), Configuracion(tope: 100_000m));

        Assert.Equal(100_000m, solicitud.Importes.Total);
        Assert.False(solicitud.Receptor.EstaIdentificado);
        Assert.Null(solicitud.Receptor.Dni);
    }

    [Fact]
    public void Armar_TotalUnCentavoSobreElTope_ConDni()
    {
        var solicitud = Armada(Final(100_000.01m), Configuracion(tope: 100_000m));

        Assert.True(solicitud.Receptor.EstaIdentificado);
        Assert.Equal(Dni, solicitud.Receptor.Dni);
    }

    [Fact]
    public void Armar_Tope40000Total50000_ConDni()
    {
        // El tope es el de la configuración recibida (FR-12), no uno fijo.
        var solicitud = Armada(Final(50_000m), Configuracion(tope: 40_000m));

        Assert.True(solicitud.Receptor.EstaIdentificado);
        Assert.Equal(Dni, solicitud.Receptor.Dni);
    }

    [Fact]
    public void Armar_TotalCero_SeArma()
    {
        // D-43 (decisión del usuario): un presupuesto Final con total 0 se factura igual.
        var b = Armada(Final(0m), Configuracion());
        Assert.Equal(TipoComprobante.FacturaB, b.Tipo);
        Assert.Equal(0m, b.Importes.Total);
        Assert.Equal(0m, b.Importes.Neto);
        Assert.Equal(0m, b.Importes.Iva);
        Assert.True(b.Importes.DiscriminaIva);
        Assert.False(b.Receptor.EstaIdentificado);

        var c = Armada(Final(0m), Configuracion(CondicionFiscal.Monotributo));
        Assert.Equal(TipoComprobante.FacturaC, c.Tipo);
        Assert.Equal(0m, c.Importes.Total);
        Assert.False(c.Importes.DiscriminaIva);
    }

    [Fact]
    public void Armar_MonotributoTotalSobreElTope_FacturaCConDni()
    {
        // El tope rige también para la Factura C (FR-11 no distingue el tipo).
        var solicitud = Armada(Final(50_000m), Configuracion(CondicionFiscal.Monotributo, tope: 40_000m));

        Assert.Equal(TipoComprobante.FacturaC, solicitud.Tipo);
        Assert.True(solicitud.Receptor.EstaIdentificado);
        Assert.Equal(Dni, solicitud.Receptor.Dni);
    }

    [Fact]
    public void Armar_Alicuota10_5_UsaLaDeLaConfiguracion()
    {
        var solicitud = Armada(Final(1105m), Configuracion(alicuota: 10.5m));

        Assert.Equal(10.5m, solicitud.Importes.Alicuota);
        Assert.Equal(1000m, solicitud.Importes.Neto);
        Assert.Equal(105m, solicitud.Importes.Iva);
    }

    [Fact]
    public void Armar_Borrador_Rechazado()
    {
        // Borrador gana sobre las demás faltas: se rechaza antes de mirar la configuración.
        var resultado = ReglasEmision.Armar(
            Borrador(1815m), Configuracion(tope: null, puntoVenta: null, alicuota: 19m), ConceptoFactura.Productos,
            Fecha);

        AssertRechazada(resultado, MotivoEmisionRechazada.Borrador);
    }

    [Fact]
    public void Armar_TopeFaltante_Rechazado()
    {
        var resultado = ReglasEmision.Armar(
            Final(1815m), Configuracion(tope: null, puntoVenta: null, alicuota: 19m), ConceptoFactura.Productos, Fecha);

        AssertRechazada(resultado, MotivoEmisionRechazada.SinTope);
    }

    [Fact]
    public void Armar_PuntoVentaFaltante_Rechazado()
    {
        var resultado = ReglasEmision.Armar(
            Final(1815m), Configuracion(puntoVenta: null, alicuota: 19m), ConceptoFactura.Productos, Fecha);

        AssertRechazada(resultado, MotivoEmisionRechazada.SinPuntoVenta);
    }

    [Fact]
    public void Armar_Alicuota19ResponsableInscripto_Rechazado()
    {
        var resultado = ReglasEmision.Armar(
            Final(1815m), Configuracion(alicuota: 19m), ConceptoFactura.Productos, Fecha);

        AssertRechazada(resultado, MotivoEmisionRechazada.AlicuotaNoAdmitida);
    }

    [Fact]
    public void Armar_ConceptoOFechaInvalidos_Error()
    {
        var concepto = Assert.Throws<ArgumentException>(() =>
            ReglasEmision.Armar(Final(1815m), Configuracion(), (ConceptoFactura)99, Fecha));
        Assert.Equal("concepto", concepto.ParamName);
        Assert.DoesNotContain("99", concepto.Message, StringComparison.Ordinal);

        var fecha = Assert.Throws<ArgumentException>(() =>
            ReglasEmision.Armar(Final(1815m), Configuracion(), ConceptoFactura.Productos, default));
        Assert.Equal("fecha", fecha.ParamName);

        Assert.Throws<ArgumentNullException>(() =>
            ReglasEmision.Armar(null!, Configuracion(), ConceptoFactura.Productos, Fecha));
        Assert.Throws<ArgumentNullException>(() =>
            ReglasEmision.Armar(Final(1815m), null!, ConceptoFactura.Productos, Fecha));
    }
}
