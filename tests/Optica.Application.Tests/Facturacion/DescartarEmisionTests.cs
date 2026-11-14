using Optica.Application.Facturacion;
using Optica.Domain.Facturacion;
using Optica.Domain.Presupuestos;

namespace Optica.Application.Tests.Facturacion;

/// <summary>
/// Spec FEAT-008, Block 5: <see cref="DescartarEmision"/> (FR-09 a FR-11, D-12) con dobles en memoria y reloj controlado.
/// Datos del cliente ficticios (D-50 de FEAT-007).
/// </summary>
public sealed class DescartarEmisionTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 31, 2, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly Hoy = new(2026, 10, 30);

    private readonly AlmacenFacturacion _almacen = new();
    private readonly RelojControlado _reloj = new(Ahora);
    private readonly RegistroEmision _registro = new();

    private DescartarEmision Crear(EntornoArca configurado = EntornoArca.Simulado) =>
        new(
            _almacen, _almacen, _almacen, _almacen, _almacen,
            new AutorizadorDoble(_almacen) { Entorno = configurado }, _reloj, _registro);

    private EmisionPendiente Pendiente(
        Presupuesto presupuesto, long numero = 125, EntornoArca entorno = EntornoArca.Simulado)
    {
        var solicitud = ReglasEmision.Armar(presupuesto, _almacen.Configuracion, ConceptoFactura.Productos, Hoy)
            .Solicitud!;
        var pendiente = new EmisionPendiente(presupuesto.Numero, Ahora.AddMinutes(-5), solicitud, numero, entorno);
        _almacen.Pendientes.Add(pendiente);
        return pendiente;
    }

    /// <summary>
    /// AC-08: después del aviso de importe distinto (125, $1.815 y $2.000), el descarte confirmado registra la fecha y la
    /// hora en UTC con el presupuesto, el comprobante y los dos importes, y quita la observación y la pendiente. AC-15:
    /// una pendiente sin número con su observación se descarta igual, sin número ni total de ARCA.
    /// </summary>
    [Fact]
    public async Task Descartar_ConObservacion_RegistraYQuitaPendiente()
    {
        var presupuesto = _almacen.Sembrar(1, EstadoPresupuesto.Final, Presupuestos_.Dni, 1815m);
        var pendiente = Pendiente(presupuesto);
        _almacen.SembrarObservacion(pendiente, MotivoObservacionEmision.ImporteDistinto, 2000m);
        _almacen.Sembrar(150, EstadoPresupuesto.Final, Presupuestos_.Dni, 1815m);
        var sinNumero = EmisionPendiente.SinNumero(
            150, Ahora.AddMinutes(-5), TipoComprobante.FacturaB, 3, Hoy, 1815m, EntornoArca.Simulado);
        _almacen.Pendientes.Add(sinNumero);
        _almacen.SembrarObservacion(sinNumero, MotivoObservacionEmision.SinNumero);
        var caso = Crear();

        Assert.Equal(ResultadoDescarte.Descartada, await caso.EjecutarAsync(1));
        Assert.Equal(ResultadoDescarte.Descartada, await caso.EjecutarAsync(150));

        Assert.Empty(_almacen.Pendientes);
        Assert.Empty(_almacen.Observaciones);
        Assert.Equal(2, _almacen.Descartes.Count);
        var importe = _almacen.Descartes[0];
        Assert.Equal(
            (Ahora, 1L, TipoComprobante.FacturaB, 3, (long?)125, 1815m, (decimal?)2000m,
                MotivoObservacionEmision.ImporteDistinto, EntornoArca.Simulado),
            (importe.Momento, importe.PresupuestoNumero, importe.Tipo, importe.PuntoVenta, importe.Numero,
                importe.TotalRegistrado, importe.TotalArca, importe.Motivo, importe.Entorno));
        var descarteSinNumero = _almacen.Descartes[1];
        Assert.Equal(
            (150L, (long?)null, 1815m, (decimal?)null, MotivoObservacionEmision.SinNumero),
            (descarteSinNumero.PresupuestoNumero, descarteSinNumero.Numero, descarteSinNumero.TotalRegistrado,
                descarteSinNumero.TotalArca, descarteSinNumero.Motivo));
    }

    /// <summary>
    /// AC-10 (sad path): una pendiente que todavía no se verificó (sin observación) no se descarta, venga de la pantalla o
    /// de un pedido directo: queda intacta y no se registra nada. Sin pendiente, "sin pendiente"; sin presupuesto (o con un
    /// número menor que 1), no encontrado.
    /// </summary>
    [Fact]
    public async Task Descartar_SinObservacion_Rechazado()
    {
        var presupuesto = _almacen.Sembrar(1);
        var pendiente = Pendiente(presupuesto);
        _almacen.Sembrar(2);
        var caso = Crear();

        Assert.Equal(ResultadoDescarte.SinObservacion, await caso.EjecutarAsync(1));
        Assert.Equal(ResultadoDescarte.SinPendiente, await caso.EjecutarAsync(2));
        Assert.Equal(ResultadoDescarte.NoEncontrado, await caso.EjecutarAsync(99));
        Assert.Equal(ResultadoDescarte.NoEncontrado, await caso.EjecutarAsync(0));

        Assert.Same(pendiente, Assert.Single(_almacen.Pendientes));
        Assert.Empty(_almacen.Descartes);
    }

    /// <summary>
    /// AC-10 y D-12 (sad path): la observación vigente ya no se sostiene con el estado actual y el descarte se rechaza: la
    /// de otro entorno, cuando el configurado volvió a ser el de la pendiente; la de sin número, sobre una pendiente que
    /// tiene número; la de importe distinto, de otro número que el de la pendiente.
    /// </summary>
    [Fact]
    public async Task Descartar_OtroEntornoQueYaCoincide_Rechazado()
    {
        var p1 = _almacen.Sembrar(1);
        var otroEntorno = Pendiente(p1, entorno: EntornoArca.Simulado);
        _almacen.SembrarObservacion(otroEntorno, MotivoObservacionEmision.OtroEntorno);
        var p2 = _almacen.Sembrar(2);
        var conNumero = Pendiente(p2);
        _almacen.SembrarObservacion(conNumero, MotivoObservacionEmision.SinNumero, sinNumero: true);
        var p3 = _almacen.Sembrar(3);
        var renumerada = Pendiente(p3, numero: 126);
        _almacen.SembrarObservacion(renumerada, MotivoObservacionEmision.ImporteDistinto, 2000m, numero: 125);
        var caso = Crear(EntornoArca.Simulado);

        Assert.Equal(ResultadoDescarte.SinObservacion, await caso.EjecutarAsync(1));
        Assert.Equal(ResultadoDescarte.SinObservacion, await caso.EjecutarAsync(2));
        Assert.Equal(ResultadoDescarte.SinObservacion, await caso.EjecutarAsync(3));

        Assert.Equal(3, _almacen.Pendientes.Count);
        Assert.Equal(3, _almacen.Observaciones.Count);
        Assert.Empty(_almacen.Descartes);

        // Con el entorno configurado distinto del de la pendiente, la de otro entorno sí se descarta.
        Assert.Equal(ResultadoDescarte.Descartada, await Crear(EntornoArca.Homologacion).EjecutarAsync(1));
        Assert.Equal(EntornoArca.Simulado, Assert.Single(_almacen.Descartes).Entorno);
    }
}
