using Microsoft.EntityFrameworkCore;
using Optica.Application.Comun;
using Optica.Domain.Facturacion;
using Optica.Domain.Presupuestos;
using Optica.Infrastructure.Persistencia;

namespace Optica.Infrastructure.Tests.Persistencia;

/// <summary>
/// Emisiones pendientes sobre SQLite (Block 4 de FEAT-007, D-09, ADR-021): una como máximo por presupuesto (FR-24), se
/// agregan y se quitan. Cliente ficticio.
/// </summary>
public class EmisionPendienteRepositoryTests
{
    private const string MensajeQuitadaSinConfirmar =
        "La emisión pendiente de este presupuesto se quitó y la baja no se confirmó.";

    private static readonly DateTimeOffset Momento = new(2026, 10, 30, 11, 15, 20, TimeSpan.FromHours(-3));

    [Fact]
    public async Task Agregar_SegundaPendiente_ConflictoDuplicado()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = FacturasPrueba.CadenaDe(baseTemporal);
        var presupuesto = await FacturasPrueba.GrabarPresupuestoFinal(baseTemporal, 1);

        await Grabar(baseTemporal, Pendiente(presupuesto, EntornoArca.Homologacion));
        Assert.True(await new EmisionPendienteRepository(baseTemporal.CrearContexto()).ExisteAsync(1));

        // FR-24: una segunda pendiente para el mismo presupuesto choca con la clave primaria.
        var error = await Assert.ThrowsAsync<ConflictoAlConfirmarException>(
            () => Grabar(baseTemporal, Pendiente(presupuesto, EntornoArca.Simulado)));

        Assert.Equal(MotivoConflicto.Unicidad, error.Motivo);
        Assert.Null(error.InnerException);

        // La fila guarda los datos que verifica el PRD 07, el momento en UTC y el entorno; no guarda el DNI del receptor.
        var filas = FacturasPrueba.Filas(cadena,
            "SELECT PresupuestoNumero, Momento, Tipo, PuntoVenta, Fecha, TotalCentavos, Entorno FROM EmisionPendiente");
        Assert.Equal(
            [["1", "2026-10-30T14:15:20.0000000Z", "b", "3", "2026-10-30", "300000", "homologacion"]], filas);
        Assert.DoesNotContain(
            FacturasPrueba.Filas(cadena, "SELECT name FROM pragma_table_info('EmisionPendiente')"),
            columna => columna[0].Contains("Dni", StringComparison.Ordinal));

