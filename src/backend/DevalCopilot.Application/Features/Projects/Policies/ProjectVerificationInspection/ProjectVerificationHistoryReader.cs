using DevalCopilot.Application.Data;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Projects.Policies.ProjectVerificationInspection;

/// <summary>
/// Reads one fixed-size keyset page of a project's recorded verification executions, newest first (ADR-0069). It reads at most 21 scalar
/// rows through the unique (ProjectId, ExecutionNumber) index (the 21st only proves that older executions exist), and refuses the whole
/// page when any selected row or the lookahead key cannot be admitted, never filtering a damaged row away or returning a partial page.
/// A page is an independent recorded read, not a snapshot of the whole project or proof that every stored row is valid. The command name
/// is the execution's immutable snapshot, never the current recipe. It reads no artifact, path, argument, status or source fact, writes
/// nothing and swallows no fault.
/// </summary>
internal static class ProjectVerificationHistoryReader
{
    private const int MaximumCommandNameLength = 200;

    public static async Task<ProjectVerificationHistoryReading> ReadAsync(
        IDevalCopilotDbContext dbContext, Guid projectId, int? beforeExecutionNumber, CancellationToken cancellationToken)
    {
        if (!await dbContext.Projects.AsNoTracking().AnyAsync(project => project.Id == projectId, cancellationToken))
        {
            return ProjectVerificationHistoryReading.NotFound;
        }

        var executions = dbContext.VerificationExecutions.AsNoTracking().Where(execution => execution.ProjectId == projectId);
        if (beforeExecutionNumber is { } before)
        {
            executions = executions.Where(execution => execution.ExecutionNumber < before);
        }

        var rows = await executions
            .OrderByDescending(execution => execution.ExecutionNumber)
            .Take(ProjectVerificationInspectionProjection.PageSize + 1)
            .ProjectHistory()
            .ToArrayAsync(cancellationToken);

        var displayed = Math.Min(rows.Length, ProjectVerificationInspectionProjection.PageSize);
        var entries = new List<ProjectVerificationHistoryEntry>(displayed);
        var identities = new HashSet<Guid>();
        // Strictly descending and below the cursor; with no cursor the bound lies above every Int32.
        long previous = beforeExecutionNumber ?? (long)int.MaxValue + 1;
        for (var index = 0; index < rows.Length; index++)
        {
            var row = rows[index];
            var identity = ProjectVerificationInspectionStorage.ParseIdentifier(row.Id);
            if (identity is not { } id || row.Number is not { } number || number >= previous || !identities.Add(id))
            {
                return ProjectVerificationHistoryReading.Unavailable;
            }

            previous = number;

            // The lookahead row proves only that an older execution exists: its identity and number are admitted, nothing else.
            if (index >= displayed)
            {
                continue;
            }

            var claimedAt = ProjectVerificationInspectionStorage.ParseUtc(row.ClaimedAtUtc);
            if (claimedAt is not { } claimed || !IsCommandName(row))
            {
                return ProjectVerificationHistoryReading.Unavailable;
            }

            entries.Add(new ProjectVerificationHistoryEntry(id, number, row.CommandName!, claimed));
        }

        var hasMore = rows.Length > displayed;
        return new ProjectVerificationHistoryReading(
            ProjectVerificationInspectionReadingKind.Recorded,
            entries,
            hasMore,
            hasMore ? entries[^1].ExecutionNumber : null);
    }

    private static bool IsCommandName(ProjectVerificationHistoryRow row) =>
        !row.CommandNameOversized
        && !string.IsNullOrWhiteSpace(row.CommandName)
        && row.CommandName.Length <= MaximumCommandNameLength;
}
