using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Policies.ExecutionCapacity;
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
        var ordered = pending.OrderBy(operation => operation.CreatedAtUtc).Select(operation => operation.Id).ToList();

        // Advisory (ADR-0058): an operation the global ceiling would not let execute is not proposed. The single-use execution gate decides
        // again under its own write lock, and nothing here terminalizes, cancels or releases a reservation.
        var identifiers = new List<Guid>(ordered.Count);
        foreach (var operationId in ordered)
        {
            if (await ExecutionCapacityFence.CheckDispatchAsync(
                    dbContext, new ExecutionOwner(ExecutionOwnerKind.LocalCommit, operationId), cancellationToken) is null)
            {
                identifiers.Add(operationId);
            }
        }

        return Result<IReadOnlyList<Guid>>.Success(identifiers);
    }
}
