namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentSupervision;

/// <summary>
/// One read of the current Agent supervision of a run (ADR-0056). Closed relationships: NoAgentAttempt has no attempt, observation or
/// attention; NotObserved has an attempt only; Observed has an attempt and an open observation with no attention; NeedsAttention has an
/// attempt, an observation and an attention; Ended has a terminal attempt and an observation (attention only as history); Unavailable
/// has a reason code and carries whatever coherent facts were read. <c>ObservedAtUtc</c> is the host clock of this read.
/// </summary>
public sealed record GetAgentSupervisionQueryResult(
    Guid RunId,
    AgentSupervisionStatus Status,
    string? ReasonCode,
    string NextActionCode,
    AgentSupervisionAttemptView? Attempt,
    AgentSupervisionObservationView? Observation,
    AgentSupervisionAttentionView? Attention,
    DateTimeOffset ObservedAtUtc);
