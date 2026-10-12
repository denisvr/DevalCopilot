using System.Globalization;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetCollaborationHistory;

public sealed class GetCollaborationHistoryQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetCollaborationHistoryQuery, Result<GetCollaborationHistoryQueryResult>>
{
    public async Task<Result<GetCollaborationHistoryQueryResult>> HandleAsync(
        GetCollaborationHistoryQuery query, CancellationToken cancellationToken)
    {
        if (!await dbContext.Runs.AsNoTracking().AnyAsync(run => run.Id == query.RunId, cancellationToken))
        {
            return Result<GetCollaborationHistoryQueryResult>.Failure(CollaborationHistoryErrors.NotFound());
        }

        var limit = query.Limit ?? GetCollaborationHistoryQuery.DefaultLimit;
        var candidates = dbContext.CollaborationMessages.AsNoTracking().Where(message => message.RunId == query.RunId);
        if (query.BeforeSequence is { } text)
        {
            var before = long.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);
            candidates = candidates.Where(message => message.Sequence < before);
        }

        // One extra row proves whether older messages remain without a count. The (RunId, Sequence) index makes the descending order
        // total and the exclusive cursor stable while newer messages are appended.
        var rows = await candidates
            .OrderByDescending(message => message.Sequence)
            .Take(limit + 1)
            .Project()
            .ToListAsync(cancellationToken);

        // A page is never partial: one selected row without an identity or a positive sequence makes the whole read unavailable.
        if (rows.Any(row => row.Id == Guid.Empty || row.Sequence <= 0))
        {
            return Result<GetCollaborationHistoryQueryResult>.Failure(CollaborationHistoryErrors.Unavailable());
        }

        var hasMore = rows.Count > limit;
        var entries = rows.Take(limit).Select(row => CollaborationHistoryRows.ToEntry(query.RunId, row)).ToList();

        return Result<GetCollaborationHistoryQueryResult>.Success(new GetCollaborationHistoryQueryResult(
            query.RunId, entries, hasMore, hasMore ? entries[^1].Sequence : null));
    }
}
