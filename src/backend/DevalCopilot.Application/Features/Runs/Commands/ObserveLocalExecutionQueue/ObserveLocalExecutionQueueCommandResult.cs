namespace DevalCopilot.Application.Features.Runs.Commands.ObserveLocalExecutionQueue;

/// <summary>The outcome of one observation page: the last coordination number visited (the next page's cursor), the fixed high-water of
/// this sweep, whether the finite range is finished (the host then wraps and refreshes the high-water) and how many transitions were
/// recorded.</summary>
public sealed record ObserveLocalExecutionQueueCommandResult(
    long LastCoordinationNumber, long HighWaterCoordinationNumber, bool SweepFinished, int Visited, int Changed);
