using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Policies.AgentAttemptOutputs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptOutputs;

public sealed class GetAgentAttemptOutputsQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetAgentAttemptOutputsQuery, Result<GetAgentAttemptOutputsQueryResult>>
{
    /// <summary>The most associated messages this inspection judges. It bounds this read only: a larger set is reported as unavailable,
    /// never as invalid.</summary>
    public const int MaximumInspectedOutputs = RecordedAgentAttemptOutputSetReader.MaximumInspectedOutputs;

    public async Task<Result<GetAgentAttemptOutputsQueryResult>> HandleAsync(
        GetAgentAttemptOutputsQuery query, CancellationToken cancellationToken)
    {
        var reading = await RecordedAgentAttemptOutputSetReader.ReadAsync(dbContext, query.RunId, query.AttemptId, cancellationToken);
        if (!reading.Found)
        {
            return Result<GetAgentAttemptOutputsQueryResult>.Failure(GetAgentAttemptOutputsErrors.NotFound());
        }

        if (reading.Set is not { } set)
        {
            return Result<GetAgentAttemptOutputsQueryResult>.Success(
                GetAgentAttemptOutputsQueryResult.Unavailable(query.RunId, query.AttemptId));
        }

        var header = new AgentAttemptOutputsHeader(
            set.AttemptId, set.AttemptNumber, set.Identity.Role, set.Identity.Provider, set.Identity.ResponseContract);
        return Result<GetAgentAttemptOutputsQueryResult>.Success(new GetAgentAttemptOutputsQueryResult(
            query.RunId,
            query.AttemptId,
            set.Outputs.Count == 0 ? AgentAttemptOutputsStatus.Empty : AgentAttemptOutputsStatus.Recorded,
            header,
            set.Outputs.Count,
            set.Outputs
                .Select(output => new AgentAttemptOutputReference(
                    output.Position, output.CollaborationMessageId, output.Type, output.CollaborationSequence, output.OccurredAtUtc))
                .ToArray()));
    }
}
