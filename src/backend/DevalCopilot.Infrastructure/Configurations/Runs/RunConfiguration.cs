using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevalCopilot.Infrastructure.Configurations.Runs;

public sealed class RunConfiguration : IEntityTypeConfiguration<Run>
{
    public void Configure(EntityTypeBuilder<Run> builder)
    {
        builder.ToTable("runs");
        builder.HasKey(run => run.Id);
        builder.Property(run => run.Objective).HasMaxLength(4000).IsRequired();

        // A concurrency token, not a schema change: EF includes this column's originally-read
        // value in every UPDATE/DELETE statement's WHERE clause, so a write against a stale
        // in-memory Lifecycle (loaded before a concurrent transition committed) affects zero rows
        // and throws DbUpdateConcurrencyException instead of silently overwriting the newer
        // state. SetCodexAssignmentPreferenceCommandHandler relies on this to close the race
        // between its own authoritative fresh read and its single SaveChangesAsync call, without
        // needing an explicit multi-statement transaction around that short write.
        builder.Property(run => run.Lifecycle).HasConversion<string>().HasMaxLength(32).IsRequired().IsConcurrencyToken();
        builder.Property(run => run.Stage).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(run => run.ActiveParticipantKind).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(run => run.ActiveAgentRole).HasConversion<string>().HasMaxLength(32);
        builder.Property(run => run.ActiveAgentProvider).HasConversion<string>().HasMaxLength(32);
        builder.Ignore(run => run.ActiveParticipant);
        builder.Property(run => run.AccumulatedAutonomousSeconds).IsRequired();
        builder.Property(run => run.MaximumReviewCorrectionAttempts).IsRequired().HasDefaultValue(2);
        builder.Property(run => run.MaximumAgentAttempts).IsRequired().HasDefaultValue(16);

        // The run-wide Agent invocation-TIME budget: persisted as its exact tick count (not
        // milliseconds) so it round-trips losslessly even for a non-integral-millisecond value,
        // and deliberately given NO default value and NO backfill — unlike MaximumAgentAttempts, a
        // historical Run predating this decision must keep this truthfully NULL (no time-budget
        // policy at all), never a fabricated 120-minute ceiling it was never actually bound by.
        // Only Run.RecordIntent ever assigns a non-null value, to a newly created Run.
        builder.Property(run => run.MaximumAgentInvocationTime)
            .HasConversion(
                invocationTime => invocationTime.HasValue ? (long?)invocationTime.Value.Ticks : null,
                ticks => ticks.HasValue ? TimeSpan.FromTicks(ticks.Value) : (TimeSpan?)null);

        // No default value and no backfill, for the same reason as MaximumAgentInvocationTime
        // above: a historical Run truthfully has no explicit Codex model/effort request, never a
        // fabricated one.
        builder.Property(run => run.RequestedCodexModel).HasMaxLength(128);
        builder.Property(run => run.RequestedCodexEffort).HasMaxLength(128);

        builder.HasOne<Project>().WithMany().HasForeignKey(run => run.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(run => new { run.ProjectId, run.ExecutionNumber }).IsUnique();
    }
}
