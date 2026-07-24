using Cynara.Application.Modules.Hospitals;
using Cynara.Domain.Hospitals;
using Cynara.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cynara.Infrastructure.Modules.Hospitals;

/// <summary>
/// Ensures a deterministic bootstrap hospital exists. Used by preview
/// startup, the seed CLI, and the integration test factory.
/// </summary>
public static class HospitalBootstrap
{
    public const string DefaultBootstrapCode = "default";
    public const string DefaultBootstrapName = "Default workspace";

    public static async Task EnsureBootstrapHospitalAsync(
        this IServiceProvider services,
        HospitalBootstrapOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        string code = string.IsNullOrWhiteSpace(options.BootstrapCode)
            ? DefaultBootstrapCode
            : options.BootstrapCode.Trim();
        string name = string.IsNullOrWhiteSpace(options.BootstrapName)
            ? DefaultBootstrapName
            : options.BootstrapName.Trim();
        Hospital.Codes.EnsureValid(code);

        AsyncServiceScope scope = services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            CynaraDbContext dbContext = scope.ServiceProvider
            .GetRequiredService<CynaraDbContext>();

            _ = await EnsureBootstrapHospitalAsync(
                    dbContext,
                    options,
                    code,
                    name,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public static async Task<Hospital> EnsureBootstrapHospitalAsync(
        CynaraDbContext dbContext,
        HospitalBootstrapOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(options);

        string code = string.IsNullOrWhiteSpace(options.BootstrapCode)
            ? DefaultBootstrapCode
            : options.BootstrapCode.Trim();
        string name = string.IsNullOrWhiteSpace(options.BootstrapName)
            ? DefaultBootstrapName
            : options.BootstrapName.Trim();
        Hospital.Codes.EnsureValid(code);

        return await EnsureBootstrapHospitalAsync(
                dbContext,
                options,
                code,
                name,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<Hospital> EnsureBootstrapHospitalAsync(
        CynaraDbContext dbContext,
        HospitalBootstrapOptions options,
        string code,
        string name,
        CancellationToken cancellationToken)
    {
        Hospital? existing = await dbContext.Hospitals
            .SingleOrDefaultAsync(
                item => item.Code == code,
                cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return existing;
        }

        if (!options.AllowAutoBootstrap)
        {
            throw new InvalidOperationException(
                $"Hospital '{code}' does not exist and auto-bootstrap is disabled.");
        }

        Hospital hospital = new()
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = name,
            Status = HospitalStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _ = dbContext.Hospitals.Add(hospital);
        _ = await dbContext.SaveChangesAsync(cancellationToken)
            .ConfigureAwait(false);
        return hospital;
    }
}
