using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevalCopilot.Infrastructure.Configurations.Runs;

/// <summary>ADR-0065: the append-only dispatch witness. Typed restrictive foreign keys name exactly one operation family, every stored
/// enum and number is constrained to its exact closed vocabulary or integer range, and the step, operation and admission associations are
/// unique. The migration adds the guards that refuse any update or delete. It is retained for the run lifetime.</summary>
public sealed class LocalExecutionDispatchWitnessConfiguration : IEntityTypeConfiguration<LocalExecutionDispatchWitness>
{
    public void Configure(EntityTypeBuilder<LocalExecutionDispatchWitness> builder)
    {
        builder.ToTable("local_execution_dispatch_witnesses", table =>
        {
            table.HasCheckConstraint("ck_local_execution_dispatch_witnesses_version", "typeof(\"Version\") = 'integer' AND \"Version\" = 1");
            table.HasCheckConstraint(
                "ck_local_execution_dispatch_witnesses_family",
                "(\"OperationFamily\" = 'Agent' AND \"AgentAttemptId\" IS NOT NULL AND \"VerificationExecutionId\" IS NULL "
                + "AND \"AgentResponseContract\" IS NOT NULL AND \"RecipeCommandId\" IS NULL AND \"Kind\" <> 'Verification') "
                + "OR (\"OperationFamily\" = 'Verification' AND \"VerificationExecutionId\" IS NOT NULL AND \"AgentAttemptId\" IS NULL "
                + "AND \"AgentResponseContract\" IS NULL AND \"RecipeCommandId\" IS NOT NULL AND \"Kind\" = 'Verification')");
            table.HasCheckConstraint(
                "ck_local_execution_dispatch_witnesses_kind",
                "\"Kind\" IN ('Implementation', 'Verification', 'CodeReview', 'VerificationDiagnosis', 'ReviewCorrection', 'DiagnosisCorrection')");
            table.HasCheckConstraint(
                "ck_local_execution_dispatch_witnesses_contract",
                "\"AgentResponseContract\" IS NULL OR \"AgentResponseContract\" IN "
                + "('ImplementationReport', 'ImplementationReview', 'ReviewCorrection', 'VerificationDiagnosis')");
            table.HasCheckConstraint(
                "ck_local_execution_dispatch_witnesses_ordinal", "typeof(\"Ordinal\") = 'integer' AND \"Ordinal\" BETWEEN 0 AND 101");
            table.HasCheckConstraint(
                "ck_local_execution_dispatch_witnesses_pass", "typeof(\"PassNumber\") = 'integer' AND \"PassNumber\" BETWEEN 0 AND 3");
            table.HasCheckConstraint(
                "ck_local_execution_dispatch_witnesses_recipe",
                "\"RecipeOrdinal\" IS NULL OR (typeof(\"RecipeOrdinal\") = 'integer' AND \"RecipeOrdinal\" BETWEEN 0 AND 31)");
            table.HasCheckConstraint(
                "ck_local_execution_dispatch_witnesses_shape",
                "((\"Kind\" = 'Implementation') = (\"PassNumber\" = 0)) AND ((\"Kind\" = 'Implementation') = (\"Ordinal\" = 0)) "
                + "AND ((\"Kind\" = 'Verification') = (\"RecipeOrdinal\" IS NOT NULL)) "
                + "AND ((\"Kind\" = 'Implementation') = (\"PredecessorStepId\" IS NULL)) "
                + "AND ((\"Kind\" = 'Implementation') = (\"ReportMessageId\" IS NULL)) "
                + "AND ((\"Kind\" IN ('ReviewCorrection', 'DiagnosisCorrection')) = (\"SubjectAttemptId\" IS NOT NULL))");
            table.HasCheckConstraint(
                "ck_local_execution_dispatch_witnesses_fingerprint",
                "typeof(\"SourceFingerprintSha256\") = 'text' AND length(\"SourceFingerprintSha256\") = 64 "
                + "AND \"SourceFingerprintSha256\" NOT GLOB '*[^0-9a-f]*'");
            table.HasCheckConstraint(
                "ck_local_execution_dispatch_witnesses_time", "typeof(\"DispatchedAtUtc\") = 'text'");
        });
        builder.HasKey(witness => witness.Id);

        builder.Property(witness => witness.OperationFamily).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(witness => witness.Kind).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(witness => witness.AgentResponseContract).HasConversion<string>().HasMaxLength(32);
        builder.Property(witness => witness.SourceFingerprintSha256).HasMaxLength(64).IsRequired();
        builder.Ignore(witness => witness.OperationId);

        builder.HasIndex(witness => witness.StepId).IsUnique().HasDatabaseName("ix_local_execution_dispatch_witnesses_step");
        builder.HasIndex(witness => witness.AgentAttemptId).IsUnique().HasDatabaseName("ix_local_execution_dispatch_witnesses_attempt")
            .HasFilter("\"AgentAttemptId\" IS NOT NULL");
        builder.HasIndex(witness => witness.VerificationExecutionId).IsUnique()
            .HasDatabaseName("ix_local_execution_dispatch_witnesses_execution").HasFilter("\"VerificationExecutionId\" IS NOT NULL");
        builder.HasIndex(witness => witness.AdmissionId).IsUnique().HasDatabaseName("ix_local_execution_dispatch_witnesses_admission")
            .HasFilter("\"AdmissionId\" IS NOT NULL");
        builder.HasIndex(witness => witness.SequenceId).HasDatabaseName("ix_local_execution_dispatch_witnesses_sequence");

        // Retained for the run lifetime and never cascaded away: a witness is used provenance of a Human decision.
        builder.HasOne<LocalExecutionSequence>().WithMany().HasForeignKey(witness => witness.SequenceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Run>().WithMany().HasForeignKey(witness => witness.RunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Project>().WithMany().HasForeignKey(witness => witness.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GitWorkspace>().WithMany().HasForeignKey(witness => witness.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LocalExecutionSequenceStep>().WithMany().HasForeignKey(witness => witness.StepId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Attempt>().WithMany().HasForeignKey(witness => witness.AgentAttemptId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<VerificationExecution>().WithMany().HasForeignKey(witness => witness.VerificationExecutionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LocalExecutionAdmission>().WithMany().HasForeignKey(witness => witness.AdmissionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GitCheckpoint>().WithMany().HasForeignKey(witness => witness.SourceCheckpointId).OnDelete(DeleteBehavior.Restrict);
    }
}
