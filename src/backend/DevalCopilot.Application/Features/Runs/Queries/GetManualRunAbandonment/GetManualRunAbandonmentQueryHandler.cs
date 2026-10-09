using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.Abandonment;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetManualRunAbandonment;

public sealed class GetManualRunAbandonmentQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetManualRunAbandonmentQuery, Result<GetManualRunAbandonmentQueryResult>>
{
    public async Task<Result<GetManualRunAbandonmentQueryResult>> HandleAsync(
        GetManualRunAbandonmentQuery query, CancellationToken cancellationToken)
    {
        var projectId = await dbContext.Runs.AsNoTracking()
            .Where(candidate => candidate.Id == query.RunId)
            .Select(candidate => (Guid?)candidate.ProjectId)
            .SingleOrDefaultAsync(cancellationToken);
        if (projectId is not { } owningProjectId)
        {
            return Result<GetManualRunAbandonmentQueryResult>.Failure(RunAbandonmentErrors.RunNotFound());
        }

        var latestSequence = await dbContext.Events.AsNoTracking()
            .Where(runEvent => runEvent.RunId == query.RunId)
            .MaxAsync(runEvent => (long?)runEvent.Sequence, cancellationToken) ?? 0;

        if (await CurrentRunExecutionMode.ReadAsync(dbContext, query.RunId, cancellationToken) != RunExecutionMode.ManualAgent)
        {
            return Refused(RunAbandonmentErrors.RunNotManualCode, latestSequence);
        }

        if (await dbContext.Runs.AsNoTracking().AnyAsync(
                candidate => candidate.Id == query.RunId && candidate.Lifecycle == RunLifecycle.Abandoned, cancellationToken))
        {
            var reading = await RunAbandonmentReader.ReadAsync(dbContext, query.RunId, cancellationToken);
            return reading is { Coherent: true, Reason: { } reason, AbandonedAtUtc: { } abandonedAtUtc }
                ? Result<GetManualRunAbandonmentQueryResult>.Success(new(
                    false, RunAbandonmentErrors.AlreadyAbandonedCode, new ManualRunAbandonmentView(reason, abandonedAtUtc), latestSequence))
                : Refused(RunAbandonmentErrors.AbandonmentIncoherentCode, latestSequence);
        }

        if (!await dbContext.Runs.AsNoTracking().AnyAsync(
                candidate => candidate.Id == query.RunId
                    && (candidate.Lifecycle == RunLifecycle.Created || candidate.Lifecycle == RunLifecycle.Running),
                cancellationToken))
        {
            return Refused(RunAbandonmentErrors.RunNotAbandonableCode, latestSequence);
        }

        var blocker = await RunAbandonmentAuthority.FindBlockerAsync(dbContext, owningProjectId, cancellationToken);
        return blocker is null
            ? Result<GetManualRunAbandonmentQueryResult>.Success(new(true, null, null, latestSequence))
            : Refused(blocker.Code, latestSequence);
    }

    private static Result<GetManualRunAbandonmentQueryResult> Refused(string code, long latestSequence) =>
        Result<GetManualRunAbandonmentQueryResult>.Success(new(false, code, null, latestSequence));
}
