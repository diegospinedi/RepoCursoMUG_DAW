using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Optica.Application.Comun;
using Optica.Domain.Facturacion;
using Optica.Infrastructure.Persistencia;

namespace Optica.Infrastructure.Tests.Persistencia;

/// <summary>
/// Inmutabilidad de las facturas en la base (Block 4 de FEAT-007, FR-25, AC-23): triggers <c>registro_cerrado</c> sobre
/// <c>Factura</c> y <c>LineaFactura</c>, CHECK de los importes y la migración <c>Facturas</c>.
/// </summary>
public partial class FacturasTriggersTests
{
    private const int SqliteRestriccionCheck = 275; // SQLITE_CONSTRAINT_CHECK
    private const int SqliteRestriccionTrigger = 1811; // SQLITE_CONSTRAINT_TRIGGER
    private const int SqliteRestriccionClaveForanea = 787; // SQLITE_CONSTRAINT_FOREIGNKEY

    private const string SelectFactura = "SELECT * FROM Factura ORDER BY Id";
    private const string SelectLineas = "SELECT * FROM LineaFactura ORDER BY FacturaId, Orden";

    /// <summary>
    /// Los cinco triggers de la migración Facturas sobre <c>Factura</c> y <c>LineaFactura</c>, como (tabla, nombre),
    /// ordenados. El sexto, <c>TR_EmisionPendiente_Inmutable</c>, se verifica aparte en
    /// <see cref="Migraciones_TodasAplicadas_LosCincoTriggersExisten"/>.
    /// </summary>
    private static readonly List<string[]> TriggersDeFacturas =
    [
        ["Factura", "TR_Factura_Inmutable"],
        ["Factura", "TR_Factura_NoSeBorra"],
        ["LineaFactura", "TR_LineaFactura_Cerrada"],
        ["LineaFactura", "TR_LineaFactura_Inmutable"],
        ["LineaFactura", "TR_LineaFactura_NoSeBorra"],
    ];

    [Fact]
    public async Task Update_Factura_RechazadoRegistroCerrado()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = await ConFactura(baseTemporal);
        var antes = FacturasPrueba.Filas(cadena, SelectFactura);

        // Por EF y la unidad de trabajo: conflicto RegistroCerrado, sin la excepción de EF como causa.
        var contexto = baseTemporal.CrearContexto();
        var fila = await contexto.Set<FacturaFila>().SingleAsync();
        fila.Cae = "79999999999999";
        var error = await Assert.ThrowsAsync<ConflictoAlConfirmarException>(
            () => new UnidadDeTrabajoEf(contexto).ConfirmarAsync());
        Assert.Equal(MotivoConflicto.RegistroCerrado, error.Motivo);
        Assert.Null(error.InnerException);

