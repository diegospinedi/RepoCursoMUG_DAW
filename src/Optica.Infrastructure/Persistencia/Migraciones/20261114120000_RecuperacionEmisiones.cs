using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Optica.Infrastructure.Persistencia.Migraciones
{
    /// <summary>
    /// FEAT-008: las columnas de <c>EmisionPendiente</c> para recuperar una emisión (número pedido, concepto, neto, IVA,
    /// alícuota y si el receptor se identificó; FR-01, D-15) y las tablas <c>ObservacionEmision</c> y
    /// <c>DescarteEmision</c> con sus triggers (D-12, ADR-026).
    /// </summary>
    /// <remarks>
    /// Escrita a mano (ADR-012), como <see cref="ConfiguracionPuntoVenta"/> y <see cref="Facturas"/>. Las columnas nuevas
    /// van con <c>ALTER TABLE ADD COLUMN</c> y un CHECK de columna que nombra solo su columna: <c>EmisionPendiente</c> no se
    /// reconstruye y conserva <c>TR_EmisionPendiente_Inmutable</c>; las filas de FEAT-007 quedan con las columnas nuevas
    /// nulas (FR-14). Las tablas nuevas, con <c>CreateTable</c>; sus triggers son una sola sentencia que aborta con
    /// <c>RAISE(ABORT, 'registro_cerrado')</c>, que <see cref="UnidadDeTrabajoEf"/> traduce a un conflicto (ADR-017). El
    /// modelo y el snapshot declaran los mismos CHECK, por nombre y expresión. Antes de migrar la base de la óptica, backup
    /// con <c>VACUUM INTO</c> (AGENTS.md).
    /// </remarks>
    public partial class RecuperacionEmisiones : Migration
    {
        /// <summary>Las seis columnas nuevas de <c>EmisionPendiente</c>, en orden, con su CHECK.</summary>
        private static readonly (string Nombre, string Definicion)[] ColumnasPendiente =
        [
            ("Numero",
                "\"Numero\" INTEGER NULL CONSTRAINT \"CK_EmisionPendiente_Numero\" CHECK " +
                "(\"Numero\" IS NULL OR \"Numero\" BETWEEN 1 AND 99999999)"),
            ("Concepto",
                "\"Concepto\" TEXT NULL CONSTRAINT \"CK_EmisionPendiente_Concepto\" CHECK " +
                "(\"Concepto\" IS NULL OR \"Concepto\" IN ('productos', 'productos_y_servicios'))"),
            ("NetoCentavos",
                "\"NetoCentavos\" INTEGER NULL CONSTRAINT \"CK_EmisionPendiente_NetoCentavos\" CHECK " +
                "(\"NetoCentavos\" IS NULL OR \"NetoCentavos\" >= 0)"),
            ("IvaCentavos",
                "\"IvaCentavos\" INTEGER NULL CONSTRAINT \"CK_EmisionPendiente_IvaCentavos\" CHECK " +
                "(\"IvaCentavos\" IS NULL OR \"IvaCentavos\" >= 0)"),
            // Decimal invariante ("21", "10.5"); null en la Factura C. Sin CHECK: lo valida la lectura del repositorio.
            ("Alicuota", "\"Alicuota\" TEXT NULL"),
            ("ReceptorIdentificado",
                "\"ReceptorIdentificado\" INTEGER NULL CONSTRAINT \"CK_EmisionPendiente_ReceptorIdentificado\" CHECK " +
                "(\"ReceptorIdentificado\" IS NULL OR \"ReceptorIdentificado\" IN (0, 1))"),
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var (_, definicion) in ColumnasPendiente)
            {
                migrationBuilder.Sql($"ALTER TABLE \"EmisionPendiente\" ADD COLUMN {definicion};");
            }

            migrationBuilder.CreateTable(
                name: "ObservacionEmision",
                columns: table => new
                {
                    PresupuestoNumero = table.Column<long>(type: "INTEGER", nullable: false),
                    Motivo = table.Column<string>(type: "TEXT", nullable: false),
                    Numero = table.Column<long>(type: "INTEGER", nullable: true),
                    TotalCentavos = table.Column<long>(type: "INTEGER", nullable: false),
                    TotalArcaCentavos = table.Column<long>(type: "INTEGER", nullable: true),
                    Entorno = table.Column<string>(type: "TEXT", nullable: false),
                    Momento = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ObservacionEmision", x => x.PresupuestoNumero);
                    table.CheckConstraint("CK_ObservacionEmision_Entorno", "\"Entorno\" IN ('simulado', 'homologacion', 'produccion')");
                    table.CheckConstraint("CK_ObservacionEmision_ImporteDistinto", "\"Motivo\" <> 'importe_distinto' OR (\"Numero\" IS NOT NULL AND \"TotalArcaCentavos\" IS NOT NULL)");
                    table.CheckConstraint("CK_ObservacionEmision_Motivo", "\"Motivo\" IN ('importe_distinto', 'sin_numero', 'otro_entorno')");
                    table.CheckConstraint("CK_ObservacionEmision_Numero", "\"Numero\" IS NULL OR \"Numero\" BETWEEN 1 AND 99999999");
                    table.CheckConstraint("CK_ObservacionEmision_TotalArcaCentavos", "\"TotalArcaCentavos\" IS NULL OR \"TotalArcaCentavos\" >= 0");
                    table.CheckConstraint("CK_ObservacionEmision_TotalCentavos", "\"TotalCentavos\" >= 0");
                    table.ForeignKey(
                        name: "FK_ObservacionEmision_EmisionPendiente_PresupuestoNumero",
                        column: x => x.PresupuestoNumero,
                        principalTable: "EmisionPendiente",
                        principalColumn: "PresupuestoNumero",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DescarteEmision",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Momento = table.Column<string>(type: "TEXT", nullable: false),
                    PresupuestoNumero = table.Column<long>(type: "INTEGER", nullable: false),
                    Tipo = table.Column<string>(type: "TEXT", nullable: false),
                    PuntoVenta = table.Column<long>(type: "INTEGER", nullable: false),
                    Numero = table.Column<long>(type: "INTEGER", nullable: true),
                    TotalCentavos = table.Column<long>(type: "INTEGER", nullable: false),
                    TotalArcaCentavos = table.Column<long>(type: "INTEGER", nullable: true),
                    Motivo = table.Column<string>(type: "TEXT", nullable: false),
                    Entorno = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DescarteEmision", x => x.Id);
                    table.CheckConstraint("CK_DescarteEmision_Entorno", "\"Entorno\" IN ('simulado', 'homologacion', 'produccion')");
                    table.CheckConstraint("CK_DescarteEmision_ImporteDistinto", "\"Motivo\" <> 'importe_distinto' OR (\"Numero\" IS NOT NULL AND \"TotalArcaCentavos\" IS NOT NULL)");
                    table.CheckConstraint("CK_DescarteEmision_Motivo", "\"Motivo\" IN ('importe_distinto', 'sin_numero', 'otro_entorno')");
                    table.CheckConstraint("CK_DescarteEmision_Numero", "\"Numero\" IS NULL OR \"Numero\" BETWEEN 1 AND 99999999");
                    table.CheckConstraint("CK_DescarteEmision_PuntoVenta", "\"PuntoVenta\" BETWEEN 1 AND 99999");
                    table.CheckConstraint("CK_DescarteEmision_Tipo", "\"Tipo\" IN ('b', 'c')");
                    table.CheckConstraint("CK_DescarteEmision_TotalArcaCentavos", "\"TotalArcaCentavos\" IS NULL OR \"TotalArcaCentavos\" >= 0");
                    table.CheckConstraint("CK_DescarteEmision_TotalCentavos", "\"TotalCentavos\" >= 0");
                    table.ForeignKey(
                        name: "FK_DescarteEmision_Presupuesto_PresupuestoNumero",
                        column: x => x.PresupuestoNumero,
                        principalTable: "Presupuesto",
                        principalColumn: "Numero",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DescarteEmision_PresupuestoNumero",
                table: "DescarteEmision",
                column: "PresupuestoNumero");

            // D-12: la observación se agrega y se quita, no se modifica (ni reescribiendo el mismo valor).
            migrationBuilder.Sql(
                "CREATE TRIGGER \"TR_ObservacionEmision_Inmutable\" BEFORE UPDATE ON \"ObservacionEmision\" " +
                "FOR EACH ROW BEGIN SELECT RAISE(ABORT, 'registro_cerrado'); END;");

            // FR-10, ADR-026: el registro de descartes es de solo agregar. Con recursive_triggers (FabricaConexion), un
            // INSERT OR REPLACE sobre un descarte existente también dispara el de borrado.
            migrationBuilder.Sql(
                "CREATE TRIGGER \"TR_DescarteEmision_SinModificar\" BEFORE UPDATE ON \"DescarteEmision\" " +
                "FOR EACH ROW BEGIN SELECT RAISE(ABORT, 'registro_cerrado'); END;");

            migrationBuilder.Sql(
                "CREATE TRIGGER \"TR_DescarteEmision_SinBorrar\" BEFORE DELETE ON \"DescarteEmision\" " +
                "FOR EACH ROW BEGIN SELECT RAISE(ABORT, 'registro_cerrado'); END;");
        }

        /// <inheritdoc />
        /// <remarks>
        /// Solo para desarrollo: pierde las observaciones, el registro de descartes y el número y los datos de recuperación
        /// de las pendientes, que vuelven a la forma de FEAT-007 (que no los usa). <c>DROP TABLE</c> se lleva los triggers
        /// de cada tabla; <c>DROP COLUMN</c> admite borrar una columna con su propio CHECK sin reconstruir
        /// <c>EmisionPendiente</c>, que conserva <c>TR_EmisionPendiente_Inmutable</c>. No se aplica sobre la base de la
        /// óptica sin el backup de RNF-02 (<c>VACUUM INTO</c>).
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ObservacionEmision");
            migrationBuilder.DropTable(name: "DescarteEmision");

            for (var i = ColumnasPendiente.Length - 1; i >= 0; i--)
            {
                migrationBuilder.Sql($"ALTER TABLE \"EmisionPendiente\" DROP COLUMN \"{ColumnasPendiente[i].Nombre}\";");
            }
        }
    }
}
