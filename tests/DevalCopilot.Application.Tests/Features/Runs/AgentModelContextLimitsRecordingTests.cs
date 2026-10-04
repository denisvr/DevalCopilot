using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed class AgentModelContextLimitsRecordingTests
{
    private static AgentModelContextLimits Report(params AgentModelContextLimitEntry[] entries) => new(TestModelContextLimits.Source, entries);

    [Fact]
    public void Validate_translates_a_valid_report_into_ordinally_ordered_domain_evidence()
    {
        var error = AgentModelContextLimitsRecording.Validate(
            TestModelContextLimits.Reported, AgentProvider.ClaudeCode, AgentOutcome.ProviderInvocationFailed, dispatched: true, out var evidence);

        Assert.Null(error);
        Assert.NotNull(evidence);
        Assert.Equal(TestModelContextLimits.Snapshot, evidence.Serialize());
    }

    [Fact]
    public void Validate_accepts_an_absent_report_for_every_state_and_returns_no_evidence()
    {
        Assert.Null(AgentModelContextLimitsRecording.Validate(null, AgentProvider.ClaudeCode, AgentOutcome.Accepted, true, out var evidence));
        Assert.Null(evidence);
        Assert.Null(AgentModelContextLimitsRecording.Validate(null, null, AgentOutcome.WorkspaceNoLongerEligible, false, out _));
    }

    public static TheoryData<AgentModelContextLimits> InvalidReports() => new()
    {
        TestModelContextLimits.Unproven,
        new AgentModelContextLimits(TestModelContextLimits.Source, []),
        Report(new AgentModelContextLimitEntry("a", 1, 1), new AgentModelContextLimitEntry("a", 1, 1)),
        Report(new AgentModelContextLimitEntry("a b", 1, 1)),
        Report(new AgentModelContextLimitEntry("a", 0, 1)),
        Report(new AgentModelContextLimitEntry("a", 1, 0)),
        Report(new AgentModelContextLimitEntry("a", 1, 2)),
        new AgentModelContextLimits(
            TestModelContextLimits.Source, Enumerable.Range(0, 17).Select(index => new AgentModelContextLimitEntry($"m{index}", 1, 1)).ToList()),
        new AgentModelContextLimits(string.Empty, [new AgentModelContextLimitEntry("a", 1, 1)]),
    };

    [Theory]
    [MemberData(nameof(InvalidReports))]
    public void Validate_rejects_every_invalid_report_without_producing_evidence(AgentModelContextLimits report)
    {
        var error = AgentModelContextLimitsRecording.Validate(report, AgentProvider.ClaudeCode, AgentOutcome.ProviderInvocationFailed, true, out var evidence);

        Assert.Equal(AgentModelContextLimitsRecording.InvalidEvidenceCode, error?.Code);
        Assert.Null(evidence);
    }

    [Fact]
    public void Validate_rejects_a_report_for_another_provider()
    {
        var error = AgentModelContextLimitsRecording.Validate(
            TestModelContextLimits.Reported, AgentProvider.Codex, AgentOutcome.Proposed, true, out var evidence);

        Assert.Equal(AgentModelContextLimitsRecording.InvalidEvidenceCode, error?.Code);
        Assert.Null(evidence);
    }

    [Fact]
    public void Validate_rejects_a_report_for_an_undispatched_attempt()
    {
        var error = AgentModelContextLimitsRecording.Validate(
            TestModelContextLimits.Reported, AgentProvider.ClaudeCode, AgentOutcome.SourceChanged, false, out var evidence);

        Assert.Equal(AgentModelContextLimitsRecording.EvidenceWithoutDispatchCode, error?.Code);
        Assert.Null(evidence);
    }

    [Theory]
    [InlineData(AgentOutcome.WorkspaceNoLongerEligible)]
    [InlineData(AgentOutcome.InputAlreadyReviewed)]
    [InlineData(AgentOutcome.InputAlreadyResolved)]
    [InlineData(AgentOutcome.InputAlreadyImplemented)]
    [InlineData(AgentOutcome.InputAlreadyCodeReviewed)]
    [InlineData(AgentOutcome.InputAlreadyCorrected)]
    public void Validate_rejects_a_report_for_every_pre_invocation_outcome_regardless_of_dispatch_state(AgentOutcome outcome)
    {
        Assert.Equal(
            AgentModelContextLimitsRecording.PreInvocationOutcomeCannotCarryEvidenceCode,
            AgentModelContextLimitsRecording.Validate(TestModelContextLimits.Reported, AgentProvider.ClaudeCode, outcome, true, out var dispatched)?.Code);
        Assert.Equal(
            AgentModelContextLimitsRecording.PreInvocationOutcomeCannotCarryEvidenceCode,
            AgentModelContextLimitsRecording.Validate(TestModelContextLimits.Reported, AgentProvider.ClaudeCode, outcome, false, out _)?.Code);
        Assert.Null(dispatched);
    }

    [Theory]
    [InlineData(AgentOutcome.Accepted)]
    [InlineData(AgentOutcome.Challenged)]
    [InlineData(AgentOutcome.Implemented)]
    [InlineData(AgentOutcome.CorrectionApplied)]
    [InlineData(AgentOutcome.InvalidStructuredOutput)]
    [InlineData(AgentOutcome.ProviderInvocationFailed)]
    [InlineData(AgentOutcome.SourceChanged)]
    [InlineData(AgentOutcome.NoChangesProduced)]
    public void Validate_accepts_a_report_for_every_post_invocation_outcome_success_or_not(AgentOutcome outcome)
    {
        Assert.Null(AgentModelContextLimitsRecording.Validate(TestModelContextLimits.Reported, AgentProvider.ClaudeCode, outcome, true, out var evidence));
        Assert.NotNull(evidence);
    }
}
