using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptHistory;

public sealed class GetAgentAttemptHistoryQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetAgentAttemptHistoryQuery, Result<GetAgentAttemptHistoryQueryResult>>
{
    public async Task<Result<GetAgentAttemptHistoryQueryResult>> HandleAsync(
        GetAgentAttemptHistoryQuery query, CancellationToken cancellationToken)
    {
        if (!await dbContext.Runs.AsNoTracking().AnyAsync(run => run.Id == query.RunId, cancellationToken))
        {
            return Result<GetAgentAttemptHistoryQueryResult>.Failure(
                Error.NotFound("runs.not_found", "The requested run was not found."));
        }

        var candidates = dbContext.Attempts
            .AsNoTracking()
            .Where(attempt => attempt.RunId == query.RunId && attempt.Kind == AttemptKind.Agent);
        if (query.BeforeAttemptNumber is { } before)
        {
            candidates = candidates.Where(attempt => attempt.AttemptNumber < before);
        }

        // One extra row proves whether older attempts remain without a second count query. The
        // (RunId, AttemptNumber) unique index makes the descending order total and the cursor stable.
        // Only non-enum columns are read here, so a row holding an unreadable enum string still
        // occupies its place in the page; its identity is resolved separately below.
        var pageRows = await candidates
            .OrderByDescending(attempt => attempt.AttemptNumber)
            .Take(query.Limit + 1)
            .Select(attempt => new AgentAttemptScalars(
                attempt.Id, attempt.AttemptNumber, attempt.ClaimedAtUtc, attempt.CompletedAtUtc, attempt.AgentDispatchedAtUtc))
            .ToListAsync(cancellationToken);

        var hasMore = pageRows.Count > query.Limit;
        var page = pageRows.Take(query.Limit).ToArray();
        var items = new List<AgentAttemptHistoryEntry>(page.Length);
        foreach (var scalars in page)
        {
            var attempt = await AgentAttemptRead.TryMaterializeAsync(dbContext, scalars.Id, cancellationToken);
            items.Add(ToEntry(scalars, attempt));
        }

        return Result<GetAgentAttemptHistoryQueryResult>.Success(new GetAgentAttemptHistoryQueryResult(
            items, hasMore, hasMore ? items[^1].AttemptNumber : null));
    }

    /// <summary>An unreadable row (null attempt) or one that fails the identity rule is still listed by
    /// number and times, with status only when it was readable and role, provider, contract, and outcome
    /// always withheld.</summary>
    private static AgentAttemptHistoryEntry ToEntry(AgentAttemptScalars scalars, Attempt? attempt)
    {
        var valid = attempt is not null && AgentAttemptIdentity.IsCoherent(attempt);
        return new AgentAttemptHistoryEntry(
            scalars.Id,
            scalars.AttemptNumber,
            attempt?.Status,
            scalars.ClaimedAtUtc,
            scalars.CompletedAtUtc,
            scalars.DispatchedAtUtc,
            valid,
            valid ? attempt!.AgentRole : null,
            valid ? attempt!.AgentProvider : null,
            valid ? attempt!.AgentResponseContract : null,
            valid ? attempt!.AgentOutcome : null);
    }
}
