using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Runs.Policies.RecordedLocalCommit;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalCommitInspection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.RecordedLocalCommitInspection;

/// <summary>The shared plumbing of the ADR-0070 tests: a disposable migrated file-backed SQLite database per test, the seed, the real
/// handler over a context of its own (asserted to track nothing) and a way to write damaged storage through a context of its own with
/// foreign keys off, the only way to hold rows the Domain refuses to record.</summary>
public abstract class LocalCommitInspectionTestBase : IAsyncLifetime
{
    protected SqliteDatabaseFixture Fixture { get; } = new();

    public virtual Task InitializeAsync() => Fixture.InitializeAsync();

    public virtual Task DisposeAsync() => Fixture.DisposeAsync();

    internal Task<LocalCommitRows> SeedAsync(LocalCommitInspectionShape shape, string? reason = null) =>
        LocalCommitInspectionSeed.SeedAsync(Fixture, shape, reason);

    /// <summary>The provider's stored identifier form, for building a damage statement.</summary>
    protected static string Id(Guid id) => id.ToString("D").ToUpperInvariant();

    /// <summary>Runs one raw test-only storage write with foreign keys off and returns the number of rows it changed, so a case can prove
    /// it was not a no-op.</summary>
    protected async Task<int> ExecuteAsync(string sql)
    {
        await using var dbContext = Fixture.CreateContext();
        await dbContext.Database.OpenConnectionAsync();
        await dbContext.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF");
#pragma warning disable EF1002 // Test-owned damage statements with constant text; no external input.
        return await dbContext.Database.ExecuteSqlRawAsync(sql);
#pragma warning restore EF1002
    }

    protected async Task<Result<GetLocalCommitInspectionQueryResult>> TryInspectAsync(Guid runId, params IInterceptor[] interceptors)
    {
        await using var dbContext = Fixture.CreateContext(interceptors);
        var result = await new GetLocalCommitInspectionQueryHandler(dbContext).HandleAsync(
            new GetLocalCommitInspectionQuery(runId), CancellationToken.None);
        Assert.Empty(dbContext.ChangeTracker.Entries());
        return result;
    }

    protected async Task<GetLocalCommitInspectionQueryResult> InspectAsync(Guid runId, params IInterceptor[] interceptors)
    {
        var result = await TryInspectAsync(runId, interceptors);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null);
        return result.Value;
    }

    protected static RecordedLocalCommitOperation Recorded(GetLocalCommitInspectionQueryResult result)
    {
        Assert.Equal(LocalCommitInspectionStatus.Recorded, result.Status);
        return Assert.IsType<RecordedLocalCommitOperation>(result.Operation);
    }

    /// <summary>Every durable fact an inspection could disturb, as one comparable value: row counts of the recorded tables and every stored
    /// operation, workspace and checkpoint column the inspection reads. It deliberately does not claim whole-database identity.</summary>
    protected async Task<string> ChangeFingerprintAsync()
    {
        await using var dbContext = Fixture.CreateContext();
        var parts = new List<string>();
        foreach (var table in new[]
                 {
                     "projects", "runs", "git_workspaces", "git_checkpoints", "local_commit_operations", "local_commit_authority_members",
                     "attempts", "collaboration_messages", "repository_mutation_leases", "events",
                 })
        {
            await dbContext.Database.OpenConnectionAsync();
            await using var command = dbContext.Database.GetDbConnection().CreateCommand();
#pragma warning disable CA2100 // Constant table names owned by this test.
            command.CommandText = $"SELECT count(*) FROM {table}";
#pragma warning restore CA2100
            parts.Add($"{table}={await command.ExecuteScalarAsync()}");
        }

        await using var operations = dbContext.Database.GetDbConnection().CreateCommand();
        operations.CommandText =
            "SELECT Id, RunId, ProjectId, GitWorkspaceId, GitCheckpointId, CheckpointNumber, CheckpointFingerprintSha256, Status, BranchName, "
            + "ParentCommitSha, TreeSha, CommitSha, ChangedPathCount, TotalBytes, CreatedAtUtc, ExecutionStartedAtUtc, CompletedAtUtc, "
            + "OutcomeReasonCode FROM local_commit_operations ORDER BY Id";
        await using var reader = await operations.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            parts.Add(string.Join("|", Enumerable.Range(0, reader.FieldCount).Select(index => reader.IsDBNull(index) ? "~" : reader.GetValue(index))));
        }

        return string.Join(";", parts);
    }
}
