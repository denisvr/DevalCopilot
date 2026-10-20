using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Policies.ExecutionCapacity;

namespace DevalCopilot.Application.Features.Runs.Queries.GetExecutionCapacity;

public sealed class GetExecutionCapacityQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetExecutionCapacityQuery, Result<GetExecutionCapacityQueryResult>>
{
    public async Task<Result<GetExecutionCapacityQueryResult>> HandleAsync(
        GetExecutionCapacityQuery query, CancellationToken cancellationToken)
    {
        var reading = await ExecutionCapacityPolicy.ReadAsync(dbContext, null, cancellationToken);
        return Result<GetExecutionCapacityQueryResult>.Success(reading.Status switch
        {
            ExecutionCapacityStatus.Available => new GetExecutionCapacityQueryResult(
                reading.Status, ExecutionCapacityPolicy.Limit, reading.RecordedOwners, null),
            ExecutionCapacityStatus.AtCapacity => new GetExecutionCapacityQueryResult(
                reading.Status, ExecutionCapacityPolicy.Limit, reading.RecordedOwners, "execution_capacity_limit_reached"),
            _ => new GetExecutionCapacityQueryResult(
                ExecutionCapacityStatus.Unavailable, ExecutionCapacityPolicy.Limit, null, "execution_capacity_unavailable"),
        });
    }
}
