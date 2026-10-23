namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;

/// <summary>One row of the single admission-history statement: the root facts, one admission, its linked step and its references.</summary>
internal sealed record LocalExecutionAdmissionHistoryRow(
    LocalExecutionAdmissionHistoryRoot Root,
    LocalExecutionAdmissionHistoryAdmission Admission,
    LocalExecutionAdmissionHistoryStep Step,
    LocalExecutionAdmissionHistoryReference Reference);