        Assert.False(await new EmisionPendienteRepository(baseTemporal.CrearContexto()).ExisteAsync(2));
    }

    [Fact]
    public async Task Quitar_Pendiente_YaNoExiste()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = FacturasPrueba.CadenaDe(baseTemporal);
        var presupuesto = await FacturasPrueba.GrabarPresupuestoFinal(baseTemporal, 1);
        await Grabar(baseTemporal, Pendiente(presupuesto, EntornoArca.Simulado));
        Assert.True(await new EmisionPendienteRepository(baseTemporal.CrearContexto()).ExisteAsync(1));

        var contexto = baseTemporal.CrearContexto();
        new EmisionPendienteRepository(contexto).Quitar(1);
        await new UnidadDeTrabajoEf(contexto).ConfirmarAsync();

        Assert.False(await new EmisionPendienteRepository(baseTemporal.CrearContexto()).ExisteAsync(1));
        Assert.Equal([["0"]], FacturasPrueba.Filas(cadena, "SELECT count(*) FROM EmisionPendiente"));

        // Sin pendiente, Quitar no registra nada y la confirmación no falla (D-44).
        var otro = baseTemporal.CrearContexto();
        new EmisionPendienteRepository(otro).Quitar(1);
        new EmisionPendienteRepository(otro).Quitar(99);
        Assert.False(otro.ChangeTracker.HasChanges());
        await new UnidadDeTrabajoEf(otro).ConfirmarAsync();

        // Quitada, se puede volver a registrar una.
        await Grabar(baseTemporal, Pendiente(presupuesto, EntornoArca.Simulado));
        Assert.True(await new EmisionPendienteRepository(baseTemporal.CrearContexto()).ExisteAsync(1));
    }

    [Fact]
    public async Task Quitar_PendienteAgregadaEnElMismoContexto_NoSeGraba()
    {
        using var baseTemporal = new BaseTemporal();
        var presupuesto = await FacturasPrueba.GrabarPresupuestoFinal(baseTemporal, 1);

        var contexto = baseTemporal.CrearContexto();
        var repositorio = new EmisionPendienteRepository(contexto);
        repositorio.Agregar(Pendiente(presupuesto, EntornoArca.Simulado));
        Assert.True(contexto.ChangeTracker.HasChanges());
        repositorio.Quitar(1);
        Assert.False(contexto.ChangeTracker.HasChanges());
        await new UnidadDeTrabajoEf(contexto).ConfirmarAsync();

        Assert.False(await new EmisionPendienteRepository(baseTemporal.CrearContexto()).ExisteAsync(1));
    }

    [Fact]
    public async Task QuitarYAgregar_MismoContextoSinConfirmar_ErrorHayQueConfirmarEnElMedio()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = FacturasPrueba.CadenaDe(baseTemporal);
        var presupuesto = await FacturasPrueba.GrabarPresupuestoFinal(baseTemporal, 1);
        await Grabar(baseTemporal, Pendiente(presupuesto, EntornoArca.Simulado));

        // WARN-2: EF Core grabaría la baja y el alta de la misma clave como un UPDATE, que TR_EmisionPendiente_Inmutable
        // rechaza. Agregar lo corta antes, con un error de programación de texto fijo, y no registra nada.
        var contexto = baseTemporal.CrearContexto();
        var repositorio = new EmisionPendienteRepository(contexto);
        repositorio.Quitar(1);
        var error = Assert.Throws<InvalidOperationException>(
            () => repositorio.Agregar(Pendiente(presupuesto, EntornoArca.Homologacion)));

        Assert.Equal(MensajeQuitadaSinConfirmar, error.Message);
        Assert.Equal([EntityState.Deleted], contexto.ChangeTracker.Entries().Select(e => e.State));

        // Confirmando la baja en el medio, la nueva pendiente entra.
        await new UnidadDeTrabajoEf(contexto).ConfirmarAsync();
        repositorio.Agregar(Pendiente(presupuesto, EntornoArca.Homologacion));
        await new UnidadDeTrabajoEf(contexto).ConfirmarAsync();
        Assert.Equal([["homologacion"]], FacturasPrueba.Filas(cadena, "SELECT Entorno FROM EmisionPendiente"));
    }

    [Fact]
    public void Agregar_Null_Rechazado()
    {
        using var baseTemporal = new BaseTemporal();
        var contexto = baseTemporal.CrearContexto();

        Assert.Throws<ArgumentNullException>(() => new EmisionPendienteRepository(contexto).Agregar(null!));
        Assert.False(contexto.ChangeTracker.HasChanges());
    }

    private static EmisionPendiente Pendiente(Presupuesto presupuesto, EntornoArca entorno)
    {
        var solicitud = new SolicitudComprobante(
            TipoComprobante.FacturaB, 3, new DateOnly(2026, 10, 30), ConceptoFactura.Productos,
            ReceptorFactura.ConDni(presupuesto.Cliente.Dni), ImportesFactura.ParaFacturaB(presupuesto.Total, 21m));
        return new EmisionPendiente(presupuesto.Numero, Momento, solicitud, entorno);
    }

    private static async Task Grabar(BaseTemporal baseTemporal, EmisionPendiente pendiente)
    {
        var contexto = baseTemporal.CrearContexto();
        new EmisionPendienteRepository(contexto).Agregar(pendiente);
        await new UnidadDeTrabajoEf(contexto).ConfirmarAsync();
    }
}
