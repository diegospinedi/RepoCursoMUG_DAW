using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Optica.Application.Comun;
using Optica.Application.Facturacion;
using Optica.Domain.Facturacion;
using Optica.Infrastructure.Persistencia;
using Optica.Infrastructure.Tests.Persistencia;

namespace Optica.Infrastructure.Tests.Facturacion;

/// <summary>
/// Spec FEAT-008, Block 4 (D-11, D-28): <see cref="ProcesoEmision"/> en modo reemplazo sobre SQLite real, con los
/// repositorios y la unidad de trabajo de EF. Cliente ficticio.
/// </summary>
public sealed class ProcesoEmisionSqliteTests
{
    private static readonly DateTimeOffset Momento = new(2026, 11, 14, 13, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// D-11 y D-28 (sad path): en la transacción <c>BEGIN IMMEDIATE</c> del reemplazo se confirma la baja de la vieja y
    /// de su observación, y el alta de la nueva choca por unicidad: SQLite revierte la transacción entera, así que la
    /// pendiente vieja (número 125) y su observación siguen en la base, no hay otra pendiente y no se pidió el CAE.
    /// </summary>
    [Fact]
    public async Task Reemplazo_ConflictoDespuesDeQuitarLaVieja_RevierteYLaViejaQueda()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = FacturasPrueba.CadenaDe(baseTemporal);
        var presupuesto = await FacturasPrueba.GrabarPresupuestoFinal(baseTemporal, 1);
        var solicitud = new SolicitudComprobante(
            TipoComprobante.FacturaB, 3, new DateOnly(2026, 11, 14), ConceptoFactura.Productos,
            ReceptorFactura.SinIdentificar, ImportesFactura.ParaFacturaB(presupuesto.Total, 21m));
        var vieja = new EmisionPendiente(1, Momento, solicitud, 125, EntornoArca.Simulado);
        var siembra = baseTemporal.CrearContexto();
        new EmisionPendienteRepository(siembra).Agregar(vieja);
        await new UnidadDeTrabajoEf(siembra).ConfirmarAsync();
        new ObservacionEmisionRepository(siembra).Agregar(new ObservacionEmision(
            1, MotivoObservacionEmision.OtroEntorno, 125, presupuesto.Total, null, EntornoArca.Simulado, Momento));
        await new UnidadDeTrabajoEf(siembra).ConfirmarAsync();
        var antes = FacturasPrueba.Filas(cadena,
            "SELECT p.PresupuestoNumero, p.Numero, o.Motivo FROM EmisionPendiente p JOIN ObservacionEmision o " +
            "ON o.PresupuestoNumero = p.PresupuestoNumero");

        var contexto = baseTemporal.CrearContexto();
        var unidad = new UnidadConConflictoAlSegundoConfirmar(contexto);
        var autorizador = new AutorizadorConNumero126();
        var reloj = new FakeTimeProvider(Momento.AddMinutes(5));
        var proceso = new ProcesoEmision(
            new PresupuestoRepository(contexto), new FacturaRepository(contexto), new EmisionPendienteRepository(contexto),
            new ObservacionEmisionRepository(contexto), unidad, autorizador, reloj, NullLogger<ProcesoEmision>.Instance);
        var reemplazada = await new EmisionPendienteRepository(baseTemporal.CrearContexto()).ObtenerPendienteAsync(1);
        using var plazo = PlazoEmision.Iniciar(TimeSpan.FromSeconds(30), reloj);
        using var turno = new TurnoDePrueba();

        var resultado = await proceso.EjecutarAsync(presupuesto, solicitud, turno, plazo, reemplazada);

        Assert.Equal(TipoResultadoEmision.Conflicto, resultado.Tipo);
        Assert.Equal(MotivoConflictoEmision.Pendiente, resultado.Motivo);
        Assert.Equal(2, unidad.Confirmaciones);
        Assert.Equal([["1", "125", "otro_entorno"]], antes);
        Assert.Equal(antes, FacturasPrueba.Filas(cadena,
            "SELECT p.PresupuestoNumero, p.Numero, o.Motivo FROM EmisionPendiente p JOIN ObservacionEmision o " +
            "ON o.PresupuestoNumero = p.PresupuestoNumero"));
        Assert.Equal([["1"]], FacturasPrueba.Filas(cadena, "SELECT count(*) FROM EmisionPendiente"));
        Assert.Equal(0, autorizador.Autorizaciones);
    }

    /// <summary>
    /// La unidad de trabajo real, salvo que la segunda confirmación dentro de la operación (el alta de la nueva) choca por
    /// unicidad como si otro pedido la hubiera grabado; la primera (la baja de la vieja) se confirma de verdad. Al chocar
    /// descarta lo registrado, como <see cref="UnidadDeTrabajoEf"/> ante una unicidad.
    /// </summary>
    private sealed class UnidadConConflictoAlSegundoConfirmar(OpticaDbContext contexto) : IUnidadDeTrabajo
    {
        private readonly UnidadDeTrabajoEf _real = new(contexto);

        public int Confirmaciones { get; private set; }

        public Task<T> EjecutarEnTransaccionAsync<T>(
            Func<CancellationToken, Task<T>> operacion, CancellationToken cancelacion = default) =>
            _real.EjecutarEnTransaccionAsync(operacion, cancelacion);

        public Task ConfirmarAsync(CancellationToken cancelacion = default)
        {
            Confirmaciones++;
            if (Confirmaciones == 2)
            {
                contexto.ChangeTracker.Clear();
                throw new ConflictoAlConfirmarException("Conflicto.", MotivoConflicto.Unicidad);
            }

            return _real.ConfirmarAsync(cancelacion);
        }
    }

    /// <summary>El próximo número es el 126; la autorización no se debería pedir.</summary>
    private sealed class AutorizadorConNumero126 : IAutorizadorComprobantes
    {
        public int Autorizaciones { get; private set; }

        public EntornoArca Entorno => EntornoArca.Simulado;

        public TimeSpan TiempoMaximo => TimeSpan.FromSeconds(30);

        public Task<ResultadoProximoNumero> ProximoNumeroAsync(
            TipoComprobante tipo, int puntoVenta, CancellationToken cancelacion) =>
            Task.FromResult<ResultadoProximoNumero>(new ResultadoProximoNumero.Numero(126));

        public Task<ResultadoAutorizacion> AutorizarAsync(
            SolicitudComprobante solicitud, long numero, CancellationToken cancelacion)
        {
            Autorizaciones++;
            return Task.FromResult<ResultadoAutorizacion>(new ResultadoAutorizacion.SinRespuesta());
        }

        public Task<ResultadoConsulta> ConsultarAsync(
            TipoComprobante tipo, int puntoVenta, long numero, CancellationToken cancelacion) =>
            Task.FromResult<ResultadoConsulta>(new ResultadoConsulta.NoAutorizado());
    }

    private sealed class TurnoDePrueba : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
