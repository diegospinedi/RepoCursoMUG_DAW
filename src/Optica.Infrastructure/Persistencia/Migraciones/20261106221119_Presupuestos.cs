using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Optica.Infrastructure.Persistencia.Migraciones
{
    /// <summary>
    /// FEAT-003a: las tablas <c>Presupuesto</c> y <c>LineaPresupuesto</c>, con sus CHECK y dos claves foráneas con
    /// <c>ON DELETE RESTRICT</c> (D-18).
    /// </summary>
    /// <remarks>
    /// Generada con <c>dotnet ef migrations add</c> y revisada (ADR-012): solo <c>CreateTable</c> y <c>CreateIndex</c> de
    /// tablas nuevas; <c>Articulo</c> no se reconstruye y su secuencia de AUTOINCREMENT queda intacta. Sin triggers: los de
    /// inmutabilidad llegan con FEAT-003c. El Down borra las líneas y después los presupuestos; revertir pierde los
    /// presupuestos grabados.
    /// </remarks>
    public partial class Presupuestos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Presupuesto",
                columns: table => new
                {
                    Numero = table.Column<long>(type: "INTEGER", nullable: false),
                    Fecha = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Estado = table.Column<string>(type: "TEXT", nullable: false),
                    Apellido = table.Column<string>(type: "TEXT", nullable: false),
                    Nombre = table.Column<string>(type: "TEXT", nullable: false),
                    Dni = table.Column<string>(type: "TEXT", nullable: false),
                    Domicilio = table.Column<string>(type: "TEXT", nullable: true),
                    Email = table.Column<string>(type: "TEXT", nullable: true),
                    Telefono = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Presupuesto", x => x.Numero);
                    table.CheckConstraint("CK_Presupuesto_Apellido", "length(\"Apellido\") BETWEEN 1 AND 100");
                    table.CheckConstraint("CK_Presupuesto_Dni", "length(\"Dni\") BETWEEN 7 AND 8 AND \"Dni\" NOT GLOB '*[^0-9]*'");
                    table.CheckConstraint("CK_Presupuesto_Domicilio", "length(\"Domicilio\") <= 200");
                    table.CheckConstraint("CK_Presupuesto_Email", "length(\"Email\") <= 254");
                    table.CheckConstraint("CK_Presupuesto_Estado", "\"Estado\" IN ('borrador', 'final')");
                    table.CheckConstraint("CK_Presupuesto_Fecha", "\"Fecha\" GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]'");
                    table.CheckConstraint("CK_Presupuesto_Nombre", "length(\"Nombre\") BETWEEN 1 AND 100");
                    table.CheckConstraint("CK_Presupuesto_Numero", "\"Numero\" >= 1");
                    table.CheckConstraint("CK_Presupuesto_Telefono", "length(\"Telefono\") <= 30");
                });

            migrationBuilder.CreateTable(
                name: "LineaPresupuesto",
                columns: table => new
                {
                    PresupuestoNumero = table.Column<long>(type: "INTEGER", nullable: false),
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
                    table.PrimaryKey("PK_LineaPresupuesto", x => new { x.PresupuestoNumero, x.Orden });
                    table.CheckConstraint("CK_LineaPresupuesto_Cantidad", "\"Cantidad\" >= 1");
                    table.CheckConstraint("CK_LineaPresupuesto_Descripcion", "length(\"Descripcion\") BETWEEN 1 AND 200");
                    table.CheckConstraint("CK_LineaPresupuesto_DescuentoCentesimos", "\"DescuentoCentesimos\" BETWEEN 0 AND 10000");
                    table.CheckConstraint("CK_LineaPresupuesto_Orden", "\"Orden\" >= 1");
                    table.CheckConstraint("CK_LineaPresupuesto_PrecioConDescuentoCentavos", "\"PrecioConDescuentoCentavos\" >= 0");
                    table.CheckConstraint("CK_LineaPresupuesto_PrecioFinalCentavos", "\"PrecioFinalCentavos\" >= 0");
                    table.CheckConstraint("CK_LineaPresupuesto_PrecioUnitarioCentavos", "\"PrecioUnitarioCentavos\" >= 0");
                    table.ForeignKey(
                        name: "FK_LineaPresupuesto_Articulo_CodigoArticulo",
                        column: x => x.CodigoArticulo,
                        principalTable: "Articulo",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LineaPresupuesto_Presupuesto_PresupuestoNumero",
                        column: x => x.PresupuestoNumero,
                        principalTable: "Presupuesto",
                        principalColumn: "Numero",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LineaPresupuesto_CodigoArticulo",
                table: "LineaPresupuesto",
                column: "CodigoArticulo");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LineaPresupuesto");

            migrationBuilder.DropTable(
                name: "Presupuesto");
        }
    }
}
