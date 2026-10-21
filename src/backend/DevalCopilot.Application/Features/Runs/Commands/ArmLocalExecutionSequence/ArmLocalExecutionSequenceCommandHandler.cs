using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.ArmLocalExecutionSequence;

/// <summary>
/// Arms at most one local execution sequence per run for its whole lifetime (ADR-0060). The early reads and the fresh Git capture
/// happen outside any transaction; one short transaction whose first statement is a no-op write (taking the database write lock) then
/// re-reads every authority fact untracked, requires the very candidate the confirmation was taken against, and saves the sequence, its
/// pinned recipes and its event atomically. Reciprocal manual Agent, verification, planning, abandonment, recipe and requested-setting
/// races are decided by that serialization and by the database guards. A refusal writes nothing.
/// </summary>
public sealed class ArmLocalExecutionSequenceCommandHandler(
    IDevalCopilotDbContext dbContext,
    IGitWorkspaceEvidenceReader evidenceReader,
    TimeProvider timeProvider,
    IRunEventNotifier? eventNotifier = null)
    : ICommandHandler<ArmLocalExecutionSequenceCommand, Result<ArmLocalExecutionSequenceCommandResult>>
{
    public async Task<Result<ArmLocalExecutionSequenceCommandResult>> HandleAsync(
        ArmLocalExecutionSequenceCommand command, CancellationToken cancellationToken)
    {
        if (!await dbContext.Runs.AsNoTracking().AnyAsync(candidate => candidate.Id == command.RunId, cancellationToken))
        {
            return Failure(LocalExecutionSequenceErrors.NotFound());
        }

        var replay = await ReplayAsync(command, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        var early = await LocalExecutionCandidateReader.ReadAsync(dbContext, command.RunId, command.PlanProposalMessageId, cancellationToken);
        var earlyError = Compare(command, early);
        if (earlyError is not null)
        {
            return Failure(earlyError);
        }

        var evidence = await evidenceReader.CaptureAsync(early.Workspace!.WorkspacePath, cancellationToken);
        if (evidence.Outcome != GitWorkspaceEvidenceOutcome.Success
            || !string.Equals(evidence.FingerprintSha256, command.FingerprintSha256, StringComparison.Ordinal))
        {
            return Failure(LocalExecutionSequenceErrors.TupleStale());
        }

        await using var transaction = await dbContext.BeginTransactionAsync(cancellationToken);

        // The transaction's first statement: a no-op write that takes the database write lock. Everything below is read after it, so it
        // is atomic with the save at the end. Zero rows means the run no longer exists.
        var locked = await dbContext.Runs
            .Where(candidate => candidate.Id == command.RunId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.Lifecycle, candidate => candidate.Lifecycle), cancellationToken);
        if (locked != 1)
        {
            return Failure(LocalExecutionSequenceErrors.NotFound());
        }

        var lockedReplay = await ReplayAsync(command, cancellationToken);
        if (lockedReplay is not null)
        {
            return lockedReplay;
        }

        var fresh = await LocalExecutionCandidateReader.ReadAsync(dbContext, command.RunId, command.PlanProposalMessageId, cancellationToken);
        var freshError = Compare(command, fresh);
        if (freshError is not null)
        {
            return Failure(freshError);
        }

        var candidate = fresh.Candidate!;
        var nowUtc = timeProvider.GetUtcNow();
        var sequence = LocalExecutionSequence.Arm(
            Guid.NewGuid(),
            candidate.RunId,
            candidate.ProjectId,
            candidate.WorkspaceId,
            candidate.CheckpointId,
            candidate.FingerprintSha256,
            candidate.PlanProposalMessageId,
            candidate.PlanAuthority,
            candidate.PlanInputsSha256,
            command.ConfirmationSha256,
            candidate.SettingsSnapshot,
            candidate.Recipes.Count,
            candidate.CorrectionAllowance,
            nowUtc);
        dbContext.LocalExecutionSequences.Add(sequence);
        foreach (var pin in candidate.Recipes)
        {
            dbContext.LocalExecutionSequenceRecipes.Add(LocalExecutionSequenceRecipe.Pin(
                Guid.NewGuid(), sequence.Id, pin.Ordinal, await ReadCommandAsync(pin.CommandId, cancellationToken)));
        }

        var recorded = LocalExecutionSequenceEvents.Armed(sequence, nowUtc);
        dbContext.Events.Add(recorded);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The database guards (a running run with no mutating claim, a coherent tuple, one sequence per run) are the backstop of the
            // reads above. Whatever they refused, nothing was recorded and the refusal is fixed text.
            return Failure(LocalExecutionSequenceErrors.PriorMutation());
        }

        await NotifyAsync(sequence.RunId, recorded.Sequence, cancellationToken);
        return Result<ArmLocalExecutionSequenceCommandResult>.Success(ToResult(sequence, replayed: false));
    }

    /// <summary>The run's recorded sequence for the identical normalized tuple (a replay, nothing written), a tuple conflict, or none.</summary>
    private async Task<Result<ArmLocalExecutionSequenceCommandResult>?> ReplayAsync(
        ArmLocalExecutionSequenceCommand command, CancellationToken cancellationToken)
    {
        var recorded = await dbContext.LocalExecutionSequences.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.RunId == command.RunId, cancellationToken);
        if (recorded is null)
        {
            return null;
        }

        return recorded.PlanProposalMessageId == command.PlanProposalMessageId
            && recorded.WorkspaceId == command.WorkspaceId
            && recorded.CheckpointId == command.CheckpointId
            && string.Equals(recorded.FingerprintSha256, command.FingerprintSha256, StringComparison.Ordinal)
            && string.Equals(recorded.ConfirmationSha256, command.ConfirmationSha256, StringComparison.Ordinal)
                ? Result<ArmLocalExecutionSequenceCommandResult>.Success(ToResult(recorded, replayed: true))
                : Failure(LocalExecutionSequenceErrors.TupleConflict());
    }

    /// <summary>The first refusal of the read, or of the requested tuple and confirmation against the candidate that was just read.</summary>
    private static Error? Compare(ArmLocalExecutionSequenceCommand command, LocalExecutionCandidateReader.Reading reading)
    {
        if (reading.Error is not null)
        {
            return reading.Error;
        }

        var candidate = reading.Candidate!;
        if (candidate.WorkspaceId != command.WorkspaceId
            || candidate.CheckpointId != command.CheckpointId
            || !string.Equals(candidate.FingerprintSha256, command.FingerprintSha256, StringComparison.Ordinal))
        {
            return LocalExecutionSequenceErrors.TupleStale();
        }

        return string.Equals(candidate.ConfirmationSha256, command.ConfirmationSha256, StringComparison.Ordinal)
            ? null
            : LocalExecutionSequenceErrors.ConfirmationStale();
    }

    private async Task<DevalCopilot.Domain.Features.Projects.VerificationCommand> ReadCommandAsync(Guid commandId, CancellationToken cancellationToken) =>
        await dbContext.VerificationCommands.AsNoTracking().SingleAsync(candidate => candidate.Id == commandId, cancellationToken);

    private static ArmLocalExecutionSequenceCommandResult ToResult(LocalExecutionSequence sequence, bool replayed) => new(
        sequence.Id,
        sequence.RunId,
        sequence.PlanProposalMessageId,
        sequence.WorkspaceId,
        sequence.CheckpointId,
        sequence.FingerprintSha256,
        sequence.ConfirmationSha256,
        sequence.State,
        sequence.RecipeCount,
        sequence.CorrectionAllowance,
        sequence.ArmedAtUtc,
        sequence.AdvancementDeadlineUtc,
        sequence.Revision,
        replayed);

    private static Result<ArmLocalExecutionSequenceCommandResult> Failure(Error error) =>
        Result<ArmLocalExecutionSequenceCommandResult>.Failure(error);

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
}
