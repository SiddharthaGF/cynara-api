using Cynara.Api.Hosting;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Cynara.Api.Tests.Auth;

public sealed class IdentityHostingExtensionsTests
{
    [Fact]
    public void ProductionCredentialFiles_AreRequired()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["OpenIddict:SigningCertificatePath"] =
                    Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()),
                ["OpenIddict:SigningKeyPath"] =
                    Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()),
                ["OpenIddict:EncryptionCertificatePath"] =
                    Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()),
                ["OpenIddict:EncryptionKeyPath"] =
                    Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()),
            })
            .Build();
        var services = new ServiceCollection();
        var environment = new TestHostEnvironment
        {
            EnvironmentName = Environments.Production,
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => _ = services.AddCynaraIdentity(configuration, environment));

        Assert.Contains(
            "OpenIddict signing certificate configuration is invalid or unreadable",
            exception.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE KEY", exception.ToString(), StringComparison.Ordinal);
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = typeof(Program).Assembly.FullName!;

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
