using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionOwnershipReleases;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.ReleaseLocalExecutionOwnership;

/// <summary>
/// Records the Human release of one terminal local execution operation's retained quarantine (ADR-0065). One short write-locked
/// transaction whose first statement is a no-op write on the run row takes the database write lock; the candidate is then re-read
/// untracked with the same single statement the protected read uses and its complete chain is re-proved from fresh facts, so a claim, a
/// dispatch, a restart or a concurrent identical release that committed first is read here. Only then are the one immutable release
/// receipt and the one Human run event appended, atomically. No process, Git, filesystem, provider or other I/O happens under the lock.
///
/// <para>
/// A stale, mismatched or expired confirmation is refused without retargeting; ineligible and unavailable histories are refused with their
/// fixed codes; an identical normalized request replays the coherent receipt (also after the confirmation expired) with no write and no
/// event, and any other request after a release conflicts. The release changes no outcome, verification pass, artifact, sequence,
/// admission, workspace, lease, approval or budget; it only stops the original operation from owning its workspace and global mutating
/// capacity, so already authorized work may pass its own fresh gates afterwards.
/// </para>
/// </summary>
public sealed class ReleaseLocalExecutionOwnershipCommandHandler(
    IDevalCopilotDbContext dbContext, TimeProvider timeProvider, IRunEventNotifier? eventNotifier = null)
    : ICommandHandler<ReleaseLocalExecutionOwnershipCommand, Result<ReleaseLocalExecutionOwnershipCommandResult>>
{
    public async Task<Result<ReleaseLocalExecutionOwnershipCommandResult>> HandleAsync(
        ReleaseLocalExecutionOwnershipCommand command, CancellationToken cancellationToken)
    {
        // A foreign or unknown step is one fixed non-disclosing refusal, before anything takes the write lock.
        var known = await dbContext.LocalExecutionSequenceSteps.AsNoTracking().AnyAsync(
            candidate => candidate.Id == command.StepId
                && dbContext.LocalExecutionSequences.Any(sequence => sequence.Id == candidate.SequenceId && sequence.RunId == command.RunId),
            cancellationToken);
        if (!known)
        {
            return Failure(LocalExecutionOwnershipReleaseErrors.NotFound());
        }

        await using var transaction = await dbContext.BeginTransactionAsync(cancellationToken);

        // The run row, never a sequence or step row: an ended sequence and its steps are immutable by database guard.
        var locked = await dbContext.Runs
            .Where(candidate => candidate.Id == command.RunId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.Lifecycle, candidate => candidate.Lifecycle), cancellationToken);
        if (locked != 1)
        {
            return Failure(LocalExecutionOwnershipReleaseErrors.NotFound());
        }

        LocalExecutionReleaseReading reading;
        try
        {
            reading = await LocalExecutionReleaseReader.ReadAsync(dbContext, command.RunId, command.StepId, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException)
        {
            reading = LocalExecutionReleaseReading.Unavailable(LocalExecutionReleaseReason.EvidenceIncoherent);
        }

        switch (reading.Kind)
        {
            case LocalExecutionReleaseReadingKind.NotFound:
                return Failure(LocalExecutionOwnershipReleaseErrors.NotFound());

            case LocalExecutionReleaseReadingKind.Released:
                return IsReplay(command, reading)
                    ? Success(command, reading.Facts!, reading.Recorded!.ReleaseId, reading.Recorded.ReleasedAtUtc, reading.Recorded.EventSequence)
                    : Failure(LocalExecutionOwnershipReleaseErrors.ConfirmationConflict());

            case LocalExecutionReleaseReadingKind.NotEligible:
                return Failure(LocalExecutionOwnershipReleaseErrors.NotEligible());

            case LocalExecutionReleaseReadingKind.Unavailable:
                return Failure(LocalExecutionOwnershipReleaseErrors.Unavailable());
        }

        var nowUtc = timeProvider.GetUtcNow();
        var facts = reading.Facts!;
        if (command.ObservationId != facts.ObservationId
            || command.ConfirmationVersion != LocalExecutionOwnershipReleaseConfirmation.CurrentVersion
            || !LocalExecutionOwnershipReleaseConfirmation.IsLive(command.ConfirmationExpiresAtUtc, nowUtc)
            || !string.Equals(
                command.ConfirmationSha256,
                LocalExecutionOwnershipReleaseConfirmation.Digest(facts, command.ConfirmationExpiresAtUtc),
                StringComparison.Ordinal))
        {
            return Failure(LocalExecutionOwnershipReleaseErrors.ConfirmationStale());
        }

        var witness = await dbContext.LocalExecutionDispatchWitnesses.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == facts.WitnessId, cancellationToken);
        var observation = await dbContext.LocalExecutionTerminationObservations.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == facts.ObservationId, cancellationToken);

        var releaseId = Guid.NewGuid();
        var runEvent = LocalExecutionSequenceEvents.OwnershipReleased(facts, releaseId, nowUtc);
        LocalExecutionOwnershipRelease release;
        try
        {
            release = LocalExecutionOwnershipRelease.Record(
                releaseId,
                witness,
                observation,
                runEvent.Id,
                nowUtc,
                command.ConfirmationVersion,
                command.ConfirmationSha256,
                command.ConfirmationExpiresAtUtc,
                facts);
        }
        catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException)
        {
            return Failure(LocalExecutionOwnershipReleaseErrors.ConfirmationStale());
        }

        dbContext.Events.Add(runEvent);
        dbContext.LocalExecutionOwnershipReleases.Add(release);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Nothing was committed: a competing writer or a database guard refused this exact change.
            return Failure(LocalExecutionOwnershipReleaseErrors.PersistenceFailed());
        }

        try
        {
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Whether the commit became durable is unknown: nothing is rolled back by inference and nothing is resubmitted. A fresh
            // explicit read decides.
            return Failure(LocalExecutionOwnershipReleaseErrors.PersistenceUnresolved());
        }

        await NotifyAsync(command.RunId, runEvent.Sequence);
        return Success(command, facts, release.Id, release.ReleasedAtUtc, runEvent.Sequence);
    }

    /// <summary>An identical normalized request: the same observation and the exact confirmation (version, digest and the expiry shown)
    /// the recorded release was made under. Valid after expiry; anything else conflicts.</summary>
    private static bool IsReplay(ReleaseLocalExecutionOwnershipCommand command, LocalExecutionReleaseReading reading) =>
        command.ObservationId == reading.Facts!.ObservationId
        && command.ConfirmationVersion == reading.Recorded!.ConfirmationVersion
        && string.Equals(command.ConfirmationSha256, reading.Recorded.ConfirmationSha256, StringComparison.Ordinal)
        && command.ConfirmationExpiresAtUtc == reading.Recorded.ConfirmationExpiresAtUtc;

    private static Result<ReleaseLocalExecutionOwnershipCommandResult> Success(
        ReleaseLocalExecutionOwnershipCommand command,
        LocalExecutionOwnershipReleaseFacts facts,
        Guid releaseId,
        DateTimeOffset releasedAtUtc,
        long eventSequence) =>
        Result<ReleaseLocalExecutionOwnershipCommandResult>.Success(
            new ReleaseLocalExecutionOwnershipCommandResult(command.RunId, command.StepId, facts, releaseId, releasedAtUtc, eventSequence));

    private static Result<ReleaseLocalExecutionOwnershipCommandResult> Failure(Error error) =>
        Result<ReleaseLocalExecutionOwnershipCommandResult>.Failure(error);

    /// <summary>Run-advance notifications are only hints (ADR-0003): a notifier failure after the commit never turns the recorded release
    /// into a reported failure. The event is durable; a later cursor read delivers it.</summary>
    private async Task NotifyAsync(Guid runId, long sequence)
    {
        if (eventNotifier is null)
        {
            return;
        }

        try
        {
            await eventNotifier.NotifyRunAdvancedAsync(runId, sequence, CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
        }
    }
}
