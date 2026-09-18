using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.CreateChallengeResolutionAttempt;

/// <summary>
/// Creates one durable Codex challenge-resolution attempt for an eligible run, resolving exactly
/// the complete, already-recorded Challenge set of one specific, already-completed Challenged
/// Claude critical-review attempt against its original Proposal. Manual transaction: this handler
/// performs a fresh, bounded Git evidence capture and writes the sealed context-manifest
/// artifact, neither of which may run inside the mediator's ambient EF transaction — mirrors
/// <c>CreateClaudeCriticalReviewAttemptCommand</c> exactly.
/// </summary>
public sealed record CreateChallengeResolutionAttemptCommand(Guid RunId, Guid ChallengedReviewAttemptId)
    : IManualTransactionCommand<Result<CreateChallengeResolutionAttemptCommandResult>>;
