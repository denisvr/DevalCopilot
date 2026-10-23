using DevalCopilot.Application.Data;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;

/// <summary>
/// Reads one run's recorded admission history (ADR-0063) with exactly one database statement and proves the whole result from that single
/// snapshot (<see cref="LocalExecutionAdmissionHistoryProof"/>). There is no separate binding, ledger or step read to disagree, no
/// second revision read, no write lock, transaction or retry. It reads no current recipe, message, checkpoint, workspace, lease,
/// eligibility, Git, filesystem, process, provider or account state, writes nothing and swallows no fault.
/// </summary>
internal static class LocalExecutionAdmissionHistoryReader
{
    public static async Task<LocalExecutionAdmissionHistoryReading> ReadAsync(
        IDevalCopilotDbContext dbContext, Guid runId, CancellationToken cancellationToken)
    {
        var rows = await LocalExecutionAdmissionHistoryStatement.For(dbContext, runId).ToArrayAsync(cancellationToken);
        return rows.Length == 0
            ? LocalExecutionAdmissionHistoryReading.RunNotFound
            : LocalExecutionAdmissionHistoryProof.Prove(rows);
    }
}
