using DevalCopilot.Api.Features.Runs.GetAgentAttemptStatus;

namespace DevalCopilot.Api.Features.Runs.GetCodeReviewAttemptStatus;

/// <summary>Mirrors <c>ClaudeCriticalReviewAttemptStatusResponse</c> exactly, plus the exact
/// reviewed ExecutionReport message identity. <c>HasAttempt: false</c> means this run has never
/// requested a code review — every other field is then null/empty, not merely absent.</summary>
public sealed record CodeReviewAttemptStatusResponse(
    bool HasAttempt,
    Guid? AttemptId,
    int? AttemptNumber,
    Guid? ExecutionReportMessageId,
    string? Status,
    string? Outcome,
    DateTimeOffset? ClaimedAtUtc,
    DateTimeOffset? DispatchedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    IReadOnlyList<AgentAttemptArtifactMetadataResponse> Artifacts,
    AgentProcessExecutionResponse? ProcessExecution,
    AgentTokenUsageResponse? TokenUsage);
