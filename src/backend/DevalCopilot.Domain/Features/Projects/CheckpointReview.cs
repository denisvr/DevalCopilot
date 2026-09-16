namespace DevalCopilot.Domain.Features.Projects;

/// <summary>
/// One immutable review fact bound to an exact source checkpoint. Decided reviews may snapshot
/// a terminal local verification execution; Pending is an execution-free recorded state. A later
/// review is a new fact; this entity has no decision mutation.
/// </summary>
public sealed class CheckpointReview
{
    private CheckpointReview()
    {
    }

    public static CheckpointReview Record(
        Guid id,
        Guid projectId,
        Guid gitWorkspaceId,
        Guid gitCheckpointId,
        int checkpointNumber,
        string checkpointFingerprintSha256,
        Guid? verificationExecutionId,
        int? verificationExecutionNumber,
        string? verificationExecutionCheckpointFingerprintSha256,
        VerificationExecutionStatus? verificationExecutionStatus,
        VerificationExecutionOutcome? verificationExecutionOutcome,
        int? verificationExecutionExitCode,
        ReviewActorKind actorKind,
        ReviewDecision decision,
        DateTimeOffset recordedAtUtc)
    {
        if (!Enum.IsDefined(actorKind))
        {
            throw new ArgumentOutOfRangeException(nameof(actorKind));
        }

        if (!Enum.IsDefined(decision))
        {
            throw new ArgumentOutOfRangeException(nameof(decision));
        }

        if (id == Guid.Empty || projectId == Guid.Empty || gitWorkspaceId == Guid.Empty || gitCheckpointId == Guid.Empty || verificationExecutionId == Guid.Empty)
        {
            throw new ArgumentException("A review requires owned resource identifiers.");
        }

        if (checkpointNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(checkpointNumber));
        }

        if (!IsSha256(checkpointFingerprintSha256))
        {
            throw new ArgumentException("A review requires SHA-256 evidence fingerprints.");
        }

        if (decision != ReviewDecision.Pending && (!verificationExecutionId.HasValue
            || !verificationExecutionNumber.HasValue
            || verificationExecutionNumber < 1
            || verificationExecutionCheckpointFingerprintSha256 is null
            || !IsSha256(verificationExecutionCheckpointFingerprintSha256)
            || !verificationExecutionStatus.HasValue
            || (!verificationExecutionOutcome.HasValue
                && verificationExecutionStatus != global::DevalCopilot.Domain.Features.Projects.VerificationExecutionStatus.Interrupted
                && verificationExecutionStatus != global::DevalCopilot.Domain.Features.Projects.VerificationExecutionStatus.SourceChanged)))
        {
            throw new ArgumentException("A decided review requires verification execution evidence.");
        }

        if (decision == ReviewDecision.Pending && (verificationExecutionId.HasValue
            || verificationExecutionNumber.HasValue
            || verificationExecutionCheckpointFingerprintSha256 is not null
            || verificationExecutionStatus.HasValue
            || verificationExecutionOutcome.HasValue
            || verificationExecutionExitCode.HasValue))
        {
            throw new ArgumentException("A pending review cannot include verification execution evidence.");
        }

        if (verificationExecutionCheckpointFingerprintSha256 is not null
            && !string.Equals(checkpointFingerprintSha256, verificationExecutionCheckpointFingerprintSha256, StringComparison.Ordinal))
        {
            throw new ArgumentException("Review and verification evidence must use the same checkpoint fingerprint.");
        }

        if (verificationExecutionStatus == global::DevalCopilot.Domain.Features.Projects.VerificationExecutionStatus.Running)
        {
            throw new ArgumentException("Review evidence must be terminal.", nameof(verificationExecutionStatus));
        }

        if (verificationExecutionStatus is not null && !Enum.IsDefined(verificationExecutionStatus.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(verificationExecutionStatus));
        }

        if (verificationExecutionOutcome is not null && !Enum.IsDefined(verificationExecutionOutcome.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(verificationExecutionOutcome));
        }

        if (verificationExecutionStatus == global::DevalCopilot.Domain.Features.Projects.VerificationExecutionStatus.Interrupted
            && (verificationExecutionOutcome.HasValue || verificationExecutionExitCode.HasValue))
        {
            throw new ArgumentException("Interrupted review evidence cannot have a process outcome or exit code.", nameof(verificationExecutionStatus));
        }

        if (!verificationExecutionOutcome.HasValue && verificationExecutionExitCode.HasValue)
        {
            throw new ArgumentException("An exit code requires a process outcome.", nameof(verificationExecutionExitCode));
        }

