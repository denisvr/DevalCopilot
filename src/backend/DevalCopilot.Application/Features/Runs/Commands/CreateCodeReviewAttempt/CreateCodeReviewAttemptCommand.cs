using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.CreateCodeReviewAttempt;

/// <summary>
/// Claims one durable Codex code-review attempt for an eligible run, reviewing exactly one
/// explicit, already-recorded, provider-observed Claude implementation ExecutionReport. Mirrors
/// <c>CreateClaudeCriticalReviewAttemptCommand</c>/<c>CreateChallengeResolutionAttemptCommand</c>.
///
/// <para>
/// Manual transaction: this handler's own external Git evidence capture and artifact-sealing work
/// must never run inside the mediator's automatic per-command EF transaction, exactly like its two
/// sibling claim commands (<c>CreateCodexPlanningAttemptCommand</c>,
/// <c>CreateChallengeResolutionAttemptCommand</c>) already are. The handler owns its own explicit
/// <c>SaveChangesAsync</c> call(s), including a short, explicit guard transaction opened only after
/// all external work completes — see <c>CreateCodeReviewAttemptCommandHandler</c>.
/// </para>
///
/// <para>
/// <paramref name="RepairSourceAttemptId"/> is <see langword="null"/> for an ordinary request. When
/// set (see <see cref="ForRepair"/>), this is the one human-requested format repair of that exact
/// failed code-review attempt: the ExecutionReport and verification set are derived from the
/// source's persisted inputs, never supplied.
/// </para>
/// </summary>
public sealed record CreateCodeReviewAttemptCommand(
    Guid RunId, Guid? ExecutionReportMessageId, Guid? RepairSourceAttemptId = null)
    : IManualTransactionCommand<Result<CreateCodeReviewAttemptCommandResult>>
{
    public static CreateCodeReviewAttemptCommand ForRepair(Guid runId, Guid sourceAttemptId) =>
        new(runId, null, sourceAttemptId);
}
