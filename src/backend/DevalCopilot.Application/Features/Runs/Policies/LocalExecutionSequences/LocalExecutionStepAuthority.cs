using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;

/// <summary>
/// The claim-side authority of a coordinator-composed local-execution step (ADR-0060). <see cref="CheckEarlyAsync"/> decides from fresh
/// untracked reads before any external work. <see cref="ConfirmAtSeamAsync"/> runs inside the claim's own explicit transaction: its first
/// statement is an atomic compare-and-set on the exact reserved step that also takes SQLite's write lock, so a Stop, an expiry or a
/// restart that committed first leaves it with zero matching rows and every later read is decided against locked state. A confirmed
/// link marks the step Claimed and the sequence AwaitingStepResult as tracked changes the caller saves in the same transaction as the
/// operation, its inputs, its artifact metadata, its counters and its budget or grant consumption: there is no claim-then-link gap.
/// The exact deadline, grant, original launch, pinned source, recipes and settings are revalidated here.
/// </summary>
internal static class LocalExecutionStepAuthority
{
    public static async Task<Error?> CheckEarlyAsync(
        IDevalCopilotDbContext dbContext,
        LocalExecutionStepTicket ticket,
        Run run,
        LocalExecutionStepKind kind,
        GitWorkspace workspace,
        GitCheckpoint checkpoint,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var sequence = await dbContext.LocalExecutionSequences.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == ticket.SequenceId, cancellationToken);
        var step = await dbContext.LocalExecutionSequenceSteps.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == ticket.StepId, cancellationToken);
        return await ValidateAsync(dbContext, sequence, step, ticket, run, kind, workspace, checkpoint, nowUtc, cancellationToken);
    }

    /// <summary>
    /// Inside the claim's explicit transaction and before any other statement: confirms and locks the exact step, re-reads the pinned
    /// source, recipes and settings, and returns the link to apply with the operation. A refusal leaves nothing to roll back beyond the
    /// open transaction.
    /// </summary>
    public static async Task<(Error? Error, LocalExecutionStepLink? Link)> ConfirmAtSeamAsync(
        IDevalCopilotDbContext dbContext,
        LocalExecutionStepTicket ticket,
        Run run,
        LocalExecutionStepKind kind,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var confirmed = await dbContext.LocalExecutionSequenceSteps
            .Where(candidate => candidate.Id == ticket.StepId
                && candidate.State == LocalExecutionSequenceStepState.Reserved
                && candidate.PreparationOwnerToken == ticket.OwnerToken
                && candidate.ReservedOperationId == ticket.ReservedOperationId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.Revision, candidate => candidate.Revision), cancellationToken);
        if (confirmed != 1)
        {
            return (LocalExecutionSequenceErrors.StepAuthorityLost(), null);
        }

        var step = await dbContext.LocalExecutionSequenceSteps.SingleAsync(candidate => candidate.Id == ticket.StepId, cancellationToken);
        await dbContext.Entry(step).ReloadAsync(cancellationToken);
        var sequence = await dbContext.LocalExecutionSequences.SingleOrDefaultAsync(
            candidate => candidate.Id == ticket.SequenceId, cancellationToken);
        if (sequence is not null)
        {
            await dbContext.Entry(sequence).ReloadAsync(cancellationToken);
        }

        var workspace = await dbContext.GitWorkspaces.AsNoTracking()
            .Where(candidate => candidate.ProjectId == run.ProjectId)
            .OrderByDescending(candidate => candidate.WorkspaceNumber)
            .FirstOrDefaultAsync(cancellationToken);
        var checkpoint = workspace is null
            ? null
            : await dbContext.GitCheckpoints.AsNoTracking()
                .Where(candidate => candidate.WorkspaceId == workspace.Id)
                .OrderByDescending(candidate => candidate.CheckpointNumber)
                .FirstOrDefaultAsync(cancellationToken);
        if (workspace is null || checkpoint is null)
        {
            return (LocalExecutionSequenceErrors.StepSourceChanged(), null);
        }

        var freshRun = await dbContext.Runs.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == run.Id, cancellationToken);
        if (freshRun is null)
        {
            return (LocalExecutionSequenceErrors.StepAuthorityLost(), null);
        }

        var error = await ValidateAsync(dbContext, sequence, step, ticket, freshRun, kind, workspace, checkpoint, nowUtc, cancellationToken);
        if (error is not null)
        {
            return (error, null);
        }

        var leaseIsActive = await dbContext.RepositoryMutationLeases.AsNoTracking()
            .AnyAsync(lease => lease.WorkspaceId == workspace.Id && lease.Status == LeaseStatus.Active, cancellationToken);
        return leaseIsActive
            ? (null, new LocalExecutionStepLink(sequence!, step))
            : (LocalExecutionSequenceErrors.StepSourceChanged(), null);
    }

    /// <summary>The recorded verification operations of one pass, ordered by their pinned recipe position. Callers compare this whole
    /// ordered list with the executions they would select, never only counts.</summary>
    public static async Task<IReadOnlyList<(int RecipeOrdinal, Guid ExecutionId)>> PassExecutionsAsync(
        IDevalCopilotDbContext dbContext, Guid sequenceId, int passNumber, CancellationToken cancellationToken)
    {
        var rows = await dbContext.LocalExecutionSequenceSteps.AsNoTracking()
            .Where(step => step.SequenceId == sequenceId
                && step.Kind == LocalExecutionStepKind.Verification
                && step.PassNumber == passNumber
                && step.OperationId != null
                && (step.State == LocalExecutionSequenceStepState.Claimed || step.State == LocalExecutionSequenceStepState.Concluded))
            .OrderBy(step => step.RecipeOrdinal)
            .Select(step => new { Ordinal = step.RecipeOrdinal!.Value, Id = step.OperationId!.Value })
            .ToListAsync(cancellationToken);
        return rows.Select(row => (row.Ordinal, row.Id)).ToList();
    }

    private static async Task<Error?> ValidateAsync(
        IDevalCopilotDbContext dbContext,
        LocalExecutionSequence? sequence,
        LocalExecutionSequenceStep? step,
        LocalExecutionStepTicket ticket,
        Run run,
        LocalExecutionStepKind kind,
        GitWorkspace workspace,
        GitCheckpoint checkpoint,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (sequence is null
            || step is null
            || sequence.Id != ticket.SequenceId
            || step.Id != ticket.StepId
            || step.SequenceId != sequence.Id
            || sequence.RunId != run.Id
            || step.Ordinal != ticket.Ordinal
            || step.Kind != kind
            || ticket.Kind != kind
            || step.State != LocalExecutionSequenceStepState.Reserved
            || step.PreparationOwnerToken != ticket.OwnerToken
            || step.ReservedOperationId != ticket.ReservedOperationId
            || step.SourceCheckpointId != ticket.SourceCheckpointId
            || step.ReportMessageId != ticket.ReportMessageId
            || step.SubjectAttemptId != ticket.SubjectAttemptId
            || sequence.State != LocalExecutionSequenceState.PreparingStep)
        {
            return LocalExecutionSequenceErrors.StepAuthorityLost();
        }

        if (nowUtc >= step.PreparationDeadlineUtc || nowUtc >= sequence.AdvancementDeadlineUtc)
        {
            return LocalExecutionSequenceErrors.StepExpired();
        }

        // The exact pinned source: never a newer workspace or checkpoint, never an unrelated capture, edit or verification.
        if (workspace.Id != sequence.WorkspaceId
            || workspace.ProjectId != sequence.ProjectId
            || workspace.Status != WorkspaceStatus.Ready
            || checkpoint.Id != step.SourceCheckpointId
            || checkpoint.WorkspaceId != sequence.WorkspaceId
            || !string.Equals(checkpoint.FingerprintSha256, step.SourceFingerprintSha256, StringComparison.Ordinal)
            || run.ProjectId != sequence.ProjectId
            || !string.Equals(LocalExecutionSettingsSnapshot.Of(run), sequence.SettingsSnapshot, StringComparison.Ordinal))
        {
            return LocalExecutionSequenceErrors.StepSourceChanged();
        }

        return await RecipesHoldAsync(dbContext, sequence, cancellationToken) ? null : LocalExecutionSequenceErrors.StepSourceChanged();
    }

    /// <summary>The project's whole enabled recipe set still equals the pinned set in every execution-relevant member and order.</summary>
    public static async Task<bool> RecipesHoldAsync(
        IDevalCopilotDbContext dbContext, LocalExecutionSequence sequence, CancellationToken cancellationToken)
    {
        var pinned = await dbContext.LocalExecutionSequenceRecipes.AsNoTracking()
            .Where(recipe => recipe.SequenceId == sequence.Id)
            .OrderBy(recipe => recipe.Ordinal)
            .ToListAsync(cancellationToken);
        var current = await dbContext.VerificationCommands.AsNoTracking()
            .Where(command => command.ProjectId == sequence.ProjectId && command.IsEnabled)
            .OrderBy(command => command.CommandNumber)
            .ThenBy(command => command.Id)
            .Take(LocalExecutionSequencePolicy.MaximumRecipes + 1)
            .ToListAsync(cancellationToken);
        if (pinned.Count != sequence.RecipeCount || current.Count != pinned.Count)
        {
            return false;
        }

        for (var index = 0; index < pinned.Count; index++)
        {
            if (pinned[index].Ordinal != index || !pinned[index].Matches(current[index]))
            {
                return false;
            }
        }

        return true;
    }
}
