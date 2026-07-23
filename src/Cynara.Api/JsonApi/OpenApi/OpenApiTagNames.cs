using System.Text;

namespace Cynara.Api.JsonApi.OpenApi;

/// <summary>
/// Formats OpenAPI/Scalar tag names as spaced Title Case so JSON:API
/// public names (formDefinitions) match controller tags like "Form AI".
/// </summary>
internal static class OpenApiTagNames
{
    public static string ToTitleCase(string name)
    {
        if (string.IsNullOrWhiteSpace(name)
            || name.Contains(' ', StringComparison.Ordinal))
        {
            return name;
        }

        StringBuilder builder = new(name.Length + 8);
        for (int i = 0; i < name.Length; i++)
        {
            char current = name[i];
            if (i > 0 && char.IsUpper(current) && !char.IsUpper(name[i - 1]))
            {
                _ = builder.Append(' ');
            }

            _ = builder.Append(i == 0 ? char.ToUpperInvariant(current) : current);
        }

        string result = builder.ToString();
        if (result.StartsWith("Ai ", StringComparison.Ordinal))
        {
            result = $"AI {result.AsSpan(3)}";
        }

        return result.Replace(" Ai", " AI", StringComparison.Ordinal);
    }
}
