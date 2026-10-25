using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevalCopilot.Infrastructure.Configurations.Runs;

/// <summary>ADR-0065 and ADR-0066: the append-only Human release receipt. It names one unique witness, one unique observation, one unique
/// step and the one Human run event, stores the exact confirmation the Human submitted for exact replay, and stores the exact confirmed
/// tuple as typed columns (the <c>Intent</c> columns) that every reader, the shared ownership expression and the live-fence triggers
/// compare field for field against the current facts. Stored enums and numbers are constrained to their exact closed vocabularies and
/// integer ranges, the family-specific members are present exactly for their family, and the confirmation lifetime is bounded exactly, at
/// 100 ns tick precision. The migration adds the guards that refuse any update or delete.</summary>
public sealed class LocalExecutionOwnershipReleaseConfiguration : IEntityTypeConfiguration<LocalExecutionOwnershipRelease>
{
    /// <summary>
    /// The exact UTC tick count (100 ns units since the Unix epoch) of a normalized stored time column, in SQL integer arithmetic. The host
    /// stores a UTC time as <c>yyyy-MM-dd HH:mm:ss</c>, an optional <c>.</c> and one to seven fractional digits with trailing zeros
    /// trimmed, and <c>+00:00</c>; the fraction is padded back to seven digits. It is exact (integer, not floating point) and is the only
    /// place where the lifetime limit is decided, because a provider-neutral query cannot express it and a julianday difference cannot be
    /// exact at tick precision. It supports exactly the normalized storage the host writes, never another offset or a malformed value.
    /// </summary>
    private static string Ticks(string column) =>
        $"(CAST(strftime('%s', substr(\"{column}\", 1, 19)) AS INTEGER) * 10000000 + CASE WHEN substr(\"{column}\", 20, 1) = '.' "
        + $"THEN CAST(substr(substr(\"{column}\", 21, instr(substr(\"{column}\", 21), '+') - 1) || '0000000', 1, 7) AS INTEGER) ELSE 0 END)";

    /// <summary>The longest confirmation lifetime, exactly two minutes, in ticks.</summary>
    private const long MaximumLifetimeTicks = 1_200_000_000;

