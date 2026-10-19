using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.PlanningSequences;

/// <summary>
/// The claim-side authority of a coordinator-composed step (ADR-0055). <see cref="CheckEarlyAsync"/> decides from fresh untracked reads
/// before any external work. <see cref="ConfirmAtSeamAsync"/> runs inside the claim's own explicit transaction: its first statement is
/// an atomic compare-and-set on the exact reserved step that also takes SQLite's write lock, so a Stop, an expiry or a restart that
/// committed first leaves it with zero matching rows and every later read is decided against locked state. A confirmed link marks the
/// step Claimed and the sequence AwaitingStepResult as tracked changes the caller saves in the same transaction as the attempt, its
/// inputs, its artifact metadata and its budget consumption: there is no claim-then-link gap.
/// </summary>
internal static class PlanningSequenceStepAuthority
{
    /// <summary>The fresh early decision: null when the exact ticket still authorizes this claim against the pinned source.</summary>
    public static async Task<Error?> CheckEarlyAsync(
        IDevalCopilotDbContext dbContext,
        PlanningSequenceStepTicket ticket,
        Run run,
        AgentRole role,
        GitWorkspace workspace,
        GitCheckpoint checkpoint,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var sequence = await dbContext.PlanningSequences.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == ticket.SequenceId, cancellationToken);
        var step = await dbContext.PlanningSequenceSteps.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == ticket.StepId, cancellationToken);
        return Validate(sequence, step, ticket, run, role, workspace, checkpoint, nowUtc);
    }

    /// <summary>
    /// Inside the claim's explicit transaction and before any other statement: confirms and locks the exact step, re-reads the pinned
    /// source, and returns the link to apply with the attempt. A refusal leaves nothing to roll back beyond the open transaction.
    /// </summary>
    public static async Task<(Error? Error, PlanningSequenceStepLink? Link)> ConfirmAtSeamAsync(
        IDevalCopilotDbContext dbContext,
        PlanningSequenceStepTicket ticket,
        Run run,
        AgentRole role,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var confirmed = await dbContext.PlanningSequenceSteps
            .Where(candidate => candidate.Id == ticket.StepId
                && candidate.State == PlanningSequenceStepState.Reserved
                && candidate.PreparationOwnerToken == ticket.OwnerToken
                && candidate.ReservedAttemptId == ticket.ReservedAttemptId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.Revision, candidate => candidate.Revision), cancellationToken);
        if (confirmed != 1)
        {
            return (PlanningSequenceErrors.StepAuthorityLost(), null);
        }

        var step = await dbContext.PlanningSequenceSteps.SingleAsync(candidate => candidate.Id == ticket.StepId, cancellationToken);
        await dbContext.Entry(step).ReloadAsync(cancellationToken);
        var sequence = await dbContext.PlanningSequences.SingleOrDefaultAsync(candidate => candidate.Id == ticket.SequenceId, cancellationToken);
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
            return (PlanningSequenceErrors.StepSourceChanged(), null);
        }

        var error = Validate(sequence, step, ticket, run, role, workspace, checkpoint, nowUtc);
        if (error is not null)
        {
            return (error, null);
        }

        var leaseIsActive = await dbContext.RepositoryMutationLeases.AsNoTracking()
            .AnyAsync(lease => lease.WorkspaceId == workspace.Id && lease.Status == LeaseStatus.Active, cancellationToken);
        return leaseIsActive
            ? (null, new PlanningSequenceStepLink(sequence!, step))
            : (PlanningSequenceErrors.StepSourceChanged(), null);
    }

    private static Error? Validate(
        PlanningSequence? sequence,
        PlanningSequenceStep? step,
        PlanningSequenceStepTicket ticket,
        Run run,
        AgentRole role,
        GitWorkspace workspace,
        GitCheckpoint checkpoint,
        DateTimeOffset nowUtc)
    {
        if (sequence is null
            || step is null
            || sequence.Id != ticket.SequenceId
            || step.Id != ticket.StepId
            || step.SequenceId != sequence.Id
            || sequence.RunId != run.Id
            || step.Ordinal != ticket.Ordinal
            || step.Role != role
            || step.State != PlanningSequenceStepState.Reserved
            || step.PreparationOwnerToken != ticket.OwnerToken
            || step.ReservedAttemptId != ticket.ReservedAttemptId
            || step.PredecessorAttemptId != ticket.PredecessorAttemptId
            || step.InputProposalMessageId != ticket.InputProposalMessageId
            || sequence.State != PlanningSequenceState.PreparingStep)
        {
            return PlanningSequenceErrors.StepAuthorityLost();
        }

        if (nowUtc >= step.PreparationDeadlineUtc)
        {
            return PlanningSequenceErrors.StepExpired();
        }

        // The exact pinned source and objective: never a newer workspace or checkpoint, never a changed objective.
        return workspace.Id != sequence.WorkspaceId
            || workspace.ProjectId != sequence.ProjectId
            || workspace.Status != WorkspaceStatus.Ready
            || checkpoint.Id != sequence.CheckpointId
            || checkpoint.WorkspaceId != sequence.WorkspaceId
            || !string.Equals(checkpoint.FingerprintSha256, sequence.FingerprintSha256, StringComparison.Ordinal)
            || !string.Equals(PlanningSequenceObjective.Hash(run.Objective), sequence.ObjectiveSha256, StringComparison.Ordinal)
                ? PlanningSequenceErrors.StepSourceChanged()
                : null;
    }
}
