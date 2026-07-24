using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.Extensions.Options;

using Swashbuckle.AspNetCore.SwaggerGen;

namespace Cynara.Api.JsonApi.OpenApi;

/// <summary>
/// Post-JsonApiDotNetCore Swagger tweaks that must run after
/// <c>ConfigureSwaggerGenOptions</c>:
/// <list type="bullet">
/// <item>
/// Title-case OpenAPI tags so Scalar matches controller tags like "Form AI".
/// </item>
/// <item>
/// Register <see cref="ActorIdOperationFilter"/> last so JADNC's documentation
/// filter can still distinguish collection (0 params) vs get-by-id (1 param)
/// before <c>X-Actor-Id</c> is injected.
/// </item>
/// </list>
/// </summary>
internal sealed class CynaraSwaggerGenConfigureOptions
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

        options.OperationFilter<ActorIdOperationFilter>();
    }
}
