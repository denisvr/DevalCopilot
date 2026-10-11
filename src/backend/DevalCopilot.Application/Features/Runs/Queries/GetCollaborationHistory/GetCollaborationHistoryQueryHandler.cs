using System.Globalization;
using System.Text.Json;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetCollaborationHistory;

public sealed class GetCollaborationHistoryQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetCollaborationHistoryQuery, Result<GetCollaborationHistoryQueryResult>>
{
    private const int Absent = 0;
    private const int Unrecognized = 99;

    // The database classifies every stored enum column and bounds the two text columns, so an unknown value is never parsed into an
    // enum and an oversized summary or content JSON is never read: the text columns come back null past the Domain bounds. Kind,
    // type and provenance use 0 for an unknown value; role and provider use 0 for a null column and 99 for a stored value not known.
    private sealed record MessageRow(
        long Sequence,
        Guid Id,
        DateTimeOffset OccurredAtUtc,
        bool VersionKnown,
        int TypeCode,
        int ProvenanceCode,
        int ActorKindCode,
        int ActorRoleCode,
        int ActorProviderCode,
        int RecipientKindCode,
        int RecipientRoleCode,
        int RecipientProviderCode,
        string? Summary,
        string? StructuredContentJson);

    private static readonly CollaborationMessageType[] TypesByCode =
    [
        CollaborationMessageType.Proposal,
        CollaborationMessageType.Acceptance,
        CollaborationMessageType.Challenge,
        CollaborationMessageType.Question,
        CollaborationMessageType.Decision,
        CollaborationMessageType.ExecutionReport,
        CollaborationMessageType.ReviewFinding,
        CollaborationMessageType.RevisionResponse,
        CollaborationMessageType.Escalation,
        CollaborationMessageType.ReviewApproval,
        CollaborationMessageType.HumanInstruction,
    ];

    public async Task<Result<GetCollaborationHistoryQueryResult>> HandleAsync(
        GetCollaborationHistoryQuery query, CancellationToken cancellationToken)
    {
        if (!await dbContext.Runs.AsNoTracking().AnyAsync(run => run.Id == query.RunId, cancellationToken))
        {
            return Result<GetCollaborationHistoryQueryResult>.Failure(CollaborationHistoryErrors.NotFound());
        }

        var limit = query.Limit ?? GetCollaborationHistoryQuery.DefaultLimit;
        var candidates = dbContext.CollaborationMessages.AsNoTracking().Where(message => message.RunId == query.RunId);
        if (query.BeforeSequence is { } text)
        {
            var before = long.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);
            candidates = candidates.Where(message => message.Sequence < before);
        }

        const string version = CollaborationMessage.ProtocolVersionOne;
        // length() of a TEXT value stops at an embedded NUL, so the guards bound the stored bytes (CAST AS BLOB) instead. A UTF-16 unit is at
        // most three UTF-8 bytes, so a value within the Domain character bounds always fits; the exact bounds are applied again in memory.
        const int summaryByteBound = CollaborationMessageContentPolicy.MaximumSummaryLength * 3;
        const int contentByteBound = CollaborationMessageContentPolicy.MaximumStructuredContentLength * 3;

