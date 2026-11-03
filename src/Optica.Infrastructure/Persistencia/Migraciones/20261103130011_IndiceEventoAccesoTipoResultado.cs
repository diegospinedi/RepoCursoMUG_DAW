using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Optica.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class IndiceEventoAccesoTipoResultado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_EventoAcceso_Tipo_Resultado",
                table: "EventoAcceso",
                columns: new[] { "Tipo", "Resultado" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EventoAcceso_Tipo_Resultado",
                table: "EventoAcceso");
        }
    }
}
