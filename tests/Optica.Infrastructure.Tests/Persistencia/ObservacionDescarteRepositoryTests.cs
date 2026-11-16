using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Optica.Application.Comun;
using Optica.Domain.Facturacion;
using Optica.Domain.Presupuestos;
using Optica.Infrastructure.Persistencia;

namespace Optica.Infrastructure.Tests.Persistencia;

/// <summary>
/// Observaciones y descartes de emisiones pendientes sobre SQLite (Block 1 de FEAT-008, FR-10, D-12, ADR-026): la
/// observación se agrega y se quita, el descarte solo se agrega. Ninguna guarda el DNI. Cliente ficticio.
/// </summary>
public class ObservacionDescarteRepositoryTests
{
    private const int SqliteRestriccionCheck = 275; // SQLITE_CONSTRAINT_CHECK
    private const int SqliteRestriccionTrigger = 1811; // SQLITE_CONSTRAINT_TRIGGER
    private const int SqliteRestriccionClaveForanea = 787; // SQLITE_CONSTRAINT_FOREIGNKEY

    private const string MensajeQuitadaSinConfirmar =
        "La observación de este presupuesto se quitó y la baja no se confirmó.";

    private static readonly DateTimeOffset Momento = new(2026, 11, 14, 10, 30, 0, TimeSpan.FromHours(-3));

    [Fact]
    public async Task Observacion_GrabaLeeYQuita()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = FacturasPrueba.CadenaDe(baseTemporal);
        await ConPendiente(baseTemporal, 1);

        var contexto = baseTemporal.CrearContexto();
        new ObservacionEmisionRepository(contexto).Agregar(ImporteDistinto(1));
        await new UnidadDeTrabajoEf(contexto).ConfirmarAsync();

        Assert.Equal(
            [["1", "importe_distinto", "125", "300000", "200000", "homologacion", "2026-11-14T13:30:00.0000000Z"]],
            FacturasPrueba.Filas(cadena,
                "SELECT PresupuestoNumero, Motivo, Numero, TotalCentavos, TotalArcaCentavos, Entorno, Momento " +
                "FROM ObservacionEmision"));

        var leida = await new ObservacionEmisionRepository(baseTemporal.CrearContexto()).ObtenerObservacionAsync(1);
        Assert.NotNull(leida);
        Assert.Equal(MotivoObservacionEmision.ImporteDistinto, leida.Motivo);
        Assert.Equal(125, leida.Numero);
        Assert.Equal(3000m, leida.TotalRegistrado);
        Assert.Equal(2000m, leida.TotalArca);
        Assert.Equal(EntornoArca.Homologacion, leida.Entorno);
        Assert.Equal(Momento, leida.Momento);
        Assert.Null(await new ObservacionEmisionRepository(baseTemporal.CrearContexto()).ObtenerObservacionAsync(2));

        // Se quita (D-12: verificar de nuevo reemplaza la observación, confirmando la baja en el medio).
        var otro = baseTemporal.CrearContexto();
        var repositorio = new ObservacionEmisionRepository(otro);
        repositorio.QuitarObservacion(1);
        var error = Assert.Throws<InvalidOperationException>(() => repositorio.Agregar(ImporteDistinto(1)));
        Assert.Equal(MensajeQuitadaSinConfirmar, error.Message);
        await new UnidadDeTrabajoEf(otro).ConfirmarAsync();
        Assert.Null(await new ObservacionEmisionRepository(baseTemporal.CrearContexto()).ObtenerObservacionAsync(1));

        repositorio.Agregar(new ObservacionEmision(
            1, MotivoObservacionEmision.OtroEntorno, 125, 3000m, null, EntornoArca.Simulado, Momento));
        await new UnidadDeTrabajoEf(otro).ConfirmarAsync();
        var nueva = (await new ObservacionEmisionRepository(baseTemporal.CrearContexto()).ObtenerObservacionAsync(1))!;
        Assert.Equal(MotivoObservacionEmision.OtroEntorno, nueva.Motivo);
        Assert.Null(nueva.TotalArca);

