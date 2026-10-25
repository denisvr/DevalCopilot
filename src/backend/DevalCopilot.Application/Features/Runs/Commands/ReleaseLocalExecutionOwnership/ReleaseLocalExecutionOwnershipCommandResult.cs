using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Commands.ReleaseLocalExecutionOwnership;

/// <summary>The released graph (ADR-0065): the released operation's proved tuple, the release identity and time, and the sequence number
/// of the one Human event. A fresh release and an exact replay are indistinguishable.</summary>
public sealed record ReleaseLocalExecutionOwnershipCommandResult(
    Guid RunId,
    Guid StepId,
    LocalExecutionOwnershipReleaseFacts Facts,
    Guid ReleaseId,
    DateTimeOffset ReleasedAtUtc,
    long EventSequence);
