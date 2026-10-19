using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentSupervision;

/// <summary>An admitted receipt as stored: the immutable observer identities, the phase the host observer last reported, its last committed
/// report and absolute deadline, the closure when closed, and the revision. <c>OwnedByCurrentHost</c> says whether this very host launch
/// wrote it; another launch's freshness is never accepted.</summary>
public sealed record AgentSupervisionObservationView(
    Guid HostInstanceId,
    Guid ObserverId,
    bool OwnedByCurrentHost,
    long Revision,
    AgentSupervisionPhase Phase,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset LastHeartbeatAtUtc,
    DateTimeOffset PhaseEnteredAtUtc,
    DateTimeOffset PhaseDeadlineUtc,
    string? ClosureReason,
    DateTimeOffset? ClosedAtUtc);
