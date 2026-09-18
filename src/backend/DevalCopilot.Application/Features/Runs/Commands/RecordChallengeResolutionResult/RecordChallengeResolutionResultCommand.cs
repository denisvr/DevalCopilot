using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordChallengeResolutionResult;

/// <summary>
/// Atomically records a Codex challenge-resolution attempt's terminal outcome, its sealed
/// artifact metadata, and — only when the outcome is <see cref="AgentOutcome.Resolved"/> — one
/// <see cref="CollaborationMessage"/> Decision per input Challenge (each replying to that
/// Challenge) plus exactly one revised Proposal (replying to the original Proposal), and their
/// <see cref="RunEvent"/>s. No adapter or external I/O happens in this handler, so this is a
/// plain, automatically-transacted command — mirrors <c>RecordClaudeCriticalReviewResultCommand</c>.
/// </summary>
public sealed record RecordChallengeResolutionResultCommand(
    Guid RunId,
    Guid AttemptId,
    AgentOutcome Outcome,
    string? CompletionFingerprintSha256,
    IReadOnlyList<SealedChallengeResolutionArtifact> SealedArtifacts,
    ValidatedChallengeResolution? Resolution,
    string? ProviderSessionId) : ICommand<Result<RecordChallengeResolutionResultCommandResult>>;
