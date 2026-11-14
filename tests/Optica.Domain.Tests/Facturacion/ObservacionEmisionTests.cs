using System.Reflection;
using Optica.Domain.Facturacion;

namespace Optica.Domain.Tests.Facturacion;

/// <summary>
/// La observación que deja la verificación de una pendiente y el descarte registrado (Block 1 de FEAT-008, FR-07, FR-10,
/// FR-14, FR-15, D-12, ADR-026). Ninguna de las dos lleva datos del cliente.
/// </summary>
public class ObservacionEmisionTests
{
    private static readonly DateTimeOffset Momento = new(2026, 11, 14, 10, 30, 0, TimeSpan.FromHours(-3));

    [Fact]
    public void ObservacionEmision_ImporteDistintoSinTotalArca_Rechazado()
    {
        // D-12: importe_distinto exige el número consultado y el total que informó ARCA.
        var sinTotal = Assert.Throws<ArgumentException>(() => new ObservacionEmision(
            41, MotivoObservacionEmision.ImporteDistinto, 125, 1815m, null, EntornoArca.Homologacion, Momento));
        Assert.Equal("totalArca", sinTotal.ParamName);

        var sinNumero = Assert.Throws<ArgumentException>(() => new ObservacionEmision(
            41, MotivoObservacionEmision.ImporteDistinto, null, 1815m, 2000m, EntornoArca.Homologacion, Momento));
        Assert.Equal("numero", sinNumero.ParamName);

        // Con los dos, entra (AC-07).
        var observacion = new ObservacionEmision(
            41, MotivoObservacionEmision.ImporteDistinto, 125, 1815m, 2000m, EntornoArca.Homologacion, Momento);
        Assert.Equal(41, observacion.PresupuestoNumero);
        Assert.Equal(MotivoObservacionEmision.ImporteDistinto, observacion.Motivo);
        Assert.Equal(125, observacion.Numero);
        Assert.Equal(1815m, observacion.TotalRegistrado);
        Assert.Equal(2000m, observacion.TotalArca);
        Assert.Equal(EntornoArca.Homologacion, observacion.Entorno);
        Assert.Equal(TimeSpan.Zero, observacion.Momento.Offset);
        Assert.Equal(Momento, observacion.Momento);
    }

    [Fact]
    public void ObservacionEmision_SinNumeroYOtroEntorno_SinTotalArca()
    {
        var sinNumero = new ObservacionEmision(
            150, MotivoObservacionEmision.SinNumero, null, 1815m, null, EntornoArca.Simulado, Momento);
        Assert.Null(sinNumero.Numero);
        Assert.Null(sinNumero.TotalArca);

        var otroEntorno = new ObservacionEmision(
            41, MotivoObservacionEmision.OtroEntorno, 125, 1815m, null, EntornoArca.Simulado, Momento);
        Assert.Equal(EntornoArca.Simulado, otroEntorno.Entorno);
    }

    [Fact]
    public void ObservacionEmision_DatosInvalidos_ErrorConElCampoSinElValor()
    {
        AfirmarCampo("presupuestoNumero", () => new ObservacionEmision(
            0, MotivoObservacionEmision.SinNumero, null, 1815m, null, EntornoArca.Simulado, Momento));
        AfirmarCampo("motivo", () => new ObservacionEmision(
            41, (MotivoObservacionEmision)9, null, 1815m, null, EntornoArca.Simulado, Momento));
        AfirmarCampo("numero", () => new ObservacionEmision(
            41, MotivoObservacionEmision.OtroEntorno, 100_000_000, 1815m, null, EntornoArca.Simulado, Momento));
        AfirmarCampo("totalRegistrado", () => new ObservacionEmision(
            41, MotivoObservacionEmision.SinNumero, null, -1m, null, EntornoArca.Simulado, Momento));
        AfirmarCampo("totalArca", () => new ObservacionEmision(
            41, MotivoObservacionEmision.ImporteDistinto, 125, 1815m, 20.001m, EntornoArca.Simulado, Momento));
        AfirmarCampo("entorno", () => new ObservacionEmision(
            41, MotivoObservacionEmision.SinNumero, null, 1815m, null, (EntornoArca)9, Momento));
        AfirmarCampo("momento", () => new ObservacionEmision(
            41, MotivoObservacionEmision.SinNumero, null, 1815m, null, EntornoArca.Simulado, default));
    }

