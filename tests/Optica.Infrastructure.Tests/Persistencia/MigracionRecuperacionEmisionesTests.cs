using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Optica.Infrastructure.Persistencia;

namespace Optica.Infrastructure.Tests.Persistencia;

/// <summary>
/// La migración <c>RecuperacionEmisiones</c> (Block 1 de FEAT-008, D-15, ADR-026): columnas nuevas de
/// <c>EmisionPendiente</c> por <c>ALTER TABLE ADD COLUMN</c>, sin reconstruirla, y las tablas <c>ObservacionEmision</c> y
/// <c>DescarteEmision</c> con sus triggers. Datos ficticios.
/// </summary>
public class MigracionRecuperacionEmisionesTests
{
    private const string SufijoFacturas = "_Facturas";
    private const string SufijoRecuperacion = "_RecuperacionEmisiones";

    private static readonly string[] ColumnasFeat007 =
        ["PresupuestoNumero", "Momento", "Tipo", "PuntoVenta", "Fecha", "TotalCentavos", "Entorno"];

    private static readonly string[] ColumnasNuevas =
        ["Numero", "Concepto", "NetoCentavos", "IvaCentavos", "Alicuota", "ReceptorIdentificado"];

    /// <summary>Las seis columnas tal como las agrega la migración (D-15): cada CHECK nombra solo su columna.</summary>
    private const string DefinicionesNuevas =
        ", \"Numero\" INTEGER NULL CONSTRAINT \"CK_EmisionPendiente_Numero\" CHECK " +
        "(\"Numero\" IS NULL OR \"Numero\" BETWEEN 1 AND 99999999)" +
        ", \"Concepto\" TEXT NULL CONSTRAINT \"CK_EmisionPendiente_Concepto\" CHECK " +
        "(\"Concepto\" IS NULL OR \"Concepto\" IN ('productos', 'productos_y_servicios'))" +
        ", \"NetoCentavos\" INTEGER NULL CONSTRAINT \"CK_EmisionPendiente_NetoCentavos\" CHECK " +
        "(\"NetoCentavos\" IS NULL OR \"NetoCentavos\" >= 0)" +
        ", \"IvaCentavos\" INTEGER NULL CONSTRAINT \"CK_EmisionPendiente_IvaCentavos\" CHECK " +
        "(\"IvaCentavos\" IS NULL OR \"IvaCentavos\" >= 0)" +
        ", \"Alicuota\" TEXT NULL" +
        ", \"ReceptorIdentificado\" INTEGER NULL CONSTRAINT \"CK_EmisionPendiente_ReceptorIdentificado\" CHECK " +
        "(\"ReceptorIdentificado\" IS NULL OR \"ReceptorIdentificado\" IN (0, 1))";

    [Fact]
    public async Task Migracion_SobreBaseConPendientesDeFeat007_ConservaTriggerYColumnasNulas()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = FacturasPrueba.CadenaDe(baseTemporal);
        var contexto = baseTemporal.CrearContexto();
        var migrador = contexto.GetService<IMigrator>();
        var (facturas, recuperacion) = Migraciones(contexto);

        // Por nombre (D-29 de FEAT-007): va justo después de Facturas.
        var todas = contexto.Database.GetMigrations().ToList();
        Assert.Equal(todas.IndexOf(facturas) + 1, todas.IndexOf(recuperacion));

        // D-15: SQL a mano para EmisionPendiente; solo tablas e índices nuevos además. Ninguna tabla se reconstruye.
        var ensamblado = contexto.GetService<IMigrationsAssembly>();
        var migracion = ensamblado.CreateMigration(ensamblado.Migrations[recuperacion], contexto.Database.ProviderName!);
        Assert.All(migracion.UpOperations, operacion =>
            Assert.True(operacion is CreateTableOperation or CreateIndexOperation or SqlOperation, operacion.GetType().Name));
        Assert.Equal(
            ["ObservacionEmision", "DescarteEmision"],
            migracion.UpOperations.OfType<CreateTableOperation>().Select(t => t.Name));
        Assert.All(migracion.DownOperations, operacion =>
            Assert.True(operacion is DropTableOperation or SqlOperation, operacion.GetType().Name));

