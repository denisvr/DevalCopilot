using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptStatus;

public sealed class GetAgentAttemptStatusQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetAgentAttemptStatusQuery, Result<AgentAttemptStatusQueryResult>>
{
    public async Task<Result<AgentAttemptStatusQueryResult>> HandleAsync(
        GetAgentAttemptStatusQuery query, CancellationToken cancellationToken)
    {
        var runExists = await dbContext.Runs.AsNoTracking().AnyAsync(run => run.Id == query.RunId, cancellationToken);
        if (!runExists)
        {
            return Result<AgentAttemptStatusQueryResult>.Failure(
                Error.NotFound("runs.not_found", "The requested run was not found."));
        }

        // Planner-only: this query's own contract (see GetAgentAttemptStatusQuery's doc comment)
        // is "the most recent Codex planning attempt" — without this filter, once a run also has
        // ClaudeCode critical-review attempts, "most recent Agent attempt of any role" would
        // silently start returning the wrong attempt's status under this same endpoint.
        var attempt = await dbContext.Attempts
            .AsNoTracking()
            .Where(candidate =>
                candidate.RunId == query.RunId && candidate.Kind == AttemptKind.Agent && candidate.AgentRole == AgentRole.Planner)
            .OrderByDescending(candidate => candidate.AttemptNumber)
            .FirstOrDefaultAsync(cancellationToken);

        if (attempt is null)
        {
            return Result<AgentAttemptStatusQueryResult>.Success(AgentAttemptStatusQueryResult.NoAttempt);
        }

        var artifacts = await dbContext.Artifacts
            .AsNoTracking()
            .Where(artifact => artifact.AttemptId == attempt.Id)
            .Select(artifact => new AgentAttemptArtifactMetadata(artifact.Purpose, artifact.ByteLength, artifact.Truncated, artifact.CaptureOutcome))
            .ToArrayAsync(cancellationToken);

        return Result<AgentAttemptStatusQueryResult>.Success(new AgentAttemptStatusQueryResult(
            true,
            attempt.Id,
            attempt.AttemptNumber,
            attempt.Status,
            attempt.AgentOutcome,
            attempt.ClaimedAtUtc,
            attempt.AgentDispatchedAtUtc,
            attempt.CompletedAtUtc,
            artifacts,
            attempt.GetAgentProcessExecutionEvidence(),
            attempt.AgentTimeout,
            attempt.GetAgentTokenUsageEvidence()));
    }
}
