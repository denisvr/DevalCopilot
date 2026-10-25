namespace DevalCopilot.Api.Features.Runs.GetLocalExecutionOwnershipRelease;

/// <summary>
/// The closed graph of one step's ownership release reading and of a successful or replayed release (ADR-0065). <c>Status</c> is exactly
/// <c>NotEligible</c>, <c>Unavailable</c>, <c>Available</c> or <c>Released</c>; <c>ReasonCode</c> is the fixed reason of the first two and
/// null otherwise; <c>Candidate</c> is present only for <c>Available</c> and <c>Release</c> only for <c>Released</c>. It exposes no
/// dispatch ticket, nonce, host launch identity, process identifier, path, argument or provider output.
/// </summary>
public sealed record LocalExecutionOwnershipReleaseResponse(
    Guid RunId,
    Guid StepId,
    string Status,
    string? ReasonCode,
    LocalExecutionOwnershipReleaseCandidateResponse? Candidate,
    LocalExecutionOwnershipReleaseReceiptResponse? Release);
