using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevalCopilot.Infrastructure.Configurations.Runs;

public sealed class LocalExecutionSequenceConfiguration : IEntityTypeConfiguration<LocalExecutionSequence>
{
    private const string Sha256Check = "length(\"{0}\") = 64 AND \"{0}\" NOT GLOB '*[^0-9a-f]*'";

    public void Configure(EntityTypeBuilder<LocalExecutionSequence> builder)
    {
        builder.ToTable("local_execution_sequences", table =>
        {
            table.HasCheckConstraint("ck_local_execution_sequences_fingerprint", string.Format(Sha256Check, "FingerprintSha256"));
            table.HasCheckConstraint("ck_local_execution_sequences_confirmation", string.Format(Sha256Check, "ConfirmationSha256"));
            table.HasCheckConstraint("ck_local_execution_sequences_plan_inputs", string.Format(Sha256Check, "PlanInputsSha256"));
            table.HasCheckConstraint(
                "ck_local_execution_sequences_state",
                "\"State\" IN ('Armed', 'PreparingStep', 'AwaitingStepResult', 'Stopping', 'ReadyForHumanApproval', "
                + "'HumanEscalation', 'Stopped', 'NeedsAttention', 'Interrupted')");
            table.HasCheckConstraint(
                "ck_local_execution_sequences_authority",
                "\"PlanAuthority\" IN ('AcceptedRoot', 'AcceptedRevision', 'HumanAuthorizedRevision')");
            table.HasCheckConstraint("ck_local_execution_sequences_recipes", "\"RecipeCount\" BETWEEN 1 AND 32");
            table.HasCheckConstraint("ck_local_execution_sequences_allowance", "\"CorrectionAllowance\" BETWEEN 0 AND 2");
            table.HasCheckConstraint("ck_local_execution_sequences_revision", "\"Revision\" >= 1");
            table.HasCheckConstraint(
                "ck_local_execution_sequences_final",
                "(\"State\" = 'ReadyForHumanApproval') = (\"FinalReviewAttemptId\" IS NOT NULL) "
                + "AND (\"State\" = 'HumanEscalation') = (\"FinalEscalationMessageId\" IS NOT NULL) "
                + "AND (\"State\" IN ('ReadyForHumanApproval', 'HumanEscalation')) = (\"FinalReportMessageId\" IS NOT NULL)");
        });
        builder.HasKey(sequence => sequence.Id);

        builder.Property(sequence => sequence.FingerprintSha256).HasMaxLength(64).IsRequired();
        builder.Property(sequence => sequence.ConfirmationSha256).HasMaxLength(64).IsRequired();
        builder.Property(sequence => sequence.PlanInputsSha256).HasMaxLength(64).IsRequired();
        builder.Property(sequence => sequence.SettingsSnapshot).HasMaxLength(LocalExecutionSequence.MaximumSettingsSnapshotLength).IsRequired();
        builder.Property(sequence => sequence.PlanAuthority).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(sequence => sequence.State).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(sequence => sequence.ReasonCode).HasMaxLength(64);
        builder.Property(sequence => sequence.DetailCode).HasMaxLength(LocalExecutionSequenceReason.MaximumDetailLength);
        builder.Ignore(sequence => sequence.IsAuthorityLive);

        // Every transition compares the revision it read: two writers can never both apply a decision made from the same read.
        builder.Property(sequence => sequence.Revision).IsConcurrencyToken();

        // One sequence per run for the run's whole lifetime: arming never repeats, retargets or rearms.
        builder.HasIndex(sequence => sequence.RunId).IsUnique().HasDatabaseName("ix_local_execution_sequences_run");
        builder.HasIndex(sequence => sequence.WorkspaceId).HasDatabaseName("ix_local_execution_sequences_workspace");
        builder.HasIndex(sequence => sequence.ProjectId).HasDatabaseName("ix_local_execution_sequences_project");

        // The exhaustion or provider escalation message owns the sequence's idempotent final reference.
        builder.HasIndex(sequence => sequence.FinalEscalationMessageId)
            .IsUnique()
            .HasDatabaseName("ix_local_execution_sequences_final_escalation")
            .HasFilter("\"FinalEscalationMessageId\" IS NOT NULL");

        // Retained for the run's lifetime: no cascade, so a future run deletion must explicitly own the hard deletion of these rows.
        builder.HasOne<Run>().WithMany().HasForeignKey(sequence => sequence.RunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GitWorkspace>().WithMany().HasForeignKey(sequence => sequence.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GitCheckpoint>().WithMany().HasForeignKey(sequence => sequence.CheckpointId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CollaborationMessage>().WithMany().HasForeignKey(sequence => sequence.PlanProposalMessageId)
            .HasPrincipalKey(message => message.Id).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CollaborationMessage>().WithMany().HasForeignKey(sequence => sequence.FinalReportMessageId)
            .HasPrincipalKey(message => message.Id).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CollaborationMessage>().WithMany().HasForeignKey(sequence => sequence.FinalEscalationMessageId)
            .HasPrincipalKey(message => message.Id).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Attempt>().WithMany().HasForeignKey(sequence => sequence.FinalReviewAttemptId).OnDelete(DeleteBehavior.Restrict);
    }
}
