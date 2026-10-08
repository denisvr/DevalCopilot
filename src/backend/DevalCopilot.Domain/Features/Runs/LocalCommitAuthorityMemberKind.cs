namespace DevalCopilot.Domain.Features.Runs;

public enum LocalCommitAuthorityMemberKind
{
    /// <summary>One enabled verification recipe with its latest Passed execution.</summary>
    Verification = 0,

    /// <summary>One human checkpoint-review row for the checkpoint; every pinned row is Approved.</summary>
    HumanReview = 1,
}
