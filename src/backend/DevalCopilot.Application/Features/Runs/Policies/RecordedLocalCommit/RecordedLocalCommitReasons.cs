using System.Collections.Frozen;

namespace DevalCopilot.Application.Features.Runs.Policies.RecordedLocalCommit;

/// <summary>
/// The operation-owned finite allowlist of recorded reason codes that the inspection describes (ADR-0070). Matching is exact and ordinal,
/// so a prefix lookalike, a differently cased code or a dynamically constructed native detail is never a known code. The list changes no
/// producer's accepted reasons: any other bounded nonblank reason is <see cref="RecordedLocalCommitReason.Unrecognized"/> and its text is
/// never returned.
/// </summary>
internal static class RecordedLocalCommitReasons
{
    public static readonly IReadOnlyList<string> KnownCodes =
    [
        "local_commit.authority_changed",
        "local_commit.git_unprovable",
        "local_commit.ownership_unprovable",
        "local_commit.head_not_bound",
        "local_commit.prepared_state_unexpected",
        "local_commit.unpromoted_state_unknown",
        "local_commit.branch_tip_unrecognized",
        "local_commit.unknown_index_lock",
        "local_commit.index_state_unrecognized",
        "local_commit.unknown_reference_lock",
        "local_commit.reference_lock_unproven",
        "local_commit.interrupted_before_execution",
        "local_commit.interrupted_unpromoted",
    ];

    private static readonly FrozenSet<string> Known = KnownCodes.ToFrozenSet(StringComparer.Ordinal);

    public static RecordedLocalCommitReason Describe(string reason) =>
        Known.TryGetValue(reason, out var code)
            ? new RecordedLocalCommitReason(RecordedLocalCommitReasonKind.Known, code)
            : RecordedLocalCommitReason.Unrecognized;
}
