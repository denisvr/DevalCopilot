namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionSequenceAvailability;

/// <summary>The requested assignment and stop-policy facts the confirmation pins, disclosed so the Human sees what is confirmed.</summary>
public sealed record LocalExecutionSequenceAvailabilityDisclosure(
    string? CodexModel,
    string? CodexEffort,
    string? ClaudeModel,
    string? ClaudeEffort,
    int? ClaudeMaxTurns,
    int? CodexAccountUsageStopPercent,
    long? CodexTokenStopThreshold,
    long? ClaudeTokenStopThreshold);
