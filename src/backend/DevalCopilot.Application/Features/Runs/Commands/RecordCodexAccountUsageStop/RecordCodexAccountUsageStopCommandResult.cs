using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordCodexAccountUsageStop;

/// <summary>The terminal state the attempt was resolved to and the sequence of the completion event that was recorded with it, so the
/// supervisor can notify only after the durable commit.</summary>
public sealed record RecordCodexAccountUsageStopCommandResult(AttemptStatus Status, AgentOutcome Outcome, long LatestEventSequence);
