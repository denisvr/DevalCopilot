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
        return Result<GetExecutionCapacityQueryResult>.Success(
            new GetExecutionCapacityQueryResult(
                reading.Status, reading.Limit, reading.RecordedOwners, ExecutionCapacityPolicy.ReasonCodeOf(reading.Status)));
    }
}
