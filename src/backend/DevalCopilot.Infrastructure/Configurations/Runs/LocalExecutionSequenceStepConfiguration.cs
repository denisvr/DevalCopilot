using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevalCopilot.Infrastructure.Configurations.Runs;

public sealed class LocalExecutionSequenceStepConfiguration : IEntityTypeConfiguration<LocalExecutionSequenceStep>
{
    public void Configure(EntityTypeBuilder<LocalExecutionSequenceStep> builder)
    {
        builder.ToTable("local_execution_sequence_steps", table =>
        {
            table.HasCheckConstraint("ck_local_execution_sequence_steps_ordinal", "\"Ordinal\" BETWEEN 0 AND 101");
            table.HasCheckConstraint(
                "ck_local_execution_sequence_steps_kind",
                "\"Kind\" IN ('Implementation', 'Verification', 'CodeReview', 'VerificationDiagnosis', 'ReviewCorrection', 'DiagnosisCorrection')");
            table.HasCheckConstraint(
                "ck_local_execution_sequence_steps_state",
                "\"State\" IN ('Reserved', 'Claimed', 'Concluded', 'Revoked', 'Unresolved')");
            table.HasCheckConstraint("ck_local_execution_sequence_steps_pass", "\"PassNumber\" BETWEEN 0 AND 3");
            table.HasCheckConstraint(
                "ck_local_execution_sequence_steps_shape",
                "((\"Kind\" = 'Implementation') = (\"PassNumber\" = 0)) AND ((\"Kind\" = 'Implementation') = (\"Ordinal\" = 0)) "
                + "AND ((\"Kind\" = 'Verification') = (\"RecipeOrdinal\" IS NOT NULL)) "
                + "AND ((\"Kind\" = 'Implementation') = (\"PredecessorStepId\" IS NULL)) "
                + "AND ((\"Kind\" = 'Implementation') = (\"ReportMessageId\" IS NULL)) "
                + "AND ((\"Kind\" IN ('ReviewCorrection', 'DiagnosisCorrection')) = (\"SubjectAttemptId\" IS NOT NULL))");
            table.HasCheckConstraint(
                "ck_local_execution_sequence_steps_operation_link",
                "(\"State\" IN ('Claimed', 'Concluded')) = (\"OperationId\" IS NOT NULL) "
                + "AND (\"OperationId\" IS NULL OR \"OperationId\" = \"ReservedOperationId\") "
                + "AND (\"OperationId\" IS NOT NULL) = (\"ClaimedAtUtc\" IS NOT NULL) "
                + "AND (\"OperationId\" IS NOT NULL) = (\"ResultDeadlineUtc\" IS NOT NULL)");
            table.HasCheckConstraint("ck_local_execution_sequence_steps_revision", "\"Revision\" >= 1");
        });
        builder.HasKey(step => step.Id);

        builder.Property(step => step.Kind).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(step => step.State).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(step => step.SourceFingerprintSha256).HasMaxLength(64).IsRequired();
        builder.Property(step => step.ReasonCode).HasMaxLength(64);
        builder.Ignore(step => step.IsOutstanding);
        builder.Ignore(step => step.IsAgent);

        // Every transition compares the revision it read, so exactly one of a Stop, an expiry and the claim's own link wins a step.
        builder.Property(step => step.Revision).IsConcurrencyToken();

        builder.HasIndex(step => new { step.SequenceId, step.Ordinal }).IsUnique().HasDatabaseName("ix_local_execution_sequence_steps_ordinal");
        builder.HasIndex(step => step.ReservedOperationId).IsUnique().HasDatabaseName("ix_local_execution_sequence_steps_reserved_operation");
        builder.HasIndex(step => step.OperationId)
            .IsUnique()
            .HasDatabaseName("ix_local_execution_sequence_steps_operation")
            .HasFilter("\"OperationId\" IS NOT NULL");

        // Exactly one verification membership per (sequence, pass, recipe) for the sequence's whole lifetime.
        builder.HasIndex(step => new { step.SequenceId, step.PassNumber, step.RecipeOrdinal })
            .IsUnique()
            .HasDatabaseName("ix_local_execution_sequence_steps_membership")
            .HasFilter("\"RecipeOrdinal\" IS NOT NULL");

        // At most one outstanding step per sequence: a duplicate worker or ambiguous response cannot create another step to run.
        builder.HasIndex(step => step.SequenceId)
            .IsUnique()
            .HasDatabaseName("ix_local_execution_sequence_steps_outstanding")
            .HasFilter("\"State\" IN ('Reserved', 'Claimed')");

        builder.HasOne<LocalExecutionSequence>().WithMany().HasForeignKey(step => step.SequenceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<LocalExecutionSequenceStep>().WithMany().HasForeignKey(step => step.PredecessorStepId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GitCheckpoint>().WithMany().HasForeignKey(step => step.SourceCheckpointId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CollaborationMessage>().WithMany().HasForeignKey(step => step.ReportMessageId)
            .HasPrincipalKey(message => message.Id).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Attempt>().WithMany().HasForeignKey(step => step.SubjectAttemptId).OnDelete(DeleteBehavior.Restrict);
    }
}
