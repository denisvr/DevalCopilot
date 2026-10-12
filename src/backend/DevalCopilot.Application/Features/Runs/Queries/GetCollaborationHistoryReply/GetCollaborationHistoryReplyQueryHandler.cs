using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Queries.GetCollaborationHistory;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetCollaborationHistoryReply;

public sealed class GetCollaborationHistoryReplyQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetCollaborationHistoryReplyQuery, Result<GetCollaborationHistoryReplyQueryResult>>
{
    public async Task<Result<GetCollaborationHistoryReplyQueryResult>> HandleAsync(
        GetCollaborationHistoryReplyQuery query, CancellationToken cancellationToken)
    {
        if (!await dbContext.Runs.AsNoTracking().AnyAsync(run => run.Id == query.RunId, cancellationToken))
        {
            return Result<GetCollaborationHistoryReplyQueryResult>.Failure(GetCollaborationHistoryReplyErrors.NotFound());
        }

        // The source is read by its exact identity within the run: header and reference columns only, never its text.
        var source = await dbContext.CollaborationMessages.AsNoTracking()
            .Where(message => message.RunId == query.RunId && message.Id == query.MessageId)
            .ProjectReference()
            .FirstOrDefaultAsync(cancellationToken);
        if (source is null)
        {
            return Result<GetCollaborationHistoryReplyQueryResult>.Failure(GetCollaborationHistoryReplyErrors.NotFound());
        }

        if (source.Id == Guid.Empty || source.Sequence <= 0)
        {
            return Result<GetCollaborationHistoryReplyQueryResult>.Failure(GetCollaborationHistoryReplyErrors.Unavailable());
        }

        var (status, parent) = await ResolveAsync(query.RunId, source, cancellationToken);
        return Result<GetCollaborationHistoryReplyQueryResult>.Success(
            new GetCollaborationHistoryReplyQueryResult(query.RunId, source.Id, source.Sequence, status, parent));
    }

    private async Task<(CollaborationReplyStatus Status, CollaborationHistoryEntry? Parent)> ResolveAsync(
        Guid runId, CollaborationReplyReference source, CancellationToken cancellationToken)
    {
        var unavailable = (CollaborationReplyStatus.Unavailable, (CollaborationHistoryEntry?)null);
        if (!source.VersionKnown || CollaborationHistoryRows.TypeOf(source.TypeCode) is not { } type)
        {
            return unavailable;
        }

        if (source.InReplyToMessageId is not { } parentId)
        {
            return CollaborationMessageReplyPolicy.EvaluateReference(type, hasReply: false) == CollaborationMessageReplyViolation.None
                ? (CollaborationReplyStatus.None, null)
                : unavailable;
        }

        if (parentId == Guid.Empty || parentId == source.Id)
        {
            return unavailable;
        }

        // At most one parent, by its stored identity: this run, strictly earlier, version 1.0. Nothing else is ever substituted.
        const string version = CollaborationMessage.ProtocolVersionOne;
        var parent = await dbContext.CollaborationMessages.AsNoTracking()
            .Where(message => message.RunId == runId && message.Id == parentId
                && message.Sequence < source.Sequence && message.ProtocolVersion == version)
            .Project()
            .FirstOrDefaultAsync(cancellationToken);
        if (parent is null || parent.Id == Guid.Empty || parent.Sequence <= 0
            || CollaborationHistoryRows.TypeOf(parent.TypeCode) is not { } parentType
            || CollaborationMessageReplyPolicy.Evaluate(type, parentType) != CollaborationMessageReplyViolation.None)
        {
            return unavailable;
        }

        return (CollaborationReplyStatus.Available, CollaborationHistoryRows.ToEntry(runId, parent));
    }
}
