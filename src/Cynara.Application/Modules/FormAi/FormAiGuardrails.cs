using System.Text.RegularExpressions;

namespace Cynara.Application.Modules.FormAi;

internal enum FormAiGuardCode
{
    NetworkForbidden = 0,
    CapabilityForbidden = 1,
    OutOfScope = 2,
}

internal sealed record FormAiGuardViolation(FormAiGuardCode Code, string Message);

internal static partial class FormAiGuardrails
{
    [GeneratedRegex(@"\b(search|browse|google|bing)\s+(the\s+)?(web|internet|online)\b|\b(web|internet|online)\s+search\b|\b(fetch|scrape|crawl|download)\b[\s\S]{0,40}\b(url|website|web\s*page|site|internet)\b|\b(open|visit|navigate\s+to)\b[\s\S]{0,40}\b(https?://|www\.)|\bbusca(?:r)?\s+(en\s+)?(internet|la\s+web|google|bing)\b|\bnavega(?:r)?\b[\s\S]{0,40}\b(web|internet|url|sitio|p[aá]gina)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NetworkPattern();

    [GeneratedRegex(@"\b(tool[_ -]?calls?|function[_ -]?calls?|mcp\b|plugins?)\b|\b(ejecuta(?:r)?|run|execute)\b[\s\S]{0,40}\b(c[oó]digo|code|shell|terminal|comando|command|script)\b|\b(instala(?:r)?|install)\b[\s\S]{0,30}\b(paquete|package|npm|pip|dependency)\b|\b(llama(?:r)?|call)\b[\s\S]{0,30}\b(api\s+externa|external\s+api|third[ -]party\s+api)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CapabilityPattern();

    [GeneratedRegex(@"\b(cu[eé]ntame\s+un\s+chiste|tell\s+me\s+a\s+joke|escribe\s+un\s+poema|write\s+a\s+poem)\b|\b(ignora(?:r)?\s+(las\s+)?(reglas|instrucciones)|ignore\s+(all\s+)?(previous\s+)?(rules|instructions))\b|\b(act[uú]a\s+como|pretend\s+you\s+are|jailbreak|dan\s+mode)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OutOfScopePattern();

    [GeneratedRegex(@"\b(vac[ií]a|vaciar|limpia|limpiar|borra|borrar|elimina|eliminar)\b[\s\S]{0,48}\b(el\s+)?formulario\b|\b(borra|borrar|elimina|eliminar|limpia|limpiar)\s+todo\b|\bempezar\s+de\s+(0|cero|nuevo)\b|\b(clear|empty|reset)\b[\s\S]{0,48}\b(the\s+)?form\b|\b(clear|empty|wipe)\s+(all\s+)?(fields|questions)\b|\bstart\s+(over|from\s+scratch|from\s+zero)\b|\bremove\s+all\s+(fields|questions)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DraftResetPattern();

    public static FormAiGuardViolation? Detect(string content, string locale)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        FormAiGuardCode? code;
        if (NetworkPattern().IsMatch(content))
        {
            code = FormAiGuardCode.NetworkForbidden;
        }
        else if (CapabilityPattern().IsMatch(content))
        {
            code = FormAiGuardCode.CapabilityForbidden;
        }
        else
        {
            code = OutOfScopePattern().IsMatch(content) ? FormAiGuardCode.OutOfScope : null;
        }

        return code is null ? null : new(code.Value, LimitationMessage(code.Value, locale));
    }

    public static bool IsDraftReset(string content)
    {
        return !string.IsNullOrWhiteSpace(content)
            && DraftResetPattern().IsMatch(content);
    }

    public static string LimitationMessage(FormAiGuardCode code, string locale)
    {
        bool spanish = locale.StartsWith("es", StringComparison.OrdinalIgnoreCase);
        return code switch
        {
            FormAiGuardCode.NetworkForbidden => spanish
                ? "No puedo usar internet, abrir URLs ni buscar datos externos. Solo genero o corrijo el formulario abierto. Reformula tu pedido como un requisito de formulario."
                : "I cannot use the internet, open URLs, or fetch external data. I only generate or correct the open form. Rephrase as a form requirement.",
            FormAiGuardCode.CapabilityForbidden => spanish
                ? "No puedo ejecutar herramientas, código, terminal ni APIs externas. Solo redacto el formulario abierto. Reformula como un cambio de formulario."
                : "I cannot run tools, code, shells, or external APIs. I only author the open form. Rephrase as a form change.",
            FormAiGuardCode.OutOfScope => spanish
                ? "Este chat solo crea y corrige formularios clínicos de Cynara. Reformula tu mensaje como un requisito o cambio de formulario."
                : "This chat only authors and corrects Cynara clinical forms. Rephrase your message as a form requirement or schema change.",
            _ => spanish
                            ? "Este chat solo crea y corrige formularios clínicos de Cynara. Reformula tu mensaje como un requisito o cambio de formulario."
                            : "This chat only authors and corrects Cynara clinical forms. Rephrase your message as a form requirement or schema change.",
        };
    }

    public static string LimitationSummary(string locale)
    {
        return locale.StartsWith("es", StringComparison.OrdinalIgnoreCase)
            ? "Fuera de alcance: sin cambios en el borrador."
            : "Out of scope: draft left unchanged.";
    }

    public static string DraftResetMessage(string locale)
    {
        return locale.StartsWith("es", StringComparison.OrdinalIgnoreCase)
            ? "Listo: vacié el borrador. Puedes empezar de cero."
            : "Done - I cleared the draft. You can start from scratch.";
    }

    public static string DraftResetSummary(string locale)
    {
        return locale.StartsWith("es", StringComparison.OrdinalIgnoreCase)
            ? "Formulario vaciado."
            : "Form cleared.";
    }
}
