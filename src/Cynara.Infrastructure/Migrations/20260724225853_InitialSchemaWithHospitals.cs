using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cynara.Infrastructure.Migrations;

/// <inheritdoc />
public partial class InitialSchemaWithHospitals : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        _ = migrationBuilder.CreateTable(
            name: "ai_provider_settings",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                HospitalId = table.Column<Guid>(type: "TEXT", nullable: false),
                ApiKey = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                BaseUrl = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                Model = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                JsonObject = table.Column<bool>(type: "INTEGER", nullable: true),
                UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
            },
            constraints: table => table.PrimaryKey("PK_ai_provider_settings", x => new { x.HospitalId, x.Id }));

        _ = migrationBuilder.CreateTable(
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
                MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
            },
            constraints: table => table.PrimaryKey("PK_audit_events", x => x.Id));

        _ = migrationBuilder.CreateTable(
            name: "component_definitions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                HospitalId = table.Column<Guid>(type: "TEXT", nullable: false),
                Code = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                DeletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
            },
            constraints: table => table.PrimaryKey("PK_component_definitions", x => x.Id));

        _ = migrationBuilder.CreateTable(
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
                MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
            },
            constraints: table => table.PrimaryKey("PK_failure_logs", x => x.Id));

        _ = migrationBuilder.CreateTable(
            name: "form_definitions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                HospitalId = table.Column<Guid>(type: "TEXT", nullable: false),
                Code = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                DeletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
            },
            constraints: table => table.PrimaryKey("PK_form_definitions", x => x.Id));

        _ = migrationBuilder.CreateTable(
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
                RowVersion = table.Column<uint>(type: "INTEGER", nullable: false),
            },
            constraints: table => table.PrimaryKey("PK_hospitals", x => x.Id));

        _ = migrationBuilder.CreateTable(
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
                RetiredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
            },
            constraints: table =>
            {
                _ = table.PrimaryKey("PK_component_versions", x => x.Id);
                _ = table.ForeignKey(
                    name: "FK_component_versions_component_definitions_ComponentDefinitionId",
                    column: x => x.ComponentDefinitionId,
                    principalTable: "component_definitions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        _ = migrationBuilder.CreateTable(
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
                LastReviewedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
            },
            constraints: table =>
            {
                _ = table.PrimaryKey("PK_form_versions", x => x.Id);
                _ = table.ForeignKey(
                    name: "FK_form_versions_form_definitions_FormDefinitionId",
                    column: x => x.FormDefinitionId,
                    principalTable: "form_definitions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        _ = migrationBuilder.CreateTable(
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
                DeletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
            },
            constraints: table =>
            {
                _ = table.PrimaryKey("PK_form_responses", x => x.Id);
                _ = table.ForeignKey(
                    name: "FK_form_responses_form_versions_FormVersionId",
                    column: x => x.FormVersionId,
                    principalTable: "form_versions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        _ = migrationBuilder.CreateTable(
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
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
            },
            constraints: table =>
            {
                _ = table.PrimaryKey("PK_form_response_revisions", x => x.Id);
                _ = table.ForeignKey(
                    name: "FK_form_response_revisions_form_responses_FormResponseId",
                    column: x => x.FormResponseId,
                    principalTable: "form_responses",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        _ = migrationBuilder.CreateIndex(
            name: "IX_audit_events_HospitalId",
            table: "audit_events",
            column: "HospitalId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_audit_events_HospitalId_ActorId",
            table: "audit_events",
            columns: ["HospitalId", "ActorId"]);

        _ = migrationBuilder.CreateIndex(
            name: "IX_audit_events_HospitalId_OccurredAt",
            table: "audit_events",
            columns: ["HospitalId", "OccurredAt"]);

        _ = migrationBuilder.CreateIndex(
            name: "IX_audit_events_HospitalId_ResourceType_ResourceId",
            table: "audit_events",
            columns: ["HospitalId", "ResourceType", "ResourceId"]);

        _ = migrationBuilder.CreateIndex(
            name: "IX_component_definitions_HospitalId",
            table: "component_definitions",
            column: "HospitalId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_component_definitions_HospitalId_Code",
            table: "component_definitions",
            columns: ["HospitalId", "Code"],
            unique: true);

        _ = migrationBuilder.CreateIndex(
            name: "IX_component_versions_ComponentDefinitionId",
            table: "component_versions",
            column: "ComponentDefinitionId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_component_versions_HospitalId",
            table: "component_versions",
            column: "HospitalId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_component_versions_HospitalId_ComponentDefinitionId_Version",
            table: "component_versions",
            columns: ["HospitalId", "ComponentDefinitionId", "Version"],
            unique: true);

        _ = migrationBuilder.CreateIndex(
            name: "IX_failure_logs_ExceptionType",
            table: "failure_logs",
            column: "ExceptionType");

        _ = migrationBuilder.CreateIndex(
            name: "IX_failure_logs_HospitalId",
            table: "failure_logs",
            column: "HospitalId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_failure_logs_OccurredAt",
            table: "failure_logs",
            column: "OccurredAt");

        _ = migrationBuilder.CreateIndex(
            name: "IX_failure_logs_TraceId",
            table: "failure_logs",
            column: "TraceId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_form_definitions_HospitalId",
            table: "form_definitions",
            column: "HospitalId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_form_definitions_HospitalId_Code",
            table: "form_definitions",
            columns: ["HospitalId", "Code"],
            unique: true);

        _ = migrationBuilder.CreateIndex(
            name: "IX_form_response_revisions_FormResponseId_RevisionNumber",
            table: "form_response_revisions",
            columns: ["FormResponseId", "RevisionNumber"],
            unique: true);

        _ = migrationBuilder.CreateIndex(
            name: "IX_form_response_revisions_HospitalId",
            table: "form_response_revisions",
            column: "HospitalId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_form_responses_FormVersionId",
            table: "form_responses",
            column: "FormVersionId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_form_responses_HospitalId",
            table: "form_responses",
            column: "HospitalId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_form_versions_FormDefinitionId",
            table: "form_versions",
            column: "FormDefinitionId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_form_versions_HospitalId",
            table: "form_versions",
            column: "HospitalId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_form_versions_HospitalId_FormDefinitionId_Version",
            table: "form_versions",
            columns: ["HospitalId", "FormDefinitionId", "Version"],
            unique: true);

        _ = migrationBuilder.CreateIndex(
            name: "IX_hospitals_Code",
            table: "hospitals",
            column: "Code",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        _ = migrationBuilder.DropTable(
            name: "ai_provider_settings");

        _ = migrationBuilder.DropTable(
            name: "audit_events");

        _ = migrationBuilder.DropTable(
            name: "component_versions");

        _ = migrationBuilder.DropTable(
            name: "failure_logs");

        _ = migrationBuilder.DropTable(
            name: "form_response_revisions");

        _ = migrationBuilder.DropTable(
            name: "hospitals");

        _ = migrationBuilder.DropTable(
            name: "component_definitions");

        _ = migrationBuilder.DropTable(
            name: "form_responses");

        _ = migrationBuilder.DropTable(
            name: "form_versions");

        _ = migrationBuilder.DropTable(
            name: "form_definitions");
    }
}
