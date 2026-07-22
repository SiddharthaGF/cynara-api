using System.Text.Json;

using Cynara.Application.Failures;
using Cynara.Domain.Failures;
using Cynara.Infrastructure.Persistence;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Cynara.Infrastructure.Failures;

public sealed class FailureLogWriter(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<FailureLogWriter> logger) : IFailureLogWriter
{
    private const int MessageMaxLength = 2048;
    private const int StackTraceMaxLength = 16 * 1024;

    private static readonly JsonSerializerOptions MetadataJsonOptions = new()
    {
        WriteIndented = false,
    };

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Failure logging must never propagate exceptions to the caller of the exception handler; if persistence fails we only log via ILogger.")]
    public async Task RecordAsync(
        Exception exception,
        FailureRequestContext context,
        int statusCode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(context);

        FailureLog entry = BuildEntry(exception, context, statusCode);

        try
        {
            AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                CynaraDbContext dbContext = scope.ServiceProvider
                    .GetRequiredService<CynaraDbContext>();
                _ = dbContext.FailureLogs.Add(entry);
                _ = await dbContext.SaveChangesAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception writerException)
        {
            logger.LogError(
                writerException,
                "Failed to persist failure log entry {FailureId} ({ExceptionType}).",
                entry.Id,
                entry.ExceptionType);
        }
    }

    private FailureLog BuildEntry(Exception exception, FailureRequestContext context, int statusCode)
    {
        DateTimeOffset occurredAt = timeProvider.GetUtcNow();
        string? metadata = SerializeMetadata(occurredAt, context.TraceId);

        return new FailureLog
        {
            Id = Guid.NewGuid(),
            OccurredAt = occurredAt,
            ExceptionType = Truncate(exception.GetType().FullName ?? exception.GetType().Name, 256),
            Message = Truncate(exception.Message, MessageMaxLength),
            StackTrace = exception.StackTrace is null
                ? null
                : Truncate(exception.StackTrace, StackTraceMaxLength),
            RequestMethod = Truncate(context.Method, 16),
            RequestPath = Truncate(context.Path, 512),
            RequestQuery = context.Query,
            StatusCode = statusCode,
            TraceId = Truncate(context.TraceId, 64),
            ActorId = Truncate(context.ActorId, 128),
            MetadataJson = metadata,
        };
    }

    private static string SerializeMetadata(DateTimeOffset occurredAt, string? traceId)
    {
        var payload = new
        {
            occurredAt,
            traceId,
            environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"),
            machineName = Environment.MachineName,
        };

        return JsonSerializer.Serialize(payload, MetadataJsonOptions);
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
