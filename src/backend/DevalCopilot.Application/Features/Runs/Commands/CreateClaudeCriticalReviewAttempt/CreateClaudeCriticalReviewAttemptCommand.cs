using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.CreateClaudeCriticalReviewAttempt;

/// <summary>
/// Creates one durable Claude critical-review attempt for an eligible run, reviewing exactly one
/// explicit, already-recorded, provider-observed Codex Proposal. Manual transaction: this handler
/// performs a fresh, bounded Git evidence capture and writes the sealed context-manifest
/// artifact, neither of which may run inside the mediator's ambient EF transaction — mirrors
/// <c>CreateCodexPlanningAttemptCommand</c> exactly. <paramref name="RepairSourceAttemptId"/> is
/// <see langword="null"/> for an ordinary request. When set (see <see cref="ForRepair"/>), this is the
/// one human-requested format repair of that exact failed critical-review attempt: the target
/// Proposal is derived from the source's persisted input, never supplied, and the claim adds
/// source-eligibility checks at the request and again inside the durable claim transaction.
/// </summary>
public sealed record CreateClaudeCriticalReviewAttemptCommand(
    Guid RunId, Guid? ProposalMessageId, Guid? RepairSourceAttemptId = null)
    : IManualTransactionCommand<Result<CreateClaudeCriticalReviewAttemptCommandResult>>
{
    public static CreateClaudeCriticalReviewAttemptCommand ForRepair(Guid runId, Guid sourceAttemptId) =>
        new(runId, null, sourceAttemptId);
}
