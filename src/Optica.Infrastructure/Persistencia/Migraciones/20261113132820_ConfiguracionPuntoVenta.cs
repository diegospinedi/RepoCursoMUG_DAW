using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Optica.Infrastructure.Persistencia.Migraciones
{
    /// <summary>
    /// FEAT-007: la columna <c>PuntoVenta</c> de <c>ConfiguracionNegocio</c>, con su CHECK de 1 a 99999 (D-07). Las filas
    /// existentes quedan sin punto de venta: la operadora lo carga en la pantalla de configuración (FR-28).
    /// </summary>
    /// <remarks>
    /// Escrita a mano (ADR-012), como <see cref="CierrePresupuestos"/>. Lo que genera EF Core para el CHECK
    /// (<c>AddCheckConstraint</c>) no tiene equivalente en SQLite y obliga a reconstruir la tabla; con
    /// <c>ALTER TABLE ADD COLUMN</c> y el CHECK como restricción de la columna, la tabla no se reconstruye y la fila única
    /// (ADR-008) conserva sus valores. La columna no lleva DEFAULT: sin valor es el estado inicial. El modelo y el snapshot
    /// declaran el mismo CHECK, por nombre y expresión. La regla de las 6 alícuotas de ARCA (FR-26) no va a la base: se
    /// aplica al grabar (D-06) y <c>CK_ConfiguracionNegocio_Alicuota</c> sigue siendo de 0 a 10000 centésimas.
    /// </remarks>
    public partial class ConfiguracionPuntoVenta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE \"ConfiguracionNegocio\" ADD COLUMN \"PuntoVenta\" INTEGER NULL " +
                "CONSTRAINT \"CK_ConfiguracionNegocio_PuntoVenta\" CHECK " +
                "(\"PuntoVenta\" IS NULL OR \"PuntoVenta\" BETWEEN 1 AND 99999);");
        }

        /// <inheritdoc />
        /// <remarks>
        /// Solo para desarrollo: pierde el punto de venta cargado. Antes de migrar una base con datos reales vale el backup
        /// de RNF-02 (<c>VACUUM INTO</c>).
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // DROP COLUMN admite borrar una columna con su propio CHECK: no reconstruye la tabla.
            migrationBuilder.Sql("ALTER TABLE \"ConfiguracionNegocio\" DROP COLUMN \"PuntoVenta\";");
        }
    }
}
