using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionReleaseInspection;

/// <summary>The one proved Human release receipt (ADR-0067): the release identity, its time and the sequence number of the one Human run
/// event, with the complete proved tuple of the released operation and its original-host termination observation. The host launch
/// identity, nonce, ticket and confirmation are never part of it.</summary>
public sealed record LocalExecutionReleaseInspectionReceipt(
    LocalExecutionOwnershipReleaseFacts Facts,
    Guid ReleaseId,
    DateTimeOffset ReleasedAtUtc,
    long EventSequence);
