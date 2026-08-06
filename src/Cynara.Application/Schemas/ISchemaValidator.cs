namespace Cynara.Application.Schemas;

public interface ISchemaValidator
{
    public void ValidateComponentDraft(string clinicalSchemaJson, string? uiSchemaJson);

    public void ValidateFormDraft(string clinicalSchemaJson, string? uiSchemaJson, string? rulesSchemaJson = null);

    public void ValidateWorkflowDraft(string workflowSchemaJson);

    public void ValidateWorkflowForPublish(string workflowSchemaJson);
}
