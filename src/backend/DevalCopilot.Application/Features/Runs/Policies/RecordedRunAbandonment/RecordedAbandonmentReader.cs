using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Policies.Abandonment;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.RecordedRunAbandonment;

/// <summary>
/// Proves the exact recorded abandonment of one run from one bounded scalar statement (ADR-0071): the registered run and its registered
/// project are read with a left join to the run's exact stored-key <c>run.abandoned</c> events and at most two rows are materialized
/// (none, one candidate or detected excess). Absence is decided only by a recognized non-Abandoned run with both abandonment columns SQL
/// NULL and no associated event row, never by a failure to parse an identity, a time or a payload, so present but damaged (or doubled)
/// evidence is Unavailable and never NotRecorded. Recorded requires exactly one admitted event and the unchanged
/// <see cref="RunAbandonmentPolicy.IsCoherent"/> over the facts the shared <see cref="RunAbandonmentReader.ReadReason"/> interpretation
/// of the payload yields; the rule and the interpretation are not repeated here. Current eligibility, workspaces, leases, the live
/// status, Git, files, processes and providers are never consulted. It is unsigned consistency of the facts read now, writes nothing
/// and swallows no fault.
/// </summary>
internal static class RecordedAbandonmentReader
{
    private const int AbandonedLifecycleCode = 6;

    public static async Task<RecordedAbandonmentReading> ReadAsync(
        IDevalCopilotDbContext dbContext, Guid runId, CancellationToken cancellationToken)
    {
        var rows = await (
            from run in dbContext.Runs.AsNoTracking()
            where run.Id == runId
            join project in dbContext.Projects.AsNoTracking() on run.ProjectId equals project.Id
            join abandoned in dbContext.Events.AsNoTracking().Where(runEvent => runEvent.EventType == RunEventType.RunAbandoned)
                on run.Id equals abandoned.RunId into abandonedGroup
            from abandoned in abandonedGroup.DefaultIfEmpty()
            select new RecordedAbandonmentJoin
            {
                Run = run,
                Project = project,
                AbandonedEvent = abandoned,
            })
            .Project()
            .Take(2)
            .ToArrayAsync(cancellationToken);

        if (rows.Length == 0)
        {
            return RecordedAbandonmentReading.NotFound;
        }

        // A second associated event is excess evidence, never a choice between two.
        return rows.Length > 1 ? RecordedAbandonmentReading.Unavailable : Classify(rows[0], runId);
    }

    private static RecordedAbandonmentReading Classify(RecordedAbandonmentRow row, Guid runId)
    {
        var projectId = RecordedAbandonmentStorage.ParseIdentifier(row.ProjectId);
        var mode = RunExecutionModeStorage.Read(row.StoredMode);
        if (RecordedAbandonmentStorage.ParseIdentifier(row.RunId) != runId
            || RecordedAbandonmentStorage.ParseIdentifier(row.RunProjectId) != projectId
            || projectId is not { } project
            || row.LifecycleCode == 0
            || mode == RunExecutionModeStorage.Unrecognized)
        {
            return RecordedAbandonmentReading.Unavailable;
        }

        if (!row.EventPresent)
        {
            return row.LifecycleCode != AbandonedLifecycleCode && !row.ReasonPresent && !row.AbandonedAtPresent
                ? RecordedAbandonmentReading.NotRecorded
                : RecordedAbandonmentReading.Unavailable;
        }

        return Prove(row, runId, project, mode) is { } abandonment
            ? RecordedAbandonmentReading.Recorded(abandonment)
            : RecordedAbandonmentReading.Unavailable;
    }

    private static RecordedAbandonment? Prove(RecordedAbandonmentRow row, Guid runId, Guid project, RunExecutionMode mode)
    {
        var reason = RecordedAbandonmentStorage.ParseText(row.Reason);
        var lastAdvanced = RecordedAbandonmentStorage.ParseTime(row.LastAdvancedAtUtc);
        var eventId = RecordedAbandonmentStorage.ParseIdentifier(row.EventId);
        var payload = RecordedAbandonmentStorage.ParseText(row.EventPayload);
        var occurredAt = RecordedAbandonmentStorage.ParseTime(row.EventOccurredAtUtc);
        if (row.LifecycleCode != AbandonedLifecycleCode
            || !row.ReasonPresent
            || reason is null
            || !row.AbandonedAtPresent
            || row.AbandonedAtUtc is not { } abandonedAt
            || lastAdvanced is not { } advancedAt
            || eventId is not { } supportingEvent
            || row.EventSequence is not { } sequence
            || RecordedAbandonmentStorage.ParseIdentifier(row.EventRunId) != runId
            || payload is null
            || occurredAt is not { } eventAt)
        {
            return null;
        }

        var facts = new RunAbandonmentFacts(
            RunLifecycle.Abandoned,
            mode,
            reason,
            abandonedAt,
            advancedAt,
            row.NoActiveParticipant,
            [new RunAbandonmentEventFacts(row.EventHumanAndRunScoped, ReadReason(payload), eventAt)]);
        return RunAbandonmentPolicy.IsCoherent(facts)
            ? new RecordedAbandonment(project, reason, abandonedAt, supportingEvent, sequence)
            : null;
    }

    // The shared interpretation reports malformed JSON as no reason but lets a lone-surrogate escape throw an InvalidOperationException;
    // here that one payload fault is the same unreadable payload, never a failure of the read. Nothing else is caught.
    private static string? ReadReason(string payload)
    {
        try
        {
            return RunAbandonmentReader.ReadReason(payload);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
