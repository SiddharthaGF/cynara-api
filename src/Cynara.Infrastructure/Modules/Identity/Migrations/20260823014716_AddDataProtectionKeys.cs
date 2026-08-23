using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Cynara.Infrastructure.Modules.Identity.Migrations
{
    /// <summary>
    /// Creates the <c>data_protection_keys</c> table that stores the
    /// ASP.NET Core DataProtection key ring in the identity database.
    /// Persisting the ring keeps OpenIddict refresh tokens and
    /// authorization artifacts valid across restarts, deploys, and
    /// horizontally scaled instances; without it every boot regenerates
    /// an ephemeral ring and invalidates all outstanding refresh tokens,
    /// forcing users to sign in again. The table is consumed by
    /// <see cref="CynaraIdentityDbContext"/> (via
    /// <c>PersistKeysToDbContext</c>) and applied automatically at startup
    /// by <c>InitializeDatabaseAsync</c>.
    /// </summary>
    public partial class AddDataProtectionKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "data_protection_keys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FriendlyName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Xml = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_data_protection_keys", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_data_protection_keys_FriendlyName",
                table: "data_protection_keys",
                column: "FriendlyName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "data_protection_keys");
        }
    }
}
