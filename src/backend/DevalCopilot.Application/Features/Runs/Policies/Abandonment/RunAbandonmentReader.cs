using System.Text.Json;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.Abandonment;

/// <summary>
/// The one fresh, untracked reading of recorded abandonment facts (ADR-0031), shared by intake, replay arbitration, the project
/// summary and the read-only status so their answers cannot drift. A reading is made of the Abandoned runs it selects only; the
/// coherence verdict is <see cref="RunAbandonmentPolicy.IsCoherent"/>, never a second rule. Participants are compared in the database,
/// the event payload is parsed defensively and the recorded abandonment time comes through its tolerant column mapping (a malformed
/// stored time reads as null), so a malformed stored row is reported incoherent and never throws or disturbs another run's reading.
/// </summary>
internal static class RunAbandonmentReader
{
    public static async Task<RunAbandonmentReading?> ReadAsync(IDevalCopilotDbContext dbContext, Guid runId, CancellationToken cancellationToken) =>
        (await ReadAsync(dbContext, dbContext.Runs.Where(run => run.Id == runId), cancellationToken)).SingleOrDefault();

    public static Task<IReadOnlyList<RunAbandonmentReading>> ReadForProjectAsync(
        IDevalCopilotDbContext dbContext, Guid projectId, CancellationToken cancellationToken) =>
        ReadAsync(dbContext, dbContext.Runs.Where(run => run.ProjectId == projectId), cancellationToken);

    public static Task<IReadOnlyList<RunAbandonmentReading>> ReadAllAsync(IDevalCopilotDbContext dbContext, CancellationToken cancellationToken) =>
        ReadAsync(dbContext, dbContext.Runs, cancellationToken);

    private static async Task<IReadOnlyList<RunAbandonmentReading>> ReadAsync(
        IDevalCopilotDbContext dbContext, IQueryable<Run> scope, CancellationToken cancellationToken)
    {
        var runs = await scope.AsNoTracking()
            .Where(run => run.Lifecycle == RunLifecycle.Abandoned)
            .Select(run => new
            {
                run.Id,
                run.ProjectId,
                run.ExecutionNumber,
                StoredMode = EF.Property<string>(run, Run.ExecutionModeStorageProperty),
                run.AbandonmentReason,
                run.AbandonedAtUtc,
                run.LastAdvancedAtUtc,
                NoActiveParticipant = run.ActiveParticipantKind == ParticipantKind.None
                    && run.ActiveAgentRole == null
                    && run.ActiveAgentProvider == null,
            })
            .ToListAsync(cancellationToken);
        if (runs.Count == 0)
        {
            return [];
        }

        var runIds = runs.Select(run => run.Id).ToList();
        var events = await dbContext.Events.AsNoTracking()
            .Where(runEvent => runIds.Contains(runEvent.RunId) && runEvent.EventType == RunEventType.RunAbandoned)
            .Select(runEvent => new
            {
                runEvent.RunId,
                HumanAndRunScoped = runEvent.ActorKind == ParticipantKind.Human
                    && runEvent.ActorAgentRole == null
                    && runEvent.ActorAgentProvider == null
                    && runEvent.AttemptId == null,
                runEvent.PayloadJson,
                runEvent.OccurredAtUtc,
            })
            .ToListAsync(cancellationToken);
        var eventsByRun = events.ToLookup(runEvent => runEvent.RunId);

        return runs
            .Select(run =>
            {
                var facts = new RunAbandonmentFacts(
                    RunLifecycle.Abandoned,
                    RunExecutionModeStorage.Read(run.StoredMode),
                    run.AbandonmentReason,
                    run.AbandonedAtUtc,
                    run.LastAdvancedAtUtc,
                    run.NoActiveParticipant,
                    eventsByRun[run.Id]
                        .Select(runEvent => new RunAbandonmentEventFacts(
                            runEvent.HumanAndRunScoped, ReadReason(runEvent.PayloadJson), runEvent.OccurredAtUtc))
                        .ToList());

                return new RunAbandonmentReading(
                    run.Id, run.ProjectId, run.ExecutionNumber, run.AbandonmentReason, run.AbandonedAtUtc,
                    RunAbandonmentPolicy.IsCoherent(facts));
            })
            .ToList();
    }

    /// <summary>The payload of the one abandonment event: <c>{"reason": text}</c>; anything else is no reason at all.</summary>
    internal static string? ReadReason(string payloadJson)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("reason", out var reason)
                && reason.ValueKind == JsonValueKind.String
                    ? reason.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
