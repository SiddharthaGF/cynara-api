using Cynara.Application.Modules.FormAi.Persistence;
using Cynara.Domain.FormAi;
using Cynara.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;

namespace Cynara.Infrastructure.Modules.FormAi;

public static class FormAiPersistenceModule
{
    public static IServiceCollection AddFormAiPersistenceModule(
        this IServiceCollection services)
    {
        _ = services.AddScoped<IAiProviderSettingsRepository, AiProviderSettingsRepository>();
        return services;
    }
}

public sealed class AiProviderSettingsRepository(CynaraDbContext dbContext)
    : IAiProviderSettingsRepository
{
    public Task<AiProviderSettings?> GetAsync(
        Guid hospitalId,
        CancellationToken cancellationToken)
    {
        return dbContext.AiProviderSettings
            .SingleOrDefaultAsync(
                item => item.Id == AiProviderSettings.DefaultId
                    && item.HospitalId == hospitalId,
                cancellationToken);
    }

    public void Add(AiProviderSettings settings)
    {
        _ = dbContext.AiProviderSettings.Add(settings);
    }
}

public sealed class AiProviderSettingsConfiguration
    : IEntityTypeConfiguration<AiProviderSettings>
{
    public void Configure(EntityTypeBuilder<AiProviderSettings> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _ = builder.ToTable("ai_provider_settings");
        _ = builder.HasKey(item => new { item.HospitalId, item.Id });
        _ = builder.Property(item => item.HospitalId).IsRequired();
        _ = builder.Property(item => item.Id).HasMaxLength(64);
        _ = builder.Property(item => item.ApiKey).HasMaxLength(2048);
        _ = builder.Property(item => item.BaseUrl).HasMaxLength(1024);
        _ = builder.Property(item => item.Model).HasMaxLength(256);
        _ = builder.Property(item => item.JsonObject);
    }
}
