using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Runs.Policies.RecordedRunAbandonment;
using DevalCopilot.Application.Features.Runs.Queries.GetRunAbandonmentInspection;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.RunAbandonmentInspection;

/// <summary>The shared plumbing of the ADR-0071 tests: a disposable migrated file-backed SQLite database per test, runs closed by the
/// production abandonment command (so every Recorded case starts from what the producer wrote), the real handler over a context of its
/// own (asserted to track nothing) and a way to write damaged storage through a context of its own with foreign keys off, the only way
/// to hold rows the producer refuses to record.</summary>
public abstract class RunAbandonmentInspectionTestBase : IAsyncLifetime
{
    protected SqliteDatabaseFixture Fixture { get; } = new();

    public virtual Task InitializeAsync() => Fixture.InitializeAsync();

    public virtual Task DisposeAsync() => Fixture.DisposeAsync();

    /// <summary>The provider's stored identifier form, for building a damage statement.</summary>
    protected static string Id(Guid id) => id.ToString("D").ToUpperInvariant();

    /// <summary>A manual run closed by the production command: Created without a workspace, or Running at a preserved stage.</summary>
    internal async Task<AbandonedRun> AbandonAsync(
        bool running = true, bool withWorkspace = false, string reason = AbandonmentRaceSupport.Reason, DateTimeOffset? at = null)
    {
        var scene = await AbandonmentScene.CreateAsync(Fixture, running, withWorkspace);
        var closed = await AbandonmentRaceSupport.AbandonAsync(Fixture, scene.RunId, reason, at);
        Assert.True(closed.IsSuccess, closed.IsFailure ? closed.Errors[0].Code : null);
        await using var dbContext = Fixture.CreateContext();
        var recorded = await dbContext.Events.AsNoTracking()
            .SingleAsync(runEvent => runEvent.RunId == scene.RunId && runEvent.EventType == RunEventType.RunAbandoned);
        return new AbandonedRun(scene, reason, at ?? AbandonmentRaceSupport.AbandonedAt, recorded.Id, recorded.Sequence);
    }

    /// <summary>Runs one raw test-only storage write with foreign keys off and returns the number of rows it changed, so a case can prove
    /// it was not a no-op.</summary>
    protected async Task<int> ExecuteAsync(string sql)
    {
        await using var dbContext = Fixture.CreateContext();
        await dbContext.Database.OpenConnectionAsync();
        await using var pragma = dbContext.Database.GetDbConnection().CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = OFF";
        await pragma.ExecuteNonQueryAsync();
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
#pragma warning disable CA2100 // Test-owned damage statements with constant text; no external input, and braces stay literal.
        command.CommandText = sql;
#pragma warning restore CA2100
        return await command.ExecuteNonQueryAsync();
    }

    protected async Task<Result<GetRunAbandonmentInspectionQueryResult>> TryInspectAsync(Guid runId, params IInterceptor[] interceptors)
    {
        await using var dbContext = Fixture.CreateContext(interceptors);
        var result = await new GetRunAbandonmentInspectionQueryHandler(dbContext).HandleAsync(
            new GetRunAbandonmentInspectionQuery(runId), CancellationToken.None);
        Assert.Empty(dbContext.ChangeTracker.Entries());
        return result;
    }

    protected async Task<GetRunAbandonmentInspectionQueryResult> InspectAsync(Guid runId, params IInterceptor[] interceptors)
    {
        var result = await TryInspectAsync(runId, interceptors);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null);
        return result.Value;
    }

    protected static RecordedAbandonment Recorded(GetRunAbandonmentInspectionQueryResult result)
    {
        Assert.Equal(RunAbandonmentInspectionStatus.Recorded, result.Status);
        return Assert.IsType<RecordedAbandonment>(result.Abandonment);
    }

    /// <summary>Every durable fact an inspection could disturb, as one comparable value: the row counts of the tables near the read and
    /// every stored run and event column the inspection reads. It deliberately does not claim whole-database identity.</summary>
    protected async Task<string> ChangeFingerprintAsync()
    {
        await using var dbContext = Fixture.CreateContext();
        await dbContext.Database.OpenConnectionAsync();
        var parts = new List<string>();
        foreach (var table in new[]
                 {
                     "projects", "runs", "git_workspaces", "git_checkpoints", "local_commit_operations", "attempts",
                     "collaboration_messages", "repository_mutation_leases", "events",
                 })
        {
            await using var command = dbContext.Database.GetDbConnection().CreateCommand();
#pragma warning disable CA2100 // Constant table names owned by this test.
            command.CommandText = $"SELECT count(*) FROM {table}";
#pragma warning restore CA2100
            parts.Add($"{table}={await command.ExecuteScalarAsync()}");
        }

        foreach (var query in new[]
                 {
                     "SELECT Id, ProjectId, ExecutionMode, Lifecycle, Stage, LastAdvancedAtUtc, AbandonmentReason, AbandonedAtUtc, "
                     + "ActiveParticipantKind, typeof(AbandonedAtUtc) FROM runs ORDER BY Id",
                     "SELECT Sequence, Id, RunId, AttemptId, EventType, ActorKind, ActorAgentRole, ActorAgentProvider, PayloadJson, "
                     + "OccurredAtUtc FROM events ORDER BY Sequence",
                 })
        {
            await using var command = dbContext.Database.GetDbConnection().CreateCommand();
#pragma warning disable CA2100 // Constant statements owned by this test.
            command.CommandText = query;
#pragma warning restore CA2100
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                parts.Add(string.Join("|", Enumerable.Range(0, reader.FieldCount).Select(index => reader.IsDBNull(index) ? "~" : reader.GetValue(index))));
            }
        }

        return string.Join(";", parts);
    }
}
