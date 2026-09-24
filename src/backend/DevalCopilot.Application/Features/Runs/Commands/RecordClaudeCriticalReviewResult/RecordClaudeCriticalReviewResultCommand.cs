using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordClaudeCriticalReviewResult;

/// <summary>
/// Atomically records a Claude critical-review attempt's terminal outcome, its sealed artifact
/// metadata, and — only when the outcome is <see cref="AgentOutcome.Accepted"/> or
/// <see cref="AgentOutcome.Challenged"/> — the resulting <see cref="CollaborationMessage"/>
/// Acceptance (exactly one) or Challenge set (one to five, all replying to the same reviewed
/// Proposal) and their <see cref="RunEvent"/>. No adapter or external I/O happens in this handler
/// (everything it touches was already sealed/resolved by the caller before this dispatch), so this
/// is a plain, automatically-transacted command — mirroring <c>RecordAgentAttemptResultCommand</c>.
/// </summary>
public sealed record RecordClaudeCriticalReviewResultCommand(
    Guid RunId,
    Guid AttemptId,
    AgentOutcome Outcome,
    string? CompletionFingerprintSha256,
    IReadOnlyList<SealedCriticalReviewArtifact> SealedArtifacts,
    ValidatedCriticalReview? Review,
    string? ProviderSessionId,
    AgentProcessEvidence? ProcessEvidence = null) : ICommand<Result<RecordClaudeCriticalReviewResultCommandResult>>;
