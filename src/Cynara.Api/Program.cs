using Cynara.Api.Hosting;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Services.AddCynaraApi(builder.Configuration, builder.Environment);

WebApplication app = builder.Build();
await app.UseCynaraApiAsync().ConfigureAwait(false);
await app.RunAsync().ConfigureAwait(false);

internal sealed partial class Program
{
    private Program()
    {
    }
}
