using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevalCopilot.Infrastructure.Configurations.Runs;

public sealed class LocalExecutionAdmissionConfiguration : IEntityTypeConfiguration<LocalExecutionAdmission>
{
    public void Configure(EntityTypeBuilder<LocalExecutionAdmission> builder)
    {
        builder.ToTable("local_execution_admissions", table =>
        {
            table.HasCheckConstraint("ck_local_execution_admissions_number", "typeof(\"AdmissionNumber\") = 'integer' AND \"AdmissionNumber\" >= 1");
            table.HasCheckConstraint("ck_local_execution_admissions_ordinal", "typeof(\"Ordinal\") = 'integer' AND \"Ordinal\" BETWEEN 0 AND 101");
            table.HasCheckConstraint("ck_local_execution_admissions_pass", "\"PassNumber\" BETWEEN 0 AND 3");
            table.HasCheckConstraint(
                "ck_local_execution_admissions_kind",
                "\"Kind\" IN ('Implementation', 'Verification', 'CodeReview', 'VerificationDiagnosis', 'ReviewCorrection', 'DiagnosisCorrection')");
            table.HasCheckConstraint(
                "ck_local_execution_admissions_state",
                "\"State\" IN ('Waiting', 'Reserved', 'Consumed', 'Retired', 'Unresolved')");
            table.HasCheckConstraint(
                "ck_local_execution_admissions_waiting_reason",
                "\"WaitingReason\" IN ('AwaitingAdmissionCheck', 'WaitingForWorkspace', 'WaitingForCapacity', "
                + "'WaitingForEarlierEligibleWork', 'WaitingForPreparationCapacity', 'Unavailable')");
            table.HasCheckConstraint(
                "ck_local_execution_admissions_fingerprint",
                "length(\"SourceFingerprintSha256\") = 64 AND \"SourceFingerprintSha256\" NOT GLOB '*[^0-9a-f]*'");
            table.HasCheckConstraint(
                "ck_local_execution_admissions_shape",
                "((\"Kind\" = 'Implementation') = (\"PassNumber\" = 0)) AND ((\"Kind\" = 'Implementation') = (\"Ordinal\" = 0)) "
                + "AND ((\"Kind\" = 'Verification') = (\"RecipeOrdinal\" IS NOT NULL)) "
                + "AND ((\"Kind\" = 'Implementation') = (\"PredecessorStepId\" IS NULL)) "
                + "AND ((\"Kind\" = 'Implementation') = (\"ReportMessageId\" IS NULL)) "
                + "AND ((\"Kind\" IN ('ReviewCorrection', 'DiagnosisCorrection')) = (\"SubjectAttemptId\" IS NOT NULL))");
            table.HasCheckConstraint(
                "ck_local_execution_admissions_step_link",
                "(\"State\" NOT IN ('Reserved', 'Consumed', 'Unresolved') OR \"StepId\" IS NOT NULL) "
                + "AND (\"State\" <> 'Waiting' OR \"StepId\" IS NULL)");
            table.HasCheckConstraint(
                "ck_local_execution_admissions_reason",
                "(\"State\" IN ('Retired', 'Unresolved')) = (\"ReasonCode\" IS NOT NULL)");
            table.HasCheckConstraint("ck_local_execution_admissions_revision", "\"Revision\" >= 1");
        });
        builder.HasKey(admission => admission.Id);

        builder.Property(admission => admission.Kind).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(admission => admission.State).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(admission => admission.SourceFingerprintSha256).HasMaxLength(64).IsRequired();
        builder.Property(admission => admission.WaitingReason).HasMaxLength(48).IsRequired();
        builder.Property(admission => admission.ReasonCode).HasMaxLength(64);
        builder.Ignore(admission => admission.IsHeld);

        // Every transition compares the revision it read, so exactly one of a Stop, a deadline, a reservation and a claim wins.
        builder.Property(admission => admission.Revision).IsConcurrencyToken();

        builder.HasIndex(admission => admission.AdmissionNumber).IsUnique().HasDatabaseName("ix_local_execution_admissions_number");
        builder.HasIndex(admission => new { admission.SequenceId, admission.Ordinal }).IsUnique()
            .HasDatabaseName("ix_local_execution_admissions_ordinal");
        builder.HasIndex(admission => admission.StepId).IsUnique().HasDatabaseName("ix_local_execution_admissions_step")
            .HasFilter("\"StepId\" IS NOT NULL");

        // One pending action per cycle: at most one waiting or reserved admission, so a duplicate worker cannot queue another.
        builder.HasIndex(admission => admission.SequenceId).IsUnique().HasDatabaseName("ix_local_execution_admissions_outstanding")
            .HasFilter("\"State\" IN ('Waiting', 'Reserved')");

        builder.HasIndex(admission => admission.State).HasDatabaseName("ix_local_execution_admissions_state");

        // Retained for the run lifetime: the admission follows its sequence and nothing here is purged by a feature endpoint.
        builder.HasOne<LocalExecutionSequence>().WithMany().HasForeignKey(admission => admission.SequenceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<LocalExecutionSequenceStep>().WithMany().HasForeignKey(admission => admission.StepId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LocalExecutionSequenceStep>().WithMany().HasForeignKey(admission => admission.PredecessorStepId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DevalCopilot.Domain.Features.Projects.GitCheckpoint>().WithMany()
            .HasForeignKey(admission => admission.SourceCheckpointId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CollaborationMessage>().WithMany().HasForeignKey(admission => admission.ReportMessageId)
            .HasPrincipalKey(message => message.Id).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Attempt>().WithMany().HasForeignKey(admission => admission.SubjectAttemptId).OnDelete(DeleteBehavior.Restrict);
    }
}
