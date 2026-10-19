using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevalCopilot.Infrastructure.Configurations.Runs;

public sealed class PlanningSequenceStepConfiguration : IEntityTypeConfiguration<PlanningSequenceStep>
{
    public void Configure(EntityTypeBuilder<PlanningSequenceStep> builder)
    {
        builder.ToTable("planning_sequence_steps", table =>
        {
            table.HasCheckConstraint("ck_planning_sequence_steps_ordinal", "\"Ordinal\" BETWEEN 0 AND 4");
            table.HasCheckConstraint(
                "ck_planning_sequence_steps_role",
                "(\"Ordinal\" = 0 AND \"Role\" = 'Planner') OR (\"Ordinal\" IN (1, 3) AND \"Role\" = 'CriticalReviewer') "
                + "OR (\"Ordinal\" IN (2, 4) AND \"Role\" = 'Resolver')");
            table.HasCheckConstraint(
                "ck_planning_sequence_steps_state",
                "\"State\" IN ('Reserved', 'Claimed', 'Concluded', 'Revoked', 'Unresolved')");
            table.HasCheckConstraint(
                "ck_planning_sequence_steps_predecessor",
                "(\"Ordinal\" = 0) = (\"PredecessorAttemptId\" IS NULL) AND (\"Ordinal\" = 0) = (\"InputProposalMessageId\" IS NULL)");
            table.HasCheckConstraint(
                "ck_planning_sequence_steps_attempt_link",
                "(\"State\" IN ('Claimed', 'Concluded')) = (\"AttemptId\" IS NOT NULL) "
                + "AND (\"AttemptId\" IS NULL OR \"AttemptId\" = \"ReservedAttemptId\")");
            table.HasCheckConstraint("ck_planning_sequence_steps_revision", "\"Revision\" >= 1");
        });
        builder.HasKey(step => step.Id);

        builder.Property(step => step.Role).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(step => step.State).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(step => step.ReasonCode).HasMaxLength(64);
        builder.Ignore(step => step.IsOutstanding);

        // Every transition compares the revision it read. The Stop and expiry revocations and the claim's own link all contend on it,
        // so exactly one of them wins a step.
        builder.Property(step => step.Revision).IsConcurrencyToken();

        // Exactly one step per ordinal, one attempt identity per step and per attempt, for the sequence's whole lifetime.
        builder.HasIndex(step => new { step.SequenceId, step.Ordinal }).IsUnique().HasDatabaseName("ix_planning_sequence_steps_ordinal");
        builder.HasIndex(step => step.ReservedAttemptId).IsUnique().HasDatabaseName("ix_planning_sequence_steps_reserved_attempt");
        builder.HasIndex(step => step.AttemptId)
            .IsUnique()
            .HasDatabaseName("ix_planning_sequence_steps_attempt")
            .HasFilter("\"AttemptId\" IS NOT NULL");

        // At most one outstanding step per sequence: a duplicate worker or ambiguous response cannot create another step to run.
        builder.HasIndex(step => step.SequenceId)
            .IsUnique()
            .HasDatabaseName("ix_planning_sequence_steps_outstanding")
            .HasFilter("\"State\" IN ('Reserved', 'Claimed')");

        builder.HasOne<PlanningSequence>().WithMany().HasForeignKey(step => step.SequenceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Attempt>().WithMany().HasForeignKey(step => step.AttemptId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Attempt>().WithMany().HasForeignKey(step => step.PredecessorAttemptId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CollaborationMessage>().WithMany().HasForeignKey(step => step.InputProposalMessageId)
            .HasPrincipalKey(message => message.Id).OnDelete(DeleteBehavior.Restrict);
    }
}
