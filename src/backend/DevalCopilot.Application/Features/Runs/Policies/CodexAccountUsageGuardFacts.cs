using DevalCopilot.Application.Features.Runs.Ports;

namespace DevalCopilot.Application.Features.Runs.Policies;

/// <summary>
/// The immutable facts of one guard check: the threshold being enforced, the exact vetted launch tuple the observation was made
/// against, the observation itself, and the host's read interval around it. A claim check has no attempt yet
/// (<see cref="AttemptId"/> is null); a dispatch check is bound to the one claimed attempt. Internal to the host: no HTTP contract
/// accepts it, and a consumer re-validates every fact against current stored state instead of trusting the caller, so a fact that is
/// missing, changed, expired or bound to another attempt, threshold or launch tuple can never permit anything.
/// </summary>
public sealed record CodexAccountUsageGuardFacts(
    Guid? AttemptId,
    int ThresholdPercent,
    string ExecutablePath,
    string? ScriptPath,
    AccountUsageObservation Observation,
    DateTimeOffset ReadStartedUtc,
    DateTimeOffset ReadCompletedUtc);
