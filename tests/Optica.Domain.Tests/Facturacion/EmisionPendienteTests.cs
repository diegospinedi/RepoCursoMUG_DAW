using System.Reflection;
using Optica.Domain.Facturacion;

namespace Optica.Domain.Tests.Facturacion;

/// <summary>
/// La emisión pendiente con número (Block 1 de FEAT-008, FR-01, AC-01) y la de FEAT-007 sin número (FR-14, AC-15). Datos
/// ficticios: el DNI empieza con 00 y no es de una persona.
/// </summary>
public class EmisionPendienteTests
{
    private const string Dni = "00718294";
    private static readonly DateOnly Fecha = new(2026, 10, 30);
    private static readonly DateTimeOffset Momento = new(2026, 10, 30, 11, 15, 0, TimeSpan.FromHours(-3));

    private static SolicitudComprobante SolicitudB(ConceptoFactura concepto = ConceptoFactura.Productos) =>
        new(TipoComprobante.FacturaB, 3, Fecha, concepto, ReceptorFactura.ConDni(Dni),
            ImportesFactura.ParaFacturaB(1815m, 21m));

    [Fact]
    public void EmisionPendiente_ConSolicitudYNumero_GuardaNumeroConceptoEImportes()
    {
        // AC-01: el número 125, Factura B, $1.815,00 y el presupuesto, registrados antes de enviar.
        var pendiente = new EmisionPendiente(41, Momento, SolicitudB(ConceptoFactura.ProductosYServicios), 125,
            EntornoArca.Homologacion);

        Assert.Equal(41, pendiente.PresupuestoNumero);
        Assert.Equal(TimeSpan.Zero, pendiente.Momento.Offset);
        Assert.Equal(Momento, pendiente.Momento);
        Assert.Equal(TipoComprobante.FacturaB, pendiente.Tipo);
        Assert.Equal(3, pendiente.PuntoVenta);
        Assert.Equal(Fecha, pendiente.Fecha);
        Assert.Equal(1815m, pendiente.Total);
        Assert.Equal(EntornoArca.Homologacion, pendiente.Entorno);

        Assert.True(pendiente.TieneNumero);
        Assert.Equal(125, pendiente.Numero);
        Assert.Equal(ConceptoFactura.ProductosYServicios, pendiente.Concepto);
        var importes = Assert.IsType<ImportesFactura>(pendiente.Importes);
        Assert.Equal(1815m, importes.Total);
        Assert.Equal(1500m, importes.Neto);
        Assert.Equal(315m, importes.Iva);
        Assert.Equal(21m, importes.Alicuota);
        Assert.True(pendiente.ReceptorIdentificado);

        // Sin el DNI: la pendiente guarda si el receptor se identificó, no quién es (Ley 25.326).
        var sinIdentificar = new EmisionPendiente(41, Momento,
            new SolicitudComprobante(TipoComprobante.FacturaC, 3, Fecha, ConceptoFactura.Productos,
                ReceptorFactura.SinIdentificar, ImportesFactura.ParaFacturaC(1815m)), 7, EntornoArca.Simulado);
        Assert.False(sinIdentificar.ReceptorIdentificado);
        Assert.Null(sinIdentificar.Importes!.Alicuota);
        Assert.DoesNotContain(Dni, pendiente.ToString(), StringComparison.Ordinal);
        Assert.All(
            typeof(EmisionPendiente).GetProperties(BindingFlags.Public | BindingFlags.Instance),
            propiedad => Assert.True(
                propiedad.PropertyType != typeof(SolicitudComprobante) &&
                propiedad.PropertyType != typeof(ReceptorFactura) &&
                !propiedad.Name.Contains("Dni", StringComparison.Ordinal),
                propiedad.Name));
    }

    [Fact]
    public void EmisionPendiente_NumeroInvalido_Rechazado()
    {
        foreach (var numero in new[] { 0L, -1L, 100_000_000L })
        {
            var error = Assert.Throws<ArgumentException>(
                () => new EmisionPendiente(41, Momento, SolicitudB(), numero, EntornoArca.Simulado));
            Assert.Equal("numero", error.ParamName);
            Assert.DoesNotContain("100000000", error.Message, StringComparison.Ordinal);
        }

        Assert.Equal(99_999_999, new EmisionPendiente(41, Momento, SolicitudB(), 99_999_999, EntornoArca.Simulado).Numero);
        Assert.Equal(1, new EmisionPendiente(41, Momento, SolicitudB(), 1, EntornoArca.Simulado).Numero);
    }

