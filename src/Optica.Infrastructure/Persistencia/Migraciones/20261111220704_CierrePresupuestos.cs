using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Optica.Infrastructure.Persistencia.Migraciones
{
    /// <summary>
    /// FEAT-003c: la columna <c>Version</c> de <c>Presupuesto</c>, con su CHECK, y los triggers de cierre que impiden
    /// modificar o borrar un presupuesto en Final, tocar sus líneas y cambiar la fecha o el número (ADR-017).
    /// </summary>
    /// <remarks>
    /// Escrita a mano (ADR-012, D-04). Lo que genera EF Core para el CHECK (<c>AddCheckConstraint</c>) no tiene equivalente
    /// en SQLite y obliga a reconstruir la tabla; con <c>ALTER TABLE ADD COLUMN</c> y el CHECK como restricción de la
    /// columna, la tabla no se reconstruye y las filas existentes toman versión 0. Los triggers se crean con <c>Sql()</c>:
    /// una reconstrucción futura de <c>Presupuesto</c> o <c>LineaPresupuesto</c> los borraría (advertencia en
    /// <see cref="OpticaDbContext"/>). El modelo y el snapshot declaran el mismo CHECK, por nombre y expresión.
    /// Un trigger que protege un registro cerrado aborta con <c>RAISE(ABORT, 'registro_cerrado')</c>, que
    /// <see cref="UnidadDeTrabajoEf"/> traduce a un conflicto; el de la fecha y el número usa otro texto y se propaga
    /// como error.
    /// </remarks>
    public partial class CierrePresupuestos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // El DEFAULT vive solo acá, para las filas que ya estaban; el repositorio asigna siempre la versión.
            migrationBuilder.Sql(
                "ALTER TABLE \"Presupuesto\" ADD COLUMN \"Version\" INTEGER NOT NULL DEFAULT 0 " +
                "CONSTRAINT \"CK_Presupuesto_Version\" CHECK (\"Version\" BETWEEN 0 AND 9007199254740991);");

            // Sin OF: cubre todas las columnas, incluido el paso de Final a Borrador (FR-05, FR-06).
            migrationBuilder.Sql(
                "CREATE TRIGGER \"TR_Presupuesto_FinalInmutable\" BEFORE UPDATE ON \"Presupuesto\" " +
                "FOR EACH ROW WHEN OLD.\"Estado\" = 'final' " +
                "BEGIN SELECT RAISE(ABORT, 'registro_cerrado'); END;");

            migrationBuilder.Sql(
                "CREATE TRIGGER \"TR_Presupuesto_FinalNoSeBorra\" BEFORE DELETE ON \"Presupuesto\" " +
                "FOR EACH ROW WHEN OLD.\"Estado\" = 'final' " +
                "BEGIN SELECT RAISE(ABORT, 'registro_cerrado'); END;");

            // IS NOT: reescribir el mismo valor no dispara (FR-01, D-06).
            migrationBuilder.Sql(
                "CREATE TRIGGER \"TR_Presupuesto_FechaYNumeroFijos\" BEFORE UPDATE ON \"Presupuesto\" " +
                "FOR EACH ROW WHEN NEW.\"Fecha\" IS NOT OLD.\"Fecha\" OR NEW.\"Numero\" IS NOT OLD.\"Numero\" " +
                "BEGIN SELECT RAISE(ABORT, 'presupuesto_fecha_numero'); END;");

            migrationBuilder.Sql(
                "CREATE TRIGGER \"TR_LineaPresupuesto_FinalInsert\" BEFORE INSERT ON \"LineaPresupuesto\" " +
                "FOR EACH ROW WHEN EXISTS (SELECT 1 FROM \"Presupuesto\" " +
                "WHERE \"Numero\" = NEW.\"PresupuestoNumero\" AND \"Estado\" = 'final') " +
                "BEGIN SELECT RAISE(ABORT, 'registro_cerrado'); END;");

            // El presupuesto de antes y el de después: una línea no entra ni sale de un Final.
            migrationBuilder.Sql(
                "CREATE TRIGGER \"TR_LineaPresupuesto_FinalUpdate\" BEFORE UPDATE ON \"LineaPresupuesto\" " +
                "FOR EACH ROW WHEN EXISTS (SELECT 1 FROM \"Presupuesto\" " +
                "WHERE \"Numero\" IN (OLD.\"PresupuestoNumero\", NEW.\"PresupuestoNumero\") AND \"Estado\" = 'final') " +
                "BEGIN SELECT RAISE(ABORT, 'registro_cerrado'); END;");

            migrationBuilder.Sql(
                "CREATE TRIGGER \"TR_LineaPresupuesto_FinalDelete\" BEFORE DELETE ON \"LineaPresupuesto\" " +
                "FOR EACH ROW WHEN EXISTS (SELECT 1 FROM \"Presupuesto\" " +
                "WHERE \"Numero\" = OLD.\"PresupuestoNumero\" AND \"Estado\" = 'final') " +
                "BEGIN SELECT RAISE(ABORT, 'registro_cerrado'); END;");
        }

        /// <inheritdoc />
        /// <remarks>
        /// Solo para desarrollo: deja sin protección a los presupuestos en Final y pierde su versión. No se aplica sobre la
        /// base de la óptica (AGENTS.md: los triggers de CierrePresupuestos no se borran); antes de migrar una base con
        /// datos reales vale el backup de RNF-02 (<c>VACUUM INTO</c>).
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Primero los triggers: SQLite rechaza DROP COLUMN de una columna que el esquema todavía usa.
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS \"TR_LineaPresupuesto_FinalDelete\";");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS \"TR_LineaPresupuesto_FinalUpdate\";");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS \"TR_LineaPresupuesto_FinalInsert\";");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS \"TR_Presupuesto_FechaYNumeroFijos\";");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS \"TR_Presupuesto_FinalNoSeBorra\";");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS \"TR_Presupuesto_FinalInmutable\";");

            // DROP COLUMN admite borrar una columna con su propio CHECK: no reconstruye la tabla.
            migrationBuilder.Sql("ALTER TABLE \"Presupuesto\" DROP COLUMN \"Version\";");
        }
    }
}
