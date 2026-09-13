using System.Text.Json;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevalCopilot.Infrastructure.Configurations.Runs;

public sealed class AttemptConfiguration : IEntityTypeConfiguration<Attempt>
{
    private static readonly ValueComparer<IReadOnlyList<string>> ProcessArgumentsComparer = new(
        (left, right) => (left ?? Array.Empty<string>()).SequenceEqual(right ?? Array.Empty<string>()),
        value => value.Aggregate(0, (hash, argument) => HashCode.Combine(hash, argument)),
        value => value.ToArray());

    public void Configure(EntityTypeBuilder<Attempt> builder)
    {
        builder.ToTable("attempts");
        builder.HasKey(attempt => attempt.Id);
        builder.Property(attempt => attempt.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        // Every attempt recorded before Kind existed is classified Simulated, not left blank.
        builder.Property(attempt => attempt.Kind)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired()
            .HasDefaultValue(AttemptKind.Simulated);

        // Process-only columns: null for every Simulated attempt. Never a secret — the
        // adapter's own environment allowlist is never persisted (see ProcessExecutionIntent).
        builder.Property(attempt => attempt.ProcessExecutablePath).HasMaxLength(1024);
        builder.Property(attempt => attempt.ProcessWorkingDirectory).HasMaxLength(1024);
        builder.Property(attempt => attempt.ProcessApprovedRoot).HasMaxLength(1024);
        builder.Property(attempt => attempt.ProcessOutcome).HasConversion<string>().HasMaxLength(32);

        // JSON is purely an Infrastructure/EF storage detail: Attempt.ProcessArguments is a
        // plain typed IReadOnlyList<string> to every Domain and Application caller.
        builder.Property(attempt => attempt.ProcessArguments)
            .HasConversion(
                arguments => arguments.Count == 0 ? string.Empty : JsonSerializer.Serialize(arguments, (JsonSerializerOptions?)null),
                json => string.IsNullOrEmpty(json)
                    ? Array.Empty<string>()
                    : JsonSerializer.Deserialize<string[]>(json, (JsonSerializerOptions?)null)!)
            .IsRequired()
            .Metadata.SetValueComparer(ProcessArgumentsComparer);

        // Stored as whole milliseconds: TimeSpan has no native SQLite representation, and a
        // process timeout has no need for finer precision than a millisecond.
        builder.Property(attempt => attempt.ProcessTimeout)
            .HasConversion(
                timeout => timeout.HasValue ? (long?)timeout.Value.TotalMilliseconds : null,
                milliseconds => milliseconds.HasValue ? TimeSpan.FromMilliseconds(milliseconds.Value) : (TimeSpan?)null);

        builder.HasOne<Run>().WithMany().HasForeignKey(attempt => attempt.RunId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(attempt => new { attempt.RunId, attempt.AttemptNumber }).IsUnique();
    }
}
