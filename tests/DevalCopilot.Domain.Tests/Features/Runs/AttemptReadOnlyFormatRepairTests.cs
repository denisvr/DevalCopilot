using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Runs;

/// <summary>
/// The Domain rule of the one manual format repair for the three read-only stages beyond the Planner
/// (CriticalReviewer, Resolver, CodeReviewer): the repair claim is an ordinary read-only attempt of its role plus
/// one immutable source link, and a source is eligible only in the path's exact failed, clean-exit, current-contract
/// shape (a CriticalReviewer source may also be a historical v1 or v2 attempt). The Planner's own rule is separate and unchanged.
/// </summary>
public sealed class AttemptReadOnlyFormatRepairTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
    private const string Fingerprint = "fingerprint-1";

    private static readonly AgentResponseContract[] AllContracts =
    [
        AgentResponseContract.CriticalReview,
        AgentResponseContract.ChallengeResolution,
        AgentResponseContract.ImplementationReview,
    ];

    public static TheoryData<AgentResponseContract> Contracts => new()
    {
        AgentResponseContract.CriticalReview,
        AgentResponseContract.ChallengeResolution,
        AgentResponseContract.ImplementationReview,
    };

    private static Attempt Claim(AgentResponseContract contract, Guid? sourceId = null, Guid? id = null, string? model = null, string? effort = null)
    {
        var attemptId = id ?? Guid.NewGuid();
        var runId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var checkpointId = Guid.NewGuid();
        var manifestId = Guid.NewGuid();
        var timeout = TimeSpan.FromMinutes(10);
        return contract switch
        {
            AgentResponseContract.CriticalReview => Attempt.ClaimAgentCriticalReviewWithModelRequest(
                attemptId, runId, 2, workspaceId, checkpointId, Fingerprint, manifestId, timeout, 1024, 2048, Now,
                model is null ? null : "opus", effort is null ? null : "high", 2, sourceId),
            AgentResponseContract.ChallengeResolution => Attempt.ClaimAgentChallengeResolutionWithAssignment(
                attemptId, runId, 2, workspaceId, checkpointId, Fingerprint, manifestId, timeout, 1024, 2048, Now, model, effort, 2, sourceId),
            _ => Attempt.ClaimAgentCodeReviewWithAssignment(
                attemptId, runId, 2, workspaceId, checkpointId, Fingerprint, manifestId, timeout, 1024, 2048, Now, model, effort, 2, sourceId),
        };
    }

    private static Attempt InvalidOutput(AgentResponseContract contract)
    {
        var attempt = Claim(contract);
        attempt.MarkAgentDispatched(Now);
        attempt.CompleteAgent(AgentOutcome.InvalidStructuredOutput, Fingerprint, Now, TestProcessEvidence.CleanExit);
        return attempt;
    }

    [Theory]
    [MemberData(nameof(Contracts))]
    public void A_repair_claim_is_an_ordinary_read_only_attempt_of_its_role_with_an_immutable_source_link(AgentResponseContract contract)
    {
        var sourceId = Guid.NewGuid();

        var repair = Claim(contract, sourceId);
        var ordinary = Claim(contract);

        Assert.Equal(sourceId, repair.AgentRepairSourceAttemptId);
        Assert.Null(ordinary.AgentRepairSourceAttemptId);
        Assert.Equal(AttemptKind.Agent, repair.Kind);
        Assert.Equal(AttemptStatus.Running, repair.Status);
        Assert.Equal(contract, repair.AgentResponseContract);
        Assert.Equal(ordinary.AgentProvider, repair.AgentProvider);
        Assert.Equal(ordinary.AgentRole, repair.AgentRole);
        Assert.Equal(ordinary.AgentExpectedMessageType, repair.AgentExpectedMessageType);
        Assert.Equal(AgentPermissionProfile.ReadOnly, repair.AgentPermissionProfile);
        Assert.Equal(ordinary.AgentAdapterContractVersion, repair.AgentAdapterContractVersion);
        Assert.True(ReadOnlyFormatRepairPolicy.HasExactTuple(repair, contract));
    }

    [Theory]
    [MemberData(nameof(Contracts))]
    public void A_repair_claim_rejects_an_empty_source_identity_and_a_self_link(AgentResponseContract contract)
    {
        Assert.Throws<ArgumentException>(() => Claim(contract, Guid.Empty));

        var id = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => Claim(contract, id, id));
    }

    [Theory]
    [MemberData(nameof(Contracts))]
    public void The_pinned_adapter_contract_versions_are_the_current_known_values(AgentResponseContract contract)
    {
        var expected = contract switch
        {
            AgentResponseContract.CriticalReview => "claude-critical-review-v3",
            AgentResponseContract.ChallengeResolution => "codex-challenge-resolution-v1",
            _ => "codex-implementation-review-v1",
        };

        Assert.Equal(expected, Claim(contract).AgentAdapterContractVersion);
        Assert.Equal(expected, ReadOnlyFormatRepairPolicy.ExpectedTuple(contract)!.Value.AdapterContractVersion);
    }

    [Theory]
    [MemberData(nameof(Contracts))]
    public void A_failed_dispatched_clean_exit_invalid_output_attempt_is_an_eligible_source(AgentResponseContract contract)
    {
        var source = InvalidOutput(contract);

        Assert.True(ReadOnlyFormatRepairPolicy.Supports(contract));
        Assert.True(ReadOnlyFormatRepairPolicy.IsEligibleSource(source, contract));
    }

    [Theory]
    [MemberData(nameof(Contracts))]
    public void A_source_of_another_response_contract_is_never_eligible(AgentResponseContract contract)
    {
        var source = InvalidOutput(contract);

        foreach (var other in AllContracts.Where(candidate => candidate != contract))
        {
            Assert.False(ReadOnlyFormatRepairPolicy.IsEligibleSource(source, other));
        }

        Assert.False(ReadOnlyFormatRepairPolicy.IsEligibleSource(source, AgentResponseContract.Proposal));
        Assert.False(ReadOnlyFormatRepairPolicy.IsEligibleSource(source, AgentResponseContract.ImplementationReport));
        Assert.False(ReadOnlyFormatRepairPolicy.Supports(AgentResponseContract.Proposal));
        Assert.False(ReadOnlyFormatRepairPolicy.Supports(AgentResponseContract.ImplementationReport));
        Assert.False(ReadOnlyFormatRepairPolicy.Supports(AgentResponseContract.ReviewCorrection));
    }

    [Theory]
    [MemberData(nameof(Contracts))]
    public void A_source_that_did_not_end_as_invalid_structured_output_is_not_eligible(AgentResponseContract contract)
    {
        var undispatched = Claim(contract);
        undispatched.Fail(Now);
        var providerFailure = Claim(contract);
        providerFailure.MarkAgentDispatched(Now);
        providerFailure.CompleteAgent(AgentOutcome.ProviderInvocationFailed, Fingerprint, Now);
        var running = Claim(contract);
        running.MarkAgentDispatched(Now);
        var success = Claim(contract);
        success.MarkAgentDispatched(Now);
        success.CompleteAgent(
            contract switch
            {
                AgentResponseContract.CriticalReview => AgentOutcome.Accepted,
                AgentResponseContract.ChallengeResolution => AgentOutcome.Resolved,
                _ => AgentOutcome.ReviewApproved,
            },
            Fingerprint, Now, TestProcessEvidence.CleanExit);

        Assert.False(ReadOnlyFormatRepairPolicy.IsEligibleSource(undispatched, contract));
        Assert.False(ReadOnlyFormatRepairPolicy.IsEligibleSource(providerFailure, contract));
        Assert.False(ReadOnlyFormatRepairPolicy.IsEligibleSource(running, contract));
        Assert.False(ReadOnlyFormatRepairPolicy.IsEligibleSource(success, contract));
    }

    [Theory]
    [MemberData(nameof(Contracts))]
    public void A_repair_is_never_itself_an_eligible_source(AgentResponseContract contract)
    {
        var repair = Claim(contract, Guid.NewGuid());
        repair.MarkAgentDispatched(Now);
        repair.CompleteAgent(AgentOutcome.InvalidStructuredOutput, Fingerprint, Now, TestProcessEvidence.CleanExit);

        Assert.False(ReadOnlyFormatRepairPolicy.IsEligibleSource(repair, contract));
    }

    [Theory]
    [MemberData(nameof(Contracts))]
    public void An_invalid_output_with_a_completion_fingerprint_mismatch_is_recorded_as_source_changed_and_not_eligible(
        AgentResponseContract contract)
    {
        var source = Claim(contract);
        source.MarkAgentDispatched(Now);
        source.CompleteAgent(AgentOutcome.InvalidStructuredOutput, "another-fingerprint", Now, TestProcessEvidence.CleanExit);

        Assert.Equal(AgentOutcome.SourceChanged, source.AgentOutcome);
        Assert.False(ReadOnlyFormatRepairPolicy.IsEligibleSource(source, contract));
    }

    private static void SetAdapterVersion(Attempt attempt, string? version) =>
        typeof(Attempt).GetProperty(nameof(Attempt.AgentAdapterContractVersion))!.SetValue(attempt, version);

    [Theory]
    [InlineData("claude-critical-review-v1", true)]
    [InlineData("claude-critical-review-v2", true)]
    [InlineData("claude-critical-review-v3", true)]
    [InlineData("claude-critical-review-v0", false)]
    [InlineData("claude-critical-review-v4", false)]
    [InlineData("codex-planning-v1", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void A_critical_review_source_is_eligible_under_the_current_or_a_historical_contract_only(string? version, bool expected)
    {
        var source = InvalidOutput(AgentResponseContract.CriticalReview);
        SetAdapterVersion(source, version);

        Assert.Equal(expected, ReadOnlyFormatRepairPolicy.IsEligibleSource(source, AgentResponseContract.CriticalReview));
        Assert.Equal(expected, ReadOnlyFormatRepairPolicy.HasRepairableSourceTuple(source, AgentResponseContract.CriticalReview));
    }

    [Theory]
    [InlineData("claude-critical-review-v1")]
    [InlineData("claude-critical-review-v2")]
    public void A_historical_critical_review_attempt_never_carries_the_exact_current_tuple_and_a_repair_never_uses_it(string version)
    {
        var historical = Claim(AgentResponseContract.CriticalReview, Guid.NewGuid());
        SetAdapterVersion(historical, version);

        Assert.False(ReadOnlyFormatRepairPolicy.HasExactTuple(historical, AgentResponseContract.CriticalReview));
        Assert.Equal(
            "claude-critical-review-v3", ReadOnlyFormatRepairPolicy.ExpectedTuple(AgentResponseContract.CriticalReview)!.Value.AdapterContractVersion);
        Assert.Equal("claude-critical-review-v3", Claim(AgentResponseContract.CriticalReview, Guid.NewGuid()).AgentAdapterContractVersion);
    }

    [Theory]
    [InlineData("claude-critical-review-v1", true)]
    [InlineData("claude-critical-review-v2", true)]
    [InlineData("claude-critical-review-v3", true)]
    [InlineData("claude-critical-review-v0", false)]
    [InlineData("claude-critical-review-v4", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_the_current_and_the_two_historical_critical_review_versions_are_known(string? version, bool expected)
    {
        Assert.Equal(expected, ReadOnlyFormatRepairPolicy.IsKnownCriticalReviewVersion(version));
        Assert.Equal("claude-critical-review-v1", ReadOnlyFormatRepairPolicy.HistoricalCriticalReviewAdapterContractVersionV1);
        Assert.Equal("claude-critical-review-v2", ReadOnlyFormatRepairPolicy.HistoricalCriticalReviewAdapterContractVersionV2);
        Assert.Equal("claude-critical-review-v3", ReadOnlyFormatRepairPolicy.CriticalReviewAdapterContractVersion);
    }

    [Theory]
    [InlineData(AgentResponseContract.ChallengeResolution, "claude-critical-review-v1")]
    [InlineData(AgentResponseContract.ChallengeResolution, "claude-critical-review-v2")]
    [InlineData(AgentResponseContract.ImplementationReview, "claude-critical-review-v1")]
    [InlineData(AgentResponseContract.ImplementationReview, "claude-critical-review-v2")]
    public void A_historical_critical_review_version_never_makes_another_stage_eligible(AgentResponseContract contract, string version)
    {
        var source = InvalidOutput(contract);
        SetAdapterVersion(source, version);

        Assert.False(ReadOnlyFormatRepairPolicy.IsEligibleSource(source, contract));
    }

    [Theory]
    [InlineData("claude-critical-review-v1")]
    [InlineData("claude-critical-review-v2")]
    [InlineData("claude-critical-review-v3")]
    public void A_provider_reported_error_with_a_non_zero_exit_is_not_a_repair_source_under_any_critical_review_version(string version)
    {
        var observed = Claim(AgentResponseContract.CriticalReview);
        observed.MarkAgentDispatched(Now);
        observed.CompleteAgent(
            AgentOutcome.ProviderInvocationFailed,
            Fingerprint,
            Now,
            AgentProcessExecutionEvidence.Create(
                ProcessOutcome.Exited, 1, TimeSpan.FromMilliseconds(900),
                DevalCopilot.Domain.Features.Processes.ProcessIoCompletion.CompleteWithInput));
        SetAdapterVersion(observed, version);

        Assert.False(ReadOnlyFormatRepairPolicy.IsEligibleSource(observed, AgentResponseContract.CriticalReview));
    }

    [Fact]
    public void The_planning_repair_rule_is_separate_and_unchanged()
    {
        var planner = Attempt.ClaimAgent(
            Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 1024, 2048, Now, agentBudgetSlot: 1);
        planner.MarkAgentDispatched(Now);
        planner.CompleteAgent(AgentOutcome.InvalidStructuredOutput, Fingerprint, Now, TestProcessEvidence.CleanExit);

        Assert.True(planner.IsEligiblePlanningRepairSource);
        Assert.False(ReadOnlyFormatRepairPolicy.Supports(AgentResponseContract.Proposal));
        Assert.False(ReadOnlyFormatRepairPolicy.IsEligibleSource(planner, AgentResponseContract.CriticalReview));

        foreach (var contract in AllContracts)
        {
            Assert.False(InvalidOutput(contract).IsEligiblePlanningRepairSource);
        }
    }
}
