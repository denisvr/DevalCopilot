using DevalCopilot.Application.Features.Runs.Ports;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>One completed preparation exactly as the real preparer returned it, in the order the results were captured
/// (<see cref="Sequence"/> starts at 1). Only a result that is <c>Prepared</c> AND carries facts supplies an artifact; a refusal, a
/// failure, or a malformed result supplies no artifact authority, whatever else it carries.</summary>
internal sealed record LocalCommitPreparationCompletion(
    int Sequence, LocalCommitPreparationRequest Request, LocalCommitPreparationResult Result)
{
    public bool IsPrepared => Result is { Outcome: LocalCommitPreparationOutcome.Prepared, Facts: not null };

    /// <summary>The exact relative artifact path the preparation recorded, or null when it supplied no artifact authority.</summary>
    public string? ArtifactRelativePath => IsPrepared ? Result.Facts?.PreparedIndexRelativePath : null;
}
