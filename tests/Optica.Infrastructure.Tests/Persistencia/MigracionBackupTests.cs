using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Optica.Infrastructure.Persistencia;

namespace Optica.Infrastructure.Tests.Persistencia;

/// <summary>
/// La migración <c>Backup</c> (Block 1 de FEAT-010, ADR-029): dos tablas nuevas escritas a mano (ADR-012) y los triggers
/// de solo agregar del historial, sin tocar ninguna tabla ni ningún trigger existente; el <c>Down</c> las borra y deja el
/// esquema de FEAT-009. Datos ficticios.
/// </summary>
public class MigracionBackupTests
{
    private const string SufijoDatosEmisor = "_DatosEmisor";
    private const string SufijoBackup = "_Backup";

    /// <summary>Los triggers de registros cerrados que dejó FEAT-009 en la base: ninguno puede faltar ni cambiar.</summary>
    private static readonly string[] TriggersExistentes =
    [
        "TR_DescarteEmision_SinBorrar",
        "TR_DescarteEmision_SinModificar",
        "TR_EmisionPendiente_Inmutable",
        "TR_ObservacionEmision_Inmutable",
    ];

    private static readonly string[] TriggersNuevos = ["TR_BackupRealizado_Inmutable", "TR_BackupRealizado_NoSeBorra"];

    [Fact]
    public void Migracion_Backup_ConservaTodosLosTriggers()
    {
        EnBaseVacia((contexto, migrador, cadena) =>
        {
            var (datosEmisor, backup) = Migraciones(contexto);

            // Por nombre (D-29 de FEAT-007): va justo después de DatosEmisor.
            var todas = contexto.Database.GetMigrations().ToList();
            Assert.Equal(todas.IndexOf(datosEmisor) + 1, todas.IndexOf(backup));

            // Una base como la que dejó FEAT-009, con todos sus triggers y todas sus tablas.
            migrador.Migrate(datosEmisor);
            var triggersAntes = Triggers(cadena);
            Assert.All(TriggersExistentes, nombre => Assert.Contains(triggersAntes, t => t[0] == nombre));
            Assert.Contains(triggersAntes, t => t[1] == "Presupuesto");
            Assert.Contains(triggersAntes, t => t[1] == "Factura");
            Assert.Contains(triggersAntes, t => t[1] == "LineaFactura");
            var tablasAntes = TodasLasTablas(cadena);

            // Up: el conjunto completo de triggers (nombre, tabla y SQL) es el de antes más los dos del historial, y las
            // tablas de antes no cambian. Hasta Backup y no hasta la última: mide solo su propia migración.
            migrador.Migrate(backup);
            Assert.Equal(backup, contexto.Database.GetAppliedMigrations().Last());
            Assert.False(contexto.Database.HasPendingModelChanges());
            var triggersDespues = Triggers(cadena);
            Assert.Equal(TriggersNuevos, triggersDespues.Where(t => t[1] == "BackupRealizado").Select(t => t[0]));
            Assert.Equal(triggersAntes, triggersDespues.Where(t => t[1] != "BackupRealizado").ToList());
            Assert.Equal(
                tablasAntes,
                TodasLasTablas(cadena).Where(t => t[0] is not "ConfiguracionBackup" and not "BackupRealizado").ToList());
            Assert.Equal(2, TodasLasTablas(cadena).Count(t => t[0] is "ConfiguracionBackup" or "BackupRealizado"));

            // Down: vuelve exactamente al conjunto de antes.
            migrador.Migrate(datosEmisor);
            Assert.Equal(triggersAntes, Triggers(cadena));
            Assert.Equal(tablasAntes, TodasLasTablas(cadena));
        });
    }

