using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Domain.Features.Projects;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Projects.Commands.ClaimVerificationExecution;

public sealed class ClaimVerificationExecutionCommandHandler(
    IDevalCopilotDbContext dbContext,
    IGitWorkspaceEvidenceReader evidenceReader,
    TimeProvider timeProvider)
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
        var early = await ReadAuthorityAsync(command, cancellationToken);
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

        var fresh = await ReadAuthorityAsync(command, cancellationToken);
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
        var execution = VerificationExecution.Claim(
            Guid.NewGuid(), command.ProjectId, nextNumber - 1, current.Workspace, current.Checkpoint, current.Recipe, timeProvider.GetUtcNow());
        dbContext.VerificationExecutions.Add(execution);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Result<ClaimVerificationExecutionCommandResult>.Success(new(execution.Id, execution.ExecutionNumber));
    }

    /// <summary>The project's recipe, latest workspace and selected checkpoint, read untracked and decided by the existing gates in
    /// their existing order: found and owned, enabled, ready with an active lease, and no running verification in the workspace.
    /// The selected checkpoint need only belong to that workspace; it need not be the newest.</summary>
    private async Task<AuthorityRead> ReadAuthorityAsync(ClaimVerificationExecutionCommand command, CancellationToken cancellationToken)
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

        if (await dbContext.VerificationExecutions.AsNoTracking().AnyAsync(
                execution => execution.GitWorkspaceId == workspace.Id && execution.Status == VerificationExecutionStatus.Running,
                cancellationToken))
        {
            return new AuthorityRead(
                Error.Conflict("verification.already_running", "This workspace already has a verification execution in progress."), null);
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
