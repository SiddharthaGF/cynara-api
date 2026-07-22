namespace Cynara.Application.Forms;

public sealed record FormCompilationResult(
    string ClinicalSchemaJson,
    string? UiSchemaJson,
    string? RulesSchemaJson,
    string DependencyMetadataJson,
    string ContentHash);
