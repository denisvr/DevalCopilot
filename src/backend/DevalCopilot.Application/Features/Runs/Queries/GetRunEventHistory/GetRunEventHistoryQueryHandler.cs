using System.Globalization;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetRunEventHistory;

public sealed class GetRunEventHistoryQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetRunEventHistoryQuery, Result<GetRunEventHistoryQueryResult>>
{
    private const int Absent = 0;
    private const int Unrecognized = 99;

    // The database classifies each actor column and admits an event type only when it is one of the known constants. The stored text
    // is never parsed into an enum and an unknown one is never read, so an unrecognized value keeps its row instead of failing the
    // page. Kind uses 0 for an unknown value; role and provider use 0 for a null column and 99 for a stored value not known here.
    private sealed record HeaderRow(
        long Sequence, Guid Id, string? KnownEventType, int KindCode, int RoleCode, int ProviderCode, DateTimeOffset OccurredAtUtc);

    public async Task<Result<GetRunEventHistoryQueryResult>> HandleAsync(
        GetRunEventHistoryQuery query, CancellationToken cancellationToken)
    {
        if (!await dbContext.Runs.AsNoTracking().AnyAsync(run => run.Id == query.RunId, cancellationToken))
        {
            return Result<GetRunEventHistoryQueryResult>.Failure(RunEventHistoryErrors.NotFound());
        }

        var limit = query.Limit ?? GetRunEventHistoryQuery.DefaultLimit;
        var candidates = dbContext.Events.AsNoTracking().Where(runEvent => runEvent.RunId == query.RunId);
        if (query.BeforeSequence is { } text)
        {
            var before = long.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);
            candidates = candidates.Where(runEvent => runEvent.Sequence < before);
        }

        var known = RunEventHistoryEventTypes.Known;

        // One extra row proves whether older headers remain without a count. The (RunId, Sequence) index makes the descending order
        // total and the exclusive cursor stable while newer events are appended. Only header columns are read: never the payload,
        // the attempt, a message or an artifact, and never the RunEvent entity.
        var rows = await candidates
            .OrderByDescending(runEvent => runEvent.Sequence)
            .Take(limit + 1)
            .Select(runEvent => new HeaderRow(
                runEvent.Sequence,
                runEvent.Id,
                known.Contains(runEvent.EventType) ? runEvent.EventType : null,
                runEvent.ActorKind == ParticipantKind.None ? 1
                    : runEvent.ActorKind == ParticipantKind.Orchestrator ? 2
                    : runEvent.ActorKind == ParticipantKind.Agent ? 3
                    : runEvent.ActorKind == ParticipantKind.Human ? 4
                    : 0,
                runEvent.ActorAgentRole == null ? Absent
                    : runEvent.ActorAgentRole == AgentRole.Planner ? 1
                    : runEvent.ActorAgentRole == AgentRole.CriticalReviewer ? 2
                    : runEvent.ActorAgentRole == AgentRole.Resolver ? 3
                    : runEvent.ActorAgentRole == AgentRole.Implementer ? 4
                    : runEvent.ActorAgentRole == AgentRole.CodeReviewer ? 5
                    : Unrecognized,
                runEvent.ActorAgentProvider == null ? Absent
                    : runEvent.ActorAgentProvider == AgentProvider.Codex ? 1
                    : runEvent.ActorAgentProvider == AgentProvider.ClaudeCode ? 2
                    : Unrecognized,
                runEvent.OccurredAtUtc))
            .ToListAsync(cancellationToken);

        // A page is never partial: one selected header without an identity or a positive sequence makes the whole read unavailable.
        if (rows.Any(row => row.Id == Guid.Empty || row.Sequence <= 0))
        {
            return Result<GetRunEventHistoryQueryResult>.Failure(RunEventHistoryErrors.Unavailable());
        }

        var hasMore = rows.Count > limit;
        var entries = rows
            .Take(limit)
            .Select(row => new RunEventHistoryEntry(
                query.RunId, row.Id, row.Sequence, ToKnownType(row.KnownEventType), ToActor(row), row.OccurredAtUtc))
            .ToList();

        return Result<GetRunEventHistoryQueryResult>.Success(new GetRunEventHistoryQueryResult(
            query.RunId, entries, hasMore, hasMore ? entries[^1].Sequence : null));
    }

    private static string? ToKnownType(string? stored) =>
        RunEventHistoryEventTypes.Known.FirstOrDefault(type => string.Equals(type, stored, StringComparison.Ordinal));

    /// <summary>Mirrors the combinations <see cref="ParticipantIdentity.FromParts"/> accepts, without constructing one: None,
    /// Orchestrator and Human carry neither role nor provider, an Agent requires a known provider and may have no role, and anything
    /// else (an unknown kind, role or provider, or an inconsistent pair) is disclosed only as unrecognized.</summary>
    private static RunEventHistoryActor ToActor(HeaderRow row)
    {
        var unrecognized = new RunEventHistoryActor(null, null, null);
        ParticipantKind? kind = row.KindCode switch
        {
            1 => ParticipantKind.None,
            2 => ParticipantKind.Orchestrator,
            3 => ParticipantKind.Agent,
            4 => ParticipantKind.Human,
            _ => null,
        };

        if (kind is null)
        {
            return unrecognized;
        }

        if (kind != ParticipantKind.Agent)
        {
            return row.RoleCode == Absent && row.ProviderCode == Absent ? new RunEventHistoryActor(kind, null, null) : unrecognized;
        }

        AgentProvider? provider = row.ProviderCode switch
        {
            1 => AgentProvider.Codex,
            2 => AgentProvider.ClaudeCode,
            _ => null,
        };
        AgentRole? role = row.RoleCode switch
        {
            1 => AgentRole.Planner,
            2 => AgentRole.CriticalReviewer,
            3 => AgentRole.Resolver,
            4 => AgentRole.Implementer,
            5 => AgentRole.CodeReviewer,
            _ => null,
        };

        return provider is null || (row.RoleCode != Absent && role is null)
            ? unrecognized
            : new RunEventHistoryActor(kind, role, provider);
    }
}
