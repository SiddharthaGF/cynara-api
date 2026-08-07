using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cynara.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditSubjectContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "WorkflowDefinitionId",
                table: "clinical_tasks",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "EncounterId",
                table: "audit_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PatientId",
                table: "audit_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WorkflowDefinitionId",
                table: "audit_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_clinical_tasks_HospitalId_WorkflowDefinitionId",
                table: "clinical_tasks",
                columns: new[] { "HospitalId", "WorkflowDefinitionId" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_HospitalId_EncounterId",
                table: "audit_events",
                columns: new[] { "HospitalId", "EncounterId" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_HospitalId_PatientId",
                table: "audit_events",
                columns: new[] { "HospitalId", "PatientId" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_HospitalId_WorkflowDefinitionId",
                table: "audit_events",
                columns: new[] { "HospitalId", "WorkflowDefinitionId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_clinical_tasks_HospitalId_WorkflowDefinitionId",
                table: "clinical_tasks");

            migrationBuilder.DropIndex(
                name: "IX_audit_events_HospitalId_EncounterId",
                table: "audit_events");

            migrationBuilder.DropIndex(
                name: "IX_audit_events_HospitalId_PatientId",
                table: "audit_events");

            migrationBuilder.DropIndex(
                name: "IX_audit_events_HospitalId_WorkflowDefinitionId",
                table: "audit_events");

            migrationBuilder.DropColumn(
                name: "WorkflowDefinitionId",
                table: "clinical_tasks");

            migrationBuilder.DropColumn(
                name: "EncounterId",
                table: "audit_events");

            migrationBuilder.DropColumn(
                name: "PatientId",
                table: "audit_events");

            migrationBuilder.DropColumn(
                name: "WorkflowDefinitionId",
                table: "audit_events");
        }
    }
}
