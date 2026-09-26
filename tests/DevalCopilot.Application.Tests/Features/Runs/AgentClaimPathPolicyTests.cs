using DevalCopilot.Application.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// Regression coverage for <see cref="AgentClaimPathPolicy"/>: this policy centralizes six
/// invocation-timeout values that used to be six separate, independently hardcoded
/// <c>private static readonly TimeSpan InvocationTimeout</c> fields, one per
/// <c>Create*AttemptCommandHandler</c>. Every value asserted here must remain byte-for-byte what
/// each handler used to hardcode before the centralization — this is a refactor to one source of
/// truth, never a value change. Moved here (from <c>DevalCopilot.Domain.Tests</c>) alongside the
/// policy's own corrected Application-layer location; role/provider lookups were removed from the
/// policy entirely (no consumer needed them beyond the now-leaner fit DTO), so this file no
/// longer covers them.
/// </summary>
public sealed class AgentClaimPathPolicyTests
{
    [Theory]
    [InlineData(AgentClaimPath.CodexPlanning, 10)]
    [InlineData(AgentClaimPath.ClaudeCriticalReview, 10)]
    [InlineData(AgentClaimPath.ChallengeResolution, 10)]
    [InlineData(AgentClaimPath.Implementation, 20)]
    [InlineData(AgentClaimPath.CodeReview, 10)]
    [InlineData(AgentClaimPath.ReviewCorrection, 20)]
    public void GetInvocationTimeout_returns_the_exact_previously_hardcoded_value_for_every_claim_path(
        AgentClaimPath claimPath, int expectedMinutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), AgentClaimPathPolicy.GetInvocationTimeout(claimPath));
    }

    [Fact]
    public void GetInvocationTimeout_throws_for_an_undefined_claim_path()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AgentClaimPathPolicy.GetInvocationTimeout((AgentClaimPath)999));
    }
}
