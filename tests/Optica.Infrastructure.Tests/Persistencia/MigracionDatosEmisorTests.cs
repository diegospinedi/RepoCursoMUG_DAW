using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Optica.Infrastructure.Persistencia;

namespace Optica.Infrastructure.Tests.Persistencia;

/// <summary>
/// La migración <c>DatosEmisor</c> (Block 1 de FEAT-009, ADR-028, R-07): una tabla nueva escrita a mano (ADR-012), sin
/// tocar <c>Factura</c>, <c>LineaFactura</c>, <c>Presupuesto</c> ni ningún trigger; el <c>Down</c> la borra y deja el
/// esquema de FEAT-008. Datos ficticios.
/// </summary>
public class MigracionDatosEmisorTests
{
    private const string SufijoRecuperacion = "_RecuperacionEmisiones";
    private const string SufijoDatosEmisor = "_DatosEmisor";

    /// <summary>Los triggers que FEAT-008 dejó en la base: ninguno puede faltar ni cambiar.</summary>
    private static readonly string[] TriggersEsperados =
    [
        "TR_DescarteEmision_SinBorrar",
        "TR_DescarteEmision_SinModificar",
        "TR_EmisionPendiente_Inmutable",
        "TR_ObservacionEmision_Inmutable",
    ];

    [Fact]
    public void Migracion_DatosEmisor_ConservaTodosLosTriggers()
    {
        EnBaseVacia((contexto, migrador, cadena) =>
        {
            var (recuperacion, datosEmisor) = Migraciones(contexto);

            // Por nombre (D-29 de FEAT-007): va justo después de RecuperacionEmisiones.
            var todas = contexto.Database.GetMigrations().ToList();
            Assert.Equal(todas.IndexOf(recuperacion) + 1, todas.IndexOf(datosEmisor));

            // Una base como la que dejó FEAT-008, con todos sus triggers.
            migrador.Migrate(recuperacion);
            var triggersAntes = Triggers(cadena);
            Assert.All(TriggersEsperados, nombre => Assert.Contains(triggersAntes, t => t[0] == nombre));
            Assert.Contains(triggersAntes, t => t[1] == "Presupuesto");
            Assert.Contains(triggersAntes, t => t[1] == "Factura");
            Assert.Contains(triggersAntes, t => t[1] == "LineaFactura");
            var tablasFacturacion = Tablas(cadena, "'Factura', 'LineaFactura', 'Presupuesto'");

            // Up: crea la tabla, y el conjunto completo de triggers (nombre, tabla y SQL) es el mismo. Hasta DatosEmisor y
            // no hasta la última: las migraciones posteriores (Backup, FEAT-010) agregan sus propios triggers.
            migrador.Migrate(datosEmisor);
            Assert.Equal(datosEmisor, contexto.Database.GetAppliedMigrations().Last());
            Assert.False(contexto.Database.HasPendingModelChanges());
            Assert.Single(Tablas(cadena, "'DatosEmisor'"));
            Assert.Equal(triggersAntes, Triggers(cadena));
            Assert.Equal(tablasFacturacion, Tablas(cadena, "'Factura', 'LineaFactura', 'Presupuesto'"));

            // Down: sigue igual.
            migrador.Migrate(recuperacion);
            Assert.Equal(triggersAntes, Triggers(cadena));
            Assert.Equal(tablasFacturacion, Tablas(cadena, "'Factura', 'LineaFactura', 'Presupuesto'"));
        });
    }

    [Fact]
    public void Migracion_Down_Revierte()
    {
        EnBaseVacia((contexto, migrador, cadena) =>
        {
            var (recuperacion, datosEmisor) = Migraciones(contexto);

            // ADR-012: el Up solo crea la tabla; el Down solo la borra. Ninguna otra tabla se reconstruye.
            var ensamblado = contexto.GetService<IMigrationsAssembly>();
            var migracion = ensamblado.CreateMigration(ensamblado.Migrations[datosEmisor], contexto.Database.ProviderName!);
            var creada = Assert.IsType<CreateTableOperation>(Assert.Single(migracion.UpOperations));
            Assert.Equal("DatosEmisor", creada.Name);
            Assert.Equal(
                [
                    "CK_DatosEmisor_Cuit", "CK_DatosEmisor_DomicilioComercial", "CK_DatosEmisor_Id",
                    "CK_DatosEmisor_IngresosBrutos", "CK_DatosEmisor_InicioActividades", "CK_DatosEmisor_RazonSocial",
                ],
                creada.CheckConstraints.Select(c => c.Name).Order(StringComparer.Ordinal));
            var borrada = Assert.IsType<DropTableOperation>(Assert.Single(migracion.DownOperations));
            Assert.Equal("DatosEmisor", borrada.Name);

            // El esquema de FEAT-008, antes de la migración.
            migrador.Migrate(recuperacion);
            var esquemaFeat008 = Esquema(cadena);

            migrador.Migrate();
            Assert.NotEqual(esquemaFeat008, Esquema(cadena));
            FacturasPrueba.Ejecutar(cadena,
                "INSERT INTO DatosEmisor (Id, RazonSocial, DomicilioComercial, Cuit, IngresosBrutos, InicioActividades) " +
                "VALUES (1, 'Óptica Ficticia', 'Calle Inventada 123', '20000000001', '901-123456-7', '2020-03-01')");

            // Down (solo desarrollo): sin la tabla ni sus datos, y el esquema igual al de FEAT-008.
            migrador.Migrate(recuperacion);
            Assert.Equal(esquemaFeat008, Esquema(cadena));
            Assert.Empty(Tablas(cadena, "'DatosEmisor'"));

            // Y se vuelve a aplicar, con la tabla vacía.
            migrador.Migrate();
            Assert.Empty(contexto.Database.GetPendingMigrations());
            Assert.Empty(FacturasPrueba.Filas(cadena, "SELECT Id FROM DatosEmisor"));
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

    private static (string Recuperacion, string DatosEmisor) Migraciones(OpticaDbContext contexto)
    {
        var migraciones = contexto.Database.GetMigrations().ToList();
        return (
            Assert.Single(migraciones, m => m.EndsWith(SufijoRecuperacion, StringComparison.Ordinal)),
            Assert.Single(migraciones, m => m.EndsWith(SufijoDatosEmisor, StringComparison.Ordinal)));
    }

    /// <summary>Todos los triggers de la base: nombre, tabla y SQL.</summary>
    private static List<string[]> Triggers(string cadena) =>
        FacturasPrueba.Filas(cadena,
            "SELECT name, tbl_name, sql FROM sqlite_master WHERE type = 'trigger' ORDER BY name");

    private static List<string[]> Tablas(string cadena, string nombres) =>
        FacturasPrueba.Filas(cadena,
            $"SELECT name, sql FROM sqlite_master WHERE type = 'table' AND name IN ({nombres}) ORDER BY name");

    /// <summary>Todo el esquema menos la historia de migraciones.</summary>
    private static List<string[]> Esquema(string cadena) =>
        FacturasPrueba.Filas(cadena,
            "SELECT type, name, tbl_name, IFNULL(sql, '') FROM sqlite_master " +
            "WHERE name <> '__EFMigrationsHistory' AND name NOT LIKE 'sqlite_autoindex_%' ORDER BY type, name");
}
