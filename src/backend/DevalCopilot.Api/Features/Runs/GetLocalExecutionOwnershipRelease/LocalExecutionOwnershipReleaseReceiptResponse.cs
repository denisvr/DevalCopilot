namespace DevalCopilot.Api.Features.Runs.GetLocalExecutionOwnershipRelease;

/// <summary>The one coherent Human release (ADR-0065): the released operation, the release identity, its UTC time and the canonical
/// positive Int64 sequence number of the one Human run event. It records that the retained ownership quarantine was released; it
/// changed no outcome, cycle, workspace, lease, approval or budget.</summary>
public sealed record LocalExecutionOwnershipReleaseReceiptResponse(
    Guid ReleaseId,
    string OperationFamily,
    Guid OperationId,
    string StepKind,
    string ReleasedAtUtc,
    string EventSequence);
