using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cynara.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPatientBloodType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The column stores the enum member name via HasConversion<string>();
            // backfill pre-existing rows with O positive, then drop the
            // temporary default so future writes go through the application.
            migrationBuilder.AddColumn<string>(
                name: "BloodType",
                table: "patients",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "OPositive");

            migrationBuilder.Sql(
                "ALTER TABLE patients ALTER COLUMN \"BloodType\" DROP DEFAULT");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BloodType",
                table: "patients");
        }
    }
}
