using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.FormatRepair;

/// <summary>
/// The bounded, read-only repair lineage of one Agent attempt: the source attempt's id and number,
/// both <see langword="null"/> for an ordinary attempt. The link is disclosed only when the source
/// can be proved — an Agent attempt of the same run with an earlier number, found through non-enum
/// columns only, so an unreadable source row never breaks the read. A link that cannot be proved reads
/// as no lineage rather than a guessed one. Provenance only: never a claim that the attempt corrected
/// or preserved the source's response.
/// </summary>
public readonly record struct AgentRepairLineage(Guid? SourceAttemptId, int? SourceAttemptNumber)
{
    public static AgentRepairLineage None => default;

    public static async Task<AgentRepairLineage> ReadAsync(
        IDevalCopilotDbContext dbContext, Attempt attempt, CancellationToken cancellationToken)
    {
        if (attempt.AgentRepairSourceAttemptId is not { } sourceAttemptId)
        {
            return None;
        }

        var runId = attempt.RunId;
        var attemptNumber = attempt.AttemptNumber;
        var sourceNumber = await dbContext.Attempts
            .AsNoTracking()
            .Where(candidate =>
                candidate.Id == sourceAttemptId
                && candidate.RunId == runId
                && candidate.Kind == AttemptKind.Agent
                && candidate.AttemptNumber < attemptNumber)
            .Select(candidate => (int?)candidate.AttemptNumber)
            .FirstOrDefaultAsync(cancellationToken);
        return sourceNumber is null ? None : new AgentRepairLineage(sourceAttemptId, sourceNumber);
    }

    /// <summary>The lineage a role status shows: the link's id is present for every repair, and the source's
    /// number is present only when a source Agent attempt of the same run is found (mirroring the Planner
    /// status). A source with an unreadable enum column still yields its number.</summary>
    public static async Task<AgentRepairLineage> ReadForStatusAsync(
        IDevalCopilotDbContext dbContext, Attempt attempt, CancellationToken cancellationToken)
    {
        if (attempt.AgentRepairSourceAttemptId is not { } sourceAttemptId)
        {
            return None;
        }

        var runId = attempt.RunId;
        var sourceNumber = await dbContext.Attempts
            .AsNoTracking()
            .Where(candidate => candidate.Id == sourceAttemptId && candidate.RunId == runId && candidate.Kind == AttemptKind.Agent)
            .Select(candidate => (int?)candidate.AttemptNumber)
            .FirstOrDefaultAsync(cancellationToken);
        return new AgentRepairLineage(sourceAttemptId, sourceNumber);
    }
}