        // Sin observación, quitar no registra nada.
        var vacio = baseTemporal.CrearContexto();
        new ObservacionEmisionRepository(vacio).QuitarObservacion(99);
        Assert.False(vacio.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task Observacion_SinPendiente_RechazadaPorClaveForanea()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = FacturasPrueba.CadenaDe(baseTemporal);
        await FacturasPrueba.GrabarPresupuestoFinal(baseTemporal, 1);

        var error = Assert.Throws<SqliteException>(() => Insertar(cadena, "ObservacionEmision", FilaObservacion(1)));
        Assert.Equal(SqliteRestriccionClaveForanea, error.SqliteExtendedErrorCode);
    }

    [Fact]
    public async Task Observacion_Update_RechazadoPorTrigger()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = FacturasPrueba.CadenaDe(baseTemporal);
        await ConPendiente(baseTemporal, 1);
        Insertar(cadena, "ObservacionEmision", FilaObservacion(1));
        var antes = FacturasPrueba.Filas(cadena, "SELECT * FROM ObservacionEmision");

        // D-12: la observación se agrega y se quita, no se modifica.
        AfirmarRegistroCerrado(cadena, "UPDATE ObservacionEmision SET TotalArcaCentavos = 1");
        AfirmarRegistroCerrado(cadena, "UPDATE ObservacionEmision SET Motivo = Motivo");

        var contexto = baseTemporal.CrearContexto();
        var fila = await contexto.Set<ObservacionEmisionFila>().SingleAsync();
        fila.Motivo = "otro_entorno";
        var conflicto = await Assert.ThrowsAsync<ConflictoAlConfirmarException>(
            () => new UnidadDeTrabajoEf(contexto).ConfirmarAsync());
        Assert.Equal(MotivoConflicto.RegistroCerrado, conflicto.Motivo);
        Assert.Equal(antes, FacturasPrueba.Filas(cadena, "SELECT * FROM ObservacionEmision"));

