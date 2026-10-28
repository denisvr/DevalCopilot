using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Tests.Features.Runs.RecordedLocalCommitInspection;

/// <summary>Seeds one ManualAgent run with the operation of the requested recorded shape through the Domain's own transitions over a
/// real migrated SQLite database, reusing the recorder tests' relational rows. Only the run mode and the transition times are set here.</summary>
internal static class LocalCommitInspectionSeed
{
    public static readonly DateTimeOffset Created = LocalCommitRowsSeed.Now;
    public static readonly DateTimeOffset Started = Created.AddSeconds(5);
    public static readonly DateTimeOffset Finished = Created.AddSeconds(9);
    public static readonly string ProposedCommit = new('d', 40);
    public static readonly string Parent = LocalCommitRowsSeed.SourceCommit;
    public static readonly string Tree = new('e', 40);
    public static readonly string Fingerprint = new('c', 64);

    public static async Task<LocalCommitRows> SeedAsync(
        SqliteDatabaseFixture fixture, LocalCommitInspectionShape shape, string? reason = null, bool manualAgent = true)
    {
        await using var db = fixture.CreateContext();
        var rows = await LocalCommitRowsSeed.SeedAsync(db, LocalCommitStatus.Prepared, WorkspaceStatus.Ready);
        if (manualAgent)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE runs SET ExecutionMode = 2 WHERE Id = {rows.Run.Id}");
        }

        var operation = rows.Operation;
        var failureReason = reason ?? "local_commit.git_unprovable";
        switch (shape)
        {
            case LocalCommitInspectionShape.Executing:
                operation.MarkExecuting(Started);
                break;
            case LocalCommitInspectionShape.Completed:
                operation.MarkExecuting(Started);
                operation.Complete(Finished);
                break;
            case LocalCommitInspectionShape.FailedStarted:
                operation.MarkExecuting(Started);
                operation.Fail(failureReason, Finished);
                break;
            case LocalCommitInspectionShape.FailedUnstarted:
                operation.Fail(failureReason, Finished);
                break;
            case LocalCommitInspectionShape.InterruptedStarted:
                operation.MarkExecuting(Started);
                operation.Interrupt(failureReason, Finished);
                break;
            case LocalCommitInspectionShape.InterruptedUnstarted:
                operation.Interrupt(failureReason, Finished);
                break;
            case LocalCommitInspectionShape.AttentionStarted:
                operation.MarkExecuting(Started);
                operation.MarkNeedsAttention(failureReason);
                break;
            case LocalCommitInspectionShape.AttentionUnstarted:
                operation.MarkNeedsAttention(failureReason);
                break;
        }

        await db.SaveChangesAsync();
        return rows;
    }

    /// <summary>The recorded status the shape holds.</summary>
    public static LocalCommitStatus StatusOf(LocalCommitInspectionShape shape) => shape switch
    {
        LocalCommitInspectionShape.Prepared => LocalCommitStatus.Prepared,
        LocalCommitInspectionShape.Executing => LocalCommitStatus.Executing,
        LocalCommitInspectionShape.Completed => LocalCommitStatus.Completed,
        LocalCommitInspectionShape.FailedStarted or LocalCommitInspectionShape.FailedUnstarted => LocalCommitStatus.Failed,
        LocalCommitInspectionShape.InterruptedStarted or LocalCommitInspectionShape.InterruptedUnstarted => LocalCommitStatus.Interrupted,
        _ => LocalCommitStatus.NeedsAttention,
    };
}
