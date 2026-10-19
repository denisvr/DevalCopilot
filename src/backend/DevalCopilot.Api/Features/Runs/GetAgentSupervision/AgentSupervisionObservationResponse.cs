namespace DevalCopilot.Api.Features.Runs.GetAgentSupervision;

/// <summary>An admitted observation as stored: the immutable observer identities, the phase the host observer last reported, its last
/// committed report and the absolute phase deadline, and the closure when closed (<c>ClosureReason</c> and <c>ClosedAtUtc</c> are present
/// together, and only when <c>Open</c> is false). <c>OwnedByCurrentHost</c> is false for the observation of another host launch, whose
/// freshness is never accepted. <c>Revision</c> is a canonical decimal Int64 string; times are UTC round-trip strings.</summary>
public sealed record AgentSupervisionObservationResponse(
    Guid HostInstanceId,
    Guid ObserverId,
    bool OwnedByCurrentHost,
    string Revision,
    string Phase,
    string StartedAtUtc,
    string LastHeartbeatAtUtc,
    string PhaseEnteredAtUtc,
    string PhaseDeadlineUtc,
    bool Open,
    string? ClosureReason,
    string? ClosedAtUtc);