        // Quitarla sigue permitido, y con ella ya se puede quitar la pendiente.
        FacturasPrueba.Ejecutar(cadena, "DELETE FROM ObservacionEmision");
        FacturasPrueba.Ejecutar(cadena, "DELETE FROM EmisionPendiente");
        Assert.Equal([["0", "0"]], FacturasPrueba.Filas(cadena,
            "SELECT (SELECT count(*) FROM ObservacionEmision), (SELECT count(*) FROM EmisionPendiente)"));
    }

    [Fact]
    public async Task Observacion_Check_Rechazado()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = FacturasPrueba.CadenaDe(baseTemporal);
        await ConPendiente(baseTemporal, 1);

        AfirmarCheck(cadena, "ObservacionEmision", Con(FilaObservacion(1), "Motivo", "otro"), "CK_ObservacionEmision_Motivo");
        AfirmarCheck(cadena, "ObservacionEmision", Con(FilaObservacion(1), "Entorno", "prod"), "CK_ObservacionEmision_Entorno");
        AfirmarCheck(cadena, "ObservacionEmision", Con(FilaObservacion(1), "TotalCentavos", -1L),
            "CK_ObservacionEmision_TotalCentavos");
        AfirmarCheck(cadena, "ObservacionEmision", Con(FilaObservacion(1), "TotalArcaCentavos", -1L),
            "CK_ObservacionEmision_TotalArcaCentavos");
        AfirmarCheck(cadena, "ObservacionEmision", Con(FilaObservacion(1), "Numero", 0L),
            "CK_ObservacionEmision_Numero");
        AfirmarCheck(cadena, "ObservacionEmision", Con(FilaObservacion(1), "TotalArcaCentavos", null),
            "CK_ObservacionEmision_ImporteDistinto");
        AfirmarCheck(cadena, "ObservacionEmision", Con(FilaObservacion(1), "Numero", null),
            "CK_ObservacionEmision_ImporteDistinto");

        var sinNumero = Con(Con(Con(FilaObservacion(1), "Motivo", "sin_numero"), "Numero", null), "TotalArcaCentavos", null);
        Insertar(cadena, "ObservacionEmision", sinNumero);
        Assert.Equal([["1"]], FacturasPrueba.Filas(cadena, "SELECT count(*) FROM ObservacionEmision"));
    }

    [Fact]
    public async Task Descarte_GrabaYLee_SinDni()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = FacturasPrueba.CadenaDe(baseTemporal);
        await FacturasPrueba.GrabarPresupuestoFinal(baseTemporal, 1);

        // AC-08 y AC-15: el descarte queda aunque la pendiente ya no exista; dos descartes del mismo presupuesto.
        var contexto = baseTemporal.CrearContexto();
        var repositorio = new DescarteEmisionRepository(contexto);
        repositorio.Agregar(new DescarteEmision(
            Momento, 1, TipoComprobante.FacturaB, 3, 125, 1815m, 2000m, MotivoObservacionEmision.ImporteDistinto,
            EntornoArca.Homologacion));
        repositorio.Agregar(new DescarteEmision(
            Momento.AddMinutes(5), 1, TipoComprobante.FacturaC, 3, null, 1815m, null, MotivoObservacionEmision.SinNumero,
            EntornoArca.Simulado));
        await new UnidadDeTrabajoEf(contexto).ConfirmarAsync();

        Assert.Equal(
            [
                ["1", "2026-11-14T13:30:00.0000000Z", "1", "b", "3", "125", "181500", "200000", "importe_distinto", "homologacion"],
                ["2", "2026-11-14T13:35:00.0000000Z", "1", "c", "3", "", "181500", "", "sin_numero", "simulado"],
            ],
            FacturasPrueba.Filas(cadena,
                "SELECT Id, Momento, PresupuestoNumero, Tipo, PuntoVenta, IFNULL(Numero, ''), TotalCentavos, " +
                "IFNULL(TotalArcaCentavos, ''), Motivo, Entorno FROM DescarteEmision ORDER BY Id"));
        Assert.DoesNotContain(
            FacturasPrueba.Filas(cadena, "SELECT name FROM pragma_table_info('DescarteEmision')")
                .Concat(FacturasPrueba.Filas(cadena, "SELECT name FROM pragma_table_info('ObservacionEmision')")),
            columna => columna[0].Contains("Dni", StringComparison.Ordinal));

        var leidos = await new DescarteEmisionRepository(baseTemporal.CrearContexto()).ListarDescartesAsync(1);
        Assert.Equal(2, leidos.Count);
        Assert.Equal(Momento, leidos[0].Momento);
        Assert.Equal(125, leidos[0].Numero);
        Assert.Equal(1815m, leidos[0].TotalRegistrado);
        Assert.Equal(2000m, leidos[0].TotalArca);
        Assert.Equal(MotivoObservacionEmision.ImporteDistinto, leidos[0].Motivo);
        Assert.Equal(EntornoArca.Homologacion, leidos[0].Entorno);
        Assert.Null(leidos[1].Numero);
        Assert.Equal(TipoComprobante.FacturaC, leidos[1].Tipo);
        Assert.Empty(await new DescarteEmisionRepository(baseTemporal.CrearContexto()).ListarDescartesAsync(2));

        // Índice no único por presupuesto.
        Assert.Equal([["IX_DescarteEmision_PresupuestoNumero", "0"]], FacturasPrueba.Filas(cadena,
            "SELECT name, \"unique\" FROM pragma_index_list('DescarteEmision') WHERE origin = 'c'"));
    }

    [Fact]
    public async Task Descarte_UpdateOBorrado_RechazadoPorTrigger()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = FacturasPrueba.CadenaDe(baseTemporal);
        await FacturasPrueba.GrabarPresupuestoFinal(baseTemporal, 1);
        Insertar(cadena, "DescarteEmision", FilaDescarte(1));
        var antes = FacturasPrueba.Filas(cadena, "SELECT * FROM DescarteEmision");

        // FR-10, ADR-026: el registro de descartes es de solo agregar.
        AfirmarRegistroCerrado(cadena, "UPDATE DescarteEmision SET TotalArcaCentavos = 1");
        AfirmarRegistroCerrado(cadena, "UPDATE DescarteEmision SET Momento = Momento");
        AfirmarRegistroCerrado(cadena, "DELETE FROM DescarteEmision");

        var contexto = baseTemporal.CrearContexto();
        var fila = await contexto.Set<DescarteEmisionFila>().SingleAsync();
        contexto.Remove(fila);
        var conflicto = await Assert.ThrowsAsync<ConflictoAlConfirmarException>(
            () => new UnidadDeTrabajoEf(contexto).ConfirmarAsync());
        Assert.Equal(MotivoConflicto.RegistroCerrado, conflicto.Motivo);

        // REPLACE borra la fila en conflicto: con recursive_triggers (FabricaConexion) dispara el trigger de borrado.
        var reemplazo = await Assert.ThrowsAsync<SqliteException>(() => baseTemporal.CrearContexto().Database
            .ExecuteSqlRawAsync("INSERT OR REPLACE INTO \"DescarteEmision\" SELECT * FROM \"DescarteEmision\""));
        Assert.Equal(SqliteRestriccionTrigger, reemplazo.SqliteExtendedErrorCode);

        Assert.Equal(antes, FacturasPrueba.Filas(cadena, "SELECT * FROM DescarteEmision"));

        // Los dos triggers, cada uno una sola sentencia que aborta.
        Assert.Equal(
            [["TR_DescarteEmision_SinBorrar"], ["TR_DescarteEmision_SinModificar"], ["TR_ObservacionEmision_Inmutable"]],
            FacturasPrueba.Filas(cadena,
                "SELECT name FROM sqlite_master WHERE type = 'trigger' " +
                "AND tbl_name IN ('DescarteEmision', 'ObservacionEmision') ORDER BY name"));
    }

    [Fact]
    public async Task Descarte_Check_Rechazado()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = FacturasPrueba.CadenaDe(baseTemporal);
        await FacturasPrueba.GrabarPresupuestoFinal(baseTemporal, 1);

        AfirmarCheck(cadena, "DescarteEmision", Con(FilaDescarte(1), "Tipo", "a"), "CK_DescarteEmision_Tipo");
        AfirmarCheck(cadena, "DescarteEmision", Con(FilaDescarte(1), "PuntoVenta", 0L), "CK_DescarteEmision_PuntoVenta");
        AfirmarCheck(cadena, "DescarteEmision", Con(FilaDescarte(1), "Numero", 100_000_000L), "CK_DescarteEmision_Numero");
        AfirmarCheck(cadena, "DescarteEmision", Con(FilaDescarte(1), "TotalCentavos", -1L),
            "CK_DescarteEmision_TotalCentavos");
        AfirmarCheck(cadena, "DescarteEmision", Con(FilaDescarte(1), "TotalArcaCentavos", -1L),
            "CK_DescarteEmision_TotalArcaCentavos");
        AfirmarCheck(cadena, "DescarteEmision", Con(FilaDescarte(1), "Motivo", "x"), "CK_DescarteEmision_Motivo");
        AfirmarCheck(cadena, "DescarteEmision", Con(FilaDescarte(1), "Entorno", "x"), "CK_DescarteEmision_Entorno");
        AfirmarCheck(cadena, "DescarteEmision", Con(FilaDescarte(1), "TotalArcaCentavos", null),
            "CK_DescarteEmision_ImporteDistinto");

        var error = Assert.Throws<SqliteException>(() => Insertar(cadena, "DescarteEmision", FilaDescarte(2)));
        Assert.Equal(SqliteRestriccionClaveForanea, error.SqliteExtendedErrorCode);
        Assert.Equal([["0"]], FacturasPrueba.Filas(cadena, "SELECT count(*) FROM DescarteEmision"));
    }

    /// <summary>Graba el presupuesto en Final y una pendiente con número 125, homologación.</summary>
    private static async Task ConPendiente(BaseTemporal baseTemporal, long numero)
    {
        var presupuesto = await FacturasPrueba.GrabarPresupuestoFinal(baseTemporal, numero);
        var contexto = baseTemporal.CrearContexto();
        new EmisionPendienteRepository(contexto).Agregar(Pendiente(presupuesto));
        await new UnidadDeTrabajoEf(contexto).ConfirmarAsync();
    }

    private static EmisionPendiente Pendiente(Presupuesto presupuesto) =>
        new(presupuesto.Numero, Momento,
            new SolicitudComprobante(
                TipoComprobante.FacturaB, 3, new DateOnly(2026, 11, 14), ConceptoFactura.Productos,
                ReceptorFactura.ConDni(presupuesto.Cliente.Dni), ImportesFactura.ParaFacturaB(presupuesto.Total, 21m)),
            125, EntornoArca.Homologacion);

    private static ObservacionEmision ImporteDistinto(long presupuestoNumero) =>
        new(presupuestoNumero, MotivoObservacionEmision.ImporteDistinto, 125, 3000m, 2000m, EntornoArca.Homologacion,
            Momento);

    private static Dictionary<string, object?> FilaObservacion(long presupuestoNumero) => new(StringComparer.Ordinal)
    {
        ["PresupuestoNumero"] = presupuestoNumero,
        ["Motivo"] = "importe_distinto",
        ["Numero"] = 125L,
        ["TotalCentavos"] = 300000L,
        ["TotalArcaCentavos"] = 200000L,
        ["Entorno"] = "homologacion",
        ["Momento"] = "2026-11-14T13:30:00.0000000Z",
    };

    private static Dictionary<string, object?> FilaDescarte(long presupuestoNumero) => new(StringComparer.Ordinal)
    {
        ["Momento"] = "2026-11-14T13:30:00.0000000Z",
        ["PresupuestoNumero"] = presupuestoNumero,
        ["Tipo"] = "b",
        ["PuntoVenta"] = 3L,
        ["Numero"] = 125L,
        ["TotalCentavos"] = 181500L,
        ["TotalArcaCentavos"] = 200000L,
        ["Motivo"] = "importe_distinto",
        ["Entorno"] = "homologacion",
    };

    private static Dictionary<string, object?> Con(Dictionary<string, object?> valores, string columna, object? valor)
    {
        valores[columna] = valor;
        return valores;
    }

    private static void Insertar(string cadena, string tabla, Dictionary<string, object?> valores)
    {
        using var conexion = new SqliteConnection(cadena);
        conexion.Open();
        FacturasPrueba.Insertar(conexion, tabla, valores);
    }

    private static void AfirmarCheck(string cadena, string tabla, Dictionary<string, object?> valores, string restriccion)
    {
        var error = Assert.Throws<SqliteException>(() => Insertar(cadena, tabla, valores));
        Assert.Equal(SqliteRestriccionCheck, error.SqliteExtendedErrorCode);
        Assert.Contains("CHECK constraint failed: " + restriccion, error.Message, StringComparison.Ordinal);
    }

    private static void AfirmarRegistroCerrado(string cadena, string sentencia)
    {
        var error = Assert.Throws<SqliteException>(() => FacturasPrueba.Ejecutar(cadena, sentencia));
        Assert.Equal(SqliteRestriccionTrigger, error.SqliteExtendedErrorCode);
        Assert.Contains("registro_cerrado", error.Message, StringComparison.Ordinal);
    }
}
