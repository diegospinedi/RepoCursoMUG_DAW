using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Optica.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CredencialAcceso",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    Hash = table.Column<string>(type: "TEXT", nullable: false),
                    DefinidaEn = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CredencialAcceso", x => x.Id);
                    table.CheckConstraint("CK_CredencialAcceso_Id", "\"Id\" = 1");
                });

            migrationBuilder.CreateTable(
                name: "EventoAcceso",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FechaHora = table.Column<string>(type: "TEXT", nullable: false),
                    Tipo = table.Column<string>(type: "TEXT", nullable: false),
                    Resultado = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventoAcceso", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventoAcceso_FechaHora",
                table: "EventoAcceso",
                column: "FechaHora");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CredencialAcceso");

            migrationBuilder.DropTable(
                name: "EventoAcceso");
        }
    }
}
