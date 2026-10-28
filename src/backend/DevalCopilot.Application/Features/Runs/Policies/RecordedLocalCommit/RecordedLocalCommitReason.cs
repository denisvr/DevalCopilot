namespace DevalCopilot.Application.Features.Runs.Policies.RecordedLocalCommit;

/// <summary>The recorded reason of an operation as ADR-0070 discloses it: <see cref="Code"/> is a fixed allowlisted code only for
/// <see cref="RecordedLocalCommitReasonKind.Known"/> and is otherwise null. Arbitrary stored reason text is never carried.</summary>
public sealed record RecordedLocalCommitReason(RecordedLocalCommitReasonKind Kind, string? Code)
{
    public static readonly RecordedLocalCommitReason None = new(RecordedLocalCommitReasonKind.None, null);

    public static readonly RecordedLocalCommitReason Unrecognized = new(RecordedLocalCommitReasonKind.Unrecognized, null);
}