        // One extra row proves whether older messages remain without a count. The (RunId, Sequence) index makes the descending order
        // total and the exclusive cursor stable while newer messages are appended. Only header columns and the two bounded text
        // columns are read: never the entity, the attempt, a reply target or an artifact.
        var rows = await candidates
            .OrderByDescending(message => message.Sequence)
            .Take(limit + 1)
            .Select(message => new MessageRow(
                message.Sequence,
                message.Id,
                message.OccurredAtUtc,
                message.ProtocolVersion == version,
                message.Type == CollaborationMessageType.Proposal ? 1
                    : message.Type == CollaborationMessageType.Acceptance ? 2
                    : message.Type == CollaborationMessageType.Challenge ? 3
                    : message.Type == CollaborationMessageType.Question ? 4
                    : message.Type == CollaborationMessageType.Decision ? 5
                    : message.Type == CollaborationMessageType.ExecutionReport ? 6
                    : message.Type == CollaborationMessageType.ReviewFinding ? 7
                    : message.Type == CollaborationMessageType.RevisionResponse ? 8
                    : message.Type == CollaborationMessageType.Escalation ? 9
                    : message.Type == CollaborationMessageType.ReviewApproval ? 10
                    : message.Type == CollaborationMessageType.HumanInstruction ? 11
                    : 0,
                message.Provenance == CollaborationMessageProvenance.Simulated ? 1
                    : message.Provenance == CollaborationMessageProvenance.ProviderObserved ? 2
                    : message.Provenance == CollaborationMessageProvenance.HostConstructed ? 3
                    : message.Provenance == CollaborationMessageProvenance.HumanSubmitted ? 4
                    : 0,
                message.ActorKind == ParticipantKind.None ? 1
                    : message.ActorKind == ParticipantKind.Orchestrator ? 2
                    : message.ActorKind == ParticipantKind.Agent ? 3
                    : message.ActorKind == ParticipantKind.Human ? 4
                    : 0,
                message.ActorAgentRole == null ? Absent
                    : message.ActorAgentRole == AgentRole.Planner ? 1
                    : message.ActorAgentRole == AgentRole.CriticalReviewer ? 2
                    : message.ActorAgentRole == AgentRole.Resolver ? 3
                    : message.ActorAgentRole == AgentRole.Implementer ? 4
                    : message.ActorAgentRole == AgentRole.CodeReviewer ? 5
                    : Unrecognized,
                message.ActorAgentProvider == null ? Absent
                    : message.ActorAgentProvider == AgentProvider.Codex ? 1
                    : message.ActorAgentProvider == AgentProvider.ClaudeCode ? 2
                    : Unrecognized,
                message.RecipientKind == ParticipantKind.None ? 1
                    : message.RecipientKind == ParticipantKind.Orchestrator ? 2
                    : message.RecipientKind == ParticipantKind.Agent ? 3
                    : message.RecipientKind == ParticipantKind.Human ? 4
                    : 0,
                message.RecipientAgentRole == null ? Absent
                    : message.RecipientAgentRole == AgentRole.Planner ? 1
                    : message.RecipientAgentRole == AgentRole.CriticalReviewer ? 2
                    : message.RecipientAgentRole == AgentRole.Resolver ? 3
                    : message.RecipientAgentRole == AgentRole.Implementer ? 4
                    : message.RecipientAgentRole == AgentRole.CodeReviewer ? 5
                    : Unrecognized,
                message.RecipientAgentProvider == null ? Absent
                    : message.RecipientAgentProvider == AgentProvider.Codex ? 1
                    : message.RecipientAgentProvider == AgentProvider.ClaudeCode ? 2
                    : Unrecognized,
                ((byte[])(object)message.Summary).Length <= summaryByteBound ? message.Summary : null,
                ((byte[])(object)message.StructuredContentJson).Length <= contentByteBound ? message.StructuredContentJson : null))
            .ToListAsync(cancellationToken);

        // A page is never partial: one selected row without an identity or a positive sequence makes the whole read unavailable.
        if (rows.Any(row => row.Id == Guid.Empty || row.Sequence <= 0))
        {
            return Result<GetCollaborationHistoryQueryResult>.Failure(CollaborationHistoryErrors.Unavailable());
        }

        var hasMore = rows.Count > limit;
        var entries = rows.Take(limit).Select(row => ToEntry(query.RunId, row)).ToList();