        // Una base como la que dejó FEAT-007, con una emisión pendiente (AC-15).
        migrador.Migrate(facturas);
        Assert.Equal(ColumnasFeat007, Columnas(cadena, "EmisionPendiente"));
        await FacturasPrueba.GrabarPresupuestoFinal(baseTemporal, 150);
        using (var conexion = new SqliteConnection(cadena))
        {
            conexion.Open();
            FacturasPrueba.Insertar(conexion, "EmisionPendiente", new(StringComparer.Ordinal)
            {
                ["PresupuestoNumero"] = 150L,
                ["Momento"] = "2026-10-30T14:15:20.0000000Z",
                ["Tipo"] = "b",
                ["PuntoVenta"] = 3L,
                ["Fecha"] = "2026-10-30",
                ["TotalCentavos"] = 300000L,
                ["Entorno"] = "simulado",
            });
        }

        var sqlAntes = SqlTabla(cadena, "EmisionPendiente");
        var datosAntes = FacturasPrueba.Filas(cadena, "SELECT * FROM EmisionPendiente");

        migrador.Migrate();
        Assert.Empty(contexto.Database.GetPendingMigrations());
        Assert.False(contexto.Database.HasPendingModelChanges());

        // Sin reconstruir: el CREATE TABLE es el mismo, con las columnas nuevas después de la última (SQLite las inserta
        // antes de las restricciones de tabla); los datos iguales y las columnas nuevas nulas.
        Assert.Equal([.. ColumnasFeat007, .. ColumnasNuevas], Columnas(cadena, "EmisionPendiente"));
        var sqlDespues = SqlTabla(cadena, "EmisionPendiente");
        Assert.Contains(DefinicionesNuevas, sqlDespues, StringComparison.Ordinal);
        Assert.Equal(sqlAntes, sqlDespues.Replace(DefinicionesNuevas, string.Empty, StringComparison.Ordinal));
        Assert.Equal(
            datosAntes,
            FacturasPrueba.Filas(cadena, "SELECT " + string.Join(", ", ColumnasFeat007) + " FROM EmisionPendiente"));
        Assert.Equal(
            [["1", "1", "1", "1", "1", "1"]],
            FacturasPrueba.Filas(cadena,
                "SELECT " + string.Join(", ", ColumnasNuevas.Select(c => $"\"{c}\" IS NULL")) + " FROM EmisionPendiente"));

        // El trigger de FEAT-007 sigue (no se reconstruyó la tabla) y aborta como antes.
        Assert.Equal(
            [["TR_EmisionPendiente_Inmutable"]],
            FacturasPrueba.Filas(cadena,
                "SELECT name FROM sqlite_master WHERE type = 'trigger' AND tbl_name = 'EmisionPendiente'"));
        var error = Assert.Throws<SqliteException>(
            () => FacturasPrueba.Ejecutar(cadena, "UPDATE EmisionPendiente SET Numero = 125"));
        Assert.Contains("registro_cerrado", error.Message, StringComparison.Ordinal);

        // Los CHECK de las columnas nuevas, cada uno sobre su columna.
        foreach (var (columna, valor, restriccion) in new (string, object, string)[]
        {
            ("Numero", 0L, "CK_EmisionPendiente_Numero"),
            ("Numero", 100_000_000L, "CK_EmisionPendiente_Numero"),
            ("Concepto", "servicios", "CK_EmisionPendiente_Concepto"),
            ("NetoCentavos", -1L, "CK_EmisionPendiente_NetoCentavos"),
            ("IvaCentavos", -1L, "CK_EmisionPendiente_IvaCentavos"),
            ("ReceptorIdentificado", 2L, "CK_EmisionPendiente_ReceptorIdentificado"),
        })
        {
            var check = Assert.Throws<SqliteException>(() =>
            {
                using var conexion = new SqliteConnection(cadena);
                conexion.Open();
                FacturasPrueba.Insertar(conexion, "EmisionPendiente", new(StringComparer.Ordinal)
                {
                    ["PresupuestoNumero"] = 150L,
                    ["Momento"] = "2026-10-30T14:15:20.0000000Z",
                    ["Tipo"] = "b",
                    ["PuntoVenta"] = 3L,
                    ["Fecha"] = "2026-10-30",
                    ["TotalCentavos"] = 300000L,
                    ["Entorno"] = "simulado",
                    [columna] = valor,
                });
            });
            Assert.Contains("CHECK constraint failed: " + restriccion, check.Message, StringComparison.Ordinal);
        }

