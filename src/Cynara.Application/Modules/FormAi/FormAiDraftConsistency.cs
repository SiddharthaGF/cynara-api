using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Cynara.Application.Modules.FormAi;

/// <summary>
/// Outcome of the schema-validator fallback chain. The fallback drops layers
/// of the AI draft in a fixed order until the validator is happy again;
/// the enum value records the deepest layer that had to be sacrificed.
/// </summary>
public enum FormAiFallbackOutcome
{
    None = 0,
    DroppedLayout = 1,
    DroppedRulesFields = 2,
    DroppedValidations = 3,
}

public sealed record FormAiFallbackReport(
    FormAiFallbackOutcome Outcome,
    IReadOnlyList<string> DroppedLayers)
{
    public static readonly FormAiFallbackReport NoFallback =
        new(FormAiFallbackOutcome.None, []);

    public bool DiscardedSomething => Outcome != FormAiFallbackOutcome.None;
}

/// <summary>
/// Guards against AI turns that claim (or are expected to produce) draft edits
/// while returning schemas identical to the open draft. Those turns previously
/// streamed a confident assistantMessage and a successful <c>done</c> with no
/// canvas update on the designer.
/// Also enforces honesty when the validator fallback silently strips layers
/// (layout, rule fields, validations) — those turns used to keep a confident
/// assistant claim like "I added the Vitals section" while dropping the
/// corresponding rules/validations behind the scenes.
/// </summary>
internal static partial class FormAiDraftConsistency
{
    private const string MutationUnchangedMessage =
        "AI returned unchanged for a mutation request. Retry or simplify the requirement.";

    private const RegexOptions Options =
        RegexOptions.IgnoreCase
        | RegexOptions.CultureInvariant
        | RegexOptions.ExplicitCapture;

