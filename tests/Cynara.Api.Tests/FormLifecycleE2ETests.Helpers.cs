using System.Text.Json;

namespace Cynara.Api.Tests;

public sealed partial class FormLifecycleE2ETests
{
    private FormWebApplicationFactory Factory { get; }

    private async Task CreateAndPublishComponentAsync(
        string code,
        string clinical,
        string ui)
    {
        string definitionId = await Workflow.CreateComponentDefinitionAsync(
            code,
            code,
            clinical,
            ui).ConfigureAwait(false);
        string draftId = await Workflow.GetComponentDraftIdAsync(definitionId)
            .ConfigureAwait(false);
        await Workflow.PublishComponentAsync(draftId).ConfigureAwait(false);
    }

    private static string BpClinicalSchema()
    {
        return /*lang=json,strict*/ """
            {
              "schemaVersion": "1.0.0",
              "fields": [
                { "id": "systolic", "code": "vital.bp.systolic", "type": "integer" },
                { "id": "diastolic", "code": "vital.bp.diastolic", "type": "integer" }
              ]
            }
            """;
    }

    private static string BpValidationRulesSchema()
    {
        return /*lang=json,strict*/ """
            {
              "schemaVersion": "1.0.0",
              "clinicalSchemaVersion": "1.0.0",
              "fields": {},
              "validations": [
                {
                  "code": "BP_SYSTOLIC_GT_DIASTOLIC",
                  "message": "Systolic must be greater than diastolic",
                  "when": {
                    "op": "and",
                    "args": [
                      { "op": "not", "args": [{ "op": "empty", "args": [{ "ref": "vital.bp.systolic" }] }] },
                      { "op": "not", "args": [{ "op": "empty", "args": [{ "ref": "vital.bp.diastolic" }] }] }
                    ]
                  },
                  "assert": {
                    "op": "gt",
                    "args": [
                      { "ref": "vital.bp.systolic" },
                      { "ref": "vital.bp.diastolic" }
                    ]
                  }
                }
              ]
            }
            """;
    }

    private static string FormWithComponentRefAndVitals(string sectionId, string sectionCode)
    {
        return JsonSerializer.Serialize(new
        {
            schemaVersion = "1.0.0",
            fields = new object[]
            {
                new
                {
                    id = sectionId,
                    code = sectionCode,
                    type = "component-ref",
                    componentCode = "patient-demographics",
                    componentVersion = "1.0.0",
                },
                new
                {
                    id = "systolic",
                    code = "vital.bp.systolic",
                    type = "integer",
                },
                new
                {
                    id = "diastolic",
                    code = "vital.bp.diastolic",
                    type = "integer",
                },
            },
        });
    }
}
