using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Optica.Infrastructure.Persistencia.Migraciones
{
    /// <summary>
    /// FEAT-002b: las columnas <c>Activo</c> y <c>Version</c> de <c>Articulo</c>, con sus CHECK.
    /// </summary>
    /// <remarks>
    /// Escrita a mano (D-23, ADR-012). En lo que genera EF Core, <c>AddColumn</c> se traduce a un <c>ALTER TABLE ADD
    /// COLUMN</c> con su DEFAULT, pero <c>AddCheckConstraint</c> no tiene equivalente en SQLite y obliga a reconstruir la
    /// tabla: la tabla nueva pierde los DEFAULT, porque el modelo no los declara (D-21), y al borrar la vieja se reinicia la
    /// secuencia de AUTOINCREMENT, así que un código usado y borrado podría volver a asignarse. Con <c>ALTER TABLE ADD
    /// COLUMN</c> y el CHECK como restricción de la columna, la tabla no se reconstruye; las filas existentes toman los
    /// DEFAULT y quedan activas con versión 0. <c>DROP COLUMN</c> admite borrar una columna con su propio CHECK, así que el
    /// Down tampoco reconstruye. El modelo y el snapshot declaran los mismos CHECK, por nombre y expresión.
    /// </remarks>
    public partial class CatalogoActivo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Los DEFAULT viven solo acá, para las filas que ya estaban; el repositorio asigna siempre los dos (D-21).
            migrationBuilder.Sql(
                "ALTER TABLE \"Articulo\" ADD COLUMN \"Activo\" INTEGER NOT NULL DEFAULT 1 " +
                "CONSTRAINT \"CK_Articulo_Activo\" CHECK (\"Activo\" IN (0, 1));");

            migrationBuilder.Sql(
                "ALTER TABLE \"Articulo\" ADD COLUMN \"Version\" INTEGER NOT NULL DEFAULT 0 " +
                "CONSTRAINT \"CK_Articulo_Version\" CHECK (\"Version\" >= 0);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deja la tabla de FEAT-002a; el estado inactivo de los artículos se pierde, sus datos no.
            migrationBuilder.Sql("ALTER TABLE \"Articulo\" DROP COLUMN \"Version\";");

            migrationBuilder.Sql("ALTER TABLE \"Articulo\" DROP COLUMN \"Activo\";");
        }
    }
}
