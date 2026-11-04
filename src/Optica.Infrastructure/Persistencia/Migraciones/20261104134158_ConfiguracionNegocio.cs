using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Optica.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class ConfiguracionNegocio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConfiguracionNegocio",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    AlicuotaIvaCentesimas = table.Column<long>(type: "INTEGER", nullable: false),
                    CondicionFiscal = table.Column<string>(type: "TEXT", nullable: false),
                    MultiploRedondeoCentavos = table.Column<long>(type: "INTEGER", nullable: false),
                    TopeIdentificacionCentavos = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfiguracionNegocio", x => x.Id);
                    table.CheckConstraint("CK_ConfiguracionNegocio_Alicuota", "\"AlicuotaIvaCentesimas\" BETWEEN 0 AND 10000");
                    table.CheckConstraint("CK_ConfiguracionNegocio_Condicion", "\"CondicionFiscal\" IN ('responsable_inscripto', 'monotributo')");
                    table.CheckConstraint("CK_ConfiguracionNegocio_Id", "\"Id\" = 1");
                    table.CheckConstraint("CK_ConfiguracionNegocio_Multiplo", "\"MultiploRedondeoCentavos\" >= 1");
                    table.CheckConstraint("CK_ConfiguracionNegocio_Tope", "\"TopeIdentificacionCentavos\" IS NULL OR \"TopeIdentificacionCentavos\" > 0");
                });

            // Valores iniciales (FR-07, FR-08): 21 %, Responsable Inscripto, múltiplo 0,01 y sin tope. Solo acá y no con
            // HasData en el modelo (ADR-008): se insertan una vez, también en las bases creadas con FEAT-001a y FEAT-001b,
            // y un cambio futuro del modelo no pisa lo que cargó la operadora.
            migrationBuilder.InsertData(
                table: "ConfiguracionNegocio",
                columns: new[] { "Id", "AlicuotaIvaCentesimas", "CondicionFiscal", "MultiploRedondeoCentavos", "TopeIdentificacionCentavos" },
                columnTypes: new[] { "INTEGER", "INTEGER", "TEXT", "INTEGER", "INTEGER" },
                values: new object[] { 1, 2100L, "responsable_inscripto", 1L, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConfiguracionNegocio");
        }
    }
}
