using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Policies.AgentAttemptInputs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptInputs;

public sealed class GetAgentAttemptInputsQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetAgentAttemptInputsQuery, Result<GetAgentAttemptInputsQueryResult>>
{
    /// <summary>The most input rows this inspection judges. It bounds this read only: it is not a limit on claims, stored inputs or the
    /// message-linked reader, and a larger set is reported as unavailable, never as invalid.</summary>
    public const int MaximumInspectedInputs = RecordedAgentAttemptInputSetReader.MaximumInspectedInputs;

    public async Task<Result<GetAgentAttemptInputsQueryResult>> HandleAsync(
        GetAgentAttemptInputsQuery query, CancellationToken cancellationToken)
    {
        var reading = await RecordedAgentAttemptInputSetReader.ReadAsync(dbContext, query.RunId, query.AttemptId, cancellationToken);
        if (!reading.Found)
        {
            return Result<GetAgentAttemptInputsQueryResult>.Failure(GetAgentAttemptInputsErrors.NotFound());
        }

        if (reading.Set is not { } set)
        {
            return Result<GetAgentAttemptInputsQueryResult>.Success(
                GetAgentAttemptInputsQueryResult.Unavailable(query.RunId, query.AttemptId));
        }

        var header = new AgentAttemptInputsHeader(
            set.AttemptId, set.AttemptNumber, set.Identity.Role, set.Identity.Provider, set.Identity.ResponseContract);
        if (set.Inputs.Count == 0)
        {
            return Result<GetAgentAttemptInputsQueryResult>.Success(new GetAgentAttemptInputsQueryResult(
                query.RunId, query.AttemptId, AgentAttemptInputsStatus.Empty, header, [], 0, false));
        }

        // The whole set was proved before this prefix is taken; the validator admitted the limit, which is never clamped here.
        var limit = query.Limit ?? GetAgentAttemptInputsQuery.DefaultLimit;
        return Result<GetAgentAttemptInputsQueryResult>.Success(new GetAgentAttemptInputsQueryResult(
            query.RunId,
            query.AttemptId,
            AgentAttemptInputsStatus.Recorded,
            header,
            set.Inputs
                .Take(limit)
                .Select(input => new AgentAttemptInputReference(
                    input.Position, input.CollaborationMessageId, input.Type, input.CollaborationSequence, input.OccurredAtUtc))
                .ToArray(),
            set.Inputs.Count,
            set.Inputs.Count > limit));
    }
}
