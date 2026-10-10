namespace DevalCopilot.Application.Features.Projects.Queries.GetProjectRunHistory;

/// <summary>The identity of a run's own recorded Completed local commit: exactly what locates its receipt. It does not certify that the
/// receipt is available or that anything was approved.</summary>
public sealed record ProjectRunHistoryReceiptSource(
    Guid RunId, Guid OperationId, string CommitSha, Guid CheckpointId, int CheckpointNumber);
