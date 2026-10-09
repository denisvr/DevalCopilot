using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Errors;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalDeliveryReceipt;

public sealed class GetLocalDeliveryReceiptQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetLocalDeliveryReceiptQuery, Result<GetLocalDeliveryReceiptQueryResult>>
{
    public async Task<Result<GetLocalDeliveryReceiptQueryResult>> HandleAsync(
        GetLocalDeliveryReceiptQuery query, CancellationToken cancellationToken)
    {
        var run = await dbContext.Runs.AsNoTracking()
            .Where(candidate => candidate.Id == query.RunId)
            .Select(candidate => new { candidate.ProjectId, candidate.Objective })
            .SingleOrDefaultAsync(cancellationToken);
        if (run is null)
        {
            return Result<GetLocalDeliveryReceiptQueryResult>.Failure(LocalCommitErrors.RunNotFound());
        }

        return Result<GetLocalDeliveryReceiptQueryResult>.Success(
            await new LocalDeliveryReceiptReader(dbContext).ReadAsync(query.RunId, run.ProjectId, run.Objective, cancellationToken));
    }
}
