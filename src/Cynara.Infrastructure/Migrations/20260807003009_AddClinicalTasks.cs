using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cynara.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddClinicalTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "clinical_tasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HospitalId = table.Column<Guid>(type: "uuid", nullable: false),
                    PipelineId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    NodeId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AssignedActor = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    AssignedRole = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    AssignedDiscipline = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    EncounterId = table.Column<Guid>(type: "uuid", nullable: true),
                    FormCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    FormVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    DueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClaimedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ClaimedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CanceledBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CanceledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RowVersion = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clinical_tasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_clinical_tasks_workflow_pipelines_PipelineId",
                        column: x => x.PipelineId,
                        principalTable: "workflow_pipelines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_clinical_tasks_HospitalId",
                table: "clinical_tasks",
                column: "HospitalId");

            migrationBuilder.CreateIndex(
                name: "IX_clinical_tasks_HospitalId_EncounterId",
                table: "clinical_tasks",
                columns: new[] { "HospitalId", "EncounterId" });

            migrationBuilder.CreateIndex(
                name: "IX_clinical_tasks_HospitalId_EncounterId_Status",
                table: "clinical_tasks",
                columns: new[] { "HospitalId", "EncounterId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_clinical_tasks_HospitalId_PatientId",
                table: "clinical_tasks",
                columns: new[] { "HospitalId", "PatientId" });

            migrationBuilder.CreateIndex(
                name: "IX_clinical_tasks_HospitalId_PipelineId",
                table: "clinical_tasks",
                columns: new[] { "HospitalId", "PipelineId" });

            migrationBuilder.CreateIndex(
                name: "IX_clinical_tasks_HospitalId_Status",
                table: "clinical_tasks",
                columns: new[] { "HospitalId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_clinical_tasks_PipelineId",
                table: "clinical_tasks",
                column: "PipelineId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "clinical_tasks");
        }
    }
}
