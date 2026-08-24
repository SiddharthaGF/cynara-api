using System.Text.Json.Nodes;

using Cynara.Application.Schemas;

namespace Cynara.Application.Modules.FormAi;

public sealed partial class FormAiService
{
    /// <summary>
    /// Tries validation with a progressive degradation ladder: first drop
    /// layout only, then field rules, and finally validations too, keeping
    /// as much model output as the schema validator accepts.
    /// </summary>
    private static bool TryValidateWithFallback(
        ISchemaValidator schemaValidator,
        string clinical,
        JsonObject uiObject,
        JsonObject rulesObject,
        out string ui,
        out string rules,
        out FormAiFallbackReport fallback)
    {
        var layoutClearedUi = (JsonObject)uiObject.DeepClone();
        layoutClearedUi[SchemaJsonKeys.Layout] = new JsonArray();
        if (TryValidate(
                schemaValidator,
                clinical,
                layoutClearedUi,
                rulesObject,
                out ui,
                out rules))
        {
            fallback = new FormAiFallbackReport(
                FormAiFallbackOutcome.DroppedLayout,
                LayoutOnly);
            return true;
        }

        var fieldsClearedRules = (JsonObject)rulesObject.DeepClone();
        fieldsClearedRules[SchemaJsonKeys.Fields] = new JsonObject();
        if (TryValidate(
                schemaValidator,
                clinical,
                layoutClearedUi,
                fieldsClearedRules,
                out ui,
                out rules))
        {
            fallback = new FormAiFallbackReport(
                FormAiFallbackOutcome.DroppedRulesFields,
                LayoutAndRulesFields);
            return true;
        }

        fieldsClearedRules[SchemaJsonKeys.Validations] = new JsonArray();
        bool ok = TryValidate(
            schemaValidator,
            clinical,
            layoutClearedUi,
            fieldsClearedRules,
            out ui,
            out rules);
        fallback = ok
            ? new FormAiFallbackReport(
                FormAiFallbackOutcome.DroppedValidations,
                LayoutRulesFieldsAndValidations)
            : FormAiFallbackReport.NoFallback;
        return ok;
    }

    private static bool TryValidate(
        ISchemaValidator schemaValidator,
        string clinical,
        JsonObject uiObject,
        JsonObject rulesObject,
        out string ui,
        out string rules)
    {
        ui = uiObject.ToJsonString();
        rules = rulesObject.ToJsonString();
        try
        {
            schemaValidator.ValidateFormDraft(clinical, ui, rules);
            return true;
        }
        catch (ValidationException)
        {
            return false;
        }
    }
}
