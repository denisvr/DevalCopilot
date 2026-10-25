using DevalCopilot.Application.Data;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionOwnershipReleases;

/// <summary>
/// Reads the release candidate or receipt of one step of one run (ADR-0065) with exactly one database statement and proves the whole
/// result from that single snapshot (<see cref="LocalExecutionReleaseProof"/>). There is no separate witness, observation, operation or
/// admission read to disagree, no second revision read, no retry and no external or current-eligibility read. It writes nothing and
/// swallows no fault: the caller decides how an unreadable stored value is reported.
/// </summary>
internal static class LocalExecutionReleaseReader
{
    public static async Task<LocalExecutionReleaseReading> ReadAsync(
        IDevalCopilotDbContext dbContext, Guid runId, Guid stepId, CancellationToken cancellationToken)
    {
        var rows = await LocalExecutionReleaseStatement.For(dbContext, runId, stepId).ToArrayAsync(cancellationToken);
        return LocalExecutionReleaseProof.Prove(rows);
    }
}