    public void Configure(EntityTypeBuilder<LocalExecutionOwnershipRelease> builder)
    {
        builder.ToTable("local_execution_ownership_releases", table =>
        {
            table.HasCheckConstraint("ck_local_execution_ownership_releases_version", "typeof(\"Version\") = 'integer' AND \"Version\" = 1");
            table.HasCheckConstraint(
                "ck_local_execution_ownership_releases_confirmation",
                "typeof(\"ConfirmationVersion\") = 'integer' AND \"ConfirmationVersion\" = 1 AND typeof(\"ConfirmationSha256\") = 'text' "
                + "AND length(\"ConfirmationSha256\") = 64 AND \"ConfirmationSha256\" NOT GLOB '*[^0-9a-f]*'");
            table.HasCheckConstraint(
                "ck_local_execution_ownership_releases_time",
                "typeof(\"ReleasedAtUtc\") = 'text' AND typeof(\"ConfirmationExpiresAtUtc\") = 'text' "
                + $"AND {Ticks("ConfirmationExpiresAtUtc")} - {Ticks("ReleasedAtUtc")} <= {MaximumLifetimeTicks}");

            // ---- ADR-0066: the exact confirmed tuple -------------------------------------------------------------------------------------
            table.HasCheckConstraint(
                "ck_local_execution_ownership_releases_intent_version",
                "typeof(\"IntentVersion\") = 'integer' AND \"IntentVersion\" = 1 AND typeof(\"IntentConfirmationVersion\") = 'integer' "
                + "AND \"IntentConfirmationVersion\" = 1");
            table.HasCheckConstraint(
                "ck_local_execution_ownership_releases_intent_family",
                "(\"IntentFamily\" = 'Agent' AND \"IntentAgentOutcome\" IS NOT NULL AND \"IntentAgentResponseContract\" IS NOT NULL "
                + "AND \"IntentCompletionFingerprintSha256\" IS NULL AND \"IntentRecipeCommandId\" IS NULL AND \"IntentKind\" <> 'Verification') "
                + "OR (\"IntentFamily\" = 'Verification' AND \"IntentAgentOutcome\" IS NULL AND \"IntentAgentResponseContract\" IS NULL "
                + "AND \"IntentCompletionFingerprintSha256\" IS NOT NULL AND \"IntentRecipeCommandId\" IS NOT NULL AND \"IntentKind\" = 'Verification')");
            table.HasCheckConstraint(
                "ck_local_execution_ownership_releases_intent_vocabulary",
                "\"IntentKind\" IN ('Implementation', 'Verification', 'CodeReview', 'VerificationDiagnosis', 'ReviewCorrection', 'DiagnosisCorrection') "
                + "AND \"IntentSequenceState\" IN ('Stopped', 'NeedsAttention', 'Interrupted') "
                + "AND \"IntentAdmissionMode\" IN ('Immediate', 'WaitForAdmission') "
                + "AND ((\"IntentAdmissionMode\" = 'Immediate') = (\"IntentAdmissionId\" IS NULL)) "
                + "AND \"IntentProcessOutcome\" IN ('TimedOut', 'Cancelled') "
                + "AND (\"IntentAgentResponseContract\" IS NULL OR \"IntentAgentResponseContract\" IN "
                + "('ImplementationReport', 'ImplementationReview', 'ReviewCorrection', 'VerificationDiagnosis')) "
                + "AND (\"IntentAgentOutcome\" IS NULL OR \"IntentAgentOutcome\" IN "
                + "('SourceChanged', 'ProviderInvocationFailed', 'CheckpointEvidenceUnavailable', 'VerificationEvidenceChanged')) "
                + "AND typeof(\"IntentOperationStatus\") = 'text' AND length(\"IntentOperationStatus\") BETWEEN 1 AND 64");
            table.HasCheckConstraint(
                "ck_local_execution_ownership_releases_intent_numbers",
                "typeof(\"IntentOrdinal\") = 'integer' AND \"IntentOrdinal\" BETWEEN 0 AND 101 "
                + "AND typeof(\"IntentPassNumber\") = 'integer' AND \"IntentPassNumber\" BETWEEN 0 AND 3 "
                + "AND (\"IntentRecipeOrdinal\" IS NULL OR (typeof(\"IntentRecipeOrdinal\") = 'integer' AND \"IntentRecipeOrdinal\" BETWEEN 0 AND 31)) "
                + "AND \"IntentSemanticExitCode\" IS NULL "
                + "AND typeof(\"IntentPhysicalExitCode\") = 'integer' AND \"IntentPhysicalExitCode\" BETWEEN -2147483648 AND 2147483647 "
                + "AND typeof(\"IntentProcessDuration\") = 'integer' AND typeof(\"IntentObservedElapsed\") = 'integer' "
                + "AND \"IntentObservedElapsed\" >= 0 AND \"IntentObservedElapsed\" <= \"IntentProcessDuration\"");
            table.HasCheckConstraint(
                "ck_local_execution_ownership_releases_intent_shape",
                "((\"IntentKind\" = 'Implementation') = (\"IntentPassNumber\" = 0)) AND ((\"IntentKind\" = 'Implementation') = (\"IntentOrdinal\" = 0)) "
                + "AND ((\"IntentKind\" = 'Verification') = (\"IntentRecipeOrdinal\" IS NOT NULL)) "
                + "AND ((\"IntentKind\" = 'Implementation') = (\"IntentPredecessorStepId\" IS NULL)) "
                + "AND ((\"IntentKind\" = 'Implementation') = (\"IntentReportMessageId\" IS NULL)) "
                + "AND ((\"IntentKind\" IN ('ReviewCorrection', 'DiagnosisCorrection')) = (\"IntentSubjectAttemptId\" IS NOT NULL))");
            table.HasCheckConstraint(
                "ck_local_execution_ownership_releases_intent_fingerprints",
                "typeof(\"IntentSourceFingerprintSha256\") = 'text' AND length(\"IntentSourceFingerprintSha256\") = 64 "
                + "AND \"IntentSourceFingerprintSha256\" NOT GLOB '*[^0-9a-f]*' "
                + "AND (\"IntentCompletionFingerprintSha256\" IS NULL OR (typeof(\"IntentCompletionFingerprintSha256\") = 'text' "
                + "AND length(\"IntentCompletionFingerprintSha256\") = 64 AND \"IntentCompletionFingerprintSha256\" NOT GLOB '*[^0-9a-f]*'))");
            table.HasCheckConstraint(
                "ck_local_execution_ownership_releases_intent_times",
                "typeof(\"IntentDispatchedAtUtc\") = 'text' AND typeof(\"IntentObservationRecordedAtUtc\") = 'text' "
                + "AND typeof(\"IntentConfirmationExpiresAtUtc\") = 'text'");
        });
        builder.HasKey(release => release.Id);

        builder.Property(release => release.ConfirmationSha256).HasMaxLength(64).IsRequired();

        builder.Property(release => release.IntentFamily).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(release => release.IntentSequenceState).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(release => release.IntentAdmissionMode).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(release => release.IntentKind).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(release => release.IntentSourceFingerprintSha256).HasMaxLength(64).IsRequired();
        builder.Property(release => release.IntentAgentResponseContract).HasConversion<string>().HasMaxLength(32);
        builder.Property(release => release.IntentOperationStatus).HasMaxLength(64).IsRequired();
        builder.Property(release => release.IntentAgentOutcome).HasConversion<string>().HasMaxLength(64);
        builder.Property(release => release.IntentCompletionFingerprintSha256).HasMaxLength(64);
        builder.Property(release => release.IntentProcessOutcome).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(release => release.IntentProcessDuration).HasConversion(duration => duration.Ticks, ticks => TimeSpan.FromTicks(ticks));
        builder.Property(release => release.IntentObservedElapsed).HasConversion(duration => duration.Ticks, ticks => TimeSpan.FromTicks(ticks));

        builder.HasIndex(release => release.WitnessId).IsUnique().HasDatabaseName("ix_local_execution_ownership_releases_witness");
        builder.HasIndex(release => release.ObservationId).IsUnique().HasDatabaseName("ix_local_execution_ownership_releases_observation");
        builder.HasIndex(release => release.StepId).IsUnique().HasDatabaseName("ix_local_execution_ownership_releases_step");
        builder.HasIndex(release => release.EventId).IsUnique().HasDatabaseName("ix_local_execution_ownership_releases_event");

        builder.HasOne<LocalExecutionDispatchWitness>().WithMany().HasForeignKey(release => release.WitnessId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LocalExecutionTerminationObservation>().WithMany().HasForeignKey(release => release.ObservationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LocalExecutionSequenceStep>().WithMany().HasForeignKey(release => release.StepId).OnDelete(DeleteBehavior.Restrict);

        // The event association is proved by joining the actual event header (identity, run, type and actor), not by a key that would
        // rebuild the immutable events table; its identity is unique here.
    }
}
