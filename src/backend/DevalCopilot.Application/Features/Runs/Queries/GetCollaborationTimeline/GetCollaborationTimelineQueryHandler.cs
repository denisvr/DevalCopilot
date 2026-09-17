using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetCollaborationTimeline;

public sealed class GetCollaborationTimelineQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetCollaborationTimelineQuery, Result<IReadOnlyList<CollaborationMessageTimelineQueryResult>>>
{
    public const int MaximumResults = 100;

    public async Task<Result<IReadOnlyList<CollaborationMessageTimelineQueryResult>>> HandleAsync(
        GetCollaborationTimelineQuery query,
        CancellationToken cancellationToken)
    {
        var runExists = await dbContext.Runs
            .AsNoTracking()
            .AnyAsync(run => run.Id == query.RunId, cancellationToken);
        if (!runExists)
        {
            return Result<IReadOnlyList<CollaborationMessageTimelineQueryResult>>.Failure(
                Error.NotFound("runs.not_found", "The requested run was not found."));
        }

        var timeline = await dbContext.CollaborationMessages
            .AsNoTracking()
            .Where(message => message.RunId == query.RunId)
            .OrderByDescending(message => message.Sequence)
            .Take(MaximumResults)
            .OrderBy(message => message.Sequence)
            .Select(message => new CollaborationMessageTimelineQueryResult(
                message.Sequence,
                message.Id,
                message.AttemptId,
                message.ProtocolVersion,
                message.Actor,
                message.Recipient,
                message.Type,
                message.InReplyToMessageId,
                message.Summary,
                message.StructuredContentJson,
                message.Provenance,
                message.OccurredAtUtc))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<CollaborationMessageTimelineQueryResult>>.Success(timeline);
    }
}
