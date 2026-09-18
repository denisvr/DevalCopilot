namespace DevalCopilot.Api.Features.Runs.GetAgentAttemptStatus;

/// <summary>Always a real body for an existing run. <c>HasAttempt: false</c> means this run has
/// never requested a Codex plan — every other field is then null/empty, not merely absent.</summary>
public sealed record AgentAttemptStatusResponse(
    bool HasAttempt,
    Guid? AttemptId,
    int? AttemptNumber,
    string? Status,
    string? Outcome,
    DateTimeOffset? ClaimedAtUtc,
    DateTimeOffset? DispatchedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    IReadOnlyList<AgentAttemptArtifactMetadataResponse> Artifacts);