    [Fact]
    public void Migracion_Backup_Down_Revierte()
    {
        EnBaseVacia((contexto, migrador, cadena) =>
        {
            var (datosEmisor, backup) = Migraciones(contexto);

            // ADR-012: el Up solo crea las dos tablas, el índice y los triggers; el Down solo los borra. Ninguna otra tabla
            // se reconstruye.
            var ensamblado = contexto.GetService<IMigrationsAssembly>();
            var migracion = ensamblado.CreateMigration(ensamblado.Migrations[backup], contexto.Database.ProviderName!);
            Assert.Equal(
                ["ConfiguracionBackup", "BackupRealizado"],
                migracion.UpOperations.OfType<CreateTableOperation>().Select(t => t.Name));
            Assert.All(migracion.UpOperations, operacion =>
                Assert.True(operacion is CreateTableOperation or CreateIndexOperation or SqlOperation, operacion.GetType().Name));
            Assert.Equal(
                ["BackupRealizado", "ConfiguracionBackup"],
                migracion.DownOperations.OfType<DropTableOperation>().Select(t => t.Name));
            Assert.All(migracion.DownOperations, operacion =>
                Assert.True(operacion is DropTableOperation or SqlOperation, operacion.GetType().Name));

            // El esquema de FEAT-009, antes de la migración.
            migrador.Migrate(datosEmisor);
            var esquemaFeat009 = Esquema(cadena);

            migrador.Migrate(backup);
            Assert.Equal(backup, contexto.Database.GetAppliedMigrations().Last());
            Assert.NotEqual(esquemaFeat009, Esquema(cadena));
            Assert.Single(FacturasPrueba.Filas(cadena,
                "SELECT name FROM sqlite_master WHERE type = 'index' AND name = 'IX_BackupRealizado_Fecha' " +
                "AND sql NOT LIKE '%UNIQUE%'"));
            FacturasPrueba.Ejecutar(cadena, "INSERT INTO ConfiguracionBackup (Id, Ruta) VALUES (1, '/media/pendrive/Backups')");
            FacturasPrueba.Ejecutar(cadena,
                "INSERT INTO BackupRealizado (Fecha, MomentoUtc) VALUES ('2026-10-29', '2026-10-29T23:00:00.0000000Z')");

            // Down (solo desarrollo): sin las tablas, sus datos ni sus triggers, y el esquema igual al de FEAT-009, aunque
            // el historial tenga filas que sus triggers no dejan borrar.
            migrador.Migrate(datosEmisor);
            Assert.Equal(esquemaFeat009, Esquema(cadena));
            Assert.Empty(FacturasPrueba.Filas(cadena,
                "SELECT name FROM sqlite_master WHERE tbl_name IN ('ConfiguracionBackup', 'BackupRealizado')"));

            // Y se vuelve a aplicar, con las tablas vacías.
            migrador.Migrate(backup);
            Assert.Equal(backup, contexto.Database.GetAppliedMigrations().Last());
            Assert.Empty(FacturasPrueba.Filas(cadena, "SELECT Id FROM ConfiguracionBackup"));
            Assert.Empty(FacturasPrueba.Filas(cadena, "SELECT Id FROM BackupRealizado"));
        });
    }

    private static void EnBaseVacia(Action<OpticaDbContext, IMigrator, string> prueba)
    {
        var directorio = BaseTemporal.CrearDirectorioTemporal();
        try
        {
            using var servicios = BaseTemporal.CrearServicios(directorio);
            var fabrica = servicios.GetRequiredService<FabricaConexion>();
            fabrica.PrepararDirectorio();
            using var alcance = servicios.CreateScope();
            var contexto = alcance.ServiceProvider.GetRequiredService<OpticaDbContext>();
            prueba(contexto, contexto.GetService<IMigrator>(), fabrica.CadenaConexion);
        }
        finally
        {
            BaseTemporal.BorrarDirectorio(directorio);
        }
    }

    private static (string DatosEmisor, string Backup) Migraciones(OpticaDbContext contexto)
    {
        var migraciones = contexto.Database.GetMigrations().ToList();
        return (
            Assert.Single(migraciones, m => m.EndsWith(SufijoDatosEmisor, StringComparison.Ordinal)),
            Assert.Single(migraciones, m => m.EndsWith(SufijoBackup, StringComparison.Ordinal)));
    }

    /// <summary>Todos los triggers de la base: nombre, tabla y SQL.</summary>
    private static List<string[]> Triggers(string cadena) =>
        FacturasPrueba.Filas(cadena,
            "SELECT name, tbl_name, sql FROM sqlite_master WHERE type = 'trigger' ORDER BY name");

    /// <summary>Todas las tablas menos la historia de migraciones: nombre y SQL.</summary>
    private static List<string[]> TodasLasTablas(string cadena) =>
        FacturasPrueba.Filas(cadena,
            "SELECT name, sql FROM sqlite_master WHERE type = 'table' AND name <> '__EFMigrationsHistory' " +
            "AND name NOT LIKE 'sqlite_%' ORDER BY name");

    /// <summary>Todo el esquema menos la historia de migraciones.</summary>
    private static List<string[]> Esquema(string cadena) =>
        FacturasPrueba.Filas(cadena,
            "SELECT type, name, tbl_name, IFNULL(sql, '') FROM sqlite_master " +
            "WHERE name <> '__EFMigrationsHistory' AND name NOT LIKE 'sqlite_autoindex_%' ORDER BY type, name");
}
