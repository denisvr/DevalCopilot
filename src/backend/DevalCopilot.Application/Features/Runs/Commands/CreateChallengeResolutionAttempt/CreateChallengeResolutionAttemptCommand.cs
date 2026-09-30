using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.CreateChallengeResolutionAttempt;

/// <summary>
/// Creates one durable Codex challenge-resolution attempt for an eligible run, resolving exactly
/// the complete, already-recorded Challenge set of one specific, already-completed Challenged
/// Claude critical-review attempt against its original Proposal. Manual transaction: this handler
/// performs a fresh, bounded Git evidence capture and writes the sealed context-manifest
/// artifact, neither of which may run inside the mediator's ambient EF transaction — mirrors
/// <c>CreateClaudeCriticalReviewAttemptCommand</c> exactly. <paramref name="RepairSourceAttemptId"/>
/// is <see langword="null"/> for an ordinary request. When set (see <see cref="ForRepair"/>), this
/// is the one human-requested format repair of that exact failed resolution attempt: the challenged
/// review is derived from the source's persisted inputs, never supplied.
/// </summary>
public sealed record CreateChallengeResolutionAttemptCommand(
    Guid RunId, Guid? ChallengedReviewAttemptId, Guid? RepairSourceAttemptId = null)
    : IManualTransactionCommand<Result<CreateChallengeResolutionAttemptCommandResult>>
{
    public static CreateChallengeResolutionAttemptCommand ForRepair(Guid runId, Guid sourceAttemptId) =>
        new(runId, null, sourceAttemptId);
}
