using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Policies.AgentAttemptInputs;
using DevalCopilot.Application.Features.Runs.Queries.GetCollaborationHistory;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptInputMessage;

public sealed class GetAgentAttemptInputMessageQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetAgentAttemptInputMessageQuery, Result<GetAgentAttemptInputMessageQueryResult>>
{
    public async Task<Result<GetAgentAttemptInputMessageQueryResult>> HandleAsync(
        GetAgentAttemptInputMessageQuery query, CancellationToken cancellationToken)
    {
        // The whole bounded set is proved again for every read, before any summary or content is selected.
        var reading = await RecordedAgentAttemptInputSetReader.ReadAsync(dbContext, query.RunId, query.AttemptId, cancellationToken);
        if (!reading.Found)
        {
            return Result<GetAgentAttemptInputMessageQueryResult>.Failure(GetAgentAttemptInputMessageErrors.NotFound());
        }

        var unavailable = Result<GetAgentAttemptInputMessageQueryResult>.Success(
            GetAgentAttemptInputMessageQueryResult.Unavailable(query.RunId, query.AttemptId, query.MessageId));
        if (reading.Set is not { } set)
        {
            return unavailable;
        }

        // Membership of the proved set only; an empty Planner set has none. Nothing is filtered to make a member appear.
        var selected = set.Inputs.FirstOrDefault(input => input.CollaborationMessageId == query.MessageId);
        if (selected is null)
        {
            return Result<GetAgentAttemptInputMessageQueryResult>.Failure(GetAgentAttemptInputMessageErrors.NotFound());
        }

        // Exactly that message of this run, through the unchanged guarded history projection and admission.
        var row = await dbContext.CollaborationMessages.AsNoTracking()
            .Where(message => message.RunId == query.RunId && message.Id == selected.CollaborationMessageId)
            .Project()
            .FirstOrDefaultAsync(cancellationToken);

        // The reread header must be the admitted reference; a disagreement is never substituted or repaired.
        if (row is null
            || row.Id != selected.CollaborationMessageId
            || !row.VersionKnown
            || CollaborationHistoryRows.TypeOf(row.TypeCode) != selected.Type
            || row.Sequence != selected.CollaborationSequence
            || !row.OccurredAtUtc.EqualsExact(selected.OccurredAtUtc))
        {
            return unavailable;
        }

        return Result<GetAgentAttemptInputMessageQueryResult>.Success(new GetAgentAttemptInputMessageQueryResult(
            query.RunId,
            query.AttemptId,
            query.MessageId,
            AgentAttemptInputMessageStatus.Recorded,
            new AgentAttemptInputMessageHeader(
                set.AttemptId, set.AttemptNumber, set.Identity.Role, set.Identity.Provider, set.Identity.ResponseContract),
            new AgentAttemptInputMessageReference(
                selected.Position, selected.CollaborationMessageId, selected.Type, selected.CollaborationSequence, selected.OccurredAtUtc),
            CollaborationHistoryRows.ToEntry(query.RunId, row)));
    }
}
