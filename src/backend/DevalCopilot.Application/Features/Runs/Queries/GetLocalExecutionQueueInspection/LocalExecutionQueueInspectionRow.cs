using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;

namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionQueueInspection;

/// <summary>One row of the single global statement: the candidate, its project label and one row of its ADR-0063 ledger facts.</summary>
internal sealed record LocalExecutionQueueInspectionRow(
    LocalExecutionQueueInspectionCandidate Candidate,
    LocalExecutionQueueInspectionProject Project,
    LocalExecutionAdmissionHistoryRow History);