        // Por SQL directo, cualquier columna, aun reescribiendo el mismo valor.
        AfirmarRegistroCerrado(cadena, "UPDATE Factura SET Entorno = 'produccion'");
        AfirmarRegistroCerrado(cadena, "UPDATE Factura SET PresupuestoNumero = PresupuestoNumero");
        Assert.Equal(antes, FacturasPrueba.Filas(cadena, SelectFactura));
    }

    [Fact]
    public async Task Delete_Factura_RechazadoRegistroCerrado()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = await ConFactura(baseTemporal);
        var antes = FacturasPrueba.Filas(cadena, SelectFactura);
        var lineasAntes = FacturasPrueba.Filas(cadena, SelectLineas);

        var contexto = baseTemporal.CrearContexto();
        contexto.Remove(await contexto.Set<FacturaFila>().SingleAsync());
        var error = await Assert.ThrowsAsync<ConflictoAlConfirmarException>(
            () => new UnidadDeTrabajoEf(contexto).ConfirmarAsync());
        Assert.Equal(MotivoConflicto.RegistroCerrado, error.Motivo);

        // Ni la factura ni sus líneas: el trigger aborta antes que la clave foránea.
        AfirmarRegistroCerrado(cadena, "DELETE FROM Factura");
        AfirmarRegistroCerrado(cadena, "DELETE FROM LineaFactura");
        Assert.Equal(antes, FacturasPrueba.Filas(cadena, SelectFactura));
        Assert.Equal(lineasAntes, FacturasPrueba.Filas(cadena, SelectLineas));
    }

    [Fact]
    public async Task Update_LineaFactura_RechazadoRegistroCerrado()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = await ConFactura(baseTemporal);
        var antes = FacturasPrueba.Filas(cadena, SelectLineas);

        var contexto = baseTemporal.CrearContexto();
        var linea = await contexto.Set<LineaFacturaFila>().SingleAsync(l => l.Orden == 2);
        linea.Descripcion = "Otra descripción";
        var error = await Assert.ThrowsAsync<ConflictoAlConfirmarException>(
            () => new UnidadDeTrabajoEf(contexto).ConfirmarAsync());
        Assert.Equal(MotivoConflicto.RegistroCerrado, error.Motivo);

        AfirmarRegistroCerrado(cadena, "UPDATE LineaFactura SET PrecioFinalCentavos = 0 WHERE Orden = 1");
        AfirmarRegistroCerrado(cadena, "UPDATE LineaFactura SET Orden = Orden + 10");
        Assert.Equal(antes, FacturasPrueba.Filas(cadena, SelectLineas));
    }

    [Fact]
    public async Task Insert_LineaEnFacturaCompleta_RechazadoRegistroCerrado()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = await ConFactura(baseTemporal);
        var antes = FacturasPrueba.Filas(cadena, SelectLineas);
        var facturaId = long.Parse(Assert.Single(FacturasPrueba.Filas(cadena, "SELECT Id FROM Factura"))[0]);

        // La factura tiene CantidadLineas = 3 y sus 3 líneas: una cuarta no entra (D-34).
        var contexto = baseTemporal.CrearContexto();
        contexto.Add(new LineaFacturaFila
        {
            FacturaId = facturaId,
            Orden = 4,
            CodigoArticulo = 1,
            Descripcion = "Línea agregada",
            PrecioUnitario = 10m,
            PrecioConDescuento = 10m,
            PrecioFinal = 10m,
            Cantidad = 1,
            PorcentajeDescuento = 0m,
        });
        var error = await Assert.ThrowsAsync<ConflictoAlConfirmarException>(
            () => new UnidadDeTrabajoEf(contexto).ConfirmarAsync());
        Assert.Equal(MotivoConflicto.RegistroCerrado, error.Motivo);

        using (var conexion = new SqliteConnection(cadena))
        {
            conexion.Open();
            var linea = FacturasPrueba.ValoresLinea(facturaId, orden: 5, precioFinalCentavos: 1000L);
            var rechazo = Assert.Throws<SqliteException>(() => FacturasPrueba.Insertar(conexion, "LineaFactura", linea));
            AfirmarEsRegistroCerrado(rechazo);
        }

        Assert.Equal(antes, FacturasPrueba.Filas(cadena, SelectLineas));
    }

    [Fact]
    public async Task Check_ImportesQueNoSuman_Rechazado()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = FacturasPrueba.CadenaDe(baseTemporal);
        await FacturasPrueba.GrabarPresupuestoFinal(baseTemporal, 1);
        Assert.Contains(["Factura"], FacturasPrueba.Filas(cadena, "SELECT name FROM sqlite_master WHERE type = 'table'"));
        Assert.Equal([["0"]], FacturasPrueba.Filas(cadena, "SELECT count(*) FROM Factura"));

        // CK_Factura_Importes: neto + IVA = total.
        var factura = FacturasPrueba.ValoresFactura(1, id: 1, numero: 125);
        factura["IvaCentavos"] = 52067L;
        AfirmarCheck(cadena, factura, "CK_Factura_Importes");

        // Los CHECK de rango que acompañan a los del dominio (D-45) y el del vencimiento del CAE.
        AfirmarCheck(cadena, Con(FacturasPrueba.ValoresFactura(1, 1, 125), "Numero", 100_000_000L), "CK_Factura_Numero");
        AfirmarCheck(cadena, Con(FacturasPrueba.ValoresFactura(1, 1, 125), "Numero", 0L), "CK_Factura_Numero");
        AfirmarCheck(
            cadena, Con(FacturasPrueba.ValoresFactura(1, 1, 125), "VencimientoCae", "9/11/2026"), "CK_Factura_VencimientoCae");
        AfirmarCheck(cadena, Con(FacturasPrueba.ValoresFactura(1, 1, 125), "Cae", "7123456789012A"), "CK_Factura_Cae");
        AfirmarCheck(cadena, Con(FacturasPrueba.ValoresFactura(1, 1, 125), "ReceptorDni", "123456"), "CK_Factura_ReceptorDni");
        AfirmarCheck(cadena, Con(FacturasPrueba.ValoresFactura(1, 1, 125), "Entorno", "Simulado"), "CK_Factura_Entorno");
        AfirmarCheck(cadena, Con(FacturasPrueba.ValoresFactura(1, 1, 125), "CantidadLineas", 0L), "CK_Factura_CantidadLineas");

        // El número llega hasta 99.999.999 (D-45).
        AfirmarCheck(cadena, Con(FacturasPrueba.ValoresFactura(1, 1, 125), "Numero", 99_999_999L + 1), "CK_Factura_Numero");
        Assert.Equal([["0"]], FacturasPrueba.Filas(cadena, "SELECT count(*) FROM Factura"));

        // La fila válida entra, también con el número máximo.
        FacturasPrueba.InsertarFacturaCruda(cadena, FacturasPrueba.ValoresFactura(1, 1, 99_999_999L), []);
        Assert.Equal([["1"]], FacturasPrueba.Filas(cadena, "SELECT count(*) FROM Factura"));
    }

    [Fact]
    public async Task Check_FacturaCConIvaOVencimientoAnterior_Rechazado()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = FacturasPrueba.CadenaDe(baseTemporal);
        await FacturasPrueba.GrabarPresupuestoFinal(baseTemporal, 1);

        // WARN-2: en la Factura C no hay IVA (FR-16), aunque neto + IVA sume el total.
        var conIva = FacturaC();
        conIva["NetoCentavos"] = 299900L;
        conIva["IvaCentavos"] = 100L;
        AfirmarCheck(cadena, conIva, "CK_Factura_Alicuota");

        // El vencimiento del CAE no es anterior a la fecha del comprobante (D-45); el mismo día sí entra.
        AfirmarCheck(
            cadena, Con(FacturasPrueba.ValoresFactura(1, 1, 125), "VencimientoCae", "2026-10-29"), "CK_Factura_VencimientoCae");
        Assert.Equal([["0"]], FacturasPrueba.Filas(cadena, "SELECT count(*) FROM Factura"));

        FacturasPrueba.InsertarFacturaCruda(cadena, Con(FacturaC(), "VencimientoCae", "2026-10-30"), []);
        Assert.Equal([["1"]], FacturasPrueba.Filas(cadena, "SELECT count(*) FROM Factura"));
    }

    [Fact]
    public async Task Check_EmisionPendiente_Rechazado()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = FacturasPrueba.CadenaDe(baseTemporal);
        await FacturasPrueba.GrabarPresupuestoFinal(baseTemporal, 1);

        AfirmarCheckPendiente(cadena, Con(Pendiente(1), "Tipo", "a"), "CK_EmisionPendiente_Tipo");
        AfirmarCheckPendiente(cadena, Con(Pendiente(1), "PuntoVenta", 0L), "CK_EmisionPendiente_PuntoVenta");
        AfirmarCheckPendiente(cadena, Con(Pendiente(1), "PuntoVenta", 100000L), "CK_EmisionPendiente_PuntoVenta");
        AfirmarCheckPendiente(cadena, Con(Pendiente(1), "Fecha", "30/10/2026"), "CK_EmisionPendiente_Fecha");
        AfirmarCheckPendiente(cadena, Con(Pendiente(1), "TotalCentavos", -1L), "CK_EmisionPendiente_TotalCentavos");
        AfirmarCheckPendiente(cadena, Con(Pendiente(1), "Entorno", "prod"), "CK_EmisionPendiente_Entorno");
        Assert.Equal([["0"]], FacturasPrueba.Filas(cadena, "SELECT count(*) FROM EmisionPendiente"));

        InsertarPendiente(cadena, Pendiente(1));
        Assert.Equal([["1"]], FacturasPrueba.Filas(cadena, "SELECT count(*) FROM EmisionPendiente"));
    }

    [Fact]
    public async Task ClaveForanea_PresupuestoInexistente_Rechazado()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = FacturasPrueba.CadenaDe(baseTemporal);
        await FacturasPrueba.GrabarPresupuestoFinal(baseTemporal, 1);

        // Factura y EmisionPendiente apuntan a Presupuesto(Numero) con RESTRICT: el 2 no existe.
        var factura = Assert.Throws<SqliteException>(() => FacturasPrueba.InsertarFacturaCruda(
            cadena, FacturasPrueba.ValoresFactura(2, 1, 125), []));
        Assert.Equal(SqliteRestriccionClaveForanea, factura.SqliteExtendedErrorCode);
        var pendiente = Assert.Throws<SqliteException>(() => InsertarPendiente(cadena, Pendiente(2)));
        Assert.Equal(SqliteRestriccionClaveForanea, pendiente.SqliteExtendedErrorCode);

        // Una línea apunta a la factura: sin factura, tampoco entra.
        using (var conexion = new SqliteConnection(cadena))
        {
            conexion.Open();
            var linea = Assert.Throws<SqliteException>(() => FacturasPrueba.Insertar(
                conexion, "LineaFactura", FacturasPrueba.ValoresLinea(facturaId: 9, orden: 1, precioFinalCentavos: 1L)));
            Assert.Equal(SqliteRestriccionClaveForanea, linea.SqliteExtendedErrorCode);
        }

        // Con un presupuesto existente, las mismas altas entran.
        FacturasPrueba.InsertarFacturaCruda(cadena, FacturasPrueba.ValoresFactura(1, 1, 125), []);
        InsertarPendiente(cadena, Pendiente(1));
        Assert.Equal([["1", "1"]], FacturasPrueba.Filas(cadena,
            "SELECT (SELECT count(*) FROM Factura), (SELECT count(*) FROM EmisionPendiente)"));
    }

    [Fact]
    public async Task InsertOrReplace_Factura_RechazadoRegistroCerrado()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = await ConFactura(baseTemporal);
        var antes = FacturasPrueba.Filas(cadena, SelectFactura);
        var lineasAntes = FacturasPrueba.Filas(cadena, SelectLineas);

        // WARN-1: REPLACE borra la fila en conflicto antes de insertar. Con recursive_triggers (FabricaConexion) ese
        // borrado dispara TR_Factura_NoSeBorra; sin él, SQLite lo hace sin triggers.
        var contexto = baseTemporal.CrearContexto();
        foreach (var sentencia in new[]
        {
            "INSERT OR REPLACE INTO \"Factura\" SELECT * FROM \"Factura\"",
            "REPLACE INTO \"Factura\" SELECT * FROM \"Factura\"",
            "INSERT OR REPLACE INTO \"LineaFactura\" SELECT * FROM \"LineaFactura\"",
        })
        {
            var error = await Assert.ThrowsAsync<SqliteException>(() => contexto.Database.ExecuteSqlRawAsync(sentencia));
            AfirmarEsRegistroCerrado(error);
        }

        Assert.Equal(antes, FacturasPrueba.Filas(cadena, SelectFactura));
        Assert.Equal(lineasAntes, FacturasPrueba.Filas(cadena, SelectLineas));
        Assert.Equal(1L, await Escalar(contexto, "PRAGMA recursive_triggers"));
    }

    [Fact]
    public async Task Update_EmisionPendiente_RechazadoRegistroCerrado()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = FacturasPrueba.CadenaDe(baseTemporal);
        await FacturasPrueba.GrabarPresupuestoFinal(baseTemporal, 1);
        InsertarPendiente(cadena, Pendiente(1));
        var antes = FacturasPrueba.Filas(cadena, "SELECT * FROM EmisionPendiente");

        // INFO-5: la pendiente se agrega y se quita, no se modifica.
        var contexto = baseTemporal.CrearContexto();
        var fila = await contexto.Set<EmisionPendienteFila>().SingleAsync();
        fila.Total = 1m;
        var error = await Assert.ThrowsAsync<ConflictoAlConfirmarException>(
            () => new UnidadDeTrabajoEf(contexto).ConfirmarAsync());
        Assert.Equal(MotivoConflicto.RegistroCerrado, error.Motivo);

        AfirmarRegistroCerrado(cadena, "UPDATE EmisionPendiente SET Entorno = 'produccion'");
        Assert.Equal(antes, FacturasPrueba.Filas(cadena, "SELECT * FROM EmisionPendiente"));

        // Quitarla sigue permitido.
        var otro = baseTemporal.CrearContexto();
        new EmisionPendienteRepository(otro).Quitar(1);
        await new UnidadDeTrabajoEf(otro).ConfirmarAsync();
        Assert.Equal([["0"]], FacturasPrueba.Filas(cadena, "SELECT count(*) FROM EmisionPendiente"));
    }

    /// <summary>
    /// El nombre lo fija la spec: verifica los cinco triggers de <c>Factura</c> y <c>LineaFactura</c> y, además, el de
    /// <c>EmisionPendiente</c> (INFO-5 de la auditoría), los índices únicos y que todos aborten con
    /// <c>registro_cerrado</c>.
    /// </summary>
    [Fact]
    public void Migraciones_TodasAplicadas_LosCincoTriggersExisten()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = FacturasPrueba.CadenaDe(baseTemporal);
        var contexto = baseTemporal.CrearContexto();

        Assert.Empty(contexto.Database.GetPendingMigrations());
        Assert.Equal(
            TriggersDeFacturas,
            FacturasPrueba.Filas(cadena,
                "SELECT tbl_name, name FROM sqlite_master WHERE type = 'trigger' " +
                "AND tbl_name IN ('Factura', 'LineaFactura') ORDER BY tbl_name, name"));

        // Y el de la pendiente, que se agrega y se quita pero no se modifica (INFO-5).
        Assert.Equal(
            [["EmisionPendiente", "TR_EmisionPendiente_Inmutable"]],
            FacturasPrueba.Filas(cadena,
                "SELECT tbl_name, name FROM sqlite_master WHERE type = 'trigger' AND tbl_name = 'EmisionPendiente'"));
        Assert.All(
            FacturasPrueba.Filas(cadena,
                "SELECT sql FROM sqlite_master WHERE type = 'trigger' " +
                "AND tbl_name IN ('Factura', 'LineaFactura', 'EmisionPendiente')"),
            sql => Assert.Contains("RAISE(ABORT, 'registro_cerrado')", sql[0], StringComparison.Ordinal));

        // Los índices únicos de FR-03 y del comprobante.
        Assert.Equal(
            [["IX_Factura_Comprobante", "1"], ["IX_Factura_PresupuestoNumero", "1"]],
            FacturasPrueba.Filas(cadena,
                "SELECT name, \"unique\" FROM pragma_index_list('Factura') WHERE origin = 'c' ORDER BY name"));
        Assert.Equal(
            ["Entorno", "Tipo", "PuntoVenta", "Numero"],
            FacturasPrueba.Filas(cadena, "SELECT name FROM pragma_index_info('IX_Factura_Comprobante') ORDER BY seqno")
                .Select(f => f[0]));
    }

    [Fact]
    public void Triggers_SoloAbortan_NingunoEscribe()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = FacturasPrueba.CadenaDe(baseTemporal);

        // INFO-3: FabricaConexion activa recursive_triggers suponiendo que ningún trigger escribe; si uno escribiera,
        // podría disparar otros en cadena. Todos los triggers del esquema, no solo los de facturación.
        var triggers = FacturasPrueba.Filas(cadena, "SELECT name, sql FROM sqlite_master WHERE type = 'trigger' ORDER BY name");
        Assert.True(triggers.Count >= 12, "Faltan triggers: " + triggers.Count);
        Assert.All(triggers, trigger =>
        {
            var cuerpo = RegexCuerpoTrigger().Match(trigger[1]);
            Assert.True(cuerpo.Success, trigger[0]);
            Assert.Matches(RegexSoloAborta(), cuerpo.Groups[1].Value);
        });
    }

    // El cuerpo entre BEGIN y el END final.
    [GeneratedRegex(@"\bBEGIN\b(.*)\bEND\s*;?\s*$", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex RegexCuerpoTrigger();

    // Una única sentencia: SELECT RAISE(ABORT, 'texto');
    [GeneratedRegex(@"^\s*SELECT\s+RAISE\s*\(\s*ABORT\s*,\s*'[a-z_]+'\s*\)\s*;\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex RegexSoloAborta();

    [Fact]
    public void Migracion_Facturas_AplicaYRevierteSobreLaAnterior()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = FacturasPrueba.CadenaDe(baseTemporal);
        var contexto = baseTemporal.CrearContexto();
        var migrador = contexto.GetService<IMigrator>();

        // Por nombre (D-29): Facturas va justo después de ConfiguracionPuntoVenta (D-39).
        var migraciones = contexto.Database.GetMigrations().ToList();
        var facturas = Assert.Single(migraciones, m => m.EndsWith("_Facturas", StringComparison.Ordinal));
        var anterior = Assert.Single(migraciones, m => m.EndsWith("_ConfiguracionPuntoVenta", StringComparison.Ordinal));
        Assert.Equal(migraciones.IndexOf(anterior) + 1, migraciones.IndexOf(facturas));

        // ADR-012: solo tablas e índices nuevos y los triggers; ninguna tabla existente se reconstruye.
        var ensamblado = contexto.GetService<IMigrationsAssembly>();
        var migracion = ensamblado.CreateMigration(ensamblado.Migrations[facturas], contexto.Database.ProviderName!);
        Assert.All(migracion.UpOperations, operacion =>
            Assert.True(operacion is CreateTableOperation or CreateIndexOperation or SqlOperation, operacion.GetType().Name));
        Assert.Equal(
            ["Factura", "LineaFactura", "EmisionPendiente"],
            migracion.UpOperations.OfType<CreateTableOperation>().Select(t => t.Name));
        Assert.Equal(6, migracion.UpOperations.OfType<SqlOperation>().Count());

        var sqlExistentes = SqlExistentes(cadena);

        // Down (solo desarrollo): sin triggers, índices ni tablas de facturación; lo demás queda igual.
        migrador.Migrate(anterior);
        Assert.Empty(FacturasPrueba.Filas(cadena,
            "SELECT name FROM sqlite_master WHERE tbl_name IN ('Factura', 'LineaFactura', 'EmisionPendiente')"));
        Assert.Equal(sqlExistentes, SqlExistentes(cadena));

        migrador.Migrate();
        Assert.Empty(contexto.Database.GetPendingMigrations());
        Assert.Equal(6, FacturasPrueba.Filas(cadena,
            "SELECT name FROM sqlite_master WHERE type = 'trigger' " +
            "AND tbl_name IN ('Factura', 'LineaFactura', 'EmisionPendiente')").Count);
        Assert.Equal(sqlExistentes, SqlExistentes(cadena));
    }

    /// <summary>Graba un presupuesto en Final y su Factura B de 3 líneas; devuelve la cadena de conexión.</summary>
    private static async Task<string> ConFactura(BaseTemporal baseTemporal)
    {
        var cadena = FacturasPrueba.CadenaDe(baseTemporal);
        var presupuesto = await FacturasPrueba.GrabarPresupuestoFinal(baseTemporal, 1);
        await FacturasPrueba.GrabarFactura(
            baseTemporal, FacturasPrueba.FacturaB(presupuesto, numero: 125, EntornoArca.Homologacion, identificado: true));
        Assert.True(await new FacturaRepository(baseTemporal.CrearContexto()).ExisteParaPresupuestoAsync(1));
        Assert.Equal([["3"]], FacturasPrueba.Filas(cadena, "SELECT count(*) FROM LineaFactura"));
        return cadena;
    }

    /// <summary>Una Factura C de 3.000 sin identificar, del presupuesto 1.</summary>
    private static Dictionary<string, object?> FacturaC()
    {
        var factura = FacturasPrueba.ValoresFactura(1, id: 1, numero: 125);
        factura["Tipo"] = "c";
        factura["ReceptorDni"] = null;
        factura["NetoCentavos"] = 300000L;
        factura["IvaCentavos"] = 0L;
        factura["AlicuotaCentesimas"] = null;
        return factura;
    }

    /// <summary>Las columnas de una emisión pendiente válida.</summary>
    private static Dictionary<string, object?> Pendiente(long presupuestoNumero) => new(StringComparer.Ordinal)
    {
        ["PresupuestoNumero"] = presupuestoNumero,
        ["Momento"] = "2026-10-30T14:15:20.0000000Z",
        ["Tipo"] = "b",
        ["PuntoVenta"] = 3L,
        ["Fecha"] = "2026-10-30",
        ["TotalCentavos"] = 300000L,
        ["Entorno"] = "simulado",
    };

    private static void InsertarPendiente(string cadena, Dictionary<string, object?> pendiente)
    {
        using var conexion = new SqliteConnection(cadena);
        conexion.Open();
        FacturasPrueba.Insertar(conexion, "EmisionPendiente", pendiente);
    }

    private static void AfirmarCheckPendiente(string cadena, Dictionary<string, object?> pendiente, string restriccion)
    {
        var error = Assert.Throws<SqliteException>(() => InsertarPendiente(cadena, pendiente));
        Assert.Equal(SqliteRestriccionCheck, error.SqliteExtendedErrorCode);
        Assert.Contains("CHECK constraint failed: " + restriccion, error.Message, StringComparison.Ordinal);
    }

    /// <summary>Un valor escalar leído por la conexión de EF Core, la que configura <see cref="FabricaConexion"/>.</summary>
    private static async Task<long> Escalar(OpticaDbContext contexto, string consulta)
    {
        var conexion = contexto.Database.GetDbConnection();
        await contexto.Database.OpenConnectionAsync();
        try
        {
            await using var comando = conexion.CreateCommand();
            comando.CommandText = consulta;
            return Convert.ToInt64(await comando.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
        }
        finally
        {
            await contexto.Database.CloseConnectionAsync();
        }
    }

    private static Dictionary<string, object?> Con(Dictionary<string, object?> valores, string columna, object? valor)
    {
        valores[columna] = valor;
        return valores;
    }

    private static void AfirmarCheck(string cadena, Dictionary<string, object?> factura, string restriccion)
    {
        var error = Assert.Throws<SqliteException>(() => FacturasPrueba.InsertarFacturaCruda(cadena, factura, []));
        Assert.Equal(SqliteRestriccionCheck, error.SqliteExtendedErrorCode);
        Assert.Contains("CHECK constraint failed: " + restriccion, error.Message, StringComparison.Ordinal);
    }

    private static void AfirmarRegistroCerrado(string cadena, string sentencia)
    {
        var error = Assert.Throws<SqliteException>(() => FacturasPrueba.Ejecutar(cadena, sentencia));
        AfirmarEsRegistroCerrado(error);
    }

    private static void AfirmarEsRegistroCerrado(SqliteException error)
    {
        Assert.Equal(SqliteRestriccionTrigger, error.SqliteExtendedErrorCode);
        Assert.Contains("registro_cerrado", error.Message, StringComparison.Ordinal);
    }

    /// <summary>El esquema de todo lo que no es de facturación, para ver que la migración no lo toca.</summary>
    private static List<string[]> SqlExistentes(string cadena) =>
        FacturasPrueba.Filas(cadena,
            "SELECT type, name, IFNULL(sql, '') FROM sqlite_master " +
            "WHERE tbl_name NOT IN ('Factura', 'LineaFactura', 'EmisionPendiente', '__EFMigrationsHistory') " +
            "ORDER BY type, name");
}
