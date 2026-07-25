using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cynara.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_provider_settings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    HospitalId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ApiKey = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    BaseUrl = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    Model = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    JsonObject = table.Column<bool>(type: "INTEGER", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_provider_settings", x => new { x.HospitalId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "audit_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    HospitalId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ResourceType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ResourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ActorId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "component_definitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    HospitalId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_component_definitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "failure_logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    HospitalId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ExceptionType = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Message = table.Column<string>(type: "TEXT", nullable: false),
                    StackTrace = table.Column<string>(type: "TEXT", nullable: true),
                    RequestMethod = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    RequestPath = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    RequestQuery = table.Column<string>(type: "TEXT", nullable: true),
                    StatusCode = table.Column<int>(type: "INTEGER", nullable: false),
                    TraceId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ActorId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_failure_logs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "form_definitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    HospitalId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_form_definitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "hospitals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    RowVersion = table.Column<uint>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_hospitals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "component_versions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    HospitalId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ComponentDefinitionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ClinicalSchemaJson = table.Column<string>(type: "TEXT", nullable: false),
                    UiSchemaJson = table.Column<string>(type: "TEXT", nullable: true),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    RowVersion = table.Column<uint>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    RetiredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_component_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_component_versions_component_definitions_ComponentDefinitionId",
                        column: x => x.ComponentDefinitionId,
                        principalTable: "component_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "form_versions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    HospitalId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FormDefinitionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ClinicalSchemaJson = table.Column<string>(type: "TEXT", nullable: false),
                    UiSchemaJson = table.Column<string>(type: "TEXT", nullable: true),
                    RulesSchemaJson = table.Column<string>(type: "TEXT", nullable: true),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    DependencyMetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    RowVersion = table.Column<uint>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    SubmittedForReviewAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    PublishedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    RetiredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    PublishedSchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    LastReviewComment = table.Column<string>(type: "TEXT", nullable: true),
                    LastReviewDecision = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    LastReviewedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_form_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_form_versions_form_definitions_FormDefinitionId",
                        column: x => x.FormDefinitionId,
                        principalTable: "form_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "form_responses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    HospitalId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FormVersionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    AnswersJson = table.Column<string>(type: "TEXT", nullable: false),
                    RevisionNumber = table.Column<uint>(type: "INTEGER", nullable: false),
                    RowVersion = table.Column<uint>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    DeletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_form_responses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_form_responses_form_versions_FormVersionId",
                        column: x => x.FormVersionId,
                        principalTable: "form_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "form_response_revisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    HospitalId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FormResponseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RevisionNumber = table.Column<uint>(type: "INTEGER", nullable: false),
                    AnswersJson = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ActorId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_form_response_revisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_form_response_revisions_form_responses_FormResponseId",
                        column: x => x.FormResponseId,
                        principalTable: "form_responses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_HospitalId",
                table: "audit_events",
                column: "HospitalId");

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_HospitalId_ActorId",
                table: "audit_events",
                columns: new[] { "HospitalId", "ActorId" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_HospitalId_OccurredAt",
                table: "audit_events",
                columns: new[] { "HospitalId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_HospitalId_ResourceType_ResourceId",
                table: "audit_events",
                columns: new[] { "HospitalId", "ResourceType", "ResourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_component_definitions_HospitalId",
                table: "component_definitions",
                column: "HospitalId");

            migrationBuilder.CreateIndex(
                name: "IX_component_definitions_HospitalId_Code",
                table: "component_definitions",
                columns: new[] { "HospitalId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_component_versions_ComponentDefinitionId",
                table: "component_versions",
                column: "ComponentDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_component_versions_HospitalId",
                table: "component_versions",
                column: "HospitalId");

            migrationBuilder.CreateIndex(
                name: "IX_component_versions_HospitalId_ComponentDefinitionId_Version",
                table: "component_versions",
                columns: new[] { "HospitalId", "ComponentDefinitionId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_failure_logs_ExceptionType",
                table: "failure_logs",
                column: "ExceptionType");

            migrationBuilder.CreateIndex(
                name: "IX_failure_logs_HospitalId",
                table: "failure_logs",
                column: "HospitalId");

            migrationBuilder.CreateIndex(
                name: "IX_failure_logs_OccurredAt",
                table: "failure_logs",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_failure_logs_TraceId",
                table: "failure_logs",
                column: "TraceId");

            migrationBuilder.CreateIndex(
                name: "IX_form_definitions_HospitalId",
                table: "form_definitions",
                column: "HospitalId");

            migrationBuilder.CreateIndex(
                name: "IX_form_definitions_HospitalId_Code",
                table: "form_definitions",
                columns: new[] { "HospitalId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_form_response_revisions_FormResponseId_RevisionNumber",
                table: "form_response_revisions",
                columns: new[] { "FormResponseId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_form_response_revisions_HospitalId",
                table: "form_response_revisions",
                column: "HospitalId");

            migrationBuilder.CreateIndex(
                name: "IX_form_responses_FormVersionId",
                table: "form_responses",
                column: "FormVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_form_responses_HospitalId",
                table: "form_responses",
                column: "HospitalId");

            migrationBuilder.CreateIndex(
                name: "IX_form_versions_FormDefinitionId",
                table: "form_versions",
                column: "FormDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_form_versions_HospitalId",
                table: "form_versions",
                column: "HospitalId");

            migrationBuilder.CreateIndex(
                name: "IX_form_versions_HospitalId_FormDefinitionId_Version",
                table: "form_versions",
                columns: new[] { "HospitalId", "FormDefinitionId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_hospitals_Code",
                table: "hospitals",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_provider_settings");

            migrationBuilder.DropTable(
                name: "audit_events");

            migrationBuilder.DropTable(
                name: "component_versions");

            migrationBuilder.DropTable(
                name: "failure_logs");

            migrationBuilder.DropTable(
                name: "form_response_revisions");

            migrationBuilder.DropTable(
                name: "hospitals");

            migrationBuilder.DropTable(
                name: "component_definitions");

            migrationBuilder.DropTable(
                name: "form_responses");

            migrationBuilder.DropTable(
                name: "form_versions");

            migrationBuilder.DropTable(
                name: "form_definitions");
        }
    }
}
