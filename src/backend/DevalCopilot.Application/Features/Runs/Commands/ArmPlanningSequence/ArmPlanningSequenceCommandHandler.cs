using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.Abandonment;
using DevalCopilot.Application.Features.Runs.Policies.PlanningSequences;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.ArmPlanningSequence;

/// <summary>
/// Arms at most one planning sequence per run for its whole lifetime (ADR-0055). The early reads and the fresh Git capture happen
/// outside any transaction; one short transaction whose first statement is a no-op write (taking the database write lock) then re-reads
/// every authority fact untracked, requires the very tuple the capture was taken against, and saves the sequence with its event. Both
/// directions of the race with manual Agent, verification, local-commit and abandonment admission are decided by that serialization:
/// an admission that won is read here and refuses arming with nothing written, and one that loses is refused by the database guard of
/// the sequence. A refusal writes nothing.
/// </summary>
public sealed class ArmPlanningSequenceCommandHandler(
    IDevalCopilotDbContext dbContext,
    IGitWorkspaceEvidenceReader evidenceReader,
    TimeProvider timeProvider,
    IRunEventNotifier? eventNotifier = null)
    : ICommandHandler<ArmPlanningSequenceCommand, Result<ArmPlanningSequenceCommandResult>>
{
    public async Task<Result<ArmPlanningSequenceCommandResult>> HandleAsync(
        ArmPlanningSequenceCommand command, CancellationToken cancellationToken)
    {
        var run = await dbContext.Runs.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == command.RunId, cancellationToken);
        if (run is null)
        {
            return Failure(PlanningSequenceErrors.NotFound());
        }

        var replay = await ReplayAsync(command, run, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        var early = await ReadAuthorityAsync(command, cancellationToken);
        if (early.Error is not null)
        {
            return Failure(early.Error);
        }

        var evidence = await evidenceReader.CaptureAsync(early.Workspace!.WorkspacePath, cancellationToken);
        if (evidence.Outcome != GitWorkspaceEvidenceOutcome.Success
            || !string.Equals(evidence.FingerprintSha256, command.FingerprintSha256, StringComparison.Ordinal))
        {
            return Failure(PlanningSequenceErrors.TupleStale());
        }

        await using var transaction = await dbContext.BeginTransactionAsync(cancellationToken);

        // The transaction's first statement: a no-op write that takes the database write lock. Everything below is read after it, so
        // it is atomic with the save at the end. Zero rows means the run no longer exists.
        var locked = await dbContext.Runs
            .Where(candidate => candidate.Id == command.RunId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.Lifecycle, candidate => candidate.Lifecycle), cancellationToken);
        if (locked != 1)
        {
            return Failure(PlanningSequenceErrors.NotFound());
        }

        var lockedReplay = await ReplayAsync(command, run, cancellationToken);
        if (lockedReplay is not null)
        {
            return lockedReplay;
        }

        var fresh = await ReadAuthorityAsync(command, cancellationToken);
        if (fresh.Error is not null)
        {
            return Failure(fresh.Error);
        }

        var nowUtc = timeProvider.GetUtcNow();
        var sequence = PlanningSequence.Arm(
            Guid.NewGuid(),
            run.Id,
            run.ProjectId,
            fresh.Workspace!.Id,
            fresh.Checkpoint!.Id,
            fresh.Checkpoint.FingerprintSha256,
            PlanningSequenceObjective.Hash(fresh.Objective!),
            nowUtc);
        dbContext.PlanningSequences.Add(sequence);
        var recorded = PlanningSequenceEvents.Armed(sequence, nowUtc);
        dbContext.Events.Add(recorded);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The database guards (a pristine Created run, a coherent tuple, one sequence per run) are the backstop of the reads
            // above. Whatever they refused, nothing was recorded and the refusal is fixed text.
            return Failure(PlanningSequenceErrors.RunNotPristine());
        }

        await NotifyAsync(run.Id, recorded.Sequence, cancellationToken);
        return Result<ArmPlanningSequenceCommandResult>.Success(ToResult(sequence, replayed: false));
    }

    /// <summary>The run's recorded sequence for the identical tuple (a replay, nothing written), a tuple conflict, or no sequence.</summary>
    private async Task<Result<ArmPlanningSequenceCommandResult>?> ReplayAsync(
        ArmPlanningSequenceCommand command, Run run, CancellationToken cancellationToken)
    {
        var recorded = await dbContext.PlanningSequences.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.RunId == command.RunId, cancellationToken);
        if (recorded is null)
        {
            return null;
        }

        return recorded.WorkspaceId == command.WorkspaceId
            && recorded.CheckpointId == command.CheckpointId
            && string.Equals(recorded.FingerprintSha256, command.FingerprintSha256, StringComparison.Ordinal)
            && string.Equals(recorded.ObjectiveSha256, PlanningSequenceObjective.Hash(run.Objective), StringComparison.Ordinal)
                ? Result<ArmPlanningSequenceCommandResult>.Success(ToResult(recorded, replayed: true))
                : Failure(PlanningSequenceErrors.TupleConflict());
    }

    /// <summary>Every admission fact, decided in a fixed order from untracked reads: the run is a manual Agent run that is Created
    /// and pristine, nothing of its project is active or ambiguous, and the requested tuple is exactly the project's latest ready,
    /// owned workspace with its active lease and latest checkpoint.</summary>
    private async Task<Authority> ReadAuthorityAsync(ArmPlanningSequenceCommand command, CancellationToken cancellationToken)
    {
        var run = await dbContext.Runs.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == command.RunId, cancellationToken);
        if (run is null)
        {
            return Authority.Fail(PlanningSequenceErrors.NotFound());
        }

        if (await CurrentRunExecutionMode.ReadAsync(dbContext, run.Id, cancellationToken) != RunExecutionMode.ManualAgent)
        {
            return Authority.Fail(PlanningSequenceErrors.RunNotManual());
        }

        if (!await IsPristineAsync(run, cancellationToken))
        {
            return Authority.Fail(PlanningSequenceErrors.RunNotPristine());
        }

        if (await RunAbandonmentAuthority.FindBlockerAsync(dbContext, run.ProjectId, cancellationToken) is not null)
        {
            return Authority.Fail(PlanningSequenceErrors.ProjectBusy());
        }

        var workspace = await dbContext.GitWorkspaces.AsNoTracking()
            .Where(candidate => candidate.ProjectId == run.ProjectId)
            .OrderByDescending(candidate => candidate.WorkspaceNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (workspace is null || workspace.Status != WorkspaceStatus.Ready)
        {
            return Authority.Fail(PlanningSequenceErrors.WorkspaceNotReady());
        }

        if (!await dbContext.RepositoryMutationLeases.AsNoTracking().AnyAsync(
                lease => lease.WorkspaceId == workspace.Id && lease.Status == LeaseStatus.Active, cancellationToken))
        {
            return Authority.Fail(PlanningSequenceErrors.LeaseNotActive());
        }

        var checkpoint = await dbContext.GitCheckpoints.AsNoTracking()
            .Where(candidate => candidate.WorkspaceId == workspace.Id)
            .OrderByDescending(candidate => candidate.CheckpointNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (checkpoint is null)
        {
            return Authority.Fail(PlanningSequenceErrors.CheckpointMissing());
        }

        return workspace.Id != command.WorkspaceId
            || checkpoint.Id != command.CheckpointId
            || !string.Equals(checkpoint.FingerprintSha256, command.FingerprintSha256, StringComparison.Ordinal)
                ? Authority.Fail(PlanningSequenceErrors.TupleStale())
                : new Authority(null, workspace, checkpoint, run.Objective);
    }

    private async Task<bool> IsPristineAsync(Run run, CancellationToken cancellationToken) =>
        run.Lifecycle == RunLifecycle.Created
        && !await dbContext.Attempts.AsNoTracking().AnyAsync(candidate => candidate.RunId == run.Id, cancellationToken)
        && !await dbContext.CollaborationMessages.AsNoTracking().AnyAsync(candidate => candidate.RunId == run.Id, cancellationToken)
        && !await dbContext.LocalCommitOperations.AsNoTracking().AnyAsync(candidate => candidate.RunId == run.Id, cancellationToken);

    private static ArmPlanningSequenceCommandResult ToResult(PlanningSequence sequence, bool replayed) => new(
        sequence.Id,
        sequence.RunId,
        sequence.WorkspaceId,
        sequence.CheckpointId,
        sequence.FingerprintSha256,
        sequence.State,
        sequence.ArmedAtUtc,
        sequence.Revision,
        replayed);

    private static Result<ArmPlanningSequenceCommandResult> Failure(Error error) =>
        Result<ArmPlanningSequenceCommandResult>.Failure(error);

    /// <summary>Run-advance notifications are only hints (ADR-0003): a notifier failure after the commit never turns the recorded
    /// arming into a reported failure. Cancellation still propagates.</summary>
    private async Task NotifyAsync(Guid runId, long sequence, CancellationToken cancellationToken)
    {
        if (eventNotifier is null)
        {
            return;
        }

        try
        {
            await eventNotifier.NotifyRunAdvancedAsync(runId, sequence, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The event is durable; a later cursor read delivers it.
        }
    }

    private sealed record Authority(Error? Error, GitWorkspace? Workspace, GitCheckpoint? Checkpoint, string? Objective)
    {
        public static Authority Fail(Error error) => new(error, null, null, null);
    }
}
