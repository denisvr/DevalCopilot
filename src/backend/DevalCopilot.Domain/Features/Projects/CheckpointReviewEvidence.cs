namespace DevalCopilot.Domain.Features.Projects;

/// <summary>
/// One immutable evidence-membership fact binding a decided <see cref="CheckpointReview"/> to
/// exactly one terminal <see cref="VerificationExecution"/> it claimed as evidence. A decided
/// review may bind one-to-several of these — one per currently enabled verification command at
/// review time — never a JSON blob standing in as the authoritative set. Every internal
/// consistency rule a single review's own verification snapshot needed before this entity
/// existed is preserved here unchanged; <see cref="CheckpointReview.Record"/> additionally
/// enforces the cross-member rules (uniqueness, Approved requiring every member Passed) that
/// only make sense once more than one member can exist.
/// </summary>
public sealed class CheckpointReviewEvidence
{
    private CheckpointReviewEvidence()
    {
    }

    public static CheckpointReviewEvidence Observe(
        Guid id,
        Guid checkpointReviewId,
        Guid verificationCommandId,
        Guid verificationExecutionId,
        int verificationExecutionNumber,
        string verificationExecutionCheckpointFingerprintSha256,
        VerificationExecutionStatus verificationExecutionStatus,
        VerificationExecutionOutcome? verificationExecutionOutcome,
        int? verificationExecutionExitCode)
    {
        if (id == Guid.Empty || checkpointReviewId == Guid.Empty || verificationCommandId == Guid.Empty || verificationExecutionId == Guid.Empty)
        {
            throw new ArgumentException("Review evidence requires owned resource identifiers.");
        }

        if (verificationExecutionNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(verificationExecutionNumber));
        }

        if (!IsSha256(verificationExecutionCheckpointFingerprintSha256))
        {
            throw new ArgumentException("Review evidence requires a SHA-256 checkpoint fingerprint.", nameof(verificationExecutionCheckpointFingerprintSha256));
        }

        if (!Enum.IsDefined(verificationExecutionStatus))
        {
            throw new ArgumentOutOfRangeException(nameof(verificationExecutionStatus));
        }

        if (verificationExecutionStatus == VerificationExecutionStatus.Running)
        {
            throw new ArgumentException("Review evidence must be terminal.", nameof(verificationExecutionStatus));
        }

        if (verificationExecutionOutcome is not null && !Enum.IsDefined(verificationExecutionOutcome.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(verificationExecutionOutcome));
        }

        // Interrupted evidence can never carry a process outcome (the process was never actually
        // observed to finish). SourceChanged is the one status that may legitimately go either
        // way — drift caught before dispatch has no process outcome, drift caught after
        // completion still snapshots the real one — every other terminal status always requires
        // one.
        if (verificationExecutionStatus == VerificationExecutionStatus.Interrupted
            && (verificationExecutionOutcome.HasValue || verificationExecutionExitCode.HasValue))
        {
            throw new ArgumentException("Interrupted review evidence cannot have a process outcome or exit code.", nameof(verificationExecutionStatus));
        }

        if (verificationExecutionStatus is not VerificationExecutionStatus.Interrupted and not VerificationExecutionStatus.SourceChanged
            && !verificationExecutionOutcome.HasValue)
        {
            throw new ArgumentException("Terminal process evidence requires an outcome.", nameof(verificationExecutionOutcome));
        }

        if (!verificationExecutionOutcome.HasValue && verificationExecutionExitCode.HasValue)
        {
            throw new ArgumentException("An exit code requires a process outcome.", nameof(verificationExecutionExitCode));
        }

        if (verificationExecutionOutcome.HasValue
            && (verificationExecutionOutcome == global::DevalCopilot.Domain.Features.Projects.VerificationExecutionOutcome.Exited) != verificationExecutionExitCode.HasValue)
        {
            throw new ArgumentException("Only an exited verification execution may have an exit code.", nameof(verificationExecutionExitCode));
        }

        if (verificationExecutionStatus == VerificationExecutionStatus.TimedOut
            && verificationExecutionOutcome != global::DevalCopilot.Domain.Features.Projects.VerificationExecutionOutcome.TimedOut)
        {
            throw new ArgumentException("Timed out evidence must snapshot a timed out process outcome.", nameof(verificationExecutionOutcome));
        }

        if (verificationExecutionStatus == VerificationExecutionStatus.Cancelled
            && verificationExecutionOutcome != global::DevalCopilot.Domain.Features.Projects.VerificationExecutionOutcome.Cancelled)
        {
            throw new ArgumentException("Cancelled evidence must snapshot a cancelled process outcome.", nameof(verificationExecutionOutcome));
        }

        if (verificationExecutionStatus == VerificationExecutionStatus.Failed
            && verificationExecutionOutcome is not global::DevalCopilot.Domain.Features.Projects.VerificationExecutionOutcome.Failed
            && (verificationExecutionOutcome != global::DevalCopilot.Domain.Features.Projects.VerificationExecutionOutcome.Exited || verificationExecutionExitCode == 0))
        {
            throw new ArgumentException("Failed evidence must snapshot a failed process or a non-zero exit.", nameof(verificationExecutionOutcome));
        }

        if (verificationExecutionStatus == VerificationExecutionStatus.Passed
            && (verificationExecutionOutcome != global::DevalCopilot.Domain.Features.Projects.VerificationExecutionOutcome.Exited || verificationExecutionExitCode != 0))
        {
            throw new ArgumentException("A passed review evidence snapshot must have a zero exit code.", nameof(verificationExecutionStatus));
        }

        return new CheckpointReviewEvidence
        {
            Id = id,
            CheckpointReviewId = checkpointReviewId,
            VerificationCommandId = verificationCommandId,
            VerificationExecutionId = verificationExecutionId,
            VerificationExecutionNumber = verificationExecutionNumber,
            VerificationExecutionCheckpointFingerprintSha256 = verificationExecutionCheckpointFingerprintSha256,
            VerificationExecutionStatus = verificationExecutionStatus,
            VerificationExecutionOutcome = verificationExecutionOutcome,
            VerificationExecutionExitCode = verificationExecutionExitCode,
        };
    }

    public Guid Id { get; private set; }

    public Guid CheckpointReviewId { get; private set; }

    public Guid VerificationCommandId { get; private set; }

    public Guid VerificationExecutionId { get; private set; }

    public int VerificationExecutionNumber { get; private set; }

    public string VerificationExecutionCheckpointFingerprintSha256 { get; private set; } = string.Empty;

    public VerificationExecutionStatus VerificationExecutionStatus { get; private set; }

    public VerificationExecutionOutcome? VerificationExecutionOutcome { get; private set; }

    public int? VerificationExecutionExitCode { get; private set; }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(character => char.IsAsciiHexDigit(character));
}
