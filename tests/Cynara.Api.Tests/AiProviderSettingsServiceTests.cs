using Cynara.Application.Modules.FormAi;
using Cynara.Application.Modules.FormAi.Persistence;
using Cynara.Application.Modules.Hospitals;
using Cynara.Application.Persistence;
using Cynara.Domain.FormAi;
using Cynara.Infrastructure.Modules.FormAi;

using Microsoft.Extensions.Configuration;

namespace Cynara.Api.Tests;

public sealed class AiProviderSettingsServiceTests
{
    [Fact]
    public void OpenAiConfiguration_UsesCentralizedDefaults()
    {
        var configuration = new OpenAiConfiguration(
            new ConfigurationBuilder().Build());

        OpenAiConfig config = configuration.LoadEnvironment();

        Assert.Null(config.ApiKey);
        Assert.False(config.Configured);
        Assert.Equal(OpenAiDefaults.BaseUrl, config.BaseUrl);
        Assert.Equal(OpenAiDefaults.Model, config.Model);
        Assert.Equal(OpenAiDefaults.JsonObject, config.JsonObject);
        Assert.Equal(OpenAiDefaults.NetworkTimeout, config.NetworkTimeout);
        Assert.Equal(OpenAiDefaults.FirstChunkTimeout, config.FirstChunkTimeout);
        Assert.Null(config.MaxOutputTokens);
        Assert.Null(config.Temperature);
        Assert.Null(config.TopP);
    }

    [Fact]
    public async Task ResolveActiveConfig_UsesEnvironmentRuntimeKnobsForDatabaseSettings()
    {
        var hospitalContext = new HospitalContext();
        hospitalContext.SetWorkspace(Guid.NewGuid(), "default", "Default workspace");

        var environment = new StubEnvironmentConfiguration
        {
            Config = new OpenAiConfig(
                "env-key",
                "https://env.example/v1",
                "env-model",
                Configured: true,
                JsonObject: false,
                NetworkTimeout: TimeSpan.FromMinutes(2),
                MaxOutputTokens: 1234,
                Temperature: 0.7f,
                TopP: 0.8f,
                FirstChunkTimeout: TimeSpan.FromSeconds(7)),
        };
        var repository = new StubSettingsRepository
        {
            Settings = new AiProviderSettings
            {
                Id = AiProviderSettings.DefaultId,
                HospitalId = hospitalContext.HospitalId,
                ApiKey = "db-key",
                BaseUrl = "https://db.example/v1",
                Model = "db-model",
                JsonObject = true,
            },
        };
        var service = new AiProviderSettingsService(
            repository,
            environment,
            new StubUnitOfWork(),
            hospitalContext,
            TimeProvider.System,
            new FakeCapabilityGuard());

        OpenAiConfig config = await service.ResolveActiveConfigAsync(CancellationToken.None);

        Assert.Equal("db-key", config.ApiKey);
        Assert.Equal("https://db.example/v1", config.BaseUrl);
        Assert.Equal("db-model", config.Model);
        Assert.True(config.JsonObject);
        Assert.Equal(1234, config.MaxOutputTokens);
        Assert.Equal(0.7f, config.Temperature);
        Assert.Equal(0.8f, config.TopP);
        Assert.Equal(TimeSpan.FromMinutes(2), config.NetworkTimeout);
        Assert.Equal(TimeSpan.FromSeconds(7), config.FirstChunkTimeout);
    }

    private sealed class StubEnvironmentConfiguration : IOpenAiConfiguration
    {
        public OpenAiConfig Config { get; init; } = null!;

        public OpenAiConfig LoadEnvironment()
        {
            return Config;
        }
    }

    private sealed class StubSettingsRepository : IAiProviderSettingsRepository
    {
        public AiProviderSettings? Settings { get; init; }

        public Task<AiProviderSettings?> GetAsync(
            Guid hospitalId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Settings);
        }

        public void Add(AiProviderSettings settings)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class StubUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(0);
        }
    }
}
