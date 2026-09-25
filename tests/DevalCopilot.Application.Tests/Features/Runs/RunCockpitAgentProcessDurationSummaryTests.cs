using DevalCopilot.Application.Features.Runs.Queries.GetRunCockpit;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed class RunCockpitAgentProcessDurationSummaryTests
{
    private static AgentProcessExecutionEvidence Exited(TimeSpan duration) =>
        AgentProcessExecutionEvidence.Create(ProcessOutcome.Exited, 0, duration);

    [Fact]
    public void No_dispatched_attempts_reports_nothing_to_measure()
    {
        var summary = RunCockpitAgentProcessDurationSummary.FromDispatchedAttempts([]);

        Assert.Equal(AgentProcessDurationEvidenceStatus.NoDispatchedAttempts, summary.Status);
        Assert.Null(summary.TotalMeasuredDuration);
        Assert.Equal(0, summary.DispatchedAttemptCount);
        Assert.Equal(0, summary.PendingAttemptCount);
        Assert.Equal(0, summary.ValidEvidenceCount);
        Assert.Equal(0, summary.MalformedEvidenceCount);
    }

    [Fact]
    public void Every_terminal_attempt_with_valid_evidence_is_complete()
    {
        var summary = RunCockpitAgentProcessDurationSummary.FromDispatchedAttempts(
        [
            (AttemptStatus.Completed, Exited(TimeSpan.FromSeconds(10))),
            (AttemptStatus.Failed, Exited(TimeSpan.FromSeconds(5))),
        ]);

        Assert.Equal(AgentProcessDurationEvidenceStatus.Complete, summary.Status);
        Assert.Equal(TimeSpan.FromSeconds(15), summary.TotalMeasuredDuration);
        Assert.Equal(2, summary.DispatchedAttemptCount);
        Assert.Equal(0, summary.PendingAttemptCount);
        Assert.Equal(2, summary.ValidEvidenceCount);
        Assert.Equal(0, summary.MalformedEvidenceCount);
    }

    // A real zero-duration measurement must never be confused with "no measurement available".
    [Fact]
    public void A_single_zero_duration_attempt_is_complete_with_a_real_zero_total()
    {
        var summary = RunCockpitAgentProcessDurationSummary.FromDispatchedAttempts(
        [
            (AttemptStatus.Completed, Exited(TimeSpan.Zero)),
        ]);

        Assert.Equal(AgentProcessDurationEvidenceStatus.Complete, summary.Status);
        Assert.NotNull(summary.TotalMeasuredDuration);
        Assert.Equal(TimeSpan.Zero, summary.TotalMeasuredDuration!.Value);
    }

    [Fact]
    public void All_attempts_still_pending_reports_pending_evidence_with_no_total()
    {
        var summary = RunCockpitAgentProcessDurationSummary.FromDispatchedAttempts(
        [
            (AttemptStatus.Running, null),
            (AttemptStatus.Running, null),
        ]);

        Assert.Equal(AgentProcessDurationEvidenceStatus.PendingEvidence, summary.Status);
        Assert.Null(summary.TotalMeasuredDuration);
        Assert.Equal(2, summary.DispatchedAttemptCount);
        Assert.Equal(2, summary.PendingAttemptCount);
        Assert.Equal(0, summary.ValidEvidenceCount);
    }

    [Fact]
    public void Pending_mixed_with_valid_terminal_evidence_is_pending_with_no_total()
    {
        var summary = RunCockpitAgentProcessDurationSummary.FromDispatchedAttempts(
        [
            (AttemptStatus.Completed, Exited(TimeSpan.FromSeconds(3))),
            (AttemptStatus.Running, null),
        ]);

        Assert.Equal(AgentProcessDurationEvidenceStatus.PendingEvidence, summary.Status);
        Assert.Null(summary.TotalMeasuredDuration);
        Assert.Equal(1, summary.ValidEvidenceCount);
        Assert.Equal(1, summary.PendingAttemptCount);
    }

    [Fact]
    public void A_terminal_attempt_with_missing_evidence_mixed_with_a_valid_one_is_partial()
    {
        var summary = RunCockpitAgentProcessDurationSummary.FromDispatchedAttempts(
        [
            (AttemptStatus.Completed, Exited(TimeSpan.FromSeconds(3))),
            (AttemptStatus.Interrupted, null),
        ]);

        Assert.Equal(AgentProcessDurationEvidenceStatus.PartialEvidence, summary.Status);
        Assert.Null(summary.TotalMeasuredDuration);
        Assert.Equal(1, summary.ValidEvidenceCount);
        Assert.Equal(1, summary.MalformedEvidenceCount);
    }

    [Fact]
    public void Partial_evidence_may_also_include_pending_attempts()
    {
        var summary = RunCockpitAgentProcessDurationSummary.FromDispatchedAttempts(
        [
            (AttemptStatus.Completed, Exited(TimeSpan.FromSeconds(3))),
            (AttemptStatus.Failed, null),
            (AttemptStatus.Running, null),
        ]);

        Assert.Equal(AgentProcessDurationEvidenceStatus.PartialEvidence, summary.Status);
        Assert.Equal(1, summary.ValidEvidenceCount);
        Assert.Equal(1, summary.MalformedEvidenceCount);
        Assert.Equal(1, summary.PendingAttemptCount);
    }

    [Fact]
    public void Every_terminal_attempt_with_missing_evidence_is_malformed_never_zero()
    {
        var summary = RunCockpitAgentProcessDurationSummary.FromDispatchedAttempts(
        [
            (AttemptStatus.Failed, null),
            (AttemptStatus.Interrupted, null),
        ]);

        Assert.Equal(AgentProcessDurationEvidenceStatus.MalformedEvidence, summary.Status);
        Assert.Null(summary.TotalMeasuredDuration);
        Assert.Equal(0, summary.ValidEvidenceCount);
        Assert.Equal(2, summary.MalformedEvidenceCount);
    }

    [Fact]
    public void Malformed_evidence_may_also_include_pending_attempts()
    {
        var summary = RunCockpitAgentProcessDurationSummary.FromDispatchedAttempts(
        [
            (AttemptStatus.Failed, null),
            (AttemptStatus.Running, null),
        ]);

        Assert.Equal(AgentProcessDurationEvidenceStatus.MalformedEvidence, summary.Status);
        Assert.Equal(0, summary.ValidEvidenceCount);
        Assert.Equal(1, summary.MalformedEvidenceCount);
        Assert.Equal(1, summary.PendingAttemptCount);
    }

    // Summation must be tick-overflow-safe: when every individual duration is itself valid (no
    // malformed evidence at all) but their running sum overflows what a TimeSpan can represent, the
    // result is the distinct, honest UnrepresentableTotal state — never wrapped, thrown, silently
    // truncated, or misreported as MalformedEvidence (the individual measurements ARE valid; only
    // their sum is unrepresentable).
    [Fact]
    public void Tick_overflow_while_summing_otherwise_valid_durations_is_unrepresentable_total_not_malformed()
    {
        var huge = Exited(TimeSpan.FromTicks(long.MaxValue / 2 + 1));

        var summary = RunCockpitAgentProcessDurationSummary.FromDispatchedAttempts(
        [
            (AttemptStatus.Completed, huge),
            (AttemptStatus.Completed, huge),
        ]);

        Assert.Equal(AgentProcessDurationEvidenceStatus.UnrepresentableTotal, summary.Status);
        Assert.Null(summary.TotalMeasuredDuration);
        Assert.Equal(2, summary.ValidEvidenceCount);
        Assert.Equal(0, summary.MalformedEvidenceCount);
        Assert.Equal(0, summary.PendingAttemptCount);
    }

    [Fact]
    public void Unrepresentable_total_may_also_include_pending_attempts()
    {
        var huge = Exited(TimeSpan.FromTicks(long.MaxValue / 2 + 1));

        var summary = RunCockpitAgentProcessDurationSummary.FromDispatchedAttempts(
        [
            (AttemptStatus.Completed, huge),
            (AttemptStatus.Completed, huge),
            (AttemptStatus.Running, null),
        ]);

        Assert.Equal(AgentProcessDurationEvidenceStatus.UnrepresentableTotal, summary.Status);
        Assert.Null(summary.TotalMeasuredDuration);
        Assert.Equal(2, summary.ValidEvidenceCount);
        Assert.Equal(1, summary.PendingAttemptCount);
    }

    // Malformed evidence always dominates the classification: even when the valid subset's own sum
    // would independently overflow, the presence of real malformed evidence is the more important
    // fact and must still win, exactly as it did before UnrepresentableTotal existed.
    [Fact]
    public void Malformed_evidence_dominates_even_when_the_valid_subset_would_also_overflow()
    {
        var huge = Exited(TimeSpan.FromTicks(long.MaxValue / 2 + 1));

        var summary = RunCockpitAgentProcessDurationSummary.FromDispatchedAttempts(
        [
            (AttemptStatus.Completed, huge),
            (AttemptStatus.Completed, huge),
            (AttemptStatus.Failed, null),
        ]);

        Assert.Equal(AgentProcessDurationEvidenceStatus.PartialEvidence, summary.Status);
        Assert.Null(summary.TotalMeasuredDuration);
        Assert.Equal(2, summary.ValidEvidenceCount);
        Assert.Equal(1, summary.MalformedEvidenceCount);
    }

    [Fact]
    public void Large_single_pass_sequence_is_aggregated_exactly_without_a_row_cap()
    {
        var enumerations = 0;
        IEnumerable<(AttemptStatus Status, AgentProcessExecutionEvidence? Evidence)> Stream()
        {
            enumerations++;
            for (var index = 0; index < 10_000; index++)
            {
                yield return (AttemptStatus.Completed, Exited(TimeSpan.FromSeconds(1)));
            }
        }

        var summary = RunCockpitAgentProcessDurationSummary.FromDispatchedAttempts(Stream());

        Assert.Equal(1, enumerations);
        Assert.Equal(AgentProcessDurationEvidenceStatus.Complete, summary.Status);
        Assert.Equal(10_000, summary.ValidEvidenceCount);
        Assert.Equal(TimeSpan.FromSeconds(10_000), summary.TotalMeasuredDuration);
    }
}
