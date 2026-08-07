using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cynara.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowPipelines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "workflow_pipelines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HospitalId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CurrentNodeId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RowVersion = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_pipelines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_workflow_pipelines_workflow_versions_WorkflowVersionId",
                        column: x => x.WorkflowVersionId,
                        principalTable: "workflow_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "workflow_pipeline_history",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HospitalId = table.Column<Guid>(type: "uuid", nullable: false),
                    PipelineId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ActorId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    MetadataJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_pipeline_history", x => x.Id);
                    table.ForeignKey(
                        name: "FK_workflow_pipeline_history_workflow_pipelines_PipelineId",
                        column: x => x.PipelineId,
                        principalTable: "workflow_pipelines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_pipeline_history_HospitalId",
                table: "workflow_pipeline_history",
                column: "HospitalId");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_pipeline_history_PipelineId_Sequence",
                table: "workflow_pipeline_history",
                columns: new[] { "PipelineId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workflow_pipelines_HospitalId",
                table: "workflow_pipelines",
                column: "HospitalId");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_pipelines_HospitalId_Status",
                table: "workflow_pipelines",
                columns: new[] { "HospitalId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_pipelines_HospitalId_SubjectId",
                table: "workflow_pipelines",
                columns: new[] { "HospitalId", "SubjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_pipelines_HospitalId_SubjectType",
                table: "workflow_pipelines",
                columns: new[] { "HospitalId", "SubjectType" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_pipelines_HospitalId_WorkflowVersionId",
                table: "workflow_pipelines",
                columns: new[] { "HospitalId", "WorkflowVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_pipelines_WorkflowVersionId",
                table: "workflow_pipelines",
                column: "WorkflowVersionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "workflow_pipeline_history");

            migrationBuilder.DropTable(
                name: "workflow_pipelines");
        }
    }
}
