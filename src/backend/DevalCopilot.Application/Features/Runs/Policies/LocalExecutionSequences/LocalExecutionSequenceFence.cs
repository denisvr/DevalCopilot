using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;

/// <summary>
/// The manual-control fence of a local execution sequence (ADR-0060): while its automatic authority can advance or drains a claimed
/// operation, a step preparation is reserved or unresolved, or a linked operation is unresolved (still running, or dispatched without
/// coherent host Exited evidence: an ended or expired sequence never releases such an operation), no unrelated Agent claim of its run,
/// no verification, local commit or manual checkpoint of its workspace, no abandonment of its run and no change to its project's
/// verification recipes or its run's requested settings and stop policies may be admitted. The decisive exclusion is the database
/// guard of the same predicate (migration <c>AddLocalExecutionSequence</c>), which fires inside the competing writer's own write
/// transaction; these reads only give a readable refusal before that point and classify a refused save afterwards. Reading history,
/// notifications and unrelated projects are never blocked, and only exact linked operations are exempt from this new fence.
/// </summary>
internal static class LocalExecutionSequenceFence
{
    public static Task<bool> IsRunBlockedAsync(IDevalCopilotDbContext dbContext, Guid runId, CancellationToken cancellationToken) =>
        IsLiveAsync(dbContext, dbContext.LocalExecutionSequences.AsNoTracking().Where(sequence => sequence.RunId == runId), cancellationToken);

    public static Task<bool> IsWorkspaceBlockedAsync(
        IDevalCopilotDbContext dbContext, Guid workspaceId, CancellationToken cancellationToken) =>
        IsLiveAsync(
            dbContext, dbContext.LocalExecutionSequences.AsNoTracking().Where(sequence => sequence.WorkspaceId == workspaceId), cancellationToken);

    public static Task<bool> IsProjectBlockedAsync(IDevalCopilotDbContext dbContext, Guid projectId, CancellationToken cancellationToken) =>
        IsLiveAsync(
            dbContext, dbContext.LocalExecutionSequences.AsNoTracking().Where(sequence => sequence.ProjectId == projectId), cancellationToken);

    public static async Task<Error?> CheckRunAsync(IDevalCopilotDbContext dbContext, Guid runId, CancellationToken cancellationToken) =>
        await IsRunBlockedAsync(dbContext, runId, cancellationToken) ? LocalExecutionSequenceErrors.ManualControlBlocked() : null;

    public static async Task<Error?> CheckWorkspaceAsync(
        IDevalCopilotDbContext dbContext, Guid workspaceId, CancellationToken cancellationToken) =>
        await IsWorkspaceBlockedAsync(dbContext, workspaceId, cancellationToken) ? LocalExecutionSequenceErrors.ManualControlBlocked() : null;

    public static async Task<Error?> CheckProjectAsync(
        IDevalCopilotDbContext dbContext, Guid projectId, CancellationToken cancellationToken) =>
        await IsProjectBlockedAsync(dbContext, projectId, cancellationToken) ? LocalExecutionSequenceErrors.ManualControlBlocked() : null;

    /// <summary>Classifies a refused save: true only when the fence now holds the run, so the database guard (not any other cause) is the
    /// reason. Uses an unconditional token because the caller's own may already be cancelled.</summary>
    public static Task<bool> RefusedRunSaveAsync(IDevalCopilotDbContext dbContext, Guid runId) =>
        IsRunBlockedAsync(dbContext, runId, CancellationToken.None);

    public static Task<bool> RefusedWorkspaceSaveAsync(IDevalCopilotDbContext dbContext, Guid workspaceId) =>
        IsWorkspaceBlockedAsync(dbContext, workspaceId, CancellationToken.None);

    public static Task<bool> RefusedProjectSaveAsync(IDevalCopilotDbContext dbContext, Guid projectId) =>
        IsProjectBlockedAsync(dbContext, projectId, CancellationToken.None);

    /// <summary>The sequence holds its fence: it is live, a step preparation is reserved or unresolved, or a linked operation is
    /// unresolved (running, or dispatched without coherent Exited evidence).</summary>
    private static async Task<bool> IsLiveAsync(
        IDevalCopilotDbContext dbContext, IQueryable<LocalExecutionSequence> scoped, CancellationToken cancellationToken)
    {
        if (await scoped.AnyAsync(
                sequence => sequence.State == LocalExecutionSequenceState.Armed
                    || sequence.State == LocalExecutionSequenceState.PreparingStep
                    || sequence.State == LocalExecutionSequenceState.AwaitingStepResult
                    || sequence.State == LocalExecutionSequenceState.Stopping,
                cancellationToken))
        {
            return true;
        }

        var sequenceIds = scoped.Select(sequence => sequence.Id);
        var steps = dbContext.LocalExecutionSequenceSteps.AsNoTracking().Where(step => sequenceIds.Contains(step.SequenceId));
        if (await steps.AnyAsync(
                step => step.State == LocalExecutionSequenceStepState.Reserved || step.State == LocalExecutionSequenceStepState.Unresolved,
                cancellationToken))
        {
            return true;
        }

        var agentOperations = steps
            .Where(step => step.OperationId != null && step.Kind != LocalExecutionStepKind.Verification)
            .Select(step => step.OperationId!.Value);
        if (await dbContext.Attempts.AsNoTracking()
                .Where(LocalExecutionOwnership.UnresolvedAttempt(dbContext))
                .AnyAsync(attempt => agentOperations.Contains(attempt.Id), cancellationToken))
        {
            return true;
        }

        var verificationOperations = steps
            .Where(step => step.OperationId != null && step.Kind == LocalExecutionStepKind.Verification)
            .Select(step => step.OperationId!.Value);
        return await dbContext.VerificationExecutions.AsNoTracking()
            .Where(LocalExecutionOwnership.UnresolvedExecution(dbContext))
            .AnyAsync(execution => verificationOperations.Contains(execution.Id), cancellationToken);
    }
}
