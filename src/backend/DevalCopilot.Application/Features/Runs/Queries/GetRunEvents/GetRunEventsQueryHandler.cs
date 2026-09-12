using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetRunEvents;

public sealed class GetRunEventsQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetRunEventsQuery, Result<IReadOnlyList<RunEventQueryResult>>>
{
    public async Task<Result<IReadOnlyList<RunEventQueryResult>>> HandleAsync(
        GetRunEventsQuery query,
        CancellationToken cancellationToken)
    {
        var runExists = await dbContext.Runs
            .AsNoTracking()
            .AnyAsync(run => run.Id == query.RunId, cancellationToken);

        if (!runExists)
        {
            return Result<IReadOnlyList<RunEventQueryResult>>.Failure(
                Error.NotFound("runs.not_found", "The requested run was not found."));
        }

        var events = await dbContext.Events
            .AsNoTracking()
            .Where(runEvent => runEvent.RunId == query.RunId && runEvent.Sequence > query.AfterSequence)
            .OrderBy(runEvent => runEvent.Sequence)
            .Select(runEvent => new RunEventQueryResult(
                runEvent.Sequence,
                runEvent.Id,
                runEvent.AttemptId,
                runEvent.EventType,
                runEvent.Actor,
                runEvent.PayloadJson,
                runEvent.OccurredAtUtc))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<RunEventQueryResult>>.Success(events);
    }
}
