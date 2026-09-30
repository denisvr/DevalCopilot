using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Runs;

public sealed class ClaudeMutationAdapterContractTests
{
    [Fact]
    public void The_version_constants_are_the_stable_wire_values()
    {
        Assert.Equal("claude-implementation-v1", ClaudeMutationAdapterContract.ImplementationV1);
        Assert.Equal("claude-implementation-v2", ClaudeMutationAdapterContract.ImplementationV2);
        Assert.Equal("claude-review-correction-v1", ClaudeMutationAdapterContract.ReviewCorrectionV1);
        Assert.Equal("claude-review-correction-v2", ClaudeMutationAdapterContract.ReviewCorrectionV2);
    }

    [Theory]
    [InlineData("claude-implementation-v2", true)]
    [InlineData("claude-review-correction-v2", true)]
    [InlineData("claude-implementation-v1", false)]
    [InlineData("claude-review-correction-v1", false)]
    [InlineData("claude-implementation-v3", false)]
    [InlineData("Claude-Implementation-V2", false)]
    [InlineData("claude-implementation-v2 ", false)]
    [InlineData("claude-critical-review-v1", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void CarriesTurnLimit_is_true_only_for_the_exact_v2_versions(string? version, bool expected)
    {
        Assert.Equal(expected, ClaudeMutationAdapterContract.CarriesTurnLimit(version));
    }

    [Theory]
    [InlineData(AgentResponseContract.ImplementationReport, "claude-implementation-v1", true)]
    [InlineData(AgentResponseContract.ImplementationReport, "claude-implementation-v2", true)]
    [InlineData(AgentResponseContract.ImplementationReport, "claude-implementation-v3", false)]
    [InlineData(AgentResponseContract.ImplementationReport, "claude-review-correction-v1", false)]
    [InlineData(AgentResponseContract.ImplementationReport, "claude-review-correction-v2", false)]
    [InlineData(AgentResponseContract.ImplementationReport, null, false)]
    [InlineData(AgentResponseContract.ImplementationReport, "", false)]
    [InlineData(AgentResponseContract.ReviewCorrection, "claude-review-correction-v1", true)]
    [InlineData(AgentResponseContract.ReviewCorrection, "claude-review-correction-v2", true)]
    [InlineData(AgentResponseContract.ReviewCorrection, "claude-review-correction-v3", false)]
    [InlineData(AgentResponseContract.ReviewCorrection, "claude-implementation-v1", false)]
    [InlineData(AgentResponseContract.ReviewCorrection, "claude-implementation-v2", false)]
    [InlineData(AgentResponseContract.ReviewCorrection, null, false)]
    [InlineData(AgentResponseContract.CriticalReview, "claude-implementation-v1", false)]
    [InlineData(AgentResponseContract.Proposal, "claude-review-correction-v2", false)]
    [InlineData(null, "claude-implementation-v2", false)]
    public void IsKnownVersionFor_accepts_only_the_exact_v1_or_v2_of_the_owning_path(
        AgentResponseContract? contract, string? version, bool expected)
    {
        Assert.Equal(expected, ClaudeMutationAdapterContract.IsKnownVersionFor(contract, version));
    }

    [Theory]
    [InlineData(AgentResponseContract.ImplementationReport, "claude-implementation-v1", "claude-implementation-v2")]
    [InlineData(AgentResponseContract.ReviewCorrection, "claude-review-correction-v1", "claude-review-correction-v2")]
    public void Classify_yields_the_four_outcomes_for_a_coherent_mutation_attempt(
        AgentResponseContract contract, string v1, string v2)
    {
        Assert.Equal(ClaudeMutationTurnLimitEvidence.NotRecorded, Classify(contract, v1, null));
        Assert.Equal(ClaudeMutationTurnLimitEvidence.NotRequested, Classify(contract, v2, null));
        Assert.Equal(ClaudeMutationTurnLimitEvidence.Requested, Classify(contract, v2, 1));
        Assert.Equal(ClaudeMutationTurnLimitEvidence.Requested, Classify(contract, v2, 42));
        Assert.Equal(ClaudeMutationTurnLimitEvidence.Requested, Classify(contract, v2, 100));
        Assert.Equal(ClaudeMutationTurnLimitEvidence.Unknown, Classify(contract, v1, 5));
    }

    [Theory]
    [InlineData(AgentResponseContract.ImplementationReport, "claude-implementation-v2")]
    [InlineData(AgentResponseContract.ReviewCorrection, "claude-review-correction-v2")]
    public void Classify_is_unknown_for_a_wrong_role_provider_or_permission_profile(
        AgentResponseContract contract, string v2)
    {
        foreach (var maxTurns in new int?[] { null, 5 })
        {
            AssertUnknown(contract, AgentRole.CriticalReviewer, AgentProvider.ClaudeCode, AgentPermissionProfile.WorkspaceEditOnly, v2, maxTurns);
            AssertUnknown(contract, null, AgentProvider.ClaudeCode, AgentPermissionProfile.WorkspaceEditOnly, v2, maxTurns);
            AssertUnknown(contract, AgentRole.Implementer, AgentProvider.Codex, AgentPermissionProfile.WorkspaceEditOnly, v2, maxTurns);
            AssertUnknown(contract, AgentRole.Implementer, null, AgentPermissionProfile.WorkspaceEditOnly, v2, maxTurns);
            AssertUnknown(contract, AgentRole.Implementer, AgentProvider.ClaudeCode, AgentPermissionProfile.ReadOnly, v2, maxTurns);
            AssertUnknown(contract, AgentRole.Implementer, AgentProvider.ClaudeCode, AgentPermissionProfile.Unknown, v2, maxTurns);
            AssertUnknown(contract, AgentRole.Implementer, AgentProvider.ClaudeCode, null, v2, maxTurns);
        }
    }

    [Theory]
    [InlineData(AgentResponseContract.ImplementationReport, "claude-implementation-v2")]
    [InlineData(AgentResponseContract.ReviewCorrection, "claude-review-correction-v2")]
    public void Classify_is_unknown_for_an_out_of_range_limit(AgentResponseContract contract, string v2)
    {
        foreach (var maxTurns in new[] { 0, -1, 101, int.MinValue, int.MaxValue })
        {
            Assert.Equal(ClaudeMutationTurnLimitEvidence.Unknown, Classify(contract, v2, maxTurns));
        }
    }

    [Theory]
    [InlineData(AgentResponseContract.ImplementationReport)]
    [InlineData(AgentResponseContract.ReviewCorrection)]
    public void Classify_is_unknown_for_an_unknown_missing_or_crossed_version(AgentResponseContract contract)
    {
        var other = contract == AgentResponseContract.ImplementationReport
            ? "claude-review-correction-v2"
            : "claude-implementation-v2";

        foreach (var version in new string?[] { "claude-implementation-v3", "claude-review-correction-v3", "", null, other })
        {
            Assert.Equal(ClaudeMutationTurnLimitEvidence.Unknown, Classify(contract, version, null));
            Assert.Equal(ClaudeMutationTurnLimitEvidence.Unknown, Classify(contract, version, 5));
        }
    }

    [Theory]
    [InlineData(AgentResponseContract.Proposal)]
    [InlineData(AgentResponseContract.CriticalReview)]
    [InlineData(AgentResponseContract.ChallengeResolution)]
    [InlineData(AgentResponseContract.ImplementationReview)]
    public void Classify_is_not_recorded_for_a_non_mutation_contract_without_a_limit_and_unknown_with_one(
        AgentResponseContract contract)
    {
        Assert.Equal(ClaudeMutationTurnLimitEvidence.NotRecorded, Classify(contract, "claude-critical-review-v1", null));
        Assert.Equal(ClaudeMutationTurnLimitEvidence.Unknown, Classify(contract, "claude-critical-review-v1", 1));
        Assert.Equal(
            ClaudeMutationTurnLimitEvidence.NotRecorded,
            ClaudeMutationAdapterContract.Classify(contract, null, null, null, null, null));
        Assert.Equal(
            ClaudeMutationTurnLimitEvidence.Unknown,
            ClaudeMutationAdapterContract.Classify(contract, null, null, null, null, 3));
    }

    [Fact]
    public void Classify_treats_a_missing_contract_like_a_non_mutation_attempt()
    {
        Assert.Equal(
            ClaudeMutationTurnLimitEvidence.NotRecorded,
            ClaudeMutationAdapterContract.Classify(null, null, null, null, null, null));
        Assert.Equal(
            ClaudeMutationTurnLimitEvidence.Unknown,
            ClaudeMutationAdapterContract.Classify(null, null, null, null, null, 3));
    }

    private static void AssertUnknown(
        AgentResponseContract contract,
        AgentRole? role,
        AgentProvider? provider,
        AgentPermissionProfile? profile,
        string version,
        int? maxTurns) => Assert.Equal(
        ClaudeMutationTurnLimitEvidence.Unknown,
        ClaudeMutationAdapterContract.Classify(contract, role, provider, profile, version, maxTurns));

    private static ClaudeMutationTurnLimitEvidence Classify(
        AgentResponseContract contract, string? version, int? maxTurns) =>
        ClaudeMutationAdapterContract.Classify(
            contract, AgentRole.Implementer, AgentProvider.ClaudeCode, AgentPermissionProfile.WorkspaceEditOnly,
            version, maxTurns);
}
