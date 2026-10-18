using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptInputs;

public sealed class GetAgentAttemptInputsQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetAgentAttemptInputsQuery, Result<GetAgentAttemptInputsQueryResult>>
{
    /// <summary>The most input rows this inspection judges. It bounds this read only: it is not a limit on claims, stored inputs or the
    /// message-linked reader, and a larger set is reported as unavailable, never as invalid.</summary>
    public const int MaximumInspectedInputs = AgentAttemptInputsProjection.PositionBound;

    /// <summary>The most references one answer shows, in stored order, after the whole inspected set has been proved coherent.</summary>
    public const int MaximumShownInputs = 10;

    public async Task<Result<GetAgentAttemptInputsQueryResult>> HandleAsync(
        GetAgentAttemptInputsQuery query, CancellationToken cancellationToken)
    {
        // Exactly this attempt of this run, as a bounded scalar projection; an unknown run, a missing or foreign attempt and a non-Agent
        // attempt are the same answer.
        var attempt = await dbContext.Attempts.AsNoTracking()
            .Where(candidate => candidate.RunId == query.RunId && candidate.Id == query.AttemptId)
            .ProjectScalarIdentity()
            .FirstOrDefaultAsync(cancellationToken);
        if (attempt is null || !attempt.IsAgent)
        {
            return Result<GetAgentAttemptInputsQueryResult>.Failure(GetAgentAttemptInputsErrors.NotFound());
        }

        var unavailable = Result<GetAgentAttemptInputsQueryResult>.Success(
            GetAgentAttemptInputsQueryResult.Unavailable(query.RunId, query.AttemptId));
        var identity = ScalarAgentAttemptIdentity.Evaluate(attempt);
        if (identity is null)
        {
            return unavailable;
        }

        var header = new AgentAttemptInputsHeader(
            attempt.Id, attempt.AttemptNumber, identity.Role, identity.Provider, identity.ResponseContract);

        // One more row than can be inspected, in stored order through the (AttemptId, Sequence) index: observing it is enough to refuse.
        var inputs = await dbContext.AttemptInputMessages.AsNoTracking()
            .Where(input => input.AttemptId == query.AttemptId)
            .OrderBy(input => input.Sequence)
            .Take(MaximumInspectedInputs + 1)
            .ProjectInput()
            .ToArrayAsync(cancellationToken);
        if (inputs.Length > MaximumInspectedInputs)
        {
            return unavailable;
        }

        // Only a Planner starts from no prior collaboration fact; every other role records at least one input, so zero rows for it may
        // be a lost set and is never shown as an innocent empty one.
        if (inputs.Length == 0)
        {
            return identity.Role == AgentRole.Planner
                ? Result<GetAgentAttemptInputsQueryResult>.Success(new GetAgentAttemptInputsQueryResult(
                    query.RunId, query.AttemptId, AgentAttemptInputsStatus.Empty, header, [], 0, false))
                : unavailable;
        }

        // The whole set is judged before anything is shown: gapless positions from zero and unique, nonempty references.
        var referenced = new HashSet<Guid>();
        for (var index = 0; index < inputs.Length; index++)
        {
            if (inputs[index].Position != index
                || inputs[index].CollaborationMessageId == Guid.Empty
                || !referenced.Add(inputs[index].CollaborationMessageId))
            {
                return unavailable;
            }
        }

        // At most the referenced identities, inside this run only; message identities are unique, so this is at most 64 rows. A missing
        // or foreign reference is never omitted or replaced.
        var referencedIds = referenced.ToArray();
        var messages = await dbContext.CollaborationMessages.AsNoTracking()
            .Where(message => message.RunId == query.RunId && referencedIds.Contains(message.Id))
            .ProjectReferencedMessage()
            .ToArrayAsync(cancellationToken);
        var messagesById = new Dictionary<Guid, AgentAttemptReferencedMessageRow>();
        foreach (var message in messages)
        {
            if (!messagesById.TryAdd(message.Id, message))
            {
                return unavailable;
            }
        }

        var references = new AgentAttemptInputReference[inputs.Length];
        for (var index = 0; index < inputs.Length; index++)
        {
            if (!messagesById.TryGetValue(inputs[index].CollaborationMessageId, out var message)
                || !message.VersionKnown
                || message.TypeCode is < 1 or > 11
                || message.Sequence <= 0)
            {
                return unavailable;
            }

            references[index] = new AgentAttemptInputReference(
                index,
                message.Id,
                AgentAttemptInputsProjection.TypesByCode[message.TypeCode - 1],
                message.Sequence,
                message.OccurredAtUtc);
        }

        return Result<GetAgentAttemptInputsQueryResult>.Success(new GetAgentAttemptInputsQueryResult(
            query.RunId,
            query.AttemptId,
            AgentAttemptInputsStatus.Recorded,
            header,
            references.Take(MaximumShownInputs).ToArray(),
            references.Length,
            references.Length > MaximumShownInputs));
    }
}
