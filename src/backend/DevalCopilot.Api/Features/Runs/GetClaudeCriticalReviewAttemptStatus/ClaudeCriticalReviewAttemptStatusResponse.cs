using DevalCopilot.Api.Features.Runs.GetAgentAttemptStatus;

namespace DevalCopilot.Api.Features.Runs.GetClaudeCriticalReviewAttemptStatus;

/// <summary>Mirrors <c>AgentAttemptStatusResponse</c> exactly, plus the exact reviewed Proposal
/// message identity. <c>HasAttempt: false</c> means this run has never requested a Claude
/// critical review — every other field is then null/empty, not merely absent.</summary>
public sealed record ClaudeCriticalReviewAttemptStatusResponse(
    bool HasAttempt,
    Guid? AttemptId,
    int? AttemptNumber,
    Guid? ReviewedProposalMessageId,
    string? Status,
    string? Outcome,
    DateTimeOffset? ClaimedAtUtc,
    DateTimeOffset? DispatchedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    IReadOnlyList<AgentAttemptArtifactMetadataResponse> Artifacts,
    AgentProcessExecutionResponse? ProcessExecution);
