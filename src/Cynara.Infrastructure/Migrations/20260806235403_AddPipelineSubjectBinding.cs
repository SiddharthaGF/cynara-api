using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cynara.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPipelineSubjectBinding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "EncounterId",
                table: "workflow_pipelines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PatientId",
                table: "workflow_pipelines",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_workflow_pipelines_HospitalId_EncounterId",
                table: "workflow_pipelines",
                columns: new[] { "HospitalId", "EncounterId" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_pipelines_HospitalId_PatientId",
                table: "workflow_pipelines",
                columns: new[] { "HospitalId", "PatientId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_workflow_pipelines_HospitalId_EncounterId",
                table: "workflow_pipelines");

            migrationBuilder.DropIndex(
                name: "IX_workflow_pipelines_HospitalId_PatientId",
                table: "workflow_pipelines");

            migrationBuilder.DropColumn(
                name: "EncounterId",
                table: "workflow_pipelines");

            migrationBuilder.DropColumn(
                name: "PatientId",
                table: "workflow_pipelines");
        }
    }
}
