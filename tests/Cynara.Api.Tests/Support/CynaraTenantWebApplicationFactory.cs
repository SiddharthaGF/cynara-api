using Cynara.Application.Modules.Hospitals;
using Cynara.Domain.Hospitals;
using Cynara.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cynara.Api.Tests.Support;

internal sealed class CynaraTenantWebApplicationFactory(
    TestDatabaseSettings database,
    HospitalBootstrapOptions? bootstrapOptions = null,
    bool grantAllCapabilities = true)
    : CynaraWebApplicationFactory(
        database,
        new CynaraWebApplicationFactoryOptions
        {
            BootstrapOptions = bootstrapOptions
                ?? DefaultPrimaryBootstrapOptions(),
            GrantAllCapabilities = grantAllCapabilities,
        })
{
    public const string PrimaryCode = "primary";
    public const string OtherCode = "secondary";

    public CynaraTenantWebApplicationFactory(TestDatabaseSettings database)
        : this(database, bootstrapOptions: null, grantAllCapabilities: true)
    {
    }

    private static HospitalBootstrapOptions DefaultPrimaryBootstrapOptions()
    {
        return new HospitalBootstrapOptions
        {
            BootstrapCode = PrimaryCode,
            BootstrapName = "primary workspace",
            HeaderName = "X-Hospital-Code",
            AllowAutoBootstrap = true,
        };
    }

    public FactoryScope CreateScope()
    {
        CynaraDbContext dbContext = Services
            .GetRequiredService<IServiceScopeFactory>()
            .CreateScope()
            .ServiceProvider
            .GetRequiredService<CynaraDbContext>();
        return new FactoryScope(dbContext);
    }

    public async Task SeedSecondaryHospitalAsync(
        CancellationToken cancellationToken = default)
    {
        CynaraDbContext dbContext = Services
            .GetRequiredService<IServiceScopeFactory>()
            .CreateScope()
            .ServiceProvider
            .GetRequiredService<CynaraDbContext>();
        await using (dbContext)
        {
            bool exists = await dbContext.Hospitals
                .AsNoTracking()
                .AnyAsync(item => item.Code == OtherCode, cancellationToken)
                .ConfigureAwait(false);
            if (exists)
            {
                return;
            }

            dbContext.Hospitals.Add(new Hospital
            {
                Id = Guid.NewGuid(),
                Code = OtherCode,
                Name = "secondary workspace",
                Status = HospitalStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            _ = await dbContext.SaveChangesAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public sealed class FactoryScope : IAsyncDisposable
    {
        public CynaraDbContext DbContext { get; }

        public FactoryScope(CynaraDbContext dbContext)
        {
            DbContext = dbContext;
        }

        public Hospital? PrimaryHospital { get; private set; }

        public Hospital? OtherHospital { get; private set; }

        public Hospital LoadPrimaryHospital()
        {
            return PrimaryHospital ??= LoadHospitalAsync(PrimaryCode).GetAwaiter().GetResult();
        }

        public Hospital LoadOtherHospital()
        {
            return OtherHospital ??= LoadHospitalAsync(OtherCode).GetAwaiter().GetResult();
        }

        public Task<Hospital> LoadHospitalAsync(string code)
        {
            return LoadHospitalAsyncStatic(DbContext, code);
        }

        public async Task<T> UsingAsync<T>(Func<FactoryScope, Task<T>> action)
        {
            return await action(this).ConfigureAwait(false);
        }

        public async Task UsingAsync(Func<FactoryScope, Task> action)
        {
            await action(this).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync().ConfigureAwait(false);
        }

        private static async Task<Hospital> LoadHospitalAsyncStatic(
            CynaraDbContext dbContext,
            string code)
        {
            Hospital? hospital = await dbContext.Hospitals
                .AsNoTracking()
                .SingleOrDefaultAsync(item => item.Code == code)
                .ConfigureAwait(false);
            if (hospital is not null)
            {
                return hospital;
            }

            hospital = new Hospital
            {
                Id = Guid.NewGuid(),
                Code = code,
                Name = code switch
                {
                    PrimaryCode => "primary workspace",
                    OtherCode => "secondary workspace",
                    _ => $"{code} workspace",
                },
                Status = HospitalStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            CynaraDbContext writeContext = dbContext;
            _ = writeContext.Hospitals.Add(hospital);
            _ = await writeContext.SaveChangesAsync().ConfigureAwait(false);
            return hospital;
        }
    }
}
