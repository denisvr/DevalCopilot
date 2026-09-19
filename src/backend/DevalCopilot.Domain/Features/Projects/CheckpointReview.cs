namespace DevalCopilot.Domain.Features.Projects;

/// <summary>
/// One immutable review fact bound to an exact source checkpoint. A decided review's evidence is
/// a relational set of one-to-several <see cref="CheckpointReviewEvidence"/> membership rows —
/// never a single snapshot column and never a JSON blob standing in as the authoritative set.
/// Pending is an execution-free recorded state. A later review is a new fact; this entity has no
/// decision mutation.
/// </summary>
public sealed class CheckpointReview
{
    private List<CheckpointReviewEvidence> evidence = [];

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
        ReviewActorKind actorKind,
        ReviewDecision decision,
        DateTimeOffset recordedAtUtc,
        IReadOnlyCollection<CheckpointReviewEvidence> evidenceMembers)
    {
        if (!Enum.IsDefined(actorKind))
        {
            throw new ArgumentOutOfRangeException(nameof(actorKind));
        }

        if (!Enum.IsDefined(decision))
        {
            throw new ArgumentOutOfRangeException(nameof(decision));
        }

        if (id == Guid.Empty || projectId == Guid.Empty || gitWorkspaceId == Guid.Empty || gitCheckpointId == Guid.Empty)
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

        ArgumentNullException.ThrowIfNull(evidenceMembers);

        if (decision == ReviewDecision.Pending)
        {
            if (evidenceMembers.Count > 0)
            {
                throw new ArgumentException("A pending review cannot include verification evidence.", nameof(evidenceMembers));
            }
        }
        else if (evidenceMembers.Count == 0)
        {
            throw new ArgumentException("A decided review requires at least one verification evidence member.", nameof(evidenceMembers));
        }

        var seenExecutionIds = new HashSet<Guid>();
        var seenCommandIds = new HashSet<Guid>();
        foreach (var member in evidenceMembers)
        {
            if (member.CheckpointReviewId != id)
            {
                throw new ArgumentException("Every evidence member must belong to this review.", nameof(evidenceMembers));
            }

            if (!string.Equals(checkpointFingerprintSha256, member.VerificationExecutionCheckpointFingerprintSha256, StringComparison.Ordinal))
            {
                throw new ArgumentException("Review and verification evidence must use the same checkpoint fingerprint.", nameof(evidenceMembers));
            }

            if (!seenExecutionIds.Add(member.VerificationExecutionId))
            {
                throw new ArgumentException("The same verification execution cannot be claimed as evidence twice.", nameof(evidenceMembers));
            }

            if (!seenCommandIds.Add(member.VerificationCommandId))
            {
                throw new ArgumentException("The same verification command cannot contribute more than one evidence member.", nameof(evidenceMembers));
            }
        }

        if (decision == ReviewDecision.Approved && evidenceMembers.Any(member => member.VerificationExecutionStatus != VerificationExecutionStatus.Passed))
        {
            throw new ArgumentException("Approval requires every claimed verification execution to have Passed.", nameof(decision));
        }

        return new CheckpointReview
        {
            Id = id,
            ProjectId = projectId,
            GitWorkspaceId = gitWorkspaceId,
            GitCheckpointId = gitCheckpointId,
            CheckpointNumber = checkpointNumber,
            CheckpointFingerprintSha256 = checkpointFingerprintSha256,
            ActorKind = actorKind,
            Decision = decision,
            RecordedAtUtc = recordedAtUtc,
            RecordedAtUtcTicks = recordedAtUtc.UtcTicks,
            evidence = evidenceMembers.ToList(),
        };
    }

    public Guid Id { get; private set; }

    public Guid ProjectId { get; private set; }

    public Guid GitWorkspaceId { get; private set; }

    public Guid GitCheckpointId { get; private set; }

    public int CheckpointNumber { get; private set; }

    public string CheckpointFingerprintSha256 { get; private set; } = string.Empty;

    public ReviewActorKind ActorKind { get; private set; }

    public ReviewDecision Decision { get; private set; }

    public DateTimeOffset RecordedAtUtc { get; private set; }

    /// <summary>Provider-queryable UTC ordering scalar for bounded chronological history.</summary>
    public long RecordedAtUtcTicks { get; private set; }

    public IReadOnlyCollection<CheckpointReviewEvidence> Evidence => evidence.AsReadOnly();

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(character => char.IsAsciiHexDigit(character));
}
