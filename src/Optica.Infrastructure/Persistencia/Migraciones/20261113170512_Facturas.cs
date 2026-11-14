using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Optica.Infrastructure.Persistencia.Migraciones
{
    /// <summary>
    /// FEAT-007: las tablas <c>Factura</c>, <c>LineaFactura</c> y <c>EmisionPendiente</c>, con sus CHECK, los índices únicos
    /// <c>IX_Factura_PresupuestoNumero</c> (FR-03) e <c>IX_Factura_Comprobante</c>, los triggers que impiden modificar o
    /// borrar una factura y sus líneas, o agregarle una línea de más (FR-25, D-34), y el que impide modificar una
    /// emisión pendiente.
    /// </summary>
    /// <remarks>
    /// Escrita a mano (ADR-012), como <see cref="CierrePresupuestos"/>: solo <c>CreateTable</c> y <c>CreateIndex</c> de
    /// tablas nuevas y los triggers con <c>Sql()</c>; ninguna tabla existente se reconstruye. Los triggers abortan con
    /// <c>RAISE(ABORT, 'registro_cerrado')</c>, que <see cref="UnidadDeTrabajoEf"/> traduce a un conflicto
    /// (ADR-017). Una reconstrucción futura de <c>Factura</c> o <c>LineaFactura</c> los borraría (advertencia en
    /// <see cref="OpticaDbContext"/>). El modelo y el snapshot declaran los mismos CHECK, por nombre y expresión.
    /// </remarks>
    public partial class Facturas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Factura",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PresupuestoNumero = table.Column<long>(type: "INTEGER", nullable: false),
                    Tipo = table.Column<string>(type: "TEXT", nullable: false),
                    PuntoVenta = table.Column<long>(type: "INTEGER", nullable: false),
                    Numero = table.Column<long>(type: "INTEGER", nullable: false),
                    Fecha = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Concepto = table.Column<string>(type: "TEXT", nullable: false),
                    ReceptorDni = table.Column<string>(type: "TEXT", nullable: true),
                    TotalCentavos = table.Column<long>(type: "INTEGER", nullable: false),
                    NetoCentavos = table.Column<long>(type: "INTEGER", nullable: false),
                    IvaCentavos = table.Column<long>(type: "INTEGER", nullable: false),
                    AlicuotaCentesimas = table.Column<long>(type: "INTEGER", nullable: true),
                    Cae = table.Column<string>(type: "TEXT", nullable: false),
                    VencimientoCae = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Entorno = table.Column<string>(type: "TEXT", nullable: false),
                    CantidadLineas = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Factura", x => x.Id);
                    table.CheckConstraint("CK_Factura_Alicuota", "(\"Tipo\" = 'b' AND \"AlicuotaCentesimas\" BETWEEN 0 AND 10000) OR (\"Tipo\" = 'c' AND \"AlicuotaCentesimas\" IS NULL AND \"IvaCentavos\" = 0)");
                    table.CheckConstraint("CK_Factura_Cae", "length(\"Cae\") = 14 AND \"Cae\" NOT GLOB '*[^0-9]*'");
                    table.CheckConstraint("CK_Factura_CantidadLineas", "\"CantidadLineas\" >= 1");
                    table.CheckConstraint("CK_Factura_Concepto", "\"Concepto\" IN ('productos', 'productos_y_servicios')");
                    table.CheckConstraint("CK_Factura_Entorno", "\"Entorno\" IN ('simulado', 'homologacion', 'produccion')");
                    table.CheckConstraint("CK_Factura_Fecha", "\"Fecha\" GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]'");
                    table.CheckConstraint("CK_Factura_Importes", "\"NetoCentavos\" + \"IvaCentavos\" = \"TotalCentavos\"");
                    table.CheckConstraint("CK_Factura_IvaCentavos", "\"IvaCentavos\" >= 0");
                    table.CheckConstraint("CK_Factura_NetoCentavos", "\"NetoCentavos\" >= 0");
                    table.CheckConstraint("CK_Factura_Numero", "\"Numero\" BETWEEN 1 AND 99999999");
                    table.CheckConstraint("CK_Factura_PuntoVenta", "\"PuntoVenta\" BETWEEN 1 AND 99999");
                    table.CheckConstraint("CK_Factura_ReceptorDni", "\"ReceptorDni\" IS NULL OR (length(\"ReceptorDni\") BETWEEN 7 AND 8 AND \"ReceptorDni\" NOT GLOB '*[^0-9]*')");
                    table.CheckConstraint("CK_Factura_Tipo", "\"Tipo\" IN ('b', 'c')");
                    table.CheckConstraint("CK_Factura_TotalCentavos", "\"TotalCentavos\" >= 0");
                    table.CheckConstraint("CK_Factura_VencimientoCae", "\"VencimientoCae\" GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]' AND \"VencimientoCae\" >= \"Fecha\"");
                    table.ForeignKey(
                        name: "FK_Factura_Presupuesto_PresupuestoNumero",
                        column: x => x.PresupuestoNumero,
                        principalTable: "Presupuesto",
                        principalColumn: "Numero",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LineaFactura",
                columns: table => new
                {
                    FacturaId = table.Column<long>(type: "INTEGER", nullable: false),
                    Orden = table.Column<long>(type: "INTEGER", nullable: false),
                    CodigoArticulo = table.Column<long>(type: "INTEGER", nullable: false),
                    Descripcion = table.Column<string>(type: "TEXT", nullable: false),
                    PrecioUnitarioCentavos = table.Column<long>(type: "INTEGER", nullable: false),
                    PrecioConDescuentoCentavos = table.Column<long>(type: "INTEGER", nullable: false),
                    PrecioFinalCentavos = table.Column<long>(type: "INTEGER", nullable: false),
                    Cantidad = table.Column<long>(type: "INTEGER", nullable: false),
                    DescuentoCentesimos = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LineaFactura", x => new { x.FacturaId, x.Orden });
                    table.CheckConstraint("CK_LineaFactura_Cantidad", "\"Cantidad\" >= 1");
                    table.CheckConstraint("CK_LineaFactura_Descripcion", "length(\"Descripcion\") BETWEEN 1 AND 200");
                    table.CheckConstraint("CK_LineaFactura_DescuentoCentesimos", "\"DescuentoCentesimos\" BETWEEN 0 AND 10000");
                    table.CheckConstraint("CK_LineaFactura_Orden", "\"Orden\" >= 1");
                    table.CheckConstraint("CK_LineaFactura_PrecioConDescuentoCentavos", "\"PrecioConDescuentoCentavos\" >= 0");
                    table.CheckConstraint("CK_LineaFactura_PrecioFinalCentavos", "\"PrecioFinalCentavos\" >= 0");
                    table.CheckConstraint("CK_LineaFactura_PrecioUnitarioCentavos", "\"PrecioUnitarioCentavos\" >= 0");
                    table.ForeignKey(
                        name: "FK_LineaFactura_Factura_FacturaId",
                        column: x => x.FacturaId,
                        principalTable: "Factura",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EmisionPendiente",
                columns: table => new
                {
                    PresupuestoNumero = table.Column<long>(type: "INTEGER", nullable: false),
                    Momento = table.Column<string>(type: "TEXT", nullable: false),
                    Tipo = table.Column<string>(type: "TEXT", nullable: false),
                    PuntoVenta = table.Column<long>(type: "INTEGER", nullable: false),
                    Fecha = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    TotalCentavos = table.Column<long>(type: "INTEGER", nullable: false),
                    Entorno = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmisionPendiente", x => x.PresupuestoNumero);
                    table.CheckConstraint("CK_EmisionPendiente_Entorno", "\"Entorno\" IN ('simulado', 'homologacion', 'produccion')");
                    table.CheckConstraint("CK_EmisionPendiente_Fecha", "\"Fecha\" GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]'");
                    table.CheckConstraint("CK_EmisionPendiente_PuntoVenta", "\"PuntoVenta\" BETWEEN 1 AND 99999");
                    table.CheckConstraint("CK_EmisionPendiente_Tipo", "\"Tipo\" IN ('b', 'c')");
                    table.CheckConstraint("CK_EmisionPendiente_TotalCentavos", "\"TotalCentavos\" >= 0");
                    table.ForeignKey(
                        name: "FK_EmisionPendiente_Presupuesto_PresupuestoNumero",
                        column: x => x.PresupuestoNumero,
                        principalTable: "Presupuesto",
                        principalColumn: "Numero",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Factura_Comprobante",
                table: "Factura",
                columns: new[] { "Entorno", "Tipo", "PuntoVenta", "Numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Factura_PresupuestoNumero",
                table: "Factura",
                column: "PresupuestoNumero",
                unique: true);

            // Sin OF ni WHEN: ninguna columna de una factura con CAE cambia, ni reescribiendo el mismo valor (FR-25).
            migrationBuilder.Sql(
                "CREATE TRIGGER \"TR_Factura_Inmutable\" BEFORE UPDATE ON \"Factura\" " +
                "FOR EACH ROW BEGIN SELECT RAISE(ABORT, 'registro_cerrado'); END;");

            migrationBuilder.Sql(
                "CREATE TRIGGER \"TR_Factura_NoSeBorra\" BEFORE DELETE ON \"Factura\" " +
                "FOR EACH ROW BEGIN SELECT RAISE(ABORT, 'registro_cerrado'); END;");

            migrationBuilder.Sql(
                "CREATE TRIGGER \"TR_LineaFactura_Inmutable\" BEFORE UPDATE ON \"LineaFactura\" " +
                "FOR EACH ROW BEGIN SELECT RAISE(ABORT, 'registro_cerrado'); END;");

            migrationBuilder.Sql(
                "CREATE TRIGGER \"TR_LineaFactura_NoSeBorra\" BEFORE DELETE ON \"LineaFactura\" " +
                "FOR EACH ROW BEGIN SELECT RAISE(ABORT, 'registro_cerrado'); END;");

            // Las líneas entran con la factura, en la misma confirmación; una factura que ya tiene sus CantidadLineas
            // líneas no recibe otra (D-34). Sin factura, la subconsulta es NULL, no dispara y rechaza la clave foránea.
            // Solo cierra al alcanzar CantidadLineas: una factura insertada incompleta por SQL directo (menos líneas que
            // las declaradas) admite líneas hasta completarlas, y mientras tanto la detecta la lectura estricta de
            // FacturaRepository, que exige tantas líneas como CantidadLineas y la informa como fila inválida (INFO-1).
            migrationBuilder.Sql(
                "CREATE TRIGGER \"TR_LineaFactura_Cerrada\" BEFORE INSERT ON \"LineaFactura\" " +
                "FOR EACH ROW WHEN (SELECT count(*) FROM \"LineaFactura\" WHERE \"FacturaId\" = NEW.\"FacturaId\") >= " +
                "(SELECT \"CantidadLineas\" FROM \"Factura\" WHERE \"Id\" = NEW.\"FacturaId\") " +
                "BEGIN SELECT RAISE(ABORT, 'registro_cerrado'); END;");

            // La emisión pendiente se agrega y se quita, no se modifica por UPDATE (D-09, ADR-021). El borrado queda
            // permitido: es como se resuelve una pendiente. Un INSERT OR REPLACE sobre la misma clave no es un UPDATE: con
            // recursive_triggers equivale a quitar y agregar, que también se admite.
            migrationBuilder.Sql(
                "CREATE TRIGGER \"TR_EmisionPendiente_Inmutable\" BEFORE UPDATE ON \"EmisionPendiente\" " +
                "FOR EACH ROW BEGIN SELECT RAISE(ABORT, 'registro_cerrado'); END;");
        }

        /// <inheritdoc />
        /// <remarks>
        /// Solo para desarrollo: pierde las facturas y las emisiones pendientes y deja de proteger las facturas. No se
        /// aplica sobre la base de la óptica (AGENTS.md: los triggers que protegen registros cerrados no se borran); antes
        /// de migrar una base con datos reales vale el backup de RNF-02 (<c>VACUUM INTO</c>).
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS \"TR_EmisionPendiente_Inmutable\";");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS \"TR_LineaFactura_Cerrada\";");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS \"TR_LineaFactura_NoSeBorra\";");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS \"TR_LineaFactura_Inmutable\";");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS \"TR_Factura_NoSeBorra\";");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS \"TR_Factura_Inmutable\";");

            migrationBuilder.DropIndex(name: "IX_Factura_Comprobante", table: "Factura");
            migrationBuilder.DropIndex(name: "IX_Factura_PresupuestoNumero", table: "Factura");

            migrationBuilder.DropTable(name: "EmisionPendiente");
            migrationBuilder.DropTable(name: "LineaFactura");
            migrationBuilder.DropTable(name: "Factura");
        }
    }
}
