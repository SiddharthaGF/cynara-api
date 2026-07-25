namespace Cynara.Application.Modules.FormAi;

public sealed partial class FormAiService
{
    private sealed record ParsedAiOutput(
        string Summary,
        string AssistantMessage,
        DraftTriple Triple,
        bool LimitationOnly,
        bool IsRefusal = false)
    {
        public static ParsedAiOutput Unchanged(
            string summary,
            string message,
            DraftContext draft,
            bool isRefusal = false)
        {
            return new ParsedAiOutput(
                summary,
                message,
                BuildUnchangedTriple(draft),
                LimitationOnly: true,
                IsRefusal: isRefusal);
        }

        private static DraftTriple BuildUnchangedTriple(DraftContext draft)
        {
            return new DraftTriple(
                ParseObjectOrEmpty(draft.ClinicalSchemaJson),
                ParseObjectOrEmpty(draft.UiSchemaJson),
                ParseObjectOrEmpty(draft.RulesSchemaJson));
        }
    }

    private sealed record PartialStringField(string Value, bool Complete);
}
