using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.CreateClaudeCriticalReviewAttempt;

/// <summary>
/// Creates one durable Claude critical-review attempt for an eligible run, reviewing exactly one
/// explicit, already-recorded, provider-observed Codex Proposal. Manual transaction: this handler
/// performs a fresh, bounded Git evidence capture and writes the sealed context-manifest
/// artifact, neither of which may run inside the mediator's ambient EF transaction — mirrors
/// <c>CreateCodexPlanningAttemptCommand</c> exactly.
/// </summary>
public sealed record CreateClaudeCriticalReviewAttemptCommand(Guid RunId, Guid ProposalMessageId)
    : IManualTransactionCommand<Result<CreateClaudeCriticalReviewAttemptCommandResult>>;
