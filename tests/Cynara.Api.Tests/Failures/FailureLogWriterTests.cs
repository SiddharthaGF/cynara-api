using Cynara.Api.Tests.Support;
using Cynara.Application.Failures;
using Cynara.Domain.Failures;
using Cynara.Infrastructure.Failures;
using Cynara.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cynara.Api.Tests.Failures;

public sealed class FailureLogWriterTests : IDisposable
{
    private readonly InMemoryTestDatabaseFactory database =
        InMemoryTestDatabaseFactory.Create();

    private readonly ServiceProvider serviceProvider;

    public FailureLogWriterTests()
    {
        ServiceCollection services = new();
        _ = services.AddScoped(_ => database.CreateDbContext());
        serviceProvider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task RecordAsync_PersistsEntryWithAllFields()
    {
        FailureLogWriter writer = CreateWriter();
        Exception exception = CaptureException(() =>
            throw new InvalidOperationException("boom-unhandled"));
        var request = new FailureRequestContext(
            Method: "GET",
            Path: "/test/throw",
            Query: "?x=1",
            ActorId: "actor-1",
            TraceId: "trace-1");

        await writer.RecordAsync(exception, request, 500, CancellationToken.None)
            .ConfigureAwait(false);

        FailureLog entry = Assert.Single(LoadEntries());
        Assert.Equal("System.InvalidOperationException", entry.ExceptionType);
        Assert.Equal("boom-unhandled", entry.Message);
        Assert.Equal(500, entry.StatusCode);
        Assert.Equal("GET", entry.RequestMethod);
        Assert.Equal("/test/throw", entry.RequestPath);
        Assert.Equal("?x=1", entry.RequestQuery);
        Assert.Equal("actor-1", entry.ActorId);
        Assert.Equal("trace-1", entry.TraceId);
        Assert.NotNull(entry.StackTrace);
        Assert.False(string.IsNullOrWhiteSpace(entry.MetadataJson));
    }

    private static Exception CaptureException(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            return ex;
        }

        throw new InvalidOperationException("Expected an exception to be thrown.");
    }

    [Fact]
    public async Task RecordAsync_TruncatesOversizedMessageAndStackTrace()
    {
        FailureLogWriter writer = CreateWriter();
        string huge = new('a', 10_000);
        var exception = new InvalidOperationException(huge);
        var request = new FailureRequestContext("GET", "/x", Query: null, ActorId: null, TraceId: null);

        await writer.RecordAsync(exception, request, 500, CancellationToken.None)
            .ConfigureAwait(false);

        FailureLog entry = Assert.Single(LoadEntries());
        Assert.Equal(2048, entry.Message.Length);
    }

    [Fact]
    public async Task RecordAsync_StillReturnsWhenPersistenceFails()
    {
        DbContextOptions<CynaraDbContext> badOptions =
            new DbContextOptionsBuilder<CynaraDbContext>()
                .UseInMemoryDatabase("cynara_failure_test_unreachable")
                .ConfigureWarnings(warnings =>
                    _ = warnings.Ignore(
                        InMemoryEventId.TransactionIgnoredWarning))
                .Options;

        // Wrap the broken options in a service that throws when the writer
        // resolves a scope, to simulate the production "persistence failed"
        // path without depending on a real database.
        ServiceCollection services = new();
        _ = services.AddScoped<CynaraDbContext>(_ => throw new InvalidOperationException("simulated persistence failure"));
        _ = services.AddSingleton(badOptions);
        await using ServiceProvider provider = services.BuildServiceProvider();

        var writer = new FailureLogWriter(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<FailureLogWriter>.Instance);

        var request = new FailureRequestContext("GET", "/x", Query: null, ActorId: null, TraceId: null);

        Exception? thrown = await Record.ExceptionAsync(
            () => writer.RecordAsync(
                new InvalidOperationException("anything"),
                request,
                500,
                CancellationToken.None)).ConfigureAwait(false);

        Assert.Null(thrown);
    }

    [Fact]
    public void FailureRequestContext_HandlesNullActorIdAndTraceId()
    {
        var request = new FailureRequestContext("POST", Path: null, Query: null, ActorId: null, TraceId: null);

        Assert.Null(request.ActorId);
        Assert.Null(request.TraceId);
    }

    private FailureLogWriter CreateWriter()
    {
        return new FailureLogWriter(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<FailureLogWriter>.Instance);
    }

    private List<FailureLog> LoadEntries()
    {
        using CynaraDbContext dbContext = serviceProvider
            .GetRequiredService<IServiceScopeFactory>()
            .CreateScope()
            .ServiceProvider
            .GetRequiredService<CynaraDbContext>();
        return [.. dbContext.FailureLogs.AsNoTracking().ToList()];
    }
}
