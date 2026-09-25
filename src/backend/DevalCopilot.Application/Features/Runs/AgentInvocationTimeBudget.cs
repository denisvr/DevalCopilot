using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs;

/// <summary>
/// Shared read-side query for the run-wide Agent invocation-time budget, kept in exactly one place
/// so every Agent-claiming command handler evaluates the identical fail-closed reservation rule
/// (see <see cref="AgentInvocationTimeReservation"/> and the companion ADR to ADR-0012), rather
/// than a seventh place a future change to this one rule could silently drift from the other six.
/// </summary>
public static class AgentInvocationTimeBudget
{
    /// <summary>
    /// Reads this run's own claimed Agent attempts' <see cref="Domain.Features.Runs.Attempt.AgentTimeout"/>
    /// values and returns their total reservation, or <see langword="null"/> (fail closed) if any
    /// is missing or malformed. Pass <paramref name="asNoTracking"/> <see langword="true"/> only
    /// for a post-race re-check against fresh, untracked state — the same convention the existing
    /// count-budget race reclassification already uses.
    /// </summary>
    public static async Task<TimeSpan?> ComputeReservedAsync(
        IDevalCopilotDbContext dbContext, Guid runId, bool asNoTracking, CancellationToken cancellationToken)
    {
        var query = dbContext.Attempts.Where(candidate => candidate.RunId == runId && candidate.Kind == AttemptKind.Agent);
        if (asNoTracking)
        {
            query = query.AsNoTracking();
        }

        var claimedAgentAttemptTimeouts = await query
            .Select(candidate => candidate.AgentTimeout)
            .ToListAsync(cancellationToken);

        return AgentInvocationTimeReservation.ComputeReserved(claimedAgentAttemptTimeouts);
    }
}
