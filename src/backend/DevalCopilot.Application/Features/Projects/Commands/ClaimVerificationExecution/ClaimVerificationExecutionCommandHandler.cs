using System.Data.Common;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Projects.Policies.WorkspaceExecutions;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.ExecutionCapacity;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;
using DevalCopilot.Application.Features.Runs.Policies.PlanningSequences;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DevalCopilot.Application.Features.Projects.Commands.ClaimVerificationExecution;

public sealed class ClaimVerificationExecutionCommandHandler(
    IDevalCopilotDbContext dbContext,
    IGitWorkspaceEvidenceReader evidenceReader,
    TimeProvider timeProvider,
    ILocalExecutionStepProbe? localStepProbe = null)
    : ICommandHandler<ClaimVerificationExecutionCommand, Result<ClaimVerificationExecutionCommandResult>>
{
    /// <summary>Manual transaction is required so the fresh Git capture is never performed while a transaction is open.
    /// Untracked reads decide the claim before the capture; after it, one short transaction takes the database write lock with
    /// its first statement (which also reserves the next execution number atomically), re-reads every authority fact untracked,
    /// requires them to equal what the capture was taken against, and persists the execution and the counter together. The Git
    /// observation is a point-in-time read of the working tree: it does not freeze the filesystem through the commit or the
    /// later process start.</summary>
    public async Task<Result<ClaimVerificationExecutionCommandResult>> HandleAsync(
        ClaimVerificationExecutionCommand command, CancellationToken cancellationToken)
    {
        var ticket = command.LocalStep;
        Run? sequenceRun = null;
        if (ticket is not null)
        {
            sequenceRun = await ReadSequenceRunAsync(ticket, cancellationToken);
            if (sequenceRun is null)
            {
                return Failure(LocalExecutionSequenceErrors.StepAuthorityLost());
            }
        }

        var early = await ReadAuthorityAsync(command, sequenceRun, underLock: false, cancellationToken);
        if (early.Authority is not { } observed)
        {
            return Failure(early.Error!);
        }

        var evidence = await evidenceReader.CaptureAsync(observed.Workspace.WorkspacePath, cancellationToken);
        if (evidence.Outcome != GitWorkspaceEvidenceOutcome.Success
            || !string.Equals(evidence.FingerprintSha256, observed.Checkpoint.FingerprintSha256, StringComparison.Ordinal))
        {
            return Failure(CheckpointNotCurrent("The selected source checkpoint is no longer current for this workspace."));
        }

        await using var transaction = await dbContext.BeginTransactionAsync(cancellationToken);

        // The reservation is the transaction's first statement: an atomic increment of the fresh stored counter that also takes
        // the write lock, so no other connection commits a competing change before the authority below is re-read, and a stale
        // tracked Project can neither supply the number nor overwrite the counter. A refusal rolls the increment back.
        var reserved = await dbContext.Projects
            .Where(candidate => candidate.Id == command.ProjectId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(candidate => candidate.NextVerificationExecutionNumber, candidate => candidate.NextVerificationExecutionNumber + 1),
                cancellationToken);
        if (reserved != 1)
        {
            return Failure(NotFound());
        }

        // A ticket claim confirms and locks its exact reserved step right after the counter (the first statement already holds the
        // write lock), before any other read that depends on locked state.
        LocalExecutionStepLink? link = null;
        if (ticket is not null)
        {
            var (seamError, confirmed) = await LocalExecutionStepAuthority.ConfirmAtSeamAsync(
                dbContext, ticket, sequenceRun!, LocalExecutionStepKind.Verification, timeProvider.GetUtcNow(), cancellationToken);
            if (seamError is not null)
            {
                return Failure(seamError);
            }

            link = confirmed;
        }

        var fresh = await ReadAuthorityAsync(command, sequenceRun, underLock: true, cancellationToken);
        if (fresh.Authority is not { } current)
        {
            return Failure(fresh.Error!);
        }

        if (!SameRecipe(current.Recipe, observed.Recipe))
        {
            return Failure(Error.Conflict("verification.recipe_changed", "The verification recipe changed while the request was being prepared."));
        }

        if (current.Workspace.Id != observed.Workspace.Id
            || !string.Equals(current.Workspace.WorkspacePath, observed.Workspace.WorkspacePath, StringComparison.Ordinal)
            || current.Checkpoint.Id != observed.Checkpoint.Id
            || current.Checkpoint.CheckpointNumber != observed.Checkpoint.CheckpointNumber
            || !string.Equals(current.Checkpoint.FingerprintSha256, observed.Checkpoint.FingerprintSha256, StringComparison.Ordinal))
        {
            return Failure(CheckpointNotCurrent("The selected source checkpoint is no longer current for this workspace."));
        }

        var nextNumber = await dbContext.Projects
            .AsNoTracking()
            .Where(candidate => candidate.Id == command.ProjectId)
            .Select(candidate => candidate.NextVerificationExecutionNumber)
            .SingleAsync(cancellationToken);
        var claimedAtUtc = timeProvider.GetUtcNow();
        var execution = VerificationExecution.Claim(
            ticket?.ReservedOperationId ?? Guid.NewGuid(),
            command.ProjectId,
            nextNumber - 1,
            current.Workspace,
            current.Checkpoint,
            current.Recipe,
            claimedAtUtc);
        dbContext.VerificationExecutions.Add(execution);

        // The step link, its event, the execution and the counter commit in this one transaction.
        link?.Apply(dbContext, execution.Id, TimeSpan.FromSeconds(current.Recipe.TimeoutSeconds), claimedAtUtc);

        if (ticket is not null)
        {
            return await SaveTicketClaimAsync(transaction, execution, ticket, cancellationToken);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (!await LocalExecutionSequenceFence.RefusedWorkspaceSaveAsync(dbContext, current.Workspace.Id))
            {
                throw;
            }

            // The database guard of a local execution sequence refused this claim at the write seam (ADR-0060).
            return Failure(LocalExecutionSequenceErrors.ManualControlBlocked());
        }

        await transaction.CommitAsync(cancellationToken);

        return Result<ClaimVerificationExecutionCommandResult>.Success(new(execution.Id, execution.ExecutionNumber));
    }

    /// <summary>The one save and commit of a step claim. Cancellation, an exception or a commit error never proves absence: after a
    /// best-effort rollback an independent probe of the exact step decides what is reported, and a step claim is never retried.</summary>
    private async Task<Result<ClaimVerificationExecutionCommandResult>> SaveTicketClaimAsync(
        IDbContextTransaction transaction,
        VerificationExecution execution,
        LocalExecutionStepTicket ticket,
        CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result<ClaimVerificationExecutionCommandResult>.Success(new(execution.Id, execution.ExecutionNumber));
        }
        catch (Exception exception) when (exception is OperationCanceledException or DbUpdateException or DbException)
        {
            await RollbackBestEffortAsync(transaction);
            var durability = localStepProbe is null
                ? LocalExecutionStepProbeResult.Unresolved
                : await ProbeAsync(ticket.StepId);
            return durability switch
            {
                LocalExecutionStepProbeResult.Claimed => Result<ClaimVerificationExecutionCommandResult>.Success(
                    new(execution.Id, execution.ExecutionNumber)),
                LocalExecutionStepProbeResult.Unresolved => Failure(LocalExecutionSequenceErrors.PersistenceUnresolved()),
                _ => Failure(
                    exception is DbUpdateConcurrencyException
                        ? LocalExecutionSequenceErrors.StepAuthorityLost()
                        : LocalExecutionSequenceErrors.PersistenceFailed()),
            };
        }
    }

    private async Task<LocalExecutionStepProbeResult> ProbeAsync(Guid stepId)
    {
        try
        {
            return await localStepProbe!.CheckAsync(stepId, CancellationToken.None);
        }
        catch (Exception)
        {
            return LocalExecutionStepProbeResult.Unresolved;
        }
    }

    private static async Task RollbackBestEffortAsync(IDbContextTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch (Exception)
        {
            // A rollback after an ambiguous or completed commit may throw; the probe, not this, decides what happened.
        }
    }

    private async Task<Run?> ReadSequenceRunAsync(LocalExecutionStepTicket ticket, CancellationToken cancellationToken)
    {
        var runId = await dbContext.LocalExecutionSequences.AsNoTracking()
            .Where(candidate => candidate.Id == ticket.SequenceId)
            .Select(candidate => (Guid?)candidate.RunId)
            .SingleOrDefaultAsync(cancellationToken);
        return runId is null
            ? null
            : await dbContext.Runs.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == runId, cancellationToken);
    }

    /// <summary>A step claim runs exactly the pinned recipe on exactly the step's source checkpoint (the workspace's latest). It checks
    /// the whole pinned set, source and settings early (before the Git capture) and relies on the step's own locked confirmation under
    /// the transaction; the recipe itself is compared by value both times.</summary>
    private async Task<Error?> CheckStepAsync(
        ClaimVerificationExecutionCommand command,
        LocalExecutionStepTicket ticket,
        Run run,
        VerificationCommand recipe,
        GitWorkspace workspace,
        GitCheckpoint checkpoint,
        bool underLock,
        CancellationToken cancellationToken)
    {
        if (ticket.Kind != LocalExecutionStepKind.Verification
            || command.GitCheckpointId != ticket.SourceCheckpointId
            || checkpoint.Id != ticket.SourceCheckpointId)
        {
            return LocalExecutionSequenceErrors.StepInputMismatch();
        }

        var step = await dbContext.LocalExecutionSequenceSteps.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == ticket.StepId, cancellationToken);
        if (step is null
            || step.SequenceId != ticket.SequenceId
            || step.Kind != LocalExecutionStepKind.Verification
            || step.RecipeOrdinal is not { } ordinal
            || step.ReservedOperationId != ticket.ReservedOperationId)
        {
            return LocalExecutionSequenceErrors.StepAuthorityLost();
        }

        var pinned = await dbContext.LocalExecutionSequenceRecipes.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.SequenceId == ticket.SequenceId && candidate.Ordinal == ordinal, cancellationToken);
        if (pinned is null || pinned.VerificationCommandId != command.VerificationCommandId)
        {
            return LocalExecutionSequenceErrors.StepInputMismatch();
        }

        if (!pinned.Matches(recipe))
        {
            return LocalExecutionSequenceErrors.StepSourceChanged();
        }

        var latestCheckpointId = await dbContext.GitCheckpoints.AsNoTracking()
            .Where(candidate => candidate.WorkspaceId == workspace.Id)
            .OrderByDescending(candidate => candidate.CheckpointNumber)
            .Select(candidate => (Guid?)candidate.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (latestCheckpointId != checkpoint.Id)
        {
            return LocalExecutionSequenceErrors.StepSourceChanged();
        }

        return underLock
            ? null
            : await LocalExecutionStepAuthority.CheckEarlyAsync(
                dbContext, ticket, run, LocalExecutionStepKind.Verification, workspace, checkpoint, timeProvider.GetUtcNow(), cancellationToken);
    }

    /// <summary>The project's recipe, latest workspace and selected checkpoint, read untracked and decided by the existing gates in
    /// their existing order: found and owned, enabled, ready with an active lease, and no running verification in the workspace.
    /// The selected checkpoint need only belong to that workspace; it need not be the newest.</summary>
    private async Task<AuthorityRead> ReadAuthorityAsync(
        ClaimVerificationExecutionCommand command, Run? sequenceRun, bool underLock, CancellationToken cancellationToken)
    {
        var projectExists = await dbContext.Projects.AsNoTracking().AnyAsync(candidate => candidate.Id == command.ProjectId, cancellationToken);
        var recipe = await dbContext.VerificationCommands.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.Id == command.VerificationCommandId && candidate.ProjectId == command.ProjectId, cancellationToken);
        var checkpoint = await dbContext.GitCheckpoints.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.Id == command.GitCheckpointId, cancellationToken);
        var workspace = await dbContext.GitWorkspaces
            .AsNoTracking()
            .Where(candidate => candidate.ProjectId == command.ProjectId)
            .OrderByDescending(candidate => candidate.WorkspaceNumber)
            .FirstOrDefaultAsync(cancellationToken);

        if (!projectExists || recipe is null || checkpoint is null || workspace is null || checkpoint.WorkspaceId != workspace.Id)
        {
            return new AuthorityRead(NotFound(), null);
        }

        if (!recipe.IsEnabled)
        {
            return new AuthorityRead(Error.Conflict("verification.disabled", "This verification recipe is disabled."), null);
        }

        if (workspace.Status != WorkspaceStatus.Ready || !await dbContext.RepositoryMutationLeases.AsNoTracking().AnyAsync(
                lease => lease.WorkspaceId == workspace.Id && lease.Status == LeaseStatus.Active, cancellationToken))
        {
            return new AuthorityRead(
                Error.Conflict("verification.workspace_not_ready", "A ready, owned workspace is required to run verification."), null);
        }

        // A planning sequence (ADR-0055) that still holds this workspace refuses verification. This read runs both before the Git capture
        // and again after the claim transaction took the write lock, so the exclusion is decided against locked state.
        if (await PlanningSequenceFence.IsWorkspaceBlockedAsync(dbContext, workspace.Id, cancellationToken))
        {
            return new AuthorityRead(PlanningSequenceErrors.ManualControlBlocked(), null);
        }

        // A local execution sequence (ADR-0060) that still holds this workspace refuses every claim but its own exact step, which
        // instead proves its pinned recipe, source and authority. The ordinary workspace and capacity gates below are never exempted.
        if (command.LocalStep is { } ticket)
        {
            if (await CheckStepAsync(command, ticket, sequenceRun!, recipe, workspace, checkpoint, underLock, cancellationToken) is { } stepError)
            {
                return new AuthorityRead(stepError, null);
            }
        }
        else if (await LocalExecutionSequenceFence.CheckWorkspaceAsync(dbContext, workspace.Id, cancellationToken) is { } localBlocked)
        {
            return new AuthorityRead(localBlocked, null);
        }

        if (await dbContext.VerificationExecutions.AsNoTracking().AnyAsync(
                execution => execution.GitWorkspaceId == workspace.Id && execution.Status == VerificationExecutionStatus.Running,
                cancellationToken))
        {
            return new AuthorityRead(
                Error.Conflict("verification.already_running", "This workspace already has a verification execution in progress."), null);
        }

        // ADR-0057: an Agent attempt, a local-commit reservation, conflicting or untrustworthy ownership also refuses the claim. Like the
        // read above, it runs before the Git capture and again under the claim transaction's write lock.
        if (await WorkspaceExecutionFence.CheckClaimAsync(
            dbContext, command.ProjectId, workspace.Id, cancellationToken, command.LocalStep) is { } busy)
        {
            return new AuthorityRead(busy, null);
        }

        // ADR-0058: a verification command may mutate, so the claim is also one of at most two recorded potentially mutating owners across
        // the database. Like the reads above, it runs before the Git capture and again under the claim transaction's write lock.
        if (await ExecutionCapacityFence.CheckClaimAsync(dbContext, cancellationToken, command.LocalStep) is { } capacityRefusal)
        {
            return new AuthorityRead(capacityRefusal, null);
        }

        return new AuthorityRead(null, new Authority(recipe, workspace, checkpoint));
    }

    private static bool SameRecipe(VerificationCommand left, VerificationCommand right) =>
        left.Id == right.Id
        && left.IsEnabled == right.IsEnabled
        && string.Equals(left.Name, right.Name, StringComparison.Ordinal)
        && string.Equals(left.ExecutablePath, right.ExecutablePath, StringComparison.Ordinal)
        && left.TimeoutSeconds == right.TimeoutSeconds
        && left.Arguments.SequenceEqual(right.Arguments, StringComparer.Ordinal);

    private static Result<ClaimVerificationExecutionCommandResult> Failure(Error error) =>
        Result<ClaimVerificationExecutionCommandResult>.Failure(error);

    private static Error NotFound() =>
        Error.NotFound("verification.not_found", "The verification recipe or source checkpoint was not found for this project.");

    private static Error CheckpointNotCurrent(string description) => Error.Conflict("verification.checkpoint_not_current", description);

    private sealed record Authority(VerificationCommand Recipe, GitWorkspace Workspace, GitCheckpoint Checkpoint);

    private sealed record AuthorityRead(Error? Error, Authority? Authority);
}
