using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.ObserveLocalExecutionQueue;

/// <summary>
/// The coordinator's bounded observation of waiting queued cycles (ADR-0062), one finite keyset page at a time. The page and its
/// high-water are read by Int64 coordination number, never by an in-memory sort of an unbounded set, so an old waiting set cannot starve
/// the observation of later live records: the host finishes the finite range fixed at the sweep's start and refreshes it on wrap. The
/// decisions are those of <see cref="LocalExecutionQueueEvaluator"/> under the write lock; only an actual transition (an ended cycle or a
/// changed waiting reason) is recorded, never a refresh on an unchanged tick.
/// </summary>
public sealed class ObserveLocalExecutionQueueCommandHandler(
    IDevalCopilotDbContext dbContext, TimeProvider timeProvider, IRunEventNotifier? eventNotifier = null)
    : ICommandHandler<ObserveLocalExecutionQueueCommand, Result<ObserveLocalExecutionQueueCommandResult>>
{
    public async Task<Result<ObserveLocalExecutionQueueCommandResult>> HandleAsync(
        ObserveLocalExecutionQueueCommand command, CancellationToken cancellationToken)
    {
        var waiting = dbContext.LocalExecutionSequences.AsNoTracking()
            .Where(candidate => candidate.State == LocalExecutionSequenceState.WaitingForAdmission
                && candidate.AdmissionMode == LocalExecutionAdmissionMode.WaitForAdmission);
        var highWater = command.HighWaterCoordinationNumber
            ?? await waiting.MaxAsync(candidate => (long?)candidate.CoordinationNumber, cancellationToken)
            ?? command.AfterCoordinationNumber;
        var after = command.AfterCoordinationNumber;

        // One more than a page proves whether the finite range has anything left without reading it.
        var page = await waiting
            .Where(candidate => candidate.CoordinationNumber > after && candidate.CoordinationNumber <= highWater)
            .OrderBy(candidate => candidate.CoordinationNumber)
            .Take(LocalExecutionSequencePolicy.ObservationPageSize + 1)
            .Select(candidate => new { candidate.Id, candidate.RunId, candidate.CoordinationNumber })
            .ToListAsync(cancellationToken);
        var more = page.Count > LocalExecutionSequencePolicy.ObservationPageSize;
        var visited = page.Take(LocalExecutionSequencePolicy.ObservationPageSize).ToList();
        if (visited.Count == 0)
        {
            return Success(after, highWater, finished: true, 0, 0);
        }

        var last = visited[^1].CoordinationNumber;
        await using var transaction = await dbContext.BeginTransactionAsync(cancellationToken);

        // The run row, never the sequence row: an ended sequence is immutable by database guard, and even a no-op write to it is refused.
        var firstRun = visited[0].RunId;
        var locked = await dbContext.Runs
            .Where(candidate => candidate.Id == firstRun)
            .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.Lifecycle, candidate => candidate.Lifecycle), cancellationToken);
        if (locked != 1)
        {
            return Success(last, highWater, !more, visited.Count, 0);
        }

        var nowUtc = timeProvider.GetUtcNow();
        var evaluation = await LocalExecutionQueueEvaluator.EvaluateAsync(
            dbContext, nowUtc, command.PreparationWorkerAvailable, cancellationToken);
        var pageIds = visited.Select(candidate => candidate.Id).ToHashSet();
        var events = new List<RunEvent>();
        foreach (var item in evaluation.Items.Where(candidate => pageIds.Contains(candidate.Sequence.Id)))
        {
            switch (item.Verdict.Kind)
            {
                case LocalExecutionQueueEvaluator.VerdictKind.Ended:
                    item.Admission.Retire(item.Verdict.EndReason!, nowUtc);
                    item.Sequence.End(LocalExecutionSequenceState.NeedsAttention, item.Verdict.EndReason!, detailCode: null, nowUtc);
                    events.Add(LocalExecutionSequenceEvents.Ended(item.Sequence, nowUtc));
                    break;

                case LocalExecutionQueueEvaluator.VerdictKind.Waiting:
                    if (item.Admission.RecordWaitingReason(item.Verdict.WaitingReason!, nowUtc))
                    {
                        events.Add(LocalExecutionSequenceEvents.AdmissionWaiting(item.Sequence, item.Admission, nowUtc));
                    }

                    break;
            }
        }

        if (events.Count == 0)
        {
            return Success(last, highWater, !more, visited.Count, 0);
        }

        dbContext.Events.AddRange(events);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The revisions are the compare-and-set: a competing writer that committed after this decision read is never overwritten.
            return Result<ObserveLocalExecutionQueueCommandResult>.Failure(LocalExecutionSequenceErrors.PersistenceFailed());
        }

        foreach (var runId in events.Select(recorded => recorded.RunId).Distinct())
        {
            await NotifyAsync(runId, events.Where(recorded => recorded.RunId == runId).Max(recorded => recorded.Sequence), cancellationToken);
        }

        return Success(last, highWater, !more, visited.Count, events.Count);
    }

    private static Result<ObserveLocalExecutionQueueCommandResult> Success(
        long last, long highWater, bool finished, int visited, int changed) =>
        Result<ObserveLocalExecutionQueueCommandResult>.Success(
            new ObserveLocalExecutionQueueCommandResult(last, highWater, finished, visited, changed));

    /// <summary>Run-advance notifications are only hints (ADR-0003): a notifier failure after the commit never turns the recorded
    /// transition into a reported failure. Cancellation still propagates.</summary>
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
