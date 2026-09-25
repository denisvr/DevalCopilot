using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetRunCockpit;

/// <summary>
/// A bounded, truthful, run-wide summary of HOST-MEASURED Agent process-duration evidence — pure
/// telemetry over this run's dispatched Agent attempts' own <see cref="Attempt.AgentProcessOutcome"/>/
/// <see cref="Attempt.AgentProcessExitCode"/>/<see cref="Attempt.AgentProcessDuration"/> evidence
/// (see <see cref="AgentProcessExecutionEvidence"/>). Deliberately distinct from, and never
/// combined with, <see cref="GetRunCockpitQueryResult.MaximumAgentAttempts"/>'s count budget or
/// <see cref="RunCockpitAgentInvocationTimeBudgetSummary"/>'s reservation-time budget: this summary
/// enforces nothing and never asserts a budget, only what host-measured evidence has actually been
/// recorded so far.
/// </summary>
/// <param name="Status">The evidence-state classification. See
/// <see cref="AgentProcessDurationEvidenceStatus"/>.</param>
/// <param name="TotalMeasuredDuration">The exact sum of every dispatched Agent attempt's valid
/// host-measured process duration, or <see langword="null"/> unless <paramref name="Status"/> is
/// <see cref="AgentProcessDurationEvidenceStatus.Complete"/>. A real zero-duration measurement
/// (<see cref="TimeSpan.Zero"/>) is therefore always visibly distinct from "no measurement is
/// available yet", which is always <see langword="null"/>.</param>
/// <param name="DispatchedAttemptCount">Every dispatched Agent attempt this run has, regardless of
/// its own evidence state.</param>
/// <param name="PendingAttemptCount">Dispatched Agent attempts still running (no terminal result
/// yet).</param>
/// <param name="ValidEvidenceCount">Terminal dispatched Agent attempts with valid host-measured
/// process-duration evidence.</param>
/// <param name="MalformedEvidenceCount">Terminal dispatched Agent attempts whose process-duration
/// evidence is absent or malformed — never silently treated as zero and never silently excluded
/// from the counts, even though it is excluded from <see cref="TotalMeasuredDuration"/>.</param>
public sealed record RunCockpitAgentProcessDurationSummary(
    AgentProcessDurationEvidenceStatus Status,
    TimeSpan? TotalMeasuredDuration,
    int DispatchedAttemptCount,
    int PendingAttemptCount,
    int ValidEvidenceCount,
    int MalformedEvidenceCount)
{
    public static RunCockpitAgentProcessDurationSummary NoDispatchedAttempts() =>
        new(AgentProcessDurationEvidenceStatus.NoDispatchedAttempts, null, 0, 0, 0, 0);

    public static RunCockpitAgentProcessDurationSummary Complete(
        int dispatchedAttemptCount, int validEvidenceCount, TimeSpan totalMeasuredDuration) =>
        new(AgentProcessDurationEvidenceStatus.Complete, totalMeasuredDuration, dispatchedAttemptCount, 0, validEvidenceCount, 0);

    public static RunCockpitAgentProcessDurationSummary PendingEvidence(
        int dispatchedAttemptCount, int pendingAttemptCount, int validEvidenceCount) =>
        new(AgentProcessDurationEvidenceStatus.PendingEvidence, null, dispatchedAttemptCount, pendingAttemptCount, validEvidenceCount, 0);

    public static RunCockpitAgentProcessDurationSummary PartialEvidence(
        int dispatchedAttemptCount, int pendingAttemptCount, int validEvidenceCount, int malformedEvidenceCount) =>
        new(AgentProcessDurationEvidenceStatus.PartialEvidence, null, dispatchedAttemptCount, pendingAttemptCount, validEvidenceCount, malformedEvidenceCount);

    public static RunCockpitAgentProcessDurationSummary MalformedEvidence(
        int dispatchedAttemptCount, int pendingAttemptCount, int validEvidenceCount, int malformedEvidenceCount) =>
        new(AgentProcessDurationEvidenceStatus.MalformedEvidence, null, dispatchedAttemptCount, pendingAttemptCount, validEvidenceCount, malformedEvidenceCount);

    /// <summary>Every terminal attempt observed so far has valid evidence (no malformed evidence),
    /// but the exact sum of their durations overflows what a <see cref="TimeSpan"/> can represent.
    /// <paramref name="malformedEvidenceCount"/> is always zero here — reachable only when there is
    /// no malformed evidence at all — but is still carried explicitly rather than hard-coded, so the
    /// shape stays consistent with every other factory above.</summary>
    public static RunCockpitAgentProcessDurationSummary UnrepresentableTotal(
        int dispatchedAttemptCount, int pendingAttemptCount, int validEvidenceCount, int malformedEvidenceCount = 0) =>
        new(AgentProcessDurationEvidenceStatus.UnrepresentableTotal, null, dispatchedAttemptCount, pendingAttemptCount, validEvidenceCount, malformedEvidenceCount);

    /// <summary>Aggregates one entry per dispatched Agent attempt: its terminal status (a still
    /// <see cref="AttemptStatus.Running"/> attempt is pending), and its valid process-execution
    /// evidence, or <see langword="null"/> when that terminal attempt's evidence is absent or
    /// malformed.</summary>
    public static RunCockpitAgentProcessDurationSummary FromDispatchedAttempts(
        IEnumerable<(AttemptStatus Status, AgentProcessExecutionEvidence? Evidence)> dispatchedAttempts)
    {
        ArgumentNullException.ThrowIfNull(dispatchedAttempts);
        var accumulator = new RunCockpitAgentProcessDurationAccumulator();
        foreach (var (status, evidence) in dispatchedAttempts)
        {
            accumulator.Add(status, evidence);
        }

        return accumulator.ToSummary();
    }
}
