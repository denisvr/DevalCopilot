namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionOwnershipRelease;

/// <summary>
/// The closed graph of one release reading (ADR-0065). <see cref="ReasonCode"/> is the fixed reason of NotEligible and Unavailable and is
/// null otherwise; <see cref="Candidate"/> is set exactly for Available and <see cref="Release"/> exactly for Released.
/// </summary>
public sealed record GetLocalExecutionOwnershipReleaseQueryResult(
    Guid RunId,
    Guid StepId,
    LocalExecutionOwnershipReleaseStatus Status,
    string? ReasonCode,
    LocalExecutionOwnershipReleaseCandidate? Candidate,
    LocalExecutionOwnershipReleaseReceiptView? Release);
