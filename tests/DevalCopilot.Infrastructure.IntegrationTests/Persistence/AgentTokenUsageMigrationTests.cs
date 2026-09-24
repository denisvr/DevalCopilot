using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// Proves the purely additive <c>AddAgentTokenUsageEvidence</c> migration against a disposable
/// file-backed SQLite database upgraded by the production migrations: attempts recorded before it
/// existed stay valid, keep every process-evidence and assignment fact, outcome, and timestamp, and
/// project their token usage as truthfully unknown; new usage evidence round-trips exactly, with
/// and without a cache breakdown; and an inconsistent persisted row is never partially trusted.
/// </summary>
public sealed class AgentTokenUsageMigrationTests : IDisposable
{
    private const string PreviousMigration = "AddAgentProcessExecutionEvidence";

    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"devalcopilot-agent-token-usage-migration-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_databasePath}"));
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    [Fact]
    public async Task Historical_attempts_survive_the_upgrade_with_unknown_usage_and_every_existing_fact_intact()
    {
        var claimedAt = new DateTimeOffset(2026, 9, 24, 9, 0, 0, TimeSpan.Zero);
        var dispatchedAt = claimedAt.AddSeconds(5);
        var completedAt = claimedAt.AddMinutes(3);
        var projectId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var implementationId = Guid.NewGuid();
        var interruptedPlannerId = Guid.NewGuid();

        await using (var previous = CreateContext())
        {
            await previous.Database.MigrateAsync(PreviousMigration);
            previous.Projects.Add(Project.Register(projectId, "Usage migration", $@"C:\repos\usage-migration-{Guid.NewGuid():N}", claimedAt));
            await previous.SaveChangesAsync();
            await previous.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO runs
                       (Id, ProjectId, ExecutionNumber, Objective, Lifecycle, Stage, ActiveParticipantKind,
                        CreatedAtUtc, LastAdvancedAtUtc, AccumulatedAutonomousSeconds)
                   VALUES
                       ({runId}, {projectId}, {1}, {"Historical usage"}, {nameof(RunLifecycle.Running)},
                        {nameof(RunStage.Execute)}, {nameof(ParticipantKind.None)}, {claimedAt}, {claimedAt}, {0d})");
            await previous.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO attempts
                       (Id, RunId, AttemptNumber, Kind, Status, ClaimedAtUtc, CompletedAtUtc, ProcessArguments,
                        AgentProvider, AgentRole, AgentProtocolVersion, AgentExpectedMessageType, AgentResponseContract,
                        AgentTimeout, AgentDispatchedAtUtc, AgentOutcome, AgentRequestedModel, AgentObservedModel,
                        AgentRequestedEffort, AgentObservedEffort, AgentPermissionProfile, AgentAdapterContractVersion,
                        AgentProcessOutcome, AgentProcessExitCode, AgentProcessDuration)
                   VALUES
                       ({implementationId}, {runId}, {1}, {nameof(AttemptKind.Agent)}, {nameof(AttemptStatus.Completed)},
                        {claimedAt}, {completedAt}, {""}, {nameof(AgentProvider.ClaudeCode)}, {nameof(AgentRole.Implementer)},
                        {"1.0"}, {nameof(CollaborationMessageType.Proposal)}, {nameof(AgentResponseContract.ImplementationReport)},
                        {1_200_000L}, {dispatchedAt}, {nameof(AgentOutcome.Implemented)}, {"requested-model"}, {"observed-model"},
                        {"high"}, {"medium"}, {nameof(AgentPermissionProfile.WorkspaceEditOnly)}, {"claude-implementation-v1"},
                        {nameof(ProcessOutcome.Exited)}, {0}, {12_345_678L})");
            await previous.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO attempts
                       (Id, RunId, AttemptNumber, Kind, Status, ClaimedAtUtc, CompletedAtUtc, ProcessArguments,
                        AgentProvider, AgentRole, AgentResponseContract, AgentTimeout, AgentDispatchedAtUtc)
                   VALUES
                       ({interruptedPlannerId}, {runId}, {2}, {nameof(AttemptKind.Agent)}, {nameof(AttemptStatus.Interrupted)},
                        {claimedAt}, {completedAt}, {""}, {nameof(AgentProvider.Codex)}, {nameof(AgentRole.Planner)},
                        {nameof(AgentResponseContract.Proposal)}, {600_000L}, {dispatchedAt})");
        }

        await using (var upgraded = CreateContext())
        {
            await upgraded.Database.MigrateAsync();
            Assert.Contains(
                (await upgraded.Database.GetAppliedMigrationsAsync()).Select(id => id.Split('_').Last()),
                name => name == "AddAgentTokenUsageEvidence");
        }

        await using var reopened = CreateContext();

        var implementation = await reopened.Attempts.AsNoTracking().SingleAsync(attempt => attempt.Id == implementationId);
        Assert.Equal(AttemptStatus.Completed, implementation.Status);
        Assert.Equal(AgentOutcome.Implemented, implementation.AgentOutcome);
        Assert.Equal(claimedAt, implementation.ClaimedAtUtc);
        Assert.Equal(dispatchedAt, implementation.AgentDispatchedAtUtc);
        Assert.Equal(completedAt, implementation.CompletedAtUtc);
        Assert.Equal(
            new AgentAssignmentSnapshot(
                AgentProvider.ClaudeCode, "requested-model", "observed-model", "high", "medium",
                AgentPermissionProfile.WorkspaceEditOnly, "claude-implementation-v1"),
            implementation.GetAssignmentSnapshot());
        Assert.Equal(
            AgentProcessExecutionEvidence.Create(ProcessOutcome.Exited, 0, TimeSpan.FromTicks(12_345_678)),
            implementation.GetAgentProcessExecutionEvidence());
        AssertUsageUnknown(implementation);

        var interrupted = await reopened.Attempts.AsNoTracking().SingleAsync(attempt => attempt.Id == interruptedPlannerId);
        Assert.Equal(AttemptStatus.Interrupted, interrupted.Status);
        Assert.Null(interrupted.AgentOutcome);
        Assert.Equal(dispatchedAt, interrupted.AgentDispatchedAtUtc);
        Assert.Null(interrupted.GetAgentProcessExecutionEvidence());
        AssertUsageUnknown(interrupted);

        var connection = reopened.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM attempts WHERE AgentInputTokens IS NOT NULL OR AgentOutputTokens IS NOT NULL "
            + "OR AgentCacheCreationInputTokens IS NOT NULL OR AgentCacheReadInputTokens IS NOT NULL OR AgentTokenUsageSchemaVersion IS NOT NULL";
        Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Usage_recorded_after_the_upgrade_round_trips_exactly_with_and_without_a_cache_breakdown()
    {
        var now = new DateTimeOffset(2026, 9, 24, 10, 0, 0, TimeSpan.Zero);
        var withCache = AgentTokenUsageEvidence.Create(1200, 345, 67, 890, "claude-cli-usage-v1");
        var withoutCache = AgentTokenUsageEvidence.Create(500, 60, null, null, AgentTokenUsageEvidencePolicy.ClaudeCliSchemaVersion);
        Guid withCacheId;
        Guid withoutCacheId;

        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
            var project = Project.Register(Guid.NewGuid(), "Usage round trip", $@"C:\repos\usage-round-trip-{Guid.NewGuid():N}", now);
            var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Round trip usage", now);
            context.Projects.Add(project);
            context.Runs.Add(run);

            var implementation = Attempt.ClaimAgentImplementation(
                Guid.NewGuid(), run.Id, 1, Guid.NewGuid(), Guid.NewGuid(), "fingerprint", Guid.NewGuid(),
                TimeSpan.FromMinutes(20), 1024, 2048, now, 1);
            implementation.MarkAgentDispatched(now);
            implementation.CompleteImplementation(
                AgentOutcome.ProviderInvocationFailed, null, now.AddMinutes(20),
                AgentProcessExecutionEvidence.Create(ProcessOutcome.Exited, 1, TimeSpan.FromSeconds(9)), withCache);

            var criticalReview = Attempt.ClaimAgentCriticalReview(
                Guid.NewGuid(), run.Id, 2, Guid.NewGuid(), Guid.NewGuid(), "fingerprint", Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 1024, 2048, now, 2);
            criticalReview.MarkAgentDispatched(now);
            criticalReview.CompleteAgent(
                AgentOutcome.ProviderInvocationFailed, null, now.AddMinutes(1),
                AgentProcessExecutionEvidence.Create(ProcessOutcome.Exited, 1, TimeSpan.FromSeconds(1)), withoutCache);

            context.Attempts.AddRange(implementation, criticalReview);
            await context.SaveChangesAsync();
            withCacheId = implementation.Id;
            withoutCacheId = criticalReview.Id;
        }

        await using var reopened = CreateContext();
        var persistedWithCache = await reopened.Attempts.AsNoTracking().SingleAsync(attempt => attempt.Id == withCacheId);
        Assert.Equal(AgentOutcome.ProviderInvocationFailed, persistedWithCache.AgentOutcome);
        Assert.Equal(1, persistedWithCache.AgentProcessExitCode);
        Assert.Equal(withCache, persistedWithCache.GetAgentTokenUsageEvidence());
        Assert.Equal("claude-cli-usage-v1", persistedWithCache.AgentTokenUsageSchemaVersion);

        var persistedWithoutCache = await reopened.Attempts.AsNoTracking().SingleAsync(attempt => attempt.Id == withoutCacheId);
        Assert.Equal(AgentOutcome.ProviderInvocationFailed, persistedWithoutCache.AgentOutcome);
        Assert.Equal(withoutCache, persistedWithoutCache.GetAgentTokenUsageEvidence());
        Assert.Null(persistedWithoutCache.AgentCacheCreationInputTokens);
        Assert.Null(persistedWithoutCache.AgentCacheReadInputTokens);
        Assert.Equal(500, persistedWithoutCache.AgentInputTokens);
        Assert.Equal(60, persistedWithoutCache.AgentOutputTokens);
    }

    [Theory]
    [InlineData(-5, 10, 1, 1, "claude-cli-usage-v1")]
    [InlineData(5, -10, 1, 1, "claude-cli-usage-v1")]
    [InlineData(5, 10, -1, 1, "claude-cli-usage-v1")]
    [InlineData(5, 10, 1, -1, "claude-cli-usage-v1")]
    [InlineData(5, null, 1, 1, "claude-cli-usage-v1")]
    [InlineData(null, 10, 1, 1, "claude-cli-usage-v1")]
    [InlineData(5, 10, 1, 1, null)]
    [InlineData(5, 10, 1, 1, "   ")]
    public async Task An_inconsistent_persisted_usage_row_is_projected_as_unknown_rather_than_partially_trusted(
        int? input, int? output, int? cacheCreation, int? cacheRead, string? schemaVersion)
    {
        var now = new DateTimeOffset(2026, 9, 24, 11, 0, 0, TimeSpan.Zero);
        var attemptId = Guid.NewGuid();
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
            var project = Project.Register(Guid.NewGuid(), "Usage guard", $@"C:\repos\usage-guard-{Guid.NewGuid():N}", now);
            var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Inconsistent usage", now);
            context.Projects.Add(project);
            context.Runs.Add(run);
            await context.SaveChangesAsync();
            await context.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO attempts
                       (Id, RunId, AttemptNumber, Kind, Status, ClaimedAtUtc, ProcessArguments, AgentProvider,
                        AgentResponseContract, AgentDispatchedAtUtc, AgentInputTokens, AgentOutputTokens,
                        AgentCacheCreationInputTokens, AgentCacheReadInputTokens, AgentTokenUsageSchemaVersion)
                   VALUES
                       ({attemptId}, {run.Id}, {1}, {nameof(AttemptKind.Agent)}, {nameof(AttemptStatus.Failed)}, {now}, {""},
                        {nameof(AgentProvider.ClaudeCode)}, {nameof(AgentResponseContract.CriticalReview)}, {now},
                        {input}, {output}, {cacheCreation}, {cacheRead}, {schemaVersion})");
        }

        await using var reopened = CreateContext();
        var persisted = await reopened.Attempts.AsNoTracking().SingleAsync(attempt => attempt.Id == attemptId);
        Assert.Null(persisted.GetAgentTokenUsageEvidence());
    }

    private static void AssertUsageUnknown(Attempt attempt)
    {
        Assert.Null(attempt.AgentInputTokens);
        Assert.Null(attempt.AgentOutputTokens);
        Assert.Null(attempt.AgentCacheCreationInputTokens);
        Assert.Null(attempt.AgentCacheReadInputTokens);
        Assert.Null(attempt.AgentTokenUsageSchemaVersion);
        Assert.Null(attempt.GetAgentTokenUsageEvidence());
    }

    private DevalCopilotDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<DevalCopilotDbContext>().UseSqlite($"Data Source={_databasePath}").Options);
}
