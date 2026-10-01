using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies;

/// <summary>
/// Reads, from one fresh untracked snapshot of the run, how an implementation attempt relates to the human planning
/// authorizations (ADR-0016) and turns the answer into the bounded expectation the dispatch gate, the eligibility feed,
/// and the sealed-manifest check compare. A snapshot that cannot be read, or an attempt that cannot be found, is
/// <see cref="PlanningImplementationAuthorizationEvidence.AttemptForm.Invalid"/>: nothing is dispatched or recorded on
/// facts that cannot be proven.
/// </summary>
internal static class PlanningImplementationAuthorizationAttempt
{
    private static readonly PlanningImplementationAuthorizationEvidence.AttemptClassification Unprovable =
        new(PlanningImplementationAuthorizationEvidence.AttemptForm.Invalid, null);

    public static async Task<PlanningImplementationAuthorizationEvidence.AttemptClassification> ClassifyFreshAsync(
        IDevalCopilotDbContext dbContext, Guid runId, Guid attemptId, CancellationToken cancellationToken)
    {
        var snapshot = await PlanningLineage.TryLoadSnapshotAsync(dbContext, runId, cancellationToken);
        return snapshot is not null && snapshot.AttemptsById.TryGetValue(attemptId, out var attempt)
            ? PlanningImplementationAuthorizationEvidence.ClassifyAttempt(snapshot, attempt)
            : Unprovable;
    }

    public static PlanningImplementationAuthorizationEvidence.AttemptClassification Classify(
        ImplementerExecutionReportEligibility.Snapshot snapshot, Attempt attempt) =>
        PlanningImplementationAuthorizationEvidence.ClassifyAttempt(snapshot, attempt);

    /// <summary>The bounded expectation for an authorized attempt, or <see langword="null"/> for the ordinary form.</summary>
    public static PlanningImplementationAuthorizationFact? ToFact(
        PlanningImplementationAuthorizationEvidence.AttemptClassification classification) =>
        classification.Recorded is { } recorded
            ? new PlanningImplementationAuthorizationFact(
                recorded.Grant.Id,
                recorded.Escalation.Id,
                recorded.Final.Proposal.Id,
                recorded.Instruction.Id,
                recorded.Rationale)
            : null;
}