    [Fact]
    public void DescarteEmision_GuardaLoQuePideFr10()
    {
        // AC-08: fecha y hora, el número 125, $1.815,00 y $2.000,00.
        var descarte = new DescarteEmision(
            Momento, 41, TipoComprobante.FacturaB, 3, 125, 1815m, 2000m, MotivoObservacionEmision.ImporteDistinto,
            EntornoArca.Homologacion);

        Assert.Equal(TimeSpan.Zero, descarte.Momento.Offset);
        Assert.Equal(Momento, descarte.Momento);
        Assert.Equal(41, descarte.PresupuestoNumero);
        Assert.Equal(TipoComprobante.FacturaB, descarte.Tipo);
        Assert.Equal(3, descarte.PuntoVenta);
        Assert.Equal(125, descarte.Numero);
        Assert.Equal(1815m, descarte.TotalRegistrado);
        Assert.Equal(2000m, descarte.TotalArca);
        Assert.Equal(MotivoObservacionEmision.ImporteDistinto, descarte.Motivo);
        Assert.Equal(EntornoArca.Homologacion, descarte.Entorno);

        // AC-15: sin número ni total de ARCA.
        var sinNumero = new DescarteEmision(
            Momento, 150, TipoComprobante.FacturaC, 3, null, 1815m, null, MotivoObservacionEmision.SinNumero,
            EntornoArca.Simulado);
        Assert.Null(sinNumero.Numero);
        Assert.Null(sinNumero.TotalArca);
    }

    [Fact]
    public void DescarteEmision_DatosInvalidos_ErrorConElCampoSinElValor()
    {
        AfirmarCampo("momento", () => new DescarteEmision(
            default, 41, TipoComprobante.FacturaB, 3, 125, 1815m, null, MotivoObservacionEmision.OtroEntorno,
            EntornoArca.Simulado));
        AfirmarCampo("presupuestoNumero", () => new DescarteEmision(
            Momento, 0, TipoComprobante.FacturaB, 3, 125, 1815m, null, MotivoObservacionEmision.OtroEntorno,
            EntornoArca.Simulado));
        AfirmarCampo("tipo", () => new DescarteEmision(
            Momento, 41, (TipoComprobante)5, 3, 125, 1815m, null, MotivoObservacionEmision.OtroEntorno,
            EntornoArca.Simulado));
        AfirmarCampo("puntoVenta", () => new DescarteEmision(
            Momento, 41, TipoComprobante.FacturaB, 100000, 125, 1815m, null, MotivoObservacionEmision.OtroEntorno,
            EntornoArca.Simulado));
        AfirmarCampo("numero", () => new DescarteEmision(
            Momento, 41, TipoComprobante.FacturaB, 3, 0, 1815m, null, MotivoObservacionEmision.OtroEntorno,
            EntornoArca.Simulado));
        AfirmarCampo("totalRegistrado", () => new DescarteEmision(
            Momento, 41, TipoComprobante.FacturaB, 3, 125, -0.01m, null, MotivoObservacionEmision.OtroEntorno,
            EntornoArca.Simulado));
        AfirmarCampo("totalArca", () => new DescarteEmision(
            Momento, 41, TipoComprobante.FacturaB, 3, 125, 1815m, null, MotivoObservacionEmision.ImporteDistinto,
            EntornoArca.Simulado));
        AfirmarCampo("motivo", () => new DescarteEmision(
            Momento, 41, TipoComprobante.FacturaB, 3, 125, 1815m, null, (MotivoObservacionEmision)9,
            EntornoArca.Simulado));
        AfirmarCampo("entorno", () => new DescarteEmision(
            Momento, 41, TipoComprobante.FacturaB, 3, 125, 1815m, null, MotivoObservacionEmision.OtroEntorno,
            (EntornoArca)9));
    }

    [Fact]
    public void ObservacionYDescarte_SinDatosDelCliente()
    {
        foreach (var tipo in new[] { typeof(ObservacionEmision), typeof(DescarteEmision) })
        {
            Assert.All(tipo.GetProperties(BindingFlags.Public | BindingFlags.Instance), propiedad =>
                Assert.False(
                    propiedad.Name.Contains("Dni", StringComparison.Ordinal) ||
                    propiedad.PropertyType == typeof(ReceptorFactura) ||
                    propiedad.PropertyType == typeof(SolicitudComprobante),
                    propiedad.Name));
        }
    }

    private static void AfirmarCampo(string campo, Action accion)
    {
        var error = Assert.Throws<ArgumentException>(accion);
        Assert.Equal(campo, error.ParamName);
        Assert.DoesNotContain("100000", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("1815", error.Message, StringComparison.Ordinal);
    }
}
