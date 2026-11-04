using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Optica.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Catalogo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Articulo",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CodigoProveedor = table.Column<string>(type: "TEXT", nullable: false),
                    CodigoProveedorClave = table.Column<string>(type: "TEXT", nullable: false),
                    Descripcion = table.Column<string>(type: "TEXT", nullable: false),
                    PrecioCostoCentavos = table.Column<long>(type: "INTEGER", nullable: false),
                    MargenCentesimos = table.Column<long>(type: "INTEGER", nullable: false),
                    PrecioVentaCentavos = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Articulo", x => x.Id);
                    table.CheckConstraint("CK_Articulo_CodigoProveedor", "length(\"CodigoProveedor\") BETWEEN 1 AND 50");
                    table.CheckConstraint("CK_Articulo_Descripcion", "length(\"Descripcion\") BETWEEN 1 AND 200");
                    table.CheckConstraint("CK_Articulo_Margen", "\"MargenCentesimos\" BETWEEN 0 AND 100000");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Articulo_CodigoProveedorClave",
                table: "Articulo",
                column: "CodigoProveedorClave",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Articulo");
        }
    }
}
