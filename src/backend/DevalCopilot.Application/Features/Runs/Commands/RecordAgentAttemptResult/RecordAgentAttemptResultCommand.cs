using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordAgentAttemptResult;

/// <summary>
/// Atomically records an Agent attempt's terminal outcome, its sealed artifact metadata, and —
/// only when the outcome is <see cref="AgentOutcome.Proposed"/> — one validated
/// <see cref="CollaborationMessage"/> Proposal and its <see cref="RunEvent"/>. No adapter or
/// external I/O happens in this handler (everything it touches was already sealed/resolved by the
/// caller before this dispatch), so this is a plain, automatically-transacted command — mirroring
/// <c>RecordProcessAttemptResultCommand</c> exactly.
/// </summary>
public sealed record RecordAgentAttemptResultCommand(
    Guid RunId,
    Guid AttemptId,
    AgentOutcome Outcome,
    string? CompletionFingerprintSha256,
    IReadOnlyList<SealedAgentArtifact> SealedArtifacts,
    ValidatedProposal? Proposal,
    string? ProviderSessionId,
    AgentProcessEvidence? ProcessEvidence = null) : ICommand<Result<RecordAgentAttemptResultCommandResult>>;
