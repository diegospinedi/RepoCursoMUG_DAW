using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Optica.Infrastructure.Persistencia.Migraciones
{
    /// <summary>
    /// FEAT-010: la tabla <c>ConfiguracionBackup</c>, fila única con <c>Id = 1</c> para la ruta de la carpeta de backup
    /// (FR-01), y la tabla <c>BackupRealizado</c>, el historial de solo agregar de los backups verificados (D-08,
    /// ADR-029), con sus triggers. No inserta filas: hasta que la operadora elige la ubicación no hay backup.
    /// </summary>
    /// <remarks>
    /// Escrita a mano (ADR-012), como <see cref="DatosEmisor"/> y <see cref="RecuperacionEmisiones"/>. Solo crea tablas,
    /// un índice y dos triggers: no toca <c>Factura</c>, <c>LineaFactura</c>, <c>Presupuesto</c> ni ningún trigger de
    /// registros cerrados. Los CHECK son la segunda barrera detrás del dominio; el modelo y el snapshot declaran los
    /// mismos, por nombre y expresión. Antes de migrar la base de la óptica, backup con <c>VACUUM INTO</c> (AGENTS.md).
    /// </remarks>
    public partial class Backup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConfiguracionBackup",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    Ruta = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfiguracionBackup", x => x.Id);
                    table.CheckConstraint("CK_ConfiguracionBackup_Id", "\"Id\" = 1");
                    table.CheckConstraint("CK_ConfiguracionBackup_Ruta", "length(\"Ruta\") BETWEEN 1 AND 260");
                });

            migrationBuilder.CreateTable(
                name: "BackupRealizado",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Fecha = table.Column<string>(type: "TEXT", nullable: false),
                    MomentoUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupRealizado", x => x.Id);
                    table.CheckConstraint("CK_BackupRealizado_Fecha", "\"Fecha\" GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]'");
                });

            // Más de una copia por día (la de las 20:00 después de la del arranque, D-20): índice no único.
            migrationBuilder.CreateIndex(
                name: "IX_BackupRealizado_Fecha",
                table: "BackupRealizado",
                column: "Fecha");

            // D-08, ADR-029: el historial es de solo agregar. Con recursive_triggers (FabricaConexion), un INSERT OR
            // REPLACE sobre una fila existente también dispara el de borrado.
            migrationBuilder.Sql(
                "CREATE TRIGGER \"TR_BackupRealizado_Inmutable\" BEFORE UPDATE ON \"BackupRealizado\" " +
                "FOR EACH ROW BEGIN SELECT RAISE(ABORT, 'registro_cerrado'); END;");

            migrationBuilder.Sql(
                "CREATE TRIGGER \"TR_BackupRealizado_NoSeBorra\" BEFORE DELETE ON \"BackupRealizado\" " +
                "FOR EACH ROW BEGIN SELECT RAISE(ABORT, 'registro_cerrado'); END;");
        }

        /// <inheritdoc />
        /// <remarks>
        /// Vuelve al esquema de FEAT-009: borra los dos triggers del historial y las dos tablas, que ninguna otra
        /// referencia (el <c>DELETE</c> implícito de <c>DROP TABLE</c> no dispara triggers). Se pierden la ubicación y el
        /// historial; las copias de la carpeta quedan. No se aplica sobre la base de la óptica sin el backup de RNF-02
        /// (<c>VACUUM INTO</c>).
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS \"TR_BackupRealizado_NoSeBorra\";");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS \"TR_BackupRealizado_Inmutable\";");
            migrationBuilder.DropTable(name: "BackupRealizado");
            migrationBuilder.DropTable(name: "ConfiguracionBackup");
        }
    }
}
