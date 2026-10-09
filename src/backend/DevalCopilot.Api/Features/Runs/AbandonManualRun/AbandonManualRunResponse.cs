namespace DevalCopilot.Api.Features.Runs.AbandonManualRun;

/// <summary>The recorded abandonment: the normalized reason and the one UTC time it was recorded. Abandonment is not completion; no
/// process was cancelled and nothing was repaired or deleted.</summary>
public sealed record AbandonManualRunResponse(Guid RunId, int ExecutionNumber, string Reason, DateTimeOffset AbandonedAtUtc);
