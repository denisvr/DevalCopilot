using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevalCopilot.Infrastructure.Configurations.Runs;

public sealed class PlanningSequenceConfiguration : IEntityTypeConfiguration<PlanningSequence>
{
    private const string Sha256Check = "length(\"{0}\") = 64 AND \"{0}\" NOT GLOB '*[^0-9a-f]*'";

    public void Configure(EntityTypeBuilder<PlanningSequence> builder)
    {
        builder.ToTable("planning_sequences", table =>
        {
            table.HasCheckConstraint("ck_planning_sequences_fingerprint", string.Format(Sha256Check, "FingerprintSha256"));
            table.HasCheckConstraint("ck_planning_sequences_objective", string.Format(Sha256Check, "ObjectiveSha256"));
            table.HasCheckConstraint(
                "ck_planning_sequences_state",
                "\"State\" IN ('Armed', 'PreparingStep', 'AwaitingStepResult', 'Stopping', 'ReadyForImplementation', "
                + "'AwaitingHumanDecision', 'Stopped', 'NeedsAttention', 'Interrupted')");
            table.HasCheckConstraint("ck_planning_sequences_revision", "\"Revision\" >= 1");
        });
        builder.HasKey(sequence => sequence.Id);

        builder.Property(sequence => sequence.FingerprintSha256).HasMaxLength(64).IsRequired();
        builder.Property(sequence => sequence.ObjectiveSha256).HasMaxLength(64).IsRequired();
        builder.Property(sequence => sequence.State).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(sequence => sequence.ReasonCode).HasMaxLength(64);
        builder.Property(sequence => sequence.DetailCode).HasMaxLength(PlanningSequenceReason.MaximumDetailLength);
        builder.Ignore(sequence => sequence.IsAuthorityLive);

        // Every transition compares the revision it read: two writers can never both apply a decision made from the same read.
        builder.Property(sequence => sequence.Revision).IsConcurrencyToken();

        // One sequence per run for the run's whole lifetime: arming never repeats, retargets or rearms.
        builder.HasIndex(sequence => sequence.RunId).IsUnique().HasDatabaseName("ix_planning_sequences_run");
        builder.HasIndex(sequence => sequence.WorkspaceId).HasDatabaseName("ix_planning_sequences_workspace");

        // Retained for the run's lifetime: no cascade, so a future run deletion must explicitly own the hard deletion of these rows.
        builder.HasOne<Run>().WithMany().HasForeignKey(sequence => sequence.RunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GitWorkspace>().WithMany().HasForeignKey(sequence => sequence.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GitCheckpoint>().WithMany().HasForeignKey(sequence => sequence.CheckpointId).OnDelete(DeleteBehavior.Restrict);
    }
}
