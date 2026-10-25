using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionOwnershipRelease;

/// <summary>The one coherent Human release receipt (ADR-0065): the released operation's proved tuple, the release identity and time, and
/// the sequence number of the one Human run event that recorded it.</summary>
public sealed record LocalExecutionOwnershipReleaseReceiptView(
    LocalExecutionOwnershipReleaseFacts Facts,
    Guid ReleaseId,
    DateTimeOffset ReleasedAtUtc,
    long EventSequence);