        // Se lee como una pendiente sin número (FR-14).
        var leida = await new EmisionPendienteRepository(baseTemporal.CrearContexto()).ObtenerPendienteAsync(150);
        Assert.NotNull(leida);
        Assert.False(leida.TieneNumero);
    }

    [Fact]
    public void Migracion_Down_Revierte()
    {
        var directorio = BaseTemporal.CrearDirectorioTemporal();
        try
        {
            using var servicios = BaseTemporal.CrearServicios(directorio);
            var fabrica = servicios.GetRequiredService<FabricaConexion>();
            fabrica.PrepararDirectorio();
            using var alcance = servicios.CreateScope();
            var contexto = alcance.ServiceProvider.GetRequiredService<OpticaDbContext>();
            var migrador = contexto.GetService<IMigrator>();
            var cadena = fabrica.CadenaConexion;
            var (facturas, _) = Migraciones(contexto);

            // Una base que nunca tuvo RecuperacionEmisiones: el esquema de Facturas tal como lo dejó FEAT-007.
            migrador.Migrate(facturas);
            var esquemaFacturas = Esquema(cadena);

            migrador.Migrate();
            Assert.Equal(
                [["DescarteEmision"], ["ObservacionEmision"]],
                FacturasPrueba.Filas(cadena,
                    "SELECT name FROM sqlite_master WHERE type = 'table' " +
                    "AND name IN ('ObservacionEmision', 'DescarteEmision') ORDER BY name"));
            Assert.NotEqual(esquemaFacturas, Esquema(cadena));

            // Down (solo desarrollo): sin las tablas nuevas ni las columnas, y EmisionPendiente con su trigger, igual
            // que antes.
            migrador.Migrate(facturas);
            Assert.Equal(esquemaFacturas, Esquema(cadena));
            Assert.Equal(ColumnasFeat007, Columnas(cadena, "EmisionPendiente"));

            migrador.Migrate();
            Assert.Empty(contexto.Database.GetPendingMigrations());
        }
        finally
        {
            BaseTemporal.BorrarDirectorio(directorio);
        }
    }

    private static (string Facturas, string Recuperacion) Migraciones(OpticaDbContext contexto)
    {
        var migraciones = contexto.Database.GetMigrations().ToList();
        return (
            Assert.Single(migraciones, m => m.EndsWith(SufijoFacturas, StringComparison.Ordinal)),
            Assert.Single(migraciones, m => m.EndsWith(SufijoRecuperacion, StringComparison.Ordinal)));
    }

    private static List<string> Columnas(string cadena, string tabla) =>
        FacturasPrueba.Filas(cadena, $"SELECT name FROM pragma_table_info('{tabla}') ORDER BY cid")
            .Select(fila => fila[0]).ToList();

    private static string SqlTabla(string cadena, string tabla) =>
        FacturasPrueba.Filas(cadena, $"SELECT sql FROM sqlite_master WHERE type = 'table' AND name = '{tabla}'")
            .Single()[0];

    /// <summary>Todo el esquema menos la historia de migraciones.</summary>
    private static List<string[]> Esquema(string cadena) =>
        FacturasPrueba.Filas(cadena,
            "SELECT type, name, tbl_name, IFNULL(sql, '') FROM sqlite_master " +
            "WHERE name <> '__EFMigrationsHistory' AND name NOT LIKE 'sqlite_autoindex_%' ORDER BY type, name");
}
