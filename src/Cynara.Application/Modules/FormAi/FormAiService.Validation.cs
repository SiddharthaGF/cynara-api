using System.Text.Json.Nodes;

using Cynara.Application.Common;
using Cynara.Application.Schemas;

namespace Cynara.Application.Modules.FormAi;

public sealed partial class FormAiService
{
    private static bool TryValidateWithFallback(
        ISchemaValidator schemaValidator,
        string clinical,
        JsonObject uiObject,
        JsonObject rulesObject,
        out string ui,
        out string rules,
        out FormAiFallbackReport fallback)
    {
        // 1) Drop layout only — keep field rules and validations.
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

        // 2) Drop field rules only — keep validations.
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

        // 3) Last resort: empty rules validations too.
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
