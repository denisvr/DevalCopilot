using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Data;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionQueueInspection;

public sealed class GetLocalExecutionQueueInspectionQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetLocalExecutionQueueInspectionQuery, GetLocalExecutionQueueInspectionQueryResult>
{
    public async Task<GetLocalExecutionQueueInspectionQueryResult> HandleAsync(
        GetLocalExecutionQueueInspectionQuery query, CancellationToken cancellationToken)
    {
        var rows = await LocalExecutionQueueInspectionStatement.For(dbContext).ToArrayAsync(cancellationToken);
        return LocalExecutionQueueInspectionProof.Prove(rows);
    }
}
