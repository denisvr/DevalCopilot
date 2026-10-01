using System.Reflection;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Runs;

public sealed class AttemptDirectHumanGuidanceTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static Attempt ClaimImplementation(
        string? guidance,
        string version = ClaudeMutationAdapterContract.ImplementationV2,
        AgentPermissionProfile profile = AgentPermissionProfile.WorkspaceEditOnly) =>
        Attempt.ClaimAgentImplementationWithAssignment(
            Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), "fingerprint-1", Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, BaseTime, "opus", "high", profile, version, agentBudgetSlot: 1,
            directHumanGuidance: guidance);

    private static Attempt ClaimCorrection(string? guidance) =>
        Attempt.ClaimAgentReviewCorrectionWithModelRequest(
            Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), "fingerprint-1", Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, BaseTime, "sonnet", "low", agentBudgetSlot: 1,
            directHumanGuidance: guidance);

    private static void SetStored(Attempt attempt, string? stored) =>
        typeof(Attempt).GetField(Attempt.AgentDirectHumanGuidanceStorageProperty, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(attempt, stored);

    [Theory]
    [InlineData(null, DirectHumanGuidanceEvidence.NotRecorded)]
    [InlineData("Use the existing helper.", DirectHumanGuidanceEvidence.Provided)]
    public void Both_mutation_claims_snapshot_accepted_guidance_immutably_under_v2(string? guidance, DirectHumanGuidanceEvidence evidence)
    {
        foreach (var attempt in new[] { ClaimImplementation(guidance), ClaimCorrection(guidance) })
        {
            Assert.Equal(guidance, attempt.ReadAgentDirectHumanGuidance().Text);
            Assert.False(attempt.ReadAgentDirectHumanGuidance().IsMalformed);
            Assert.Equal(evidence, attempt.GetDirectHumanGuidanceEvidence());
            Assert.True(attempt.HasDispatchCoherentDirectHumanGuidance());
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("  padded  ")]
    [InlineData("café")]
    [InlineData("a\r\nb")]
    public void The_factories_refuse_text_that_is_not_already_its_accepted_normalized_form(string guidance)
    {
        Assert.Throws<ArgumentException>(() => ClaimImplementation(guidance));
        Assert.Throws<ArgumentException>(() => ClaimCorrection(guidance));
    }

    [Fact]
    public void Guidance_is_refused_beside_a_v1_contract_or_another_permission_profile()
    {
        Assert.Throws<ArgumentException>(() => ClaimImplementation("text", ClaudeMutationAdapterContract.ImplementationV1));
        Assert.Throws<ArgumentException>(() => ClaimImplementation("text", profile: AgentPermissionProfile.ReadOnly));
    }

    [Fact]
    public void The_legacy_convenience_overloads_record_no_guidance_and_classify_as_not_recorded()
    {
        var implementation = Attempt.ClaimAgentImplementation(
            Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), "fingerprint-1", Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, BaseTime, 1);
        var correction = Attempt.ClaimAgentReviewCorrection(
            Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), "fingerprint-1", Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, BaseTime, 1);

        foreach (var attempt in new[] { implementation, correction })
        {
            Assert.True(attempt.ReadAgentDirectHumanGuidance().IsAbsent);
            Assert.Equal(DirectHumanGuidanceEvidence.NotRecorded, attempt.GetDirectHumanGuidanceEvidence());
            Assert.True(attempt.HasDispatchCoherentDirectHumanGuidance());
        }
    }

    [Fact]
    public void Malformed_stored_text_is_unknown_never_dispatchable_and_never_exposed()
    {
        foreach (var stored in new[] { "", " x ", "café", new string('a', 601), "the password is x" })
        {
            var attempt = ClaimImplementation(null);
            SetStored(attempt, stored);

            Assert.True(attempt.ReadAgentDirectHumanGuidance().IsMalformed);
            Assert.Equal(DirectHumanGuidanceEvidence.Unknown, attempt.GetDirectHumanGuidanceEvidence());
            Assert.False(attempt.HasDispatchCoherentDirectHumanGuidance());
        }
    }

    [Fact]
    public void Guidance_stored_beside_an_incompatible_assignment_is_unknown_and_not_dispatchable()
    {
        var v1 = ClaimImplementation(null, ClaudeMutationAdapterContract.ImplementationV1);
        SetStored(v1, "text");
        Assert.Equal(DirectHumanGuidanceEvidence.Unknown, v1.GetDirectHumanGuidanceEvidence());
        Assert.False(v1.HasDispatchCoherentDirectHumanGuidance());

        var coherent = ClaimCorrection(null);
        Assert.Equal(DirectHumanGuidanceEvidence.NotRecorded, coherent.GetDirectHumanGuidanceEvidence());
    }

    [Theory]
    [InlineData(AgentResponseContract.ImplementationReport, AgentRole.Implementer, AgentProvider.ClaudeCode, AgentPermissionProfile.WorkspaceEditOnly, ClaudeMutationAdapterContract.ImplementationV2, "t", DirectHumanGuidanceEvidence.Provided, true)]
    [InlineData(AgentResponseContract.ReviewCorrection, AgentRole.Implementer, AgentProvider.ClaudeCode, AgentPermissionProfile.WorkspaceEditOnly, ClaudeMutationAdapterContract.ReviewCorrectionV2, "t", DirectHumanGuidanceEvidence.Provided, true)]
    [InlineData(AgentResponseContract.ReviewCorrection, AgentRole.Implementer, AgentProvider.ClaudeCode, AgentPermissionProfile.WorkspaceEditOnly, ClaudeMutationAdapterContract.ReviewCorrectionV2, null, DirectHumanGuidanceEvidence.NotRecorded, true)]
    [InlineData(AgentResponseContract.ReviewCorrection, AgentRole.Implementer, AgentProvider.ClaudeCode, AgentPermissionProfile.WorkspaceEditOnly, ClaudeMutationAdapterContract.ReviewCorrectionV1, null, DirectHumanGuidanceEvidence.NotRecorded, true)]
    [InlineData(AgentResponseContract.ReviewCorrection, AgentRole.Implementer, AgentProvider.ClaudeCode, AgentPermissionProfile.WorkspaceEditOnly, ClaudeMutationAdapterContract.ReviewCorrectionV1, "t", DirectHumanGuidanceEvidence.Unknown, false)]
    [InlineData(AgentResponseContract.ReviewCorrection, AgentRole.Implementer, AgentProvider.ClaudeCode, AgentPermissionProfile.WorkspaceEditOnly, ClaudeMutationAdapterContract.ImplementationV2, "t", DirectHumanGuidanceEvidence.Unknown, false)]
    [InlineData(AgentResponseContract.ImplementationReport, AgentRole.Implementer, AgentProvider.Codex, AgentPermissionProfile.WorkspaceEditOnly, ClaudeMutationAdapterContract.ImplementationV2, "t", DirectHumanGuidanceEvidence.Unknown, false)]
    [InlineData(AgentResponseContract.ImplementationReport, AgentRole.CodeReviewer, AgentProvider.ClaudeCode, AgentPermissionProfile.WorkspaceEditOnly, ClaudeMutationAdapterContract.ImplementationV2, "t", DirectHumanGuidanceEvidence.Unknown, false)]
    [InlineData(AgentResponseContract.ImplementationReport, AgentRole.Implementer, AgentProvider.ClaudeCode, AgentPermissionProfile.ReadOnly, ClaudeMutationAdapterContract.ImplementationV2, "t", DirectHumanGuidanceEvidence.Unknown, false)]
    [InlineData(AgentResponseContract.ImplementationReport, AgentRole.Implementer, AgentProvider.ClaudeCode, AgentPermissionProfile.WorkspaceEditOnly, "claude-implementation-v3", "t", DirectHumanGuidanceEvidence.Unknown, false)]
    [InlineData(AgentResponseContract.Proposal, AgentRole.Planner, AgentProvider.Codex, AgentPermissionProfile.ReadOnly, "codex-planning-v1", "t", DirectHumanGuidanceEvidence.Unknown, false)]
    [InlineData(AgentResponseContract.Proposal, AgentRole.Planner, AgentProvider.Codex, AgentPermissionProfile.ReadOnly, "codex-planning-v1", null, DirectHumanGuidanceEvidence.NotRecorded, true)]
    public void The_exact_version_aware_classification_and_dispatch_coherence(
        AgentResponseContract contract, AgentRole role, AgentProvider provider, AgentPermissionProfile profile,
        string version, string? text, DirectHumanGuidanceEvidence evidence, bool coherent)
    {
        var reading = DirectHumanGuidance.Read(text);

        Assert.Equal(evidence, ClaudeMutationAdapterContract.ClassifyDirectGuidance(contract, role, provider, profile, version, reading));
        Assert.Equal(coherent, ClaudeMutationAdapterContract.IsDirectGuidanceDispatchCoherent(contract, role, provider, profile, version, reading));
    }

    [Fact]
    public void A_malformed_reading_is_always_unknown_and_never_dispatchable()
    {
        var reading = DirectHumanGuidanceReading.Malformed;

        Assert.Equal(
            DirectHumanGuidanceEvidence.Unknown,
            ClaudeMutationAdapterContract.ClassifyDirectGuidance(
                AgentResponseContract.ImplementationReport, AgentRole.Implementer, AgentProvider.ClaudeCode,
                AgentPermissionProfile.WorkspaceEditOnly, ClaudeMutationAdapterContract.ImplementationV2, reading));
        Assert.False(ClaudeMutationAdapterContract.IsDirectGuidanceDispatchCoherent(
            AgentResponseContract.ImplementationReport, AgentRole.Implementer, AgentProvider.ClaudeCode,
            AgentPermissionProfile.WorkspaceEditOnly, ClaudeMutationAdapterContract.ImplementationV2, reading));
    }
}
