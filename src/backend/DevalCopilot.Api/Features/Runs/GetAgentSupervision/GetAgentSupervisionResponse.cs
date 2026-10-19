namespace DevalCopilot.Api.Features.Runs.GetAgentSupervision;

/// <summary>
/// The current host supervision of the run's one selected Agent attempt, as read now (ADR-0056): what the host's own observer last
/// recorded, never provider progress, a live child process or execution authority. <c>Status</c> is one of NoAgentAttempt, NotObserved,
/// Observed, NeedsAttention, Ended and Unavailable. NoAgentAttempt has no attempt, observation or attention; NotObserved has an attempt
/// only; Observed has an attempt and an open observation without attention; NeedsAttention has an attempt, an observation and an
/// attention (<c>ReasonCode</c> is its effective reason); Ended has a terminal attempt and an observation, with attention only as
/// history; Unavailable has a fixed <c>ReasonCode</c> and whatever coherent facts were read. <c>ObservedAtUtc</c> is the host clock of
/// this read, a UTC round-trip string.
/// </summary>
public sealed record GetAgentSupervisionResponse(
    Guid RunId,
    string Status,
    string? ReasonCode,
    string? ReasonText,
    string NextActionCode,
    string NextActionText,
    AgentSupervisionAttemptResponse? Attempt,
    AgentSupervisionObservationResponse? Observation,
    AgentSupervisionAttentionResponse? Attention,
    string ObservedAtUtc);
