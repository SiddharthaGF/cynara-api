using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cynara.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_provider_settings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    HospitalId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApiKey = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    BaseUrl = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Model = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    JsonObject = table.Column<bool>(type: "boolean", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_provider_settings", x => new { x.HospitalId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "audit_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HospitalId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ActorId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: true),
                    EncounterId = table.Column<Guid>(type: "uuid", nullable: true),
                    WorkflowDefinitionId = table.Column<Guid>(type: "uuid", nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    MetadataJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "capability_assignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HospitalId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Capability = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Scope = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "hospital"),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AssignedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    RowVersion = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_capability_assignments", x => x.Id);
                });

            migrationBuilder.CreateTable(
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
                    CanceledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EnteredInErrorAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EnteredInErrorReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    EnteredInErrorById = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RowVersion = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clinical_documents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "component_definitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HospitalId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_component_definitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
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
                    RowVersion = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_encounters", x => x.Id);
                });

            migrationBuilder.CreateTable(
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
                    RowVersion = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_facilities", x => x.Id);
                });

            migrationBuilder.CreateTable(
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
                    MetadataJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_failure_logs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "form_definitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HospitalId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_form_definitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
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
                    RowVersion = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_hospitals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "invitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    HospitalId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProfileSnapshot = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    LinkVersion = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RowVersion = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invitations", x => x.Id);
                });

            migrationBuilder.CreateTable(
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
                    BloodType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RowVersion = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_patients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_definitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HospitalId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_definitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
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
                    RetiredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_component_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_component_versions_component_definitions_ComponentDefinitio~",
                        column: x => x.ComponentDefinitionId,
                        principalTable: "component_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
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
                    FacilityId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clinical_areas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_clinical_areas_facilities_FacilityId",
                        column: x => x.FacilityId,
                        principalTable: "facilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
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
                    LastReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
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
                name: "workflow_versions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HospitalId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    WorkflowSchemaJson = table.Column<string>(type: "text", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RowVersion = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SubmittedForReviewAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RetiredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PublishedSchemaVersion = table.Column<string>(type: "text", nullable: true),
                    LastReviewComment = table.Column<string>(type: "text", nullable: true),
                    LastReviewDecision = table.Column<string>(type: "text", nullable: true),
                    LastReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_workflow_versions_workflow_definitions_WorkflowDefinitionId",
                        column: x => x.WorkflowDefinitionId,
                        principalTable: "workflow_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
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
                    ClinicalAreaId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_disciplines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_disciplines_clinical_areas_ClinicalAreaId",
                        column: x => x.ClinicalAreaId,
                        principalTable: "clinical_areas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
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
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
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
                name: "workflow_pipelines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HospitalId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    EncounterId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    RowVersion = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_definitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_document_definitions_clinical_areas_ClinicalAreaId",
                        column: x => x.ClinicalAreaId,
                        principalTable: "clinical_areas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_document_definitions_disciplines_DisciplineId",
                        column: x => x.DisciplineId,
                        principalTable: "disciplines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_document_definitions_facilities_FacilityId",
                        column: x => x.FacilityId,
                        principalTable: "facilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_document_definitions_form_definitions_FormDefinitionId",
                        column: x => x.FormDefinitionId,
                        principalTable: "form_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_document_definitions_form_versions_FormVersionId",
                        column: x => x.FormVersionId,
                        principalTable: "form_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
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
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
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

            migrationBuilder.CreateTable(
                name: "clinical_tasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HospitalId = table.Column<Guid>(type: "uuid", nullable: false),
                    PipelineId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
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
                name: "IX_audit_events_HospitalId",
                table: "audit_events",
                column: "HospitalId");

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_HospitalId_ActorId",
                table: "audit_events",
                columns: new[] { "HospitalId", "ActorId" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_HospitalId_EncounterId",
                table: "audit_events",
                columns: new[] { "HospitalId", "EncounterId" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_HospitalId_OccurredAt",
                table: "audit_events",
                columns: new[] { "HospitalId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_HospitalId_PatientId",
                table: "audit_events",
                columns: new[] { "HospitalId", "PatientId" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_HospitalId_ResourceType_ResourceId",
                table: "audit_events",
                columns: new[] { "HospitalId", "ResourceType", "ResourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_HospitalId_WorkflowDefinitionId",
                table: "audit_events",
                columns: new[] { "HospitalId", "WorkflowDefinitionId" });

            migrationBuilder.CreateIndex(
                name: "IX_capability_assignments_ActorId_Capability",
                table: "capability_assignments",
                columns: new[] { "ActorId", "Capability" },
                unique: true,
                filter: "\"Scope\" = 'platform'");

            migrationBuilder.CreateIndex(
                name: "IX_capability_assignments_HospitalId",
                table: "capability_assignments",
                column: "HospitalId");

            migrationBuilder.CreateIndex(
                name: "IX_capability_assignments_HospitalId_ActorId_Capability",
                table: "capability_assignments",
                columns: new[] { "HospitalId", "ActorId", "Capability" },
                unique: true,
                filter: "\"Scope\" = 'hospital'");

            migrationBuilder.CreateIndex(
                name: "IX_clinical_areas_FacilityId",
                table: "clinical_areas",
                column: "FacilityId");

            migrationBuilder.CreateIndex(
                name: "IX_clinical_areas_HospitalId",
                table: "clinical_areas",
                column: "HospitalId");

            migrationBuilder.CreateIndex(
                name: "IX_clinical_areas_HospitalId_Code",
                table: "clinical_areas",
                columns: new[] { "HospitalId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_clinical_documents_HospitalId",
                table: "clinical_documents",
                column: "HospitalId");

            migrationBuilder.CreateIndex(
                name: "IX_clinical_documents_HospitalId_DocumentDefinitionId_Encounte~",
                table: "clinical_documents",
                columns: new[] { "HospitalId", "DocumentDefinitionId", "EncounterId" });

            migrationBuilder.CreateIndex(
                name: "IX_clinical_documents_HospitalId_EncounterId",
                table: "clinical_documents",
                columns: new[] { "HospitalId", "EncounterId" });

            migrationBuilder.CreateIndex(
                name: "IX_clinical_documents_HospitalId_PatientId",
                table: "clinical_documents",
                columns: new[] { "HospitalId", "PatientId" });

            migrationBuilder.CreateIndex(
                name: "IX_clinical_documents_HospitalId_Status",
                table: "clinical_documents",
                columns: new[] { "HospitalId", "Status" });

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
                name: "IX_clinical_tasks_HospitalId_WorkflowDefinitionId",
                table: "clinical_tasks",
                columns: new[] { "HospitalId", "WorkflowDefinitionId" });

            migrationBuilder.CreateIndex(
                name: "IX_clinical_tasks_PipelineId",
                table: "clinical_tasks",
                column: "PipelineId");

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
                name: "IX_disciplines_ClinicalAreaId",
                table: "disciplines",
                column: "ClinicalAreaId");

            migrationBuilder.CreateIndex(
                name: "IX_disciplines_HospitalId",
                table: "disciplines",
                column: "HospitalId");

            migrationBuilder.CreateIndex(
                name: "IX_disciplines_HospitalId_Code",
                table: "disciplines",
                columns: new[] { "HospitalId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_document_definitions_ClinicalAreaId",
                table: "document_definitions",
                column: "ClinicalAreaId");

            migrationBuilder.CreateIndex(
                name: "IX_document_definitions_DisciplineId",
                table: "document_definitions",
                column: "DisciplineId");

            migrationBuilder.CreateIndex(
                name: "IX_document_definitions_FacilityId",
                table: "document_definitions",
                column: "FacilityId");

            migrationBuilder.CreateIndex(
                name: "IX_document_definitions_FormDefinitionId",
                table: "document_definitions",
                column: "FormDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_document_definitions_FormVersionId",
                table: "document_definitions",
                column: "FormVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_document_definitions_HospitalId",
                table: "document_definitions",
                column: "HospitalId");

            migrationBuilder.CreateIndex(
                name: "IX_document_definitions_HospitalId_Code",
                table: "document_definitions",
                columns: new[] { "HospitalId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_document_definitions_HospitalId_FacilityId_ClinicalAreaId_D~",
                table: "document_definitions",
                columns: new[] { "HospitalId", "FacilityId", "ClinicalAreaId", "DisciplineId" });

            migrationBuilder.CreateIndex(
                name: "IX_encounters_HospitalId",
                table: "encounters",
                column: "HospitalId");

            migrationBuilder.CreateIndex(
                name: "IX_encounters_HospitalId_ClinicalAreaId",
                table: "encounters",
                columns: new[] { "HospitalId", "ClinicalAreaId" });

            migrationBuilder.CreateIndex(
                name: "IX_encounters_HospitalId_FacilityId",
                table: "encounters",
                columns: new[] { "HospitalId", "FacilityId" });

            migrationBuilder.CreateIndex(
                name: "IX_encounters_HospitalId_PatientId",
                table: "encounters",
                columns: new[] { "HospitalId", "PatientId" });

            migrationBuilder.CreateIndex(
                name: "IX_encounters_HospitalId_Status",
                table: "encounters",
                columns: new[] { "HospitalId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_facilities_HospitalId",
                table: "facilities",
                column: "HospitalId");

            migrationBuilder.CreateIndex(
                name: "IX_facilities_HospitalId_Code",
                table: "facilities",
                columns: new[] { "HospitalId", "Code" },
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

            migrationBuilder.CreateIndex(
                name: "IX_invitations_HospitalId",
                table: "invitations",
                column: "HospitalId");

            migrationBuilder.CreateIndex(
                name: "IX_invitations_TokenHash",
                table: "invitations",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_patients_HospitalId",
                table: "patients",
                column: "HospitalId");

            migrationBuilder.CreateIndex(
                name: "IX_patients_HospitalId_NormalizedFamilyName_NormalizedGivenName",
                table: "patients",
                columns: new[] { "HospitalId", "NormalizedFamilyName", "NormalizedGivenName" });

            migrationBuilder.CreateIndex(
                name: "IX_patients_HospitalId_NormalizedMrn",
                table: "patients",
                columns: new[] { "HospitalId", "NormalizedMrn" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_patients_HospitalId_NormalizedNationalId",
                table: "patients",
                columns: new[] { "HospitalId", "NormalizedNationalId" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_definitions_HospitalId",
                table: "workflow_definitions",
                column: "HospitalId");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_definitions_HospitalId_Code",
                table: "workflow_definitions",
                columns: new[] { "HospitalId", "Code" },
                unique: true);

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
                name: "IX_workflow_pipelines_HospitalId_EncounterId",
                table: "workflow_pipelines",
                columns: new[] { "HospitalId", "EncounterId" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_pipelines_HospitalId_PatientId",
                table: "workflow_pipelines",
                columns: new[] { "HospitalId", "PatientId" });

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

            migrationBuilder.CreateIndex(
                name: "IX_workflow_versions_HospitalId",
                table: "workflow_versions",
                column: "HospitalId");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_versions_HospitalId_WorkflowDefinitionId_Version",
                table: "workflow_versions",
                columns: new[] { "HospitalId", "WorkflowDefinitionId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workflow_versions_WorkflowDefinitionId",
                table: "workflow_versions",
                column: "WorkflowDefinitionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_provider_settings");

            migrationBuilder.DropTable(
                name: "audit_events");

            migrationBuilder.DropTable(
                name: "capability_assignments");

            migrationBuilder.DropTable(
                name: "clinical_documents");

            migrationBuilder.DropTable(
                name: "clinical_tasks");

            migrationBuilder.DropTable(
                name: "component_versions");

            migrationBuilder.DropTable(
                name: "document_definitions");

            migrationBuilder.DropTable(
                name: "encounters");

            migrationBuilder.DropTable(
                name: "failure_logs");

            migrationBuilder.DropTable(
                name: "form_response_revisions");

            migrationBuilder.DropTable(
                name: "hospitals");

            migrationBuilder.DropTable(
                name: "invitations");

            migrationBuilder.DropTable(
                name: "patients");

            migrationBuilder.DropTable(
                name: "workflow_pipeline_history");

            migrationBuilder.DropTable(
                name: "component_definitions");

            migrationBuilder.DropTable(
                name: "disciplines");

            migrationBuilder.DropTable(
                name: "form_responses");

            migrationBuilder.DropTable(
                name: "workflow_pipelines");

            migrationBuilder.DropTable(
                name: "clinical_areas");

            migrationBuilder.DropTable(
                name: "form_versions");

            migrationBuilder.DropTable(
                name: "workflow_versions");

            migrationBuilder.DropTable(
                name: "facilities");

            migrationBuilder.DropTable(
                name: "form_definitions");

            migrationBuilder.DropTable(
                name: "workflow_definitions");
        }
    }
}
