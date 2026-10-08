using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetEligibleLocalCommitOperations;

public sealed class GetEligibleLocalCommitOperationsQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetEligibleLocalCommitOperationsQuery, Result<IReadOnlyList<Guid>>>
{
    public async Task<Result<IReadOnlyList<Guid>>> HandleAsync(
        GetEligibleLocalCommitOperationsQuery query, CancellationToken cancellationToken)
    {
        // SQLite cannot order by DateTimeOffset, so the few pending operations are ordered after they are read.
        var pending = await dbContext.LocalCommitOperations.AsNoTracking()
            .Where(operation => operation.Status == LocalCommitStatus.Prepared)
            .Select(operation => new { operation.Id, operation.CreatedAtUtc })
            .ToListAsync(cancellationToken);
        IReadOnlyList<Guid> identifiers = pending.OrderBy(operation => operation.CreatedAtUtc).Select(operation => operation.Id).ToList();
        return Result<IReadOnlyList<Guid>>.Success(identifiers);
    }
}
