namespace DevalCopilot.Application.Features.Runs.Commands.AbandonManualRun;

/// <summary>The recorded abandonment: the exact normalized reason and the one UTC time it was recorded. A replay of the same normalized
/// reason returns these original values unchanged.</summary>
public sealed record AbandonManualRunCommandResult(Guid RunId, int ExecutionNumber, string Reason, DateTimeOffset AbandonedAtUtc);
