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

        // Agent-only columns: null for every Simulated/Process attempt. Never a prompt,
        // transcript, path, environment value, or credential — see Attempt.ClaimAgent.
        builder.Property(attempt => attempt.AgentProvider).HasConversion<string>().HasMaxLength(32);
        builder.Property(attempt => attempt.AgentRole).HasConversion<string>().HasMaxLength(32);
        builder.Property(attempt => attempt.AgentProtocolVersion).HasMaxLength(16);
        builder.Property(attempt => attempt.AgentExpectedMessageType).HasConversion<string>().HasMaxLength(32);
        // Every Agent attempt recorded before this column existed is backfilled truthfully to
        // Proposal — the only response contract any Agent attempt ever had before this slice.
        builder.Property(attempt => attempt.AgentResponseContract).HasConversion<string>().HasMaxLength(32);
        builder.Property(attempt => attempt.AgentCheckpointFingerprintSha256).HasMaxLength(64);
        builder.Property(attempt => attempt.AgentOutcome).HasConversion<string>().HasMaxLength(32);
        builder.Property(attempt => attempt.AgentProviderSessionId).HasMaxLength(256);
        builder.Property(attempt => attempt.AgentRequestedModel).HasMaxLength(128);
        builder.Property(attempt => attempt.AgentObservedModel).HasMaxLength(128);
        builder.Property(attempt => attempt.AgentRequestedEffort).HasMaxLength(128);
        builder.Property(attempt => attempt.AgentObservedEffort).HasMaxLength(128);
        builder.Property(attempt => attempt.AgentPermissionProfile).HasConversion<string>().HasMaxLength(32);
        builder.Property(attempt => attempt.AgentAdapterContractVersion).HasMaxLength(128);

        builder.Property(attempt => attempt.AgentTimeout)
            .HasConversion(
                timeout => timeout.HasValue ? (long?)timeout.Value.TotalMilliseconds : null,
                milliseconds => milliseconds.HasValue ? TimeSpan.FromMilliseconds(milliseconds.Value) : (TimeSpan?)null);

        // Host-measured Agent process evidence: nullable and never backfilled, so every attempt
        // recorded before these columns existed truthfully reads as unknown evidence. Unlike
        // AgentTimeout/ProcessTimeout (a caller-configured bound with no need for finer-than-
        // millisecond precision), this duration is a real host measurement around one child
        // process and is persisted as its exact tick count so it round-trips losslessly even when
        // the measured duration is not a whole number of milliseconds.
        builder.Property(attempt => attempt.AgentProcessOutcome).HasConversion<string>().HasMaxLength(32);
        builder.Property(attempt => attempt.AgentProcessDuration)
            .HasConversion(
                duration => duration.HasValue ? (long?)duration.Value.Ticks : null,
                ticks => ticks.HasValue ? TimeSpan.FromTicks(ticks.Value) : (TimeSpan?)null);

        // Provider-reported Agent token usage: plain nullable integers and a bounded version tag,
        // never backfilled, so every attempt recorded before these columns existed — and every
        // attempt whose provider has no proven usage contract — truthfully reads as unknown.
        builder.Property(attempt => attempt.AgentInputTokens);
        builder.Property(attempt => attempt.AgentOutputTokens);
        builder.Property(attempt => attempt.AgentCacheCreationInputTokens);
        builder.Property(attempt => attempt.AgentCacheReadInputTokens);
        builder.Property(attempt => attempt.AgentTokenUsageSchemaVersion).HasMaxLength(AgentTokenUsageEvidence.MaxSchemaVersionLength);

        builder.HasOne<Run>().WithMany().HasForeignKey(attempt => attempt.RunId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(attempt => new { attempt.RunId, attempt.AttemptNumber }).IsUnique();

        // The run-wide active-attempt invariant's database backstop: at most one Running attempt
        // of ANY kind per run, ever concurrently observable. The primary defense is the
        // application-level eligibility check before an attempt is claimed; this filtered unique
        // index is what turns a lost race into a safe conflict instead of two Running attempts
        // silently coexisting.
        builder.HasIndex(attempt => attempt.RunId)
            .IsUnique()
            .HasDatabaseName("ix_attempts_run_id_one_running")
            .HasFilter("\"Status\" = 'Running'");
    }
}
