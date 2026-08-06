using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cynara.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddClinicalDocumentLifecycleStates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CanceledAt",
                table: "clinical_documents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EnteredInErrorAt",
                table: "clinical_documents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EnteredInErrorById",
                table: "clinical_documents",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EnteredInErrorReason",
                table: "clinical_documents",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CanceledAt",
                table: "clinical_documents");

            migrationBuilder.DropColumn(
                name: "EnteredInErrorAt",
                table: "clinical_documents");

            migrationBuilder.DropColumn(
                name: "EnteredInErrorById",
                table: "clinical_documents");

            migrationBuilder.DropColumn(
                name: "EnteredInErrorReason",
                table: "clinical_documents");
        }
    }
}
