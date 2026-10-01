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

        // The execution mode is a field-only string holding the exact stored form (see RunExecutionModeStorage), read
        // through the same storage-class-preserving mapping as the turn-limit columns, so a REAL that would truncate to a
        // valid mode, an integer that overflows 32 bits, text, and BLOBs are seen as unrecognized instead of being coerced or
        // throwing, and round-trip exactly through an unrelated save. The default is the Legacy value 0 (a row that predates
        // the column keeps it, never inferred from attempts, providers, lifecycle, or events). A concurrency token so a claim
        // that decided against one mode cannot commit against another.
        builder.Ignore(run => run.ExecutionMode);
        builder.Property<string>(Run.ExecutionModeStorageProperty)
            .HasColumnName("ExecutionMode")
            .HasColumnType("INTEGER")
            .IsRequired()
            .HasDefaultValueSql("0")
            .IsConcurrencyToken()
            .Metadata.SetTypeMapping(new ExactStoredIntegerTextTypeMapping());

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

        // No default value and no backfill: a historical Run truthfully has no Claude model
        // request. A concurrency token, not merely a column: the claim handlers' late snapshot
        // guard and the set operation both depend on the token appearing in the UPDATE's WHERE
        // clause (see CurrentClaudeModelPreference).
        builder.Property(run => run.RequestedClaudeModel).HasMaxLength(32).IsConcurrencyToken();
        builder.Property(run => run.RequestedClaudeEffort).HasMaxLength(16).IsConcurrencyToken();

        // The owner's Claude agentic-turn-limit request for the two mutating Claude paths: no default and no
        // backfill (a historical run has none). Mapped as a field-only property holding the exact stored text in
        // an INTEGER-affinity column, so a fractional, overflowing, or non-numeric stored value is read as
        // malformed instead of being truncated or overflowing during materialization, and so a malformed value
        // still round-trips exactly (see Run.ReadRequestedClaudeMaxTurns). A concurrency token for the same reason
        // as the model request: the claim's late snapshot guard and the set operation both need it in the
        // UPDATE's WHERE clause (see CurrentClaudeMutationTurnLimit).
        builder.Ignore(run => run.RequestedClaudeMaxTurns);
        builder.Property<string?>(Run.RequestedClaudeMaxTurnsStorageProperty)
            .HasColumnName("RequestedClaudeMaxTurns")
            .HasColumnType("INTEGER")
            .IsConcurrencyToken()
            .Metadata.SetTypeMapping(new ExactStoredIntegerTextTypeMapping());

        // Advisory token-warning thresholds: no default, no backfill (a historical run has none),
        // and deliberately NOT concurrency tokens, so writing one can never make a claim's own Run
        // UPDATE fail (see SetTokenWarningThresholdCommandHandler).
        builder.Property(run => run.CodexTokenWarningThreshold);
        builder.Property(run => run.ClaudeTokenWarningThreshold);

        // Token-activity stop thresholds: no default and no backfill (a historical run has none).
        // Unlike the advisory warning thresholds these ARE concurrency tokens: a claim's Run
        // UPDATE (Claude paths) or its stop-policy guard (Codex paths, see CurrentTokenStopPolicy)
        // must fail when the policy changed after the claim decided against it, so an Attempt can
        // never durably commit against a stale stop policy.
        builder.Property(run => run.CodexTokenStopThreshold).IsConcurrencyToken();
        builder.Property(run => run.ClaudeTokenStopThreshold).IsConcurrencyToken();

        builder.HasOne<Project>().WithMany().HasForeignKey(run => run.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(run => new { run.ProjectId, run.ExecutionNumber }).IsUnique();
    }
}
