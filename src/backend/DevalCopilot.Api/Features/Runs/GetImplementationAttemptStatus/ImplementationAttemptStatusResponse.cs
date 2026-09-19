using DevalCopilot.Api.Features.Runs.GetAgentAttemptStatus;

namespace DevalCopilot.Api.Features.Runs.GetImplementationAttemptStatus;

/// <summary>Mirrors <c>ChallengeResolutionAttemptStatusResponse</c>'s discriminator pattern, plus
/// the starting/resulting checkpoint identities and the bounded changed-file evidence a
/// successful implementation actually produced. <c>hasAttempt: false</c> means this run has never
/// requested an implementation — every other field is then null/empty, not merely absent. Never
/// an absolute path, prompt, manifest, credential, environment value, or raw transcript.</summary>
public sealed record ImplementationAttemptStatusResponse(
    bool HasAttempt,
    Guid? AttemptId,
    int? AttemptNumber,
    Guid? PlanProposalMessageId,
    string? Status,
    string? Outcome,
    Guid? StartingGitCheckpointId,
    string? StartingCheckpointFingerprintSha256,
    Guid? ResultGitCheckpointId,
    string? ResultCheckpointFingerprintSha256,
    string? ExecutionReportSummary,
    IReadOnlyList<string> ChangedRelativePaths,
    DateTimeOffset? ClaimedAtUtc,
    DateTimeOffset? DispatchedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    IReadOnlyList<AgentAttemptArtifactMetadataResponse> Artifacts);
