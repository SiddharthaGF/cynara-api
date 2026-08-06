using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cynara.Infrastructure.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        _ = migrationBuilder.CreateTable(
            name: "ai_provider_settings",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                HospitalId = table.Column<Guid>(type: "uuid", nullable: false),
                ApiKey = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                BaseUrl = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                Model = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                JsonObject = table.Column<bool>(type: "boolean", nullable: true),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                _ = table.PrimaryKey("PK_ai_provider_settings", x => new { x.HospitalId, x.Id });
            });

        _ = migrationBuilder.CreateTable(
            name: "audit_events",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                HospitalId = table.Column<Guid>(type: "uuid", nullable: false),
                ResourceType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ResourceId = table.Column<Guid>(type: "uuid", nullable: false),
                Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ActorId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                MetadataJson = table.Column<string>(type: "text", nullable: true),
            },
            constraints: table =>
            {
                _ = table.PrimaryKey("PK_audit_events", x => x.Id);
            });

        _ = migrationBuilder.CreateTable(
            name: "capability_assignments",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                HospitalId = table.Column<Guid>(type: "uuid", nullable: false),
                ActorId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Capability = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                AssignedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                RowVersion = table.Column<long>(type: "bigint", nullable: false),
            },
            constraints: table =>
            {
                _ = table.PrimaryKey("PK_capability_assignments", x => x.Id);
            });

        _ = migrationBuilder.CreateTable(
            name: "clinical_documents",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                HospitalId = table.Column<Guid>(type: "uuid", nullable: false),
                DocumentDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                EncounterId = table.Column<Guid>(type: "uuid", nullable: false),
                FormVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                FormResponseId = table.Column<Guid>(type: "uuid", nullable: false),
                AuthorId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                RowVersion = table.Column<long>(type: "bigint", nullable: false),
            },
            constraints: table =>
            {
                _ = table.PrimaryKey("PK_clinical_documents", x => x.Id);
            });

        _ = migrationBuilder.CreateTable(
            name: "component_definitions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                HospitalId = table.Column<Guid>(type: "uuid", nullable: false),
                Code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
            },
            constraints: table =>
            {
                _ = table.PrimaryKey("PK_component_definitions", x => x.Id);
            });

        _ = migrationBuilder.CreateTable(
            name: "encounters",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                HospitalId = table.Column<Guid>(type: "uuid", nullable: false),
                PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                ClinicalAreaId = table.Column<Guid>(type: "uuid", nullable: false),
                Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                ResponsibleProfessionalId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                RowVersion = table.Column<long>(type: "bigint", nullable: false),
            },
            constraints: table =>
            {
                _ = table.PrimaryKey("PK_encounters", x => x.Id);
            });

        _ = migrationBuilder.CreateTable(
            name: "facilities",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                HospitalId = table.Column<Guid>(type: "uuid", nullable: false),
                Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                RetiredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                RowVersion = table.Column<long>(type: "bigint", nullable: false),
            },
            constraints: table =>
            {
                _ = table.PrimaryKey("PK_facilities", x => x.Id);
            });

        _ = migrationBuilder.CreateTable(
            name: "failure_logs",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                HospitalId = table.Column<Guid>(type: "uuid", nullable: true),
                OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ExceptionType = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                Message = table.Column<string>(type: "text", nullable: false),
                StackTrace = table.Column<string>(type: "text", nullable: true),
                RequestMethod = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                RequestPath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                RequestQuery = table.Column<string>(type: "text", nullable: true),
                StatusCode = table.Column<int>(type: "integer", nullable: false),
                TraceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                ActorId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                MetadataJson = table.Column<string>(type: "text", nullable: true),
            },
            constraints: table =>
            {
                _ = table.PrimaryKey("PK_failure_logs", x => x.Id);
            });

        _ = migrationBuilder.CreateTable(
            name: "form_definitions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                HospitalId = table.Column<Guid>(type: "uuid", nullable: false),
                Code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
            },
            constraints: table =>
            {
                _ = table.PrimaryKey("PK_form_definitions", x => x.Id);
            });

        _ = migrationBuilder.CreateTable(
            name: "hospitals",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                MetadataJson = table.Column<string>(type: "text", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                RowVersion = table.Column<long>(type: "bigint", nullable: false),
            },
            constraints: table =>
            {
                _ = table.PrimaryKey("PK_hospitals", x => x.Id);
            });

        _ = migrationBuilder.CreateTable(
            name: "patients",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                HospitalId = table.Column<Guid>(type: "uuid", nullable: false),
                Mrn = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                NormalizedMrn = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                NationalId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                NormalizedNationalId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                GivenName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                NormalizedGivenName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                FamilyName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                NormalizedFamilyName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                BirthDate = table.Column<DateOnly>(type: "date", nullable: false),
                Sex = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                RowVersion = table.Column<long>(type: "bigint", nullable: false),
            },
            constraints: table =>
            {
                _ = table.PrimaryKey("PK_patients", x => x.Id);
            });

        _ = migrationBuilder.CreateTable(
            name: "component_versions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                HospitalId = table.Column<Guid>(type: "uuid", nullable: false),
                ComponentDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                Version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                Status = table.Column<int>(type: "integer", nullable: false),
                ClinicalSchemaJson = table.Column<string>(type: "text", nullable: false),
                UiSchemaJson = table.Column<string>(type: "text", nullable: true),
                ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                RowVersion = table.Column<long>(type: "bigint", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                RetiredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
            },
            constraints: table =>
            {
                _ = table.PrimaryKey("PK_component_versions", x => x.Id);
                _ = table.ForeignKey(
                    name: "FK_component_versions_component_definitions_ComponentDefinitio~",
                    column: x => x.ComponentDefinitionId,
                    principalTable: "component_definitions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        _ = migrationBuilder.CreateTable(
            name: "clinical_areas",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                HospitalId = table.Column<Guid>(type: "uuid", nullable: false),
                Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                RetiredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                RowVersion = table.Column<long>(type: "bigint", nullable: false),
                FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
            },
            constraints: table =>
            {
                _ = table.PrimaryKey("PK_clinical_areas", x => x.Id);
                _ = table.ForeignKey(
                    name: "FK_clinical_areas_facilities_FacilityId",
                    column: x => x.FacilityId,
                    principalTable: "facilities",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        _ = migrationBuilder.CreateTable(
            name: "form_versions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                HospitalId = table.Column<Guid>(type: "uuid", nullable: false),
                FormDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                Version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                Status = table.Column<int>(type: "integer", nullable: false),
                ClinicalSchemaJson = table.Column<string>(type: "text", nullable: false),
                UiSchemaJson = table.Column<string>(type: "text", nullable: true),
                RulesSchemaJson = table.Column<string>(type: "text", nullable: true),
                ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                DependencyMetadataJson = table.Column<string>(type: "text", nullable: true),
                RowVersion = table.Column<long>(type: "bigint", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                SubmittedForReviewAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                RetiredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                PublishedSchemaVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                LastReviewComment = table.Column<string>(type: "text", nullable: true),
                LastReviewDecision = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                LastReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
            name: "disciplines",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                HospitalId = table.Column<Guid>(type: "uuid", nullable: false),
                Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                RetiredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                RowVersion = table.Column<long>(type: "bigint", nullable: false),
                ClinicalAreaId = table.Column<Guid>(type: "uuid", nullable: false),
            },
            constraints: table =>
            {
                _ = table.PrimaryKey("PK_disciplines", x => x.Id);
                _ = table.ForeignKey(
                    name: "FK_disciplines_clinical_areas_ClinicalAreaId",
                    column: x => x.ClinicalAreaId,
                    principalTable: "clinical_areas",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        _ = migrationBuilder.CreateTable(
            name: "form_responses",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                HospitalId = table.Column<Guid>(type: "uuid", nullable: false),
                FormVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                AnswersJson = table.Column<string>(type: "text", nullable: false),
                RevisionNumber = table.Column<long>(type: "bigint", nullable: false),
                RowVersion = table.Column<long>(type: "bigint", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
            name: "document_definitions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                HospitalId = table.Column<Guid>(type: "uuid", nullable: false),
                Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                FormDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                FormVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                ClinicalAreaId = table.Column<Guid>(type: "uuid", nullable: false),
                DisciplineId = table.Column<Guid>(type: "uuid", nullable: false),
                AllowsMultipleInstancesPerEncounter = table.Column<bool>(type: "boolean", nullable: false),
                RequiresActorForCreation = table.Column<bool>(type: "boolean", nullable: false),
                RequiresActorForCompletion = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                RetiredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                RowVersion = table.Column<long>(type: "bigint", nullable: false),
            },
            constraints: table =>
            {
                _ = table.PrimaryKey("PK_document_definitions", x => x.Id);
                _ = table.ForeignKey(
                    name: "FK_document_definitions_clinical_areas_ClinicalAreaId",
                    column: x => x.ClinicalAreaId,
                    principalTable: "clinical_areas",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                _ = table.ForeignKey(
                    name: "FK_document_definitions_disciplines_DisciplineId",
                    column: x => x.DisciplineId,
                    principalTable: "disciplines",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                _ = table.ForeignKey(
                    name: "FK_document_definitions_facilities_FacilityId",
                    column: x => x.FacilityId,
                    principalTable: "facilities",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                _ = table.ForeignKey(
                    name: "FK_document_definitions_form_definitions_FormDefinitionId",
                    column: x => x.FormDefinitionId,
                    principalTable: "form_definitions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                _ = table.ForeignKey(
                    name: "FK_document_definitions_form_versions_FormVersionId",
                    column: x => x.FormVersionId,
                    principalTable: "form_versions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        _ = migrationBuilder.CreateTable(
            name: "form_response_revisions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                HospitalId = table.Column<Guid>(type: "uuid", nullable: false),
                FormResponseId = table.Column<Guid>(type: "uuid", nullable: false),
                RevisionNumber = table.Column<long>(type: "bigint", nullable: false),
                AnswersJson = table.Column<string>(type: "text", nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                ActorId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
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
            name: "IX_capability_assignments_HospitalId",
            table: "capability_assignments",
            column: "HospitalId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_capability_assignments_HospitalId_ActorId_Capability",
            table: "capability_assignments",
            columns: ["HospitalId", "ActorId", "Capability"],
            unique: true);

        _ = migrationBuilder.CreateIndex(
            name: "IX_clinical_areas_FacilityId",
            table: "clinical_areas",
            column: "FacilityId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_clinical_areas_HospitalId",
            table: "clinical_areas",
            column: "HospitalId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_clinical_areas_HospitalId_Code",
            table: "clinical_areas",
            columns: ["HospitalId", "Code"],
            unique: true);

        _ = migrationBuilder.CreateIndex(
            name: "IX_clinical_documents_HospitalId",
            table: "clinical_documents",
            column: "HospitalId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_clinical_documents_HospitalId_DocumentDefinitionId_Encounte~",
            table: "clinical_documents",
            columns: ["HospitalId", "DocumentDefinitionId", "EncounterId"]);

        _ = migrationBuilder.CreateIndex(
            name: "IX_clinical_documents_HospitalId_EncounterId",
            table: "clinical_documents",
            columns: ["HospitalId", "EncounterId"]);

        _ = migrationBuilder.CreateIndex(
            name: "IX_clinical_documents_HospitalId_PatientId",
            table: "clinical_documents",
            columns: ["HospitalId", "PatientId"]);

        _ = migrationBuilder.CreateIndex(
            name: "IX_clinical_documents_HospitalId_Status",
            table: "clinical_documents",
            columns: ["HospitalId", "Status"]);

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
            name: "IX_disciplines_ClinicalAreaId",
            table: "disciplines",
            column: "ClinicalAreaId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_disciplines_HospitalId",
            table: "disciplines",
            column: "HospitalId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_disciplines_HospitalId_Code",
            table: "disciplines",
            columns: ["HospitalId", "Code"],
            unique: true);

        _ = migrationBuilder.CreateIndex(
            name: "IX_document_definitions_ClinicalAreaId",
            table: "document_definitions",
            column: "ClinicalAreaId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_document_definitions_DisciplineId",
            table: "document_definitions",
            column: "DisciplineId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_document_definitions_FacilityId",
            table: "document_definitions",
            column: "FacilityId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_document_definitions_FormDefinitionId",
            table: "document_definitions",
            column: "FormDefinitionId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_document_definitions_FormVersionId",
            table: "document_definitions",
            column: "FormVersionId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_document_definitions_HospitalId",
            table: "document_definitions",
            column: "HospitalId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_document_definitions_HospitalId_Code",
            table: "document_definitions",
            columns: ["HospitalId", "Code"],
            unique: true);

        _ = migrationBuilder.CreateIndex(
            name: "IX_document_definitions_HospitalId_FacilityId_ClinicalAreaId_D~",
            table: "document_definitions",
            columns: ["HospitalId", "FacilityId", "ClinicalAreaId", "DisciplineId"]);

        _ = migrationBuilder.CreateIndex(
            name: "IX_encounters_HospitalId",
            table: "encounters",
            column: "HospitalId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_encounters_HospitalId_ClinicalAreaId",
            table: "encounters",
            columns: ["HospitalId", "ClinicalAreaId"]);

        _ = migrationBuilder.CreateIndex(
            name: "IX_encounters_HospitalId_FacilityId",
            table: "encounters",
            columns: ["HospitalId", "FacilityId"]);

        _ = migrationBuilder.CreateIndex(
            name: "IX_encounters_HospitalId_PatientId",
            table: "encounters",
            columns: ["HospitalId", "PatientId"]);

        _ = migrationBuilder.CreateIndex(
            name: "IX_encounters_HospitalId_Status",
            table: "encounters",
            columns: ["HospitalId", "Status"]);

        _ = migrationBuilder.CreateIndex(
            name: "IX_facilities_HospitalId",
            table: "facilities",
            column: "HospitalId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_facilities_HospitalId_Code",
            table: "facilities",
            columns: ["HospitalId", "Code"],
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

        _ = migrationBuilder.CreateIndex(
            name: "IX_patients_HospitalId",
            table: "patients",
            column: "HospitalId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_patients_HospitalId_NormalizedFamilyName_NormalizedGivenName",
            table: "patients",
            columns: ["HospitalId", "NormalizedFamilyName", "NormalizedGivenName"]);

        _ = migrationBuilder.CreateIndex(
            name: "IX_patients_HospitalId_NormalizedMrn",
            table: "patients",
            columns: ["HospitalId", "NormalizedMrn"],
            unique: true);

        _ = migrationBuilder.CreateIndex(
            name: "IX_patients_HospitalId_NormalizedNationalId",
            table: "patients",
            columns: ["HospitalId", "NormalizedNationalId"]);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        _ = migrationBuilder.DropTable(
            name: "ai_provider_settings");

        _ = migrationBuilder.DropTable(
            name: "audit_events");

        _ = migrationBuilder.DropTable(
            name: "capability_assignments");

        _ = migrationBuilder.DropTable(
            name: "clinical_documents");

        _ = migrationBuilder.DropTable(
            name: "component_versions");

        _ = migrationBuilder.DropTable(
            name: "document_definitions");

        _ = migrationBuilder.DropTable(
            name: "encounters");

        _ = migrationBuilder.DropTable(
            name: "failure_logs");

        _ = migrationBuilder.DropTable(
            name: "form_response_revisions");

        _ = migrationBuilder.DropTable(
            name: "hospitals");

        _ = migrationBuilder.DropTable(
            name: "patients");

        _ = migrationBuilder.DropTable(
            name: "component_definitions");

        _ = migrationBuilder.DropTable(
            name: "disciplines");

        _ = migrationBuilder.DropTable(
            name: "form_responses");

        _ = migrationBuilder.DropTable(
            name: "clinical_areas");

        _ = migrationBuilder.DropTable(
            name: "form_versions");

        _ = migrationBuilder.DropTable(
            name: "facilities");

        _ = migrationBuilder.DropTable(
            name: "form_definitions");
    }
}
