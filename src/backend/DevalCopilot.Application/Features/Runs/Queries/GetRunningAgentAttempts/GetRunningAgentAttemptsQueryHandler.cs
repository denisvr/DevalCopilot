using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetRunningAgentAttempts;

public sealed class GetRunningAgentAttemptsQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetRunningAgentAttemptsQuery, IReadOnlyList<RunningAgentAttempt>>
{
    public async Task<IReadOnlyList<RunningAgentAttempt>> HandleAsync(
        GetRunningAgentAttemptsQuery query, CancellationToken cancellationToken)
    {
        // ADR-0060: a Running attempt linked to a local execution sequence is closed or quarantined by the sequence's own startup
        // command; generic recovery never seals or imports its partial output.
        return await dbContext.Attempts
            .AsNoTracking()
            .Where(attempt => attempt.Kind == AttemptKind.Agent && attempt.Status == AttemptStatus.Running
                && !dbContext.LocalExecutionSequenceSteps.Any(step => step.OperationId == attempt.Id))
            .Select(attempt => new RunningAgentAttempt(attempt.RunId, attempt.Id))
            .ToListAsync(cancellationToken);
    }
}
