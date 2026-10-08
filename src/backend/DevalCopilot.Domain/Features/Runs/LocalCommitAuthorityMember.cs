namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// One immutable membership fact of the authority an operation was admitted against: a verification recipe/execution pair or
/// a human review row. The relational rows, not a JSON blob, are the authoritative membership; <see cref="Digest"/> binds each
/// row's own content (command snapshot and completion fingerprint, or the review's decision and evidence) so a change that keeps
/// the identifiers is still detected.
/// </summary>
public sealed class LocalCommitAuthorityMember
{
    private LocalCommitAuthorityMember()
    {
    }

    public static LocalCommitAuthorityMember Record(
        Guid id,
        Guid operationId,
        LocalCommitAuthorityMemberKind kind,
        int sequence,
        Guid subjectId,
        Guid? commandId,
        string digest)
    {
        if (id == Guid.Empty || operationId == Guid.Empty || subjectId == Guid.Empty)
        {
            throw new ArgumentException("An authority member requires durable identifiers.");
        }

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (sequence < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence));
        }

        if (kind == LocalCommitAuthorityMemberKind.Verification && commandId is null)
        {
            throw new ArgumentException("A verification member requires its command.", nameof(commandId));
        }

        if (!LocalCommitOperation.IsSha256(digest))
        {
            throw new ArgumentException("An authority member requires a SHA-256 digest.", nameof(digest));
        }

        return new LocalCommitAuthorityMember
        {
            Id = id,
            OperationId = operationId,
            Kind = kind,
            Sequence = sequence,
            SubjectId = subjectId,
            CommandId = commandId,
            Digest = digest,
        };
    }

    public Guid Id { get; private set; }

    public Guid OperationId { get; private set; }

    public LocalCommitAuthorityMemberKind Kind { get; private set; }

    public int Sequence { get; private set; }

    /// <summary>The verification execution or the human checkpoint review.</summary>
    public Guid SubjectId { get; private set; }

    public Guid? CommandId { get; private set; }

    public string Digest { get; private set; } = string.Empty;
}
