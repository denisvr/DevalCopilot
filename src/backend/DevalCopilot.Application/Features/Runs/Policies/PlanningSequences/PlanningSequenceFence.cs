using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.PlanningSequences;

/// <summary>
/// The manual-control fence of a planning sequence (ADR-0055): while its automatic authority can advance or drains a claimed step, or
/// one of its linked attempts is still Running (an ended or expired sequence never releases an unresolved attempt), no unrelated Agent
/// claim of its run, no verification or local commit of its workspace and no abandonment of its run may be admitted. The decisive
/// exclusion is the database guard of the same predicate (migration <c>AddPlanningSequence</c>), which fires inside the competing
/// writer's own write transaction; these reads only give a readable refusal before that point and classify a refused save afterwards.
/// An unrelated project has no sequence on the run or workspace, so nothing here ever blocks it.
/// </summary>
internal static class PlanningSequenceFence
{
    /// <summary>Whether a sequence of this run holds the fence. Read fresh and untracked.</summary>
    public static Task<bool> IsRunBlockedAsync(IDevalCopilotDbContext dbContext, Guid runId, CancellationToken cancellationToken) =>
        LiveSequences(dbContext).AnyAsync(sequence => sequence.RunId == runId, cancellationToken);

    /// <summary>Whether a sequence pinned to this workspace holds the fence. Read fresh and untracked.</summary>
    public static Task<bool> IsWorkspaceBlockedAsync(
        IDevalCopilotDbContext dbContext, Guid workspaceId, CancellationToken cancellationToken) =>
        LiveSequences(dbContext).AnyAsync(sequence => sequence.WorkspaceId == workspaceId, cancellationToken);

    /// <summary>The early refusal for an unrelated Agent claim, an abandonment or a verification: null when nothing holds the run.</summary>
    public static async Task<Error?> CheckRunAsync(IDevalCopilotDbContext dbContext, Guid runId, CancellationToken cancellationToken) =>
        await IsRunBlockedAsync(dbContext, runId, cancellationToken) ? PlanningSequenceErrors.ManualControlBlocked() : null;

    /// <summary>Classifies a refused save: true only when the fence now holds the run, so the database guard (not any other cause)
    /// is the reason. Uses an unconditional token because the caller's own may already be cancelled.</summary>
    public static Task<bool> RefusedRunSaveAsync(IDevalCopilotDbContext dbContext, Guid runId) =>
        IsRunBlockedAsync(dbContext, runId, CancellationToken.None);

    private static IQueryable<PlanningSequence> LiveSequences(IDevalCopilotDbContext dbContext) =>
        dbContext.PlanningSequences.AsNoTracking().Where(sequence =>
            sequence.State == PlanningSequenceState.Armed
            || sequence.State == PlanningSequenceState.PreparingStep
            || sequence.State == PlanningSequenceState.AwaitingStepResult
            || sequence.State == PlanningSequenceState.Stopping
            || dbContext.PlanningSequenceSteps.Any(step => step.SequenceId == sequence.Id
                && dbContext.Attempts.Any(attempt => attempt.Id == step.ReservedAttemptId && attempt.Status == AttemptStatus.Running)));
}
