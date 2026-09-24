using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordImplementationReviewResult;

/// <summary>
/// Atomically records a Codex code-review attempt's terminal outcome, its sealed artifact
/// metadata, and — only when the outcome is <see cref="AgentOutcome.ReviewApproved"/> or
/// <see cref="AgentOutcome.ReviewChangesRequested"/> — one immutable
/// <see cref="DevalCopilot.Domain.Features.Projects.CheckpointReview"/> (with its complete evidence
/// set), and either exactly one <see cref="CollaborationMessageType.ReviewApproval"/> message or
/// one <see cref="CollaborationMessageType.ReviewFinding"/> message per finding (each replying to
/// the reviewed ExecutionReport), plus their <see cref="RunEvent"/>s. No adapter or external I/O
/// happens in this handler, so this is a plain, automatically-transacted command — mirrors
/// <c>RecordChallengeResolutionResultCommand</c>.
/// </summary>
public sealed record RecordImplementationReviewResultCommand(
    Guid RunId,
    Guid AttemptId,
    AgentOutcome Outcome,
    string? CompletionFingerprintSha256,
    IReadOnlyList<SealedImplementationReviewArtifact> SealedArtifacts,
    ValidatedImplementationReview? Review,
    string? ProviderSessionId,
    AgentProcessEvidence? ProcessEvidence = null,
    AgentTokenUsage? TokenUsage = null) : ICommand<Result<RecordImplementationReviewResultCommandResult>>;
