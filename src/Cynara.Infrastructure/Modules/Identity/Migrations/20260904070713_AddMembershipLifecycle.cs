using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cynara.Infrastructure.Modules.Identity.Migrations
{
    /// <inheritdoc />
    public partial class AddMembershipLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_memberships_UserId_HospitalId",
                table: "memberships");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ActivatedAt",
                table: "memberships",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RevokedAt",
                table: "memberships",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "RowVersion",
                table: "memberships",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "memberships",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Active");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "memberships",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE memberships SET \"ActivatedAt\" = \"CreatedAt\", "
                + "\"UpdatedAt\" = \"CreatedAt\" "
                + "WHERE \"ActivatedAt\" IS NULL;");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "ActivatedAt",
                table: "memberships",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "memberships",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_memberships_HospitalId_ActorId",
                table: "memberships",
                columns: new[] { "HospitalId", "ActorId" },
                unique: true,
                filter: "\"Status\" = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_memberships_UserId_HospitalId",
                table: "memberships",
                columns: new[] { "UserId", "HospitalId" },
                unique: true,
                filter: "\"Status\" = 'Active'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_memberships_HospitalId_ActorId",
                table: "memberships");

            migrationBuilder.DropIndex(
                name: "IX_memberships_UserId_HospitalId",
                table: "memberships");

            migrationBuilder.DropColumn(
                name: "ActivatedAt",
                table: "memberships");

            migrationBuilder.DropColumn(
                name: "RevokedAt",
                table: "memberships");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "memberships");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "memberships");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "memberships");

            migrationBuilder.CreateIndex(
                name: "IX_memberships_UserId_HospitalId",
                table: "memberships",
                columns: new[] { "UserId", "HospitalId" },
                unique: true);
        }
    }
}
