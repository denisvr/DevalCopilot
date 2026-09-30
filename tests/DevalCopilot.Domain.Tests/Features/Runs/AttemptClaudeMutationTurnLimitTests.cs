using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Runs;

public sealed class AttemptClaudeMutationTurnLimitTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);

    private static Attempt ClaimImplementation(
        int? maxTurns,
        string version = ClaudeMutationAdapterContract.ImplementationV2,
        AgentPermissionProfile profile = AgentPermissionProfile.WorkspaceEditOnly,
        string? model = "opus",
        string? effort = "high") => Attempt.ClaimAgentImplementationWithAssignment(
        Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), "fingerprint-1", Guid.NewGuid(),
        TimeSpan.FromMinutes(10), 262144, 524288, BaseTime, model, effort, profile, version, agentBudgetSlot: 1,
        requestedMaxTurns: maxTurns);

    private static Attempt ClaimCorrection(int? maxTurns, string? model = "sonnet", string? effort = "low") =>
        Attempt.ClaimAgentReviewCorrectionWithModelRequest(
            Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), "fingerprint-1", Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, BaseTime, model, effort, agentBudgetSlot: 1,
            requestedMaxTurns: maxTurns);

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(12)]
    [InlineData(100)]
    public void The_implementation_factory_snapshots_the_limit_under_the_v2_contract(int? maxTurns)
    {
        var attempt = ClaimImplementation(maxTurns);

        Assert.Equal(maxTurns, attempt.AgentRequestedMaxTurns);
        Assert.Equal("claude-implementation-v2", attempt.AgentAdapterContractVersion);
        Assert.Equal(
            maxTurns is null ? ClaudeMutationTurnLimitEvidence.NotRequested : ClaudeMutationTurnLimitEvidence.Requested,
            attempt.GetMutationTurnLimitEvidence());
        Assert.Equal(maxTurns, attempt.GetAssignmentSnapshot()!.RequestedMaxTurns);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(12)]
    [InlineData(100)]
    public void The_review_correction_factory_always_uses_v2_and_snapshots_the_limit(int? maxTurns)
    {
        var attempt = ClaimCorrection(maxTurns);

        Assert.Equal(maxTurns, attempt.AgentRequestedMaxTurns);
        Assert.Equal("claude-review-correction-v2", attempt.AgentAdapterContractVersion);
        Assert.Equal(
            maxTurns is null ? ClaudeMutationTurnLimitEvidence.NotRequested : ClaudeMutationTurnLimitEvidence.Requested,
            attempt.GetMutationTurnLimitEvidence());
        Assert.Equal(maxTurns, attempt.GetAssignmentSnapshot()!.RequestedMaxTurns);
    }

    [Fact]
    public void The_convenience_overloads_stay_v1_with_no_limit()
    {
        var implementation = Attempt.ClaimAgentImplementation(
            Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), "fingerprint-1", Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, BaseTime, 1);
        var correction = Attempt.ClaimAgentReviewCorrection(
            Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), "fingerprint-1", Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, BaseTime, 1);

        Assert.Equal("claude-implementation-v1", implementation.AgentAdapterContractVersion);
        Assert.Null(implementation.AgentRequestedMaxTurns);
        Assert.Equal(ClaudeMutationTurnLimitEvidence.NotRecorded, implementation.GetMutationTurnLimitEvidence());
        Assert.Equal("claude-review-correction-v1", correction.AgentAdapterContractVersion);
        Assert.Null(correction.AgentRequestedMaxTurns);
        Assert.Equal(ClaudeMutationTurnLimitEvidence.NotRecorded, correction.GetMutationTurnLimitEvidence());
    }

    [Theory]
    [InlineData("claude-implementation-v1")]
    [InlineData("claude-implementation-v3")]
    [InlineData("claude-review-correction-v2")]
    [InlineData("Claude-Implementation-V2")]
    [InlineData("custom-contract")]
    public void A_limit_is_rejected_with_any_version_other_than_exactly_implementation_v2(string version)
    {
        Assert.Throws<ArgumentException>(() => ClaimImplementation(5, version));
    }

    [Fact]
    public void A_limit_is_rejected_with_a_permission_profile_other_than_workspace_edit_only()
    {
        Assert.ThrowsAny<ArgumentException>(() => ClaimImplementation(5, profile: AgentPermissionProfile.ReadOnly));
        Assert.ThrowsAny<ArgumentException>(() => ClaimImplementation(5, profile: AgentPermissionProfile.Unknown));
    }

    [Fact]
    public void No_limit_is_still_accepted_with_the_v1_version_and_records_nothing()
    {
        var attempt = ClaimImplementation(null, ClaudeMutationAdapterContract.ImplementationV1);

        Assert.Null(attempt.AgentRequestedMaxTurns);
        Assert.Equal(ClaudeMutationTurnLimitEvidence.NotRecorded, attempt.GetMutationTurnLimitEvidence());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void Out_of_range_limits_are_rejected_by_both_factories(int maxTurns)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ClaimImplementation(maxTurns));
        Assert.Throws<ArgumentOutOfRangeException>(() => ClaimCorrection(maxTurns));
    }

    [Fact]
    public void The_limit_leaves_every_other_assignment_fact_unchanged()
    {
        var without = ClaimImplementation(null);
        var with = ClaimImplementation(25);
        var correctionWithout = ClaimCorrection(null);
        var correctionWith = ClaimCorrection(25);

        AssertSameExceptLimit(without.GetAssignmentSnapshot()!, with.GetAssignmentSnapshot()!);
        AssertSameExceptLimit(correctionWithout.GetAssignmentSnapshot()!, correctionWith.GetAssignmentSnapshot()!);
        Assert.Equal("opus", with.AgentRequestedModel);
        Assert.Equal("high", with.AgentRequestedEffort);
        Assert.Equal(AgentPermissionProfile.WorkspaceEditOnly, with.AgentPermissionProfile);
        Assert.Equal(AgentRole.Implementer, correctionWith.AgentRole);
        Assert.Equal(AgentResponseContract.ReviewCorrection, correctionWith.AgentResponseContract);
        Assert.Equal(AgentResponseContract.ImplementationReport, with.AgentResponseContract);
        Assert.Equal("sonnet", correctionWith.AgentRequestedModel);
        Assert.Equal("low", correctionWith.AgentRequestedEffort);
    }

    [Fact]
    public void Provider_observations_do_not_change_the_recorded_limit()
    {
        var attempt = ClaimImplementation(8);

        attempt.RecordAgentObservedAssignment("observed-model", "observed-effort");

        Assert.Equal(8, attempt.AgentRequestedMaxTurns);
        Assert.Equal(8, attempt.GetAssignmentSnapshot()!.RequestedMaxTurns);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("101")]
    [InlineData("2147483647")]
    [InlineData("3.5")]
    [InlineData("4294967297")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("007")]
    [InlineData("+5")]
    [InlineData(" 5")]
    [InlineData("5 ")]
    [InlineData("5.0")]
    [InlineData("1e1")]
    public void A_malformed_stored_limit_is_not_projected_never_a_request_and_classifies_as_unknown(string stored)
    {
        foreach (var attempt in new[] { ClaimImplementation(5), ClaimCorrection(5) })
        {
            typeof(Attempt)
                .GetField(Attempt.AgentRequestedMaxTurnsStorageProperty, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(attempt, stored);

            Assert.True(attempt.ReadAgentRequestedMaxTurns().IsMalformed);
            Assert.Null(attempt.ReadAgentRequestedMaxTurns().Value);
            Assert.Throws<InvalidOperationException>(() => attempt.AgentRequestedMaxTurns);
            Assert.Null(attempt.GetAssignmentSnapshot());
            Assert.Equal(ClaudeMutationTurnLimitEvidence.Unknown, attempt.GetMutationTurnLimitEvidence());
            Assert.False(attempt.HasDispatchCoherentTurnLimit());
        }
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("9", 9)]
    [InlineData("10", 10)]
    [InlineData("99", 99)]
    [InlineData("100", 100)]
    public void A_canonical_stored_limit_reads_exactly(string stored, int expected)
    {
        var reading = ClaudeMutationTurnLimit.Read(stored);

        Assert.False(reading.IsMalformed);
        Assert.Equal(expected, reading.Value);
        Assert.Equal(stored, ClaudeMutationTurnLimit.Format(expected));
    }

    [Fact]
    public void An_absent_stored_limit_is_absent_and_a_malformed_reading_is_neither_absent_nor_valid()
    {
        Assert.True(ClaudeMutationTurnLimit.Read(null).IsAbsent);
        Assert.False(ClaudeMutationTurnLimit.Read("abc").IsAbsent);
        Assert.Null(ClaudeMutationTurnLimit.Format(null));
        Assert.Throws<ArgumentOutOfRangeException>(() => ClaudeMutationTurnLimit.Format(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ClaudeMutationTurnLimit.Format(101));
    }

    [Fact]
    public void A_recorded_request_is_dispatch_coherent_only_with_the_complete_tuple_and_absence_is_unaffected()
    {
        Assert.True(ClaimImplementation(5).HasDispatchCoherentTurnLimit());
        Assert.True(ClaimCorrection(5).HasDispatchCoherentTurnLimit());
        Assert.True(ClaimImplementation(null).HasDispatchCoherentTurnLimit());

        var valid = ClaudeMutationTurnLimitReading.Absent with { Value = 7 };
        Assert.True(ClaudeMutationAdapterContract.IsDispatchCoherent(
            AgentResponseContract.ImplementationReport, AgentRole.Implementer, AgentProvider.ClaudeCode,
            AgentPermissionProfile.WorkspaceEditOnly, ClaudeMutationAdapterContract.ImplementationV2, valid));
        foreach (var profile in new AgentPermissionProfile?[] { AgentPermissionProfile.ReadOnly, AgentPermissionProfile.Unknown, null })
        {
            Assert.False(ClaudeMutationAdapterContract.IsDispatchCoherent(
                AgentResponseContract.ImplementationReport, AgentRole.Implementer, AgentProvider.ClaudeCode,
                profile, ClaudeMutationAdapterContract.ImplementationV2, valid));
        }

        foreach (var version in new[] { null, string.Empty, ClaudeMutationAdapterContract.ImplementationV1, "claude-implementation-v3", ClaudeMutationAdapterContract.ReviewCorrectionV2 })
        {
            Assert.False(ClaudeMutationAdapterContract.IsDispatchCoherent(
                AgentResponseContract.ImplementationReport, AgentRole.Implementer, AgentProvider.ClaudeCode,
                AgentPermissionProfile.WorkspaceEditOnly, version, valid));
        }

        Assert.False(ClaudeMutationAdapterContract.IsDispatchCoherent(
            AgentResponseContract.ImplementationReport, AgentRole.Implementer, AgentProvider.Codex,
            AgentPermissionProfile.WorkspaceEditOnly, ClaudeMutationAdapterContract.ImplementationV2, valid));
        Assert.False(ClaudeMutationAdapterContract.IsDispatchCoherent(
            AgentResponseContract.Proposal, AgentRole.Planner, AgentProvider.Codex,
            AgentPermissionProfile.ReadOnly, "codex-planning-v1", valid));
        Assert.False(ClaudeMutationAdapterContract.IsDispatchCoherent(
            AgentResponseContract.ImplementationReport, AgentRole.Implementer, AgentProvider.ClaudeCode,
            AgentPermissionProfile.WorkspaceEditOnly, ClaudeMutationAdapterContract.ImplementationV2, ClaudeMutationTurnLimitReading.Malformed));
        Assert.True(ClaudeMutationAdapterContract.IsDispatchCoherent(
            AgentResponseContract.ImplementationReport, AgentRole.Implementer, AgentProvider.ClaudeCode,
            null, ClaudeMutationAdapterContract.ImplementationV1, ClaudeMutationTurnLimitReading.Absent));
    }

    [Fact]
    public void A_non_mutation_attempt_has_no_limit_and_records_nothing()
    {
        var review = Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), "fingerprint-1", Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, BaseTime, 1);

        Assert.Null(review.AgentRequestedMaxTurns);
        Assert.Equal(ClaudeMutationTurnLimitEvidence.NotRecorded, review.GetMutationTurnLimitEvidence());
        Assert.Null(review.GetAssignmentSnapshot()!.RequestedMaxTurns);
    }

    private static void AssertSameExceptLimit(AgentAssignmentSnapshot expected, AgentAssignmentSnapshot actual)
    {
        Assert.Equal(expected with { RequestedMaxTurns = null }, actual with { RequestedMaxTurns = null });
    }
}