    [Fact]
    public void EmisionPendiente_DatosInvalidos_Rechazado()
    {
        Assert.Equal("presupuestoNumero", Assert.Throws<ArgumentException>(
            () => new EmisionPendiente(0, Momento, SolicitudB(), 125, EntornoArca.Simulado)).ParamName);
        Assert.Equal("momento", Assert.Throws<ArgumentException>(
            () => new EmisionPendiente(41, default, SolicitudB(), 125, EntornoArca.Simulado)).ParamName);
        Assert.Equal("entorno", Assert.Throws<ArgumentException>(
            () => new EmisionPendiente(41, Momento, SolicitudB(), 125, (EntornoArca)9)).ParamName);
        Assert.Throws<ArgumentNullException>(() => new EmisionPendiente(41, Momento, null!, 125, EntornoArca.Simulado));
    }

    [Fact]
    public void SinNumero_FilaDeFeat007_SinNumeroConceptoImportesNiReceptor()
    {
        // FR-14: la pendiente que grabó FEAT-007 no tiene número ni los datos para recuperarla.
        var pendiente = EmisionPendiente.SinNumero(
            150, Momento, TipoComprobante.FacturaB, 3, Fecha, 1815m, EntornoArca.Simulado);

        Assert.Equal(150, pendiente.PresupuestoNumero);
        Assert.Equal(Momento, pendiente.Momento);
        Assert.Equal(TipoComprobante.FacturaB, pendiente.Tipo);
        Assert.Equal(3, pendiente.PuntoVenta);
        Assert.Equal(Fecha, pendiente.Fecha);
        Assert.Equal(1815m, pendiente.Total);
        Assert.Equal(EntornoArca.Simulado, pendiente.Entorno);
        Assert.False(pendiente.TieneNumero);
        Assert.Null(pendiente.Numero);
        Assert.Null(pendiente.Concepto);
        Assert.Null(pendiente.Importes);
        Assert.Null(pendiente.ReceptorIdentificado);

        Assert.Equal("total", Assert.Throws<ArgumentException>(() => EmisionPendiente.SinNumero(
            150, Momento, TipoComprobante.FacturaB, 3, Fecha, -1m, EntornoArca.Simulado)).ParamName);
        Assert.Equal("total", Assert.Throws<ArgumentException>(() => EmisionPendiente.SinNumero(
            150, Momento, TipoComprobante.FacturaB, 3, Fecha, 1.005m, EntornoArca.Simulado)).ParamName);
        Assert.Equal("puntoVenta", Assert.Throws<ArgumentException>(() => EmisionPendiente.SinNumero(
            150, Momento, TipoComprobante.FacturaB, 0, Fecha, 1815m, EntornoArca.Simulado)).ParamName);
        Assert.Equal("tipo", Assert.Throws<ArgumentException>(() => EmisionPendiente.SinNumero(
            150, Momento, (TipoComprobante)7, 3, Fecha, 1815m, EntornoArca.Simulado)).ParamName);
        Assert.Equal("fecha", Assert.Throws<ArgumentException>(() => EmisionPendiente.SinNumero(
            150, Momento, TipoComprobante.FacturaB, 3, default, 1815m, EntornoArca.Simulado)).ParamName);
    }

    [Fact]
    public void Registrada_ReconstruyeLaGuardada_ValidaTodoONada()
    {
        var importes = ImportesFactura.ParaFacturaB(1815m, 21m);
        var pendiente = EmisionPendiente.Registrada(
            41, Momento, TipoComprobante.FacturaB, 3, Fecha, 125, ConceptoFactura.Productos, importes,
            receptorIdentificado: true, EntornoArca.Produccion);

        Assert.Equal(125, pendiente.Numero);
        Assert.Same(importes, pendiente.Importes);
        Assert.Equal(1815m, pendiente.Total);
        Assert.Equal(ConceptoFactura.Productos, pendiente.Concepto);
        Assert.True(pendiente.ReceptorIdentificado);

        // Los importes tienen que ser del tipo: la C no discrimina IVA.
        Assert.Equal("importes", Assert.Throws<ArgumentException>(() => EmisionPendiente.Registrada(
            41, Momento, TipoComprobante.FacturaC, 3, Fecha, 125, ConceptoFactura.Productos, importes, false,
            EntornoArca.Simulado)).ParamName);
        Assert.Equal("concepto", Assert.Throws<ArgumentException>(() => EmisionPendiente.Registrada(
            41, Momento, TipoComprobante.FacturaB, 3, Fecha, 125, (ConceptoFactura)9, importes, false,
            EntornoArca.Simulado)).ParamName);
        Assert.Equal("numero", Assert.Throws<ArgumentException>(() => EmisionPendiente.Registrada(
            41, Momento, TipoComprobante.FacturaB, 3, Fecha, 0, ConceptoFactura.Productos, importes, false,
            EntornoArca.Simulado)).ParamName);
        Assert.Throws<ArgumentNullException>(() => EmisionPendiente.Registrada(
            41, Momento, TipoComprobante.FacturaB, 3, Fecha, 125, ConceptoFactura.Productos, null!, false,
            EntornoArca.Simulado));
    }
}
