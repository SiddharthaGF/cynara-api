namespace Cynara.Application.Modules.FormAi;

/// <summary>
/// Provides the canonical <c>form-schema-authoring</c> skill body that the
/// chatbot must respect on every turn. Loaded once at startup and cached.
/// </summary>
public interface IFormAiSkillLoader
{
    /// <summary>
    /// Full skill body (SKILL.md + reference files). Returns an empty string
    /// if the skill cannot be located, so the chat can still respond with a
    /// degraded prompt instead of failing.
    /// </summary>
    public string GetSkillBody();
}
