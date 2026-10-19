using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Policies.AgentAttemptOutputs;
using DevalCopilot.Application.Features.Runs.Queries.GetCollaborationHistory;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptOutputMessage;

public sealed class GetAgentAttemptOutputMessageQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetAgentAttemptOutputMessageQuery, Result<GetAgentAttemptOutputMessageQueryResult>>
{
    public async Task<Result<GetAgentAttemptOutputMessageQueryResult>> HandleAsync(
        GetAgentAttemptOutputMessageQuery query, CancellationToken cancellationToken)
    {
        // The whole bounded set is proved again for every read, before any summary or content is selected.
        var reading = await RecordedAgentAttemptOutputSetReader.ReadAsync(dbContext, query.RunId, query.AttemptId, cancellationToken);
        if (!reading.Found)
        {
            return Result<GetAgentAttemptOutputMessageQueryResult>.Failure(GetAgentAttemptOutputMessageErrors.NotFound());
        }

        var unavailable = Result<GetAgentAttemptOutputMessageQueryResult>.Success(
            GetAgentAttemptOutputMessageQueryResult.Unavailable(query.RunId, query.AttemptId, query.MessageId));
        if (reading.Set is not { } set)
        {
            return unavailable;
        }

        // Membership of the proved set only; an empty set has none. Nothing is filtered to make a member appear.
        var selected = set.Outputs.FirstOrDefault(output => output.CollaborationMessageId == query.MessageId);
        if (selected is null)
        {
            return Result<GetAgentAttemptOutputMessageQueryResult>.Failure(GetAgentAttemptOutputMessageErrors.NotFound());
        }

        // Exactly that message, still associated with this attempt in this run, through the unchanged guarded history projection.
        var row = await dbContext.CollaborationMessages.AsNoTracking()
            .Where(message => message.RunId == query.RunId
                && message.AttemptId == query.AttemptId
                && message.Id == selected.CollaborationMessageId)
            .Project()
            .FirstOrDefaultAsync(cancellationToken);

        // The reread header, provenance and actor must be the admitted reference and attempt; a disagreement is never repaired.
        if (row is null
            || row.Id != selected.CollaborationMessageId
            || !row.VersionKnown
            || CollaborationHistoryRows.TypeOf(row.TypeCode) != selected.Type
            || row.Sequence != selected.CollaborationSequence
            || !row.OccurredAtUtc.EqualsExact(selected.OccurredAtUtc)
            || !RecordedAgentAttemptOutputSetReader.IsProviderObservedAuthor(
                row.ProvenanceCode, row.ActorKindCode, row.ActorRoleCode, row.ActorProviderCode, set.Identity))
        {
            return unavailable;
        }

        return Result<GetAgentAttemptOutputMessageQueryResult>.Success(new GetAgentAttemptOutputMessageQueryResult(
            query.RunId,
            query.AttemptId,
            query.MessageId,
            AgentAttemptOutputMessageStatus.Recorded,
            new AgentAttemptOutputMessageHeader(
                set.AttemptId, set.AttemptNumber, set.Identity.Role, set.Identity.Provider, set.Identity.ResponseContract),
            new AgentAttemptOutputMessageReference(
                selected.Position, selected.CollaborationMessageId, selected.Type, selected.CollaborationSequence, selected.OccurredAtUtc),
            CollaborationHistoryRows.ToEntry(query.RunId, row)));
    }
}
