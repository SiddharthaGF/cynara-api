using Cynara.Domain.Failures;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cynara.Infrastructure.Failures;

public sealed class FailureLogEntityConfiguration
    : IEntityTypeConfiguration<FailureLog>
{
    public void Configure(EntityTypeBuilder<FailureLog> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _ = builder.ToTable("failure_logs");
        _ = builder.HasKey(item => item.Id);
        _ = builder.Property(item => item.ExceptionType).HasMaxLength(256).IsRequired();
        _ = builder.Property(item => item.Message).IsRequired();
        _ = builder.Property(item => item.RequestMethod).HasMaxLength(16);
        _ = builder.Property(item => item.RequestPath).HasMaxLength(512);
        _ = builder.Property(item => item.TraceId).HasMaxLength(64);
        _ = builder.Property(item => item.ActorId).HasMaxLength(128);
        _ = builder.HasIndex(item => item.OccurredAt);
        _ = builder.HasIndex(item => item.ExceptionType);
        _ = builder.HasIndex(item => item.TraceId);
    }
}