    /// <summary>
    /// User asks to create / add / change form structure (not a pure Q&amp;A).
    /// </summary>
    [GeneratedRegex(
        @"\b(crea|crear|agrega|agregar|a[nñ]ade|a[nñ]adir|arma|armar|genera|generar|dise[nñ]a|dise[nñ]ar|modifica|modificar|actualiza|actualizar|cambia|cambiar|reemplaza|reemplazar|quita|quitar|elimina|eliminar|borra|borrar|incluye|incluir|completa|completar)\b|\b(create|add|build|generate|design|update|modify|change|replace|remove|delete|include|complete|insert)\b[\s\S]{0,80}\b(form|formulario|field|campo|section|secci[oó]n|question|pregunta|schema|draft|borrador)\b|\b(formulario|form)\b[\s\S]{0,40}\b(para|for|de|of)\b",
        Options,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex MutationIntentPattern { get; }

    /// <summary>
    /// Assistant text that asserts the draft was edited.
    /// </summary>
    [GeneratedRegex(
        @"\b(he\s+agregado|agregu[eé]|a[nñ]ad[ií]|he\s+creado|cre[eé]|he\s+actualizado|actualic[eé]|he\s+modificado|modifiqu[eé]|he\s+eliminado|elimin[eé]|he\s+vac[ií]ado|appl(?:y|ied)|added|created|updated|modified|removed|cleared|inserted)\b",
        Options,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex ClaimedApplyPattern { get; }

    public static FormAiChatResponse EnsureConsistent(
        FormAiChatResponse result,
        string? draftClinical,
        string? draftUi,
        string? draftRules,
        string latestUserContent,
        string locale,
        bool isRefusal,
        FormAiFallbackReport fallback = null!)
    {
        ArgumentNullException.ThrowIfNull(result);
        fallback ??= FormAiFallbackReport.NoFallback;

        // The fallback is the most interesting honesty bug: schemas may differ
        // from the draft (so the unchanged check passes), but layers were
        // dropped to satisfy the validator — and the assistant message still
        // claims the work happened.
        if (fallback.DiscardedSomething
            && !isRefusal
            && LooksLikeMutationRequest(latestUserContent)
            && ClaimsDraftApplied(result.AssistantMessage))
        {
            return result with
            {
                AssistantMessage = HonestPartialMessage(locale, fallback),
                Summary = HonestPartialSummary(locale, fallback),
            };
        }

        bool unchanged = SchemasEqual(
            draftClinical,
            draftUi,
            draftRules,
            result.ClinicalSchemaJson,
            result.UiSchemaJson,
            result.RulesSchemaJson);

        if (!unchanged)
        {
            return result;
        }

        bool mutation = LooksLikeMutationRequest(latestUserContent);
        if (mutation && !isRefusal)
        {
            throw new ValidationException(MutationUnchangedMessage);
        }

        if (!ClaimsDraftApplied(result.AssistantMessage))
        {
            return result;
        }

        // Q&A / refusal path: never leave a lying "I added fields" claim.
        return result with
        {
            AssistantMessage = HonestUnchangedMessage(locale),
            Summary = HonestUnchangedSummary(locale),
        };
    }

    private static bool LooksLikeMutationRequest(string? userContent)
    {
        return !string.IsNullOrWhiteSpace(userContent)
            && MutationIntentPattern.IsMatch(userContent);
    }

    private static bool ClaimsDraftApplied(string? assistantMessage)
    {
        return !string.IsNullOrWhiteSpace(assistantMessage)
            && ClaimedApplyPattern.IsMatch(assistantMessage);
    }

    private static bool SchemasEqual(
        string? leftClinical,
        string? leftUi,
        string? leftRules,
        string rightClinical,
        string rightUi,
        string rightRules)
    {
        return JsonNode.DeepEquals(
                   ParseObjectOrEmpty(leftClinical),
                   ParseObjectOrEmpty(rightClinical))
            && JsonNode.DeepEquals(
                   ParseObjectOrEmpty(leftUi),
                   ParseObjectOrEmpty(rightUi))
            && JsonNode.DeepEquals(
                   ParseObjectOrEmpty(leftRules),
                   ParseObjectOrEmpty(rightRules));
    }

    private static string HonestUnchangedMessage(string locale)
    {
        return locale.StartsWith("es", StringComparison.OrdinalIgnoreCase)
            ? "No apliqué cambios al borrador. Reformula el pedido si quieres que agregue o modifique campos."
            : "I did not change the draft. Rephrase if you want me to add or modify fields.";
    }

    private static string HonestUnchangedSummary(string locale)
    {
        return locale.StartsWith("es", StringComparison.OrdinalIgnoreCase)
            ? "Sin cambios en el borrador."
            : "Draft left unchanged.";
    }

    private static string HonestPartialMessage(string locale, FormAiFallbackReport report)
    {
        string layers = string.Join(
            ", ",
            report.DroppedLayers.Select(HumanLayer));
        return locale.StartsWith("es", StringComparison.OrdinalIgnoreCase)
            ? $"Apliqué cambios parciales, pero el motor descartó: {layers}. Pídeme aplicar lo omitido en un turno aparte o simplifica el borrador."
            : $"I applied partial changes, but the engine dropped: {layers}. Ask me to apply the missing parts in a follow-up or simplify the draft.";
    }

    private static string HonestPartialSummary(string locale, FormAiFallbackReport report)
    {
        string layers = string.Join(
            ", ",
            report.DroppedLayers.Select(HumanLayer));
        return locale.StartsWith("es", StringComparison.OrdinalIgnoreCase)
            ? $"Cambios parciales (descartado: {layers})."
            : $"Partial changes (dropped: {layers}).";
    }

    private static string HumanLayer(string layer)
    {
        return layer switch
        {
            "layout" => "layout",
            "rules.fields" => "field rules",
            "rules.validations" => "validations",
            _ => layer,
        };
    }

    private static JsonObject ParseObjectOrEmpty(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonNode.Parse(json) as JsonObject ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }
}
