using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Optica.Infrastructure.Persistencia.Migraciones
{
    /// <summary>
    /// FEAT-009: la tabla <c>DatosEmisor</c> (ADR-028), fila única con <c>Id = 1</c> para la razón social, el domicilio
    /// comercial, el CUIT sin guiones, el número de Ingresos Brutos y la fecha de inicio de actividades (FR-01 a FR-03).
    /// No inserta fila: los datos quedan sin cargar hasta que la operadora los graba en la configuración.
    /// </summary>
    /// <remarks>
    /// Escrita a mano (ADR-012), como <see cref="ConfiguracionPuntoVenta"/> y <see cref="RecuperacionEmisiones"/>. Solo
    /// crea una tabla: no toca <c>Factura</c>, <c>LineaFactura</c>, <c>Presupuesto</c> ni ningún trigger de registros
    /// cerrados (R-07). Los CHECK son la segunda barrera detrás de <c>DatosEmisor</c> del dominio; el modelo y el
    /// snapshot declaran los mismos, por nombre y expresión. Antes de migrar la base de la óptica, backup con
    /// <c>VACUUM INTO</c> (AGENTS.md).
    /// </remarks>
    public partial class DatosEmisor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DatosEmisor",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    RazonSocial = table.Column<string>(type: "TEXT", nullable: false),
                    DomicilioComercial = table.Column<string>(type: "TEXT", nullable: false),
                    Cuit = table.Column<string>(type: "TEXT", nullable: false),
                    IngresosBrutos = table.Column<string>(type: "TEXT", nullable: false),
                    InicioActividades = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DatosEmisor", x => x.Id);
                    table.CheckConstraint("CK_DatosEmisor_Cuit", "length(\"Cuit\") = 11 AND \"Cuit\" NOT GLOB '*[^0-9]*'");
                    table.CheckConstraint("CK_DatosEmisor_DomicilioComercial", "length(\"DomicilioComercial\") BETWEEN 1 AND 200");
                    table.CheckConstraint("CK_DatosEmisor_Id", "\"Id\" = 1");
                    table.CheckConstraint("CK_DatosEmisor_IngresosBrutos", "length(\"IngresosBrutos\") BETWEEN 1 AND 30");
                    table.CheckConstraint("CK_DatosEmisor_InicioActividades", "\"InicioActividades\" GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]'");
                    table.CheckConstraint("CK_DatosEmisor_RazonSocial", "length(\"RazonSocial\") BETWEEN 1 AND 120");
                });
        }

        /// <inheritdoc />
        /// <remarks>
        /// Vuelve al esquema de FEAT-008: <c>DROP TABLE</c> de <c>DatosEmisor</c>, que no tiene triggers ni la referencia
        /// ninguna otra tabla. Los datos del emisor cargados se pierden y se vuelven a cargar en la configuración. No se
        /// aplica sobre la base de la óptica sin el backup de RNF-02 (<c>VACUUM INTO</c>).
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "DatosEmisor");
        }
    }
}
