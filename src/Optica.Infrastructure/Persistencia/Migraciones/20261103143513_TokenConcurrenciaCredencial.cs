using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Optica.Infrastructure.Persistencia.Migraciones
{
    /// <summary>
    /// Sin SQL: el hash de <c>CredencialAcceso</c> pasa a ser token de concurrencia, que EF usa en el <c>WHERE</c> del
    /// <c>UPDATE</c> y no cambia el esquema. La migración deja el token en <c>OpticaDbContextModelSnapshot</c> (FEAT-001b).
    /// </summary>
    public partial class TokenConcurrenciaCredencial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
