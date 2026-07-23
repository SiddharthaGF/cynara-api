using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.Extensions.Options;

using Swashbuckle.AspNetCore.SwaggerGen;

namespace Cynara.Api.JsonApi.OpenApi;

/// <summary>
/// Wraps Swashbuckle's tag selector after JsonApiDotNetCore configures it so
/// Scalar sidebar groups use Title Case (and XmlCommentsDocumentFilter does
/// not reintroduce camelCase public names).
/// </summary>
internal sealed class TitleCaseOpenApiTagsConfigureOptions
    : IConfigureOptions<SwaggerGenOptions>
{
    public void Configure(SwaggerGenOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        Func<ApiDescription, IList<string>> inner =
            options.SwaggerGeneratorOptions.TagsSelector;

        options.TagActionsBy(description =>
        {
            IList<string> tags = inner(description);
            if (tags.Count == 0)
            {
                return tags;
            }

            return [.. tags.Select(OpenApiTagNames.ToTitleCase)];
        });
    }
}
