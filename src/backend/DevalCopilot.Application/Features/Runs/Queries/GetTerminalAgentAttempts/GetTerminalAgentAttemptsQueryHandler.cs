using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetTerminalAgentAttempts;

public sealed class GetTerminalAgentAttemptsQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetTerminalAgentAttemptsQuery, IReadOnlyList<TerminalAgentAttempt>>
{
    public async Task<IReadOnlyList<TerminalAgentAttempt>> HandleAsync(
        GetTerminalAgentAttemptsQuery query, CancellationToken cancellationToken)
    {
        // ADR-0060: a terminal attempt that is linked to a local execution sequence, dispatched and without coherent Exited process
        // evidence stays quarantined, so its partial files are retained rather than offered for orphan deletion.
        var retained = dbContext.Attempts.Where(LocalExecutionOwnership.UnresolvedAttempt(dbContext)).Select(attempt => attempt.Id);
        return await dbContext.Attempts
            .AsNoTracking()
            .Where(attempt => attempt.Kind == AttemptKind.Agent && attempt.Status != AttemptStatus.Running
                && !retained.Contains(attempt.Id))
            .Select(attempt => new TerminalAgentAttempt(attempt.RunId, attempt.Id))
            .ToListAsync(cancellationToken);
    }
}
