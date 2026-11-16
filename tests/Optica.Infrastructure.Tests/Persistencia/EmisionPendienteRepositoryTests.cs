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

    [Fact]
    public async Task Repositorio_GrabaYLeePendienteConNumero_SinDni()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = FacturasPrueba.CadenaDe(baseTemporal);
        var presupuesto = await FacturasPrueba.GrabarPresupuestoFinal(baseTemporal, 1);

        // AC-01: el número 125, Factura B, el total y el presupuesto, con lo que hace falta para recuperarla.
        await Grabar(baseTemporal, Pendiente(presupuesto, EntornoArca.Homologacion));

        Assert.Equal(
            [["125", "productos", "247934", "52066", "21", "1"]],
            FacturasPrueba.Filas(cadena,
                "SELECT Numero, Concepto, NetoCentavos, IvaCentavos, Alicuota, ReceptorIdentificado FROM EmisionPendiente"));
        Assert.DoesNotContain(
            FacturasPrueba.Filas(cadena, "SELECT * FROM EmisionPendiente").Single(),
            valor => valor.Contains(FacturasPrueba.Dni, StringComparison.Ordinal));
        Assert.DoesNotContain(
            FacturasPrueba.Filas(cadena, "SELECT name FROM pragma_table_info('EmisionPendiente')"),
            columna => columna[0].Contains("Dni", StringComparison.Ordinal));

        var leida = await new EmisionPendienteRepository(baseTemporal.CrearContexto()).ObtenerPendienteAsync(1);

        Assert.NotNull(leida);
        Assert.Equal(1, leida.PresupuestoNumero);
        Assert.Equal(Momento, leida.Momento);
        Assert.Equal(TimeSpan.Zero, leida.Momento.Offset);
        Assert.Equal(TipoComprobante.FacturaB, leida.Tipo);
        Assert.Equal(3, leida.PuntoVenta);
        Assert.Equal(new DateOnly(2026, 10, 30), leida.Fecha);
        Assert.Equal(3000m, leida.Total);
        Assert.Equal(EntornoArca.Homologacion, leida.Entorno);
        Assert.Equal(125, leida.Numero);
        Assert.Equal(ConceptoFactura.Productos, leida.Concepto);
        Assert.Equal(2479.34m, leida.Importes!.Neto);
        Assert.Equal(520.66m, leida.Importes.Iva);
        Assert.Equal(21m, leida.Importes.Alicuota);
        Assert.True(leida.ReceptorIdentificado);

        Assert.Null(await new EmisionPendienteRepository(baseTemporal.CrearContexto()).ObtenerPendienteAsync(2));
    }

    [Fact]
    public async Task Repositorio_FacturaCConAlicuotaDecimal_SeLeeIgual()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = FacturasPrueba.CadenaDe(baseTemporal);
        var presupuesto = await FacturasPrueba.GrabarPresupuestoFinal(baseTemporal, 1);
        var otro = await FacturasPrueba.GrabarPresupuestoFinal(baseTemporal, 2);

        await Grabar(baseTemporal, new EmisionPendiente(1, Momento,
            new SolicitudComprobante(TipoComprobante.FacturaC, 3, new DateOnly(2026, 10, 30),
                ConceptoFactura.ProductosYServicios, ReceptorFactura.SinIdentificar,
                ImportesFactura.ParaFacturaC(presupuesto.Total)), 99_999_999, EntornoArca.Simulado));
        await Grabar(baseTemporal, new EmisionPendiente(2, Momento,
            new SolicitudComprobante(TipoComprobante.FacturaB, 3, new DateOnly(2026, 10, 30),
                ConceptoFactura.Productos, ReceptorFactura.SinIdentificar,
                ImportesFactura.ParaFacturaB(otro.Total, 10.5m)), 1, EntornoArca.Simulado));

        Assert.Equal(
            [["1", "99999999", "productos_y_servicios", "", "0"], ["2", "1", "productos", "10.5", "0"]],
            FacturasPrueba.Filas(cadena,
                "SELECT PresupuestoNumero, Numero, Concepto, IFNULL(Alicuota, ''), ReceptorIdentificado " +
                "FROM EmisionPendiente ORDER BY PresupuestoNumero"));

        var repositorio = new EmisionPendienteRepository(baseTemporal.CrearContexto());
        var c = (await repositorio.ObtenerPendienteAsync(1))!;
        Assert.Equal(TipoComprobante.FacturaC, c.Tipo);
        Assert.Null(c.Importes!.Alicuota);
        Assert.Equal(ConceptoFactura.ProductosYServicios, c.Concepto);
        Assert.False(c.ReceptorIdentificado);
        Assert.Equal(10.5m, (await repositorio.ObtenerPendienteAsync(2))!.Importes!.Alicuota);
    }

    [Fact]
    public async Task Repositorio_FilaDeFeat007_SeLeeSinNumero()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = FacturasPrueba.CadenaDe(baseTemporal);
        await FacturasPrueba.GrabarPresupuestoFinal(baseTemporal, 150);

        // AC-15: la fila que grabó FEAT-007, con las columnas nuevas nulas.
        InsertarFilaFeat007(cadena, 150);

        var leida = await new EmisionPendienteRepository(baseTemporal.CrearContexto()).ObtenerPendienteAsync(150);

        Assert.NotNull(leida);
        Assert.False(leida.TieneNumero);
        Assert.Null(leida.Numero);
        Assert.Null(leida.Concepto);
        Assert.Null(leida.Importes);
        Assert.Null(leida.ReceptorIdentificado);
        Assert.Equal(TipoComprobante.FacturaB, leida.Tipo);
        Assert.Equal(3, leida.PuntoVenta);
        Assert.Equal(3000m, leida.Total);
        Assert.Equal(EntornoArca.Simulado, leida.Entorno);
        Assert.Equal(new DateTimeOffset(2026, 10, 30, 14, 15, 20, TimeSpan.Zero), leida.Momento);
        Assert.True(await new EmisionPendienteRepository(baseTemporal.CrearContexto()).ExisteAsync(150));
    }

    [Fact]
    public async Task Repositorio_FilaParcialmenteNula_ErrorDeLectura()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = FacturasPrueba.CadenaDe(baseTemporal);
        await FacturasPrueba.GrabarPresupuestoFinal(baseTemporal, 1);
        await FacturasPrueba.GrabarPresupuestoFinal(baseTemporal, 2);
        await FacturasPrueba.GrabarPresupuestoFinal(baseTemporal, 3);

        // Con número y sin concepto, importes ni receptor: ni de FEAT-007 ni de FEAT-008.
        var conNumero = FilaFeat007(1);
        conNumero["Numero"] = 125L;
        InsertarFila(cadena, conNumero);

        // Con todo menos el número.
        var sinNumero = FilaFeat007(2);
        sinNumero["Concepto"] = "productos";
        sinNumero["NetoCentavos"] = 247934L;
        sinNumero["IvaCentavos"] = 52066L;
        sinNumero["Alicuota"] = "21";
        sinNumero["ReceptorIdentificado"] = 0L;
        InsertarFila(cadena, sinNumero);

        // De FEAT-007 pero con alícuota.
        var conAlicuota = FilaFeat007(3);
        conAlicuota["Alicuota"] = "21";
        InsertarFila(cadena, conAlicuota);

        foreach (var numero in new[] { 1L, 2L, 3L })
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => new EmisionPendienteRepository(baseTemporal.CrearContexto()).ObtenerPendienteAsync(numero));
            Assert.Equal(MensajeFilaInvalida, error.Message);
        }
    }

    [Fact]
    public async Task Repositorio_ListarNumeros_OrdenAscendente()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = FacturasPrueba.CadenaDe(baseTemporal);
        Assert.Empty(await new EmisionPendienteRepository(baseTemporal.CrearContexto()).ListarNumerosAsync());

        // AC-03: pendientes de los presupuestos 155 y 160, grabadas en otro orden; una de FEAT-007 también cuenta.
        var p160 = await FacturasPrueba.GrabarPresupuestoFinal(baseTemporal, 160);
        await FacturasPrueba.GrabarPresupuestoFinal(baseTemporal, 155);
        await FacturasPrueba.GrabarPresupuestoFinal(baseTemporal, 158);
        await Grabar(baseTemporal, Pendiente(p160, EntornoArca.Simulado));
        InsertarFilaFeat007(cadena, 155);

        Assert.Equal(
            [155L, 160L], await new EmisionPendienteRepository(baseTemporal.CrearContexto()).ListarNumerosAsync());
    }

    private static EmisionPendiente Pendiente(Presupuesto presupuesto, EntornoArca entorno)
    {
        var solicitud = new SolicitudComprobante(
            TipoComprobante.FacturaB, 3, new DateOnly(2026, 10, 30), ConceptoFactura.Productos,
            ReceptorFactura.ConDni(presupuesto.Cliente.Dni), ImportesFactura.ParaFacturaB(presupuesto.Total, 21m));
        return new EmisionPendiente(presupuesto.Numero, Momento, solicitud, 125, entorno);
    }

    private static async Task Grabar(BaseTemporal baseTemporal, EmisionPendiente pendiente)
    {
        var contexto = baseTemporal.CrearContexto();
        new EmisionPendienteRepository(contexto).Agregar(pendiente);
        await new UnidadDeTrabajoEf(contexto).ConfirmarAsync();
    }

    private const string MensajeFilaInvalida = "Una emisión pendiente guardada no es válida.";

    private static Dictionary<string, object?> FilaFeat007(long presupuestoNumero) => new(StringComparer.Ordinal)
    {
        ["PresupuestoNumero"] = presupuestoNumero,
        ["Momento"] = "2026-10-30T14:15:20.0000000Z",
        ["Tipo"] = "b",
        ["PuntoVenta"] = 3L,
        ["Fecha"] = "2026-10-30",
        ["TotalCentavos"] = 300000L,
        ["Entorno"] = "simulado",
    };

    private static void InsertarFilaFeat007(string cadena, long presupuestoNumero) =>
        InsertarFila(cadena, FilaFeat007(presupuestoNumero));

    private static void InsertarFila(string cadena, Dictionary<string, object?> fila)
    {
        using var conexion = new Microsoft.Data.Sqlite.SqliteConnection(cadena);
        conexion.Open();
        FacturasPrueba.Insertar(conexion, "EmisionPendiente", fila);
    }
}