        if (verificationExecutionStatus is not null
            && verificationExecutionStatus is not global::DevalCopilot.Domain.Features.Projects.VerificationExecutionStatus.Interrupted
            && verificationExecutionStatus is not global::DevalCopilot.Domain.Features.Projects.VerificationExecutionStatus.SourceChanged
            && !verificationExecutionOutcome.HasValue)
        {
            throw new ArgumentException("Terminal process evidence requires an outcome.", nameof(verificationExecutionOutcome));
        }

        if (verificationExecutionOutcome.HasValue
            && (verificationExecutionOutcome == global::DevalCopilot.Domain.Features.Projects.VerificationExecutionOutcome.Exited) != verificationExecutionExitCode.HasValue)
        {
            throw new ArgumentException("Only an exited verification execution may have an exit code.", nameof(verificationExecutionExitCode));
        }

        if (verificationExecutionStatus == global::DevalCopilot.Domain.Features.Projects.VerificationExecutionStatus.TimedOut
            && verificationExecutionOutcome != global::DevalCopilot.Domain.Features.Projects.VerificationExecutionOutcome.TimedOut)
        {
            throw new ArgumentException("Timed out evidence must snapshot a timed out process outcome.", nameof(verificationExecutionOutcome));
        }

        if (verificationExecutionStatus == global::DevalCopilot.Domain.Features.Projects.VerificationExecutionStatus.Cancelled
            && verificationExecutionOutcome != global::DevalCopilot.Domain.Features.Projects.VerificationExecutionOutcome.Cancelled)
        {
            throw new ArgumentException("Cancelled evidence must snapshot a cancelled process outcome.", nameof(verificationExecutionOutcome));
        }

        if (verificationExecutionStatus == global::DevalCopilot.Domain.Features.Projects.VerificationExecutionStatus.Failed
            && verificationExecutionOutcome is not global::DevalCopilot.Domain.Features.Projects.VerificationExecutionOutcome.Failed
            && (verificationExecutionOutcome != global::DevalCopilot.Domain.Features.Projects.VerificationExecutionOutcome.Exited || verificationExecutionExitCode == 0))
        {
            throw new ArgumentException("Failed evidence must snapshot a failed process or a non-zero exit.", nameof(verificationExecutionOutcome));
        }

        if (verificationExecutionStatus == global::DevalCopilot.Domain.Features.Projects.VerificationExecutionStatus.Passed
            && (verificationExecutionOutcome != global::DevalCopilot.Domain.Features.Projects.VerificationExecutionOutcome.Exited || verificationExecutionExitCode != 0))
        {
            throw new ArgumentException("A passed review evidence snapshot must have a zero exit code.", nameof(verificationExecutionStatus));
        }

        if (decision == ReviewDecision.Approved && verificationExecutionStatus != global::DevalCopilot.Domain.Features.Projects.VerificationExecutionStatus.Passed)
        {
            throw new ArgumentException("Approval requires a passed verification execution.", nameof(decision));
        }

        return new CheckpointReview
        {
            Id = id,
            ProjectId = projectId,
            GitWorkspaceId = gitWorkspaceId,
            GitCheckpointId = gitCheckpointId,
            CheckpointNumber = checkpointNumber,
            CheckpointFingerprintSha256 = checkpointFingerprintSha256,
            VerificationExecutionId = verificationExecutionId,
            VerificationExecutionNumber = verificationExecutionNumber,
            VerificationExecutionCheckpointFingerprintSha256 = verificationExecutionCheckpointFingerprintSha256,
            VerificationExecutionStatus = verificationExecutionStatus,
            VerificationExecutionOutcome = verificationExecutionOutcome,
            VerificationExecutionExitCode = verificationExecutionExitCode,
            ActorKind = actorKind,
            Decision = decision,
            RecordedAtUtc = recordedAtUtc,
            RecordedAtUtcTicks = recordedAtUtc.UtcTicks,
        };
    }

    public Guid Id { get; private set; }

    public Guid ProjectId { get; private set; }

    public Guid GitWorkspaceId { get; private set; }

    public Guid GitCheckpointId { get; private set; }

    public int CheckpointNumber { get; private set; }

    public string CheckpointFingerprintSha256 { get; private set; } = string.Empty;

    public Guid? VerificationExecutionId { get; private set; }

    public int? VerificationExecutionNumber { get; private set; }

    public string? VerificationExecutionCheckpointFingerprintSha256 { get; private set; }

    public VerificationExecutionStatus? VerificationExecutionStatus { get; private set; }

    public VerificationExecutionOutcome? VerificationExecutionOutcome { get; private set; }

    public int? VerificationExecutionExitCode { get; private set; }

    public ReviewActorKind ActorKind { get; private set; }

    public ReviewDecision Decision { get; private set; }

    public DateTimeOffset RecordedAtUtc { get; private set; }

    /// <summary>Provider-queryable UTC ordering scalar for bounded chronological history.</summary>
    public long RecordedAtUtcTicks { get; private set; }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(character => char.IsAsciiHexDigit(character));
}