        return Result<GetCollaborationHistoryQueryResult>.Success(new GetCollaborationHistoryQueryResult(
            query.RunId, entries, hasMore, hasMore ? entries[^1].Sequence : null));
    }

    private static CollaborationHistoryEntry ToEntry(Guid runId, MessageRow row)
    {
        CollaborationMessageType? type = row.TypeCode is >= 1 and <= 11 ? TypesByCode[row.TypeCode - 1] : null;
        CollaborationMessageProvenance? provenance = row.ProvenanceCode switch
        {
            1 => CollaborationMessageProvenance.Simulated,
            2 => CollaborationMessageProvenance.ProviderObserved,
            3 => CollaborationMessageProvenance.HostConstructed,
            4 => CollaborationMessageProvenance.HumanSubmitted,
            _ => null,
        };
        var actor = ToParticipant(row.ActorKindCode, row.ActorRoleCode, row.ActorProviderCode);
        var recipient = ToParticipant(row.RecipientKindCode, row.RecipientRoleCode, row.RecipientProviderCode);
        var version = row.VersionKnown ? CollaborationMessage.ProtocolVersionOne : null;

        var fields = type is { } knownType && version is not null && provenance is { } source
            && AreCoherent(actor, recipient, source)
            ? TryReadContent(knownType, row.Summary, row.StructuredContentJson)
            : null;

        return new CollaborationHistoryEntry(
            runId, row.Id, row.Sequence, row.OccurredAtUtc, version, type?.ToString(), provenance?.ToString(), actor, recipient,
            fields is not null, fields is null ? null : row.Summary, fields ?? []);
    }

    /// <summary>Mirrors the combinations <see cref="ParticipantIdentity.FromParts"/> accepts, without constructing one. A message never
    /// carries a <c>None</c> participant, so that and anything else (an unknown kind, role or provider, or an inconsistent pair) is
    /// disclosed only as unrecognized.</summary>
    private static CollaborationHistoryParticipant ToParticipant(int kindCode, int roleCode, int providerCode)
    {
        var unrecognized = new CollaborationHistoryParticipant(null, null, null);
        switch (kindCode)
        {
            case 2:
            case 4:
                return roleCode == Absent && providerCode == Absent
                    ? new CollaborationHistoryParticipant(
                        kindCode == 2 ? ParticipantKind.Orchestrator : ParticipantKind.Human, null, null)
                    : unrecognized;
            case 3:
                AgentProvider? provider = providerCode switch
                {
                    1 => AgentProvider.Codex,
                    2 => AgentProvider.ClaudeCode,
                    _ => null,
                };
                AgentRole? role = roleCode switch
                {
                    1 => AgentRole.Planner,
                    2 => AgentRole.CriticalReviewer,
                    3 => AgentRole.Resolver,
                    4 => AgentRole.Implementer,
                    5 => AgentRole.CodeReviewer,
                    _ => null,
                };
                return provider is null || (roleCode != Absent && role is null)
                    ? unrecognized
                    : new CollaborationHistoryParticipant(ParticipantKind.Agent, role, provider);
            default:
                return unrecognized;
        }
    }

    /// <summary>The same envelope coherence the Domain factories enforce when a message is recorded, stated from the disclosures: both
    /// participants recognized, distinct, and consistent with how the message entered the ledger.</summary>
    private static bool AreCoherent(
        CollaborationHistoryParticipant actor, CollaborationHistoryParticipant recipient, CollaborationMessageProvenance provenance)
    {
        if (actor.Kind is not { } actorKind || recipient.Kind is null || actor == recipient)
        {
            return false;
        }

        return provenance switch
        {
            CollaborationMessageProvenance.ProviderObserved => actorKind == ParticipantKind.Agent && actor.Role.HasValue,
            CollaborationMessageProvenance.HostConstructed => actorKind == ParticipantKind.Orchestrator,
            CollaborationMessageProvenance.HumanSubmitted => actorKind == ParticipantKind.Human,
            _ => true,
        };
    }

    /// <summary>Returns the validated fields, or null when the content is not admitted. The lengths are checked again before parsing,
    /// then the existing Domain policy decides; only its own expected content failures (an <see cref="ArgumentException"/>) make a
    /// row unavailable, so any other fault still surfaces instead of being masked.</summary>
    private static List<CollaborationHistoryField>? TryReadContent(
        CollaborationMessageType type, string? summary, string? structuredContentJson)
    {
        if (summary is null || structuredContentJson is null
            || summary.Length > CollaborationMessageContentPolicy.MaximumSummaryLength
            || structuredContentJson.Length > CollaborationMessageContentPolicy.MaximumStructuredContentLength
            || !CollaborationMessageContentPolicy.IsSafeSummary(summary))
        {
            return null;
        }

        try
        {
            CollaborationMessageContentPolicy.Validate(type, structuredContentJson);
        }
        catch (ArgumentException)
        {
            return null;
        }

        using var document = JsonDocument.Parse(structuredContentJson);
        return document.RootElement.EnumerateObject()
            .Select(property => new CollaborationHistoryField(property.Name, property.Value.GetString()!))
            .ToList();
    }
}
