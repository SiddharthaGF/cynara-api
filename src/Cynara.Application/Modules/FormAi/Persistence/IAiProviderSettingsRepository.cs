using Cynara.Domain.FormAi;

namespace Cynara.Application.Modules.FormAi.Persistence;

public interface IAiProviderSettingsRepository
{
    public Task<AiProviderSettings?> GetAsync(
        Guid hospitalId,
        CancellationToken cancellationToken);

    public void Add(AiProviderSettings settings);
}
