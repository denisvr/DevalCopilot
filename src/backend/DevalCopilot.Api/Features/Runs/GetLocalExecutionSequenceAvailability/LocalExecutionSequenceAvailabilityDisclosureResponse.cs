namespace DevalCopilot.Api.Features.Runs.GetLocalExecutionSequenceAvailability;

/// <summary>The requested assignment and stop-policy facts the confirmation pins, as stored (absent values are null). Thresholds are
/// canonical Int64 strings.</summary>
public sealed record LocalExecutionSequenceAvailabilityDisclosureResponse(
    string? CodexModel,
    string? CodexEffort,
    string? ClaudeModel,
    string? ClaudeEffort,
    int? ClaudeMaxTurns,
    int? CodexAccountUsageStopPercent,
    string? CodexTokenStopThreshold,
    string? ClaudeTokenStopThreshold);
