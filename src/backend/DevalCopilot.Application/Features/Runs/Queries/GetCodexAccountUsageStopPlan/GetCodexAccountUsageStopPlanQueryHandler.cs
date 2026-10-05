using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetCodexAccountUsageStopPlan;

public sealed class GetCodexAccountUsageStopPlanQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetCodexAccountUsageStopPlanQuery, GetCodexAccountUsageStopPlanQueryResult>
{
    public async Task<GetCodexAccountUsageStopPlanQueryResult> HandleAsync(GetCodexAccountUsageStopPlanQuery query, CancellationToken cancellationToken)
    {
        // An attempt that cannot be read, is not a Codex Agent attempt of this run, or has no snapshot has nothing to guard here; the
        // dispatch gate stays authoritative for every attempt.
        var row = await dbContext.Attempts.AsNoTracking()
            .Where(candidate => candidate.Id == query.AttemptId && candidate.RunId == query.RunId)
            .Select(candidate => new
            {
                candidate.Kind,
                candidate.AgentProvider,
                Stored = EF.Property<string?>(candidate, Attempt.AgentCodexAccountUsageStopStorageProperty),
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null || row.Kind != AttemptKind.Agent || row.AgentProvider != AgentProvider.Codex)
        {
            return new GetCodexAccountUsageStopPlanQueryResult(CodexAccountUsageStopPlanState.NotConfigured, null);
        }

        var reading = CodexAccountUsageStop.Read(row.Stored);
        if (reading.IsMalformed)
        {
            return new GetCodexAccountUsageStopPlanQueryResult(CodexAccountUsageStopPlanState.Invalid, null);
        }

        return reading.Value is { } threshold
            ? new GetCodexAccountUsageStopPlanQueryResult(CodexAccountUsageStopPlanState.Threshold, threshold)
            : new GetCodexAccountUsageStopPlanQueryResult(CodexAccountUsageStopPlanState.NotConfigured, null);
    }
}
