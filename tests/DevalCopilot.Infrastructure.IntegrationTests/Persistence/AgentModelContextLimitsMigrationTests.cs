using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// Proves the purely additive <c>AddAgentModelContextLimits</c> migration and the one nullable canonical snapshot column against
/// a disposable file-backed SQLite database upgraded by the production migrations: attempts recorded before it existed stay
/// valid, keep every fact, and project their limits as unknown; new evidence round-trips exactly, ordered ordinally, and survives
/// a restart; recording is immutable and atomic with the outcome; and stored text that is not exactly the canonical valid
/// snapshot of a proven provider is never trusted, never throws, and never disturbs a healthy sibling.
/// </summary>
public sealed class AgentModelContextLimitsMigrationTests : IDisposable
{
    private const string PreviousMigration = "AddDiagnosisCorrectionEscalation";
    private const string Source = "claude-cli-model-usage-v1";

    private const string Healthy =
        "{\"version\":1,\"source\":\"claude-cli-model-usage-v1\",\"models\":[{\"modelId\":\"claude-a\",\"contextWindowTokens\":200000,"
        + "\"maxOutputTokens\":32000},{\"modelId\":\"claude-b\",\"contextWindowTokens\":1000000,\"maxOutputTokens\":64000}]}";

    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"devalcopilot-model-limits-migration-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_databasePath}"));
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    [Fact]
    public async Task Historical_attempts_survive_the_upgrade_with_unknown_limits_and_every_existing_fact_intact()
    {
        var claimedAt = new DateTimeOffset(2026, 10, 4, 9, 0, 0, TimeSpan.Zero);
        var dispatchedAt = claimedAt.AddSeconds(5);
        var completedAt = claimedAt.AddMinutes(3);
        var projectId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var implementationId = Guid.NewGuid();
        var interruptedPlannerId = Guid.NewGuid();

        await using (var previous = CreateContext())
        {
            await previous.Database.MigrateAsync(PreviousMigration);
            previous.Projects.Add(Project.Register(projectId, "Limits migration", $@"C:\repos\limits-migration-{Guid.NewGuid():N}", claimedAt));
            await previous.SaveChangesAsync();
            await previous.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO runs
                       (Id, ProjectId, ExecutionNumber, Objective, Lifecycle, Stage, ActiveParticipantKind,
                        CreatedAtUtc, LastAdvancedAtUtc, AccumulatedAutonomousSeconds)
                   VALUES
                       ({runId}, {projectId}, {1}, {"Historical limits"}, {nameof(RunLifecycle.Running)},
                        {nameof(RunStage.Execute)}, {nameof(ParticipantKind.None)}, {claimedAt}, {claimedAt}, {0d})");
            await previous.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO attempts
                       (Id, RunId, AttemptNumber, Kind, Status, ClaimedAtUtc, CompletedAtUtc, ProcessArguments,
                        AgentProvider, AgentRole, AgentProtocolVersion, AgentExpectedMessageType, AgentResponseContract,
                        AgentTimeout, AgentDispatchedAtUtc, AgentOutcome, AgentRequestedModel, AgentObservedModel,
                        AgentRequestedEffort, AgentObservedEffort, AgentPermissionProfile, AgentAdapterContractVersion,
                        AgentProcessOutcome, AgentProcessExitCode, AgentProcessDuration,
                        AgentInputTokens, AgentOutputTokens, AgentCacheCreationInputTokens, AgentCacheReadInputTokens,
                        AgentTokenUsageSchemaVersion)
                   VALUES
                       ({implementationId}, {runId}, {1}, {nameof(AttemptKind.Agent)}, {nameof(AttemptStatus.Completed)},
                        {claimedAt}, {completedAt}, {""}, {nameof(AgentProvider.ClaudeCode)}, {nameof(AgentRole.Implementer)},
                        {"1.0"}, {nameof(CollaborationMessageType.Proposal)}, {nameof(AgentResponseContract.ImplementationReport)},
                        {1_200_000L}, {dispatchedAt}, {nameof(AgentOutcome.Implemented)}, {"requested-model"}, {"observed-model"},
                        {"high"}, {"medium"}, {nameof(AgentPermissionProfile.WorkspaceEditOnly)}, {"claude-implementation-v1"},
                        {nameof(ProcessOutcome.Exited)}, {0}, {12_345_678L}, {1200}, {345}, {67}, {890}, {"claude-cli-usage-v1"})");
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
                name => name == "AddAgentModelContextLimits");
        }

        await using var reopened = CreateContext();

        var implementation = await reopened.Attempts.AsNoTracking().SingleAsync(attempt => attempt.Id == implementationId);
        Assert.Equal(AttemptStatus.Completed, implementation.Status);
        Assert.Equal(AgentOutcome.Implemented, implementation.AgentOutcome);
        Assert.Equal(claimedAt, implementation.ClaimedAtUtc);
        Assert.Equal(dispatchedAt, implementation.AgentDispatchedAtUtc);
        Assert.Equal(completedAt, implementation.CompletedAtUtc);
        Assert.Equal(
            AgentProcessExecutionEvidence.Create(ProcessOutcome.Exited, 0, TimeSpan.FromTicks(12_345_678)),
            implementation.GetAgentProcessExecutionEvidence());
        Assert.Equal(
            AgentTokenUsageEvidence.Create(1200, 345, 67, 890, "claude-cli-usage-v1"), implementation.GetAgentTokenUsageEvidence());
        Assert.Null(implementation.AgentModelContextLimitsSnapshot);
        Assert.Null(implementation.GetAgentModelContextLimitsEvidence());

        var interrupted = await reopened.Attempts.AsNoTracking().SingleAsync(attempt => attempt.Id == interruptedPlannerId);
        Assert.Equal(AttemptStatus.Interrupted, interrupted.Status);
        Assert.Null(interrupted.AgentModelContextLimitsSnapshot);
        Assert.Null(interrupted.GetAgentModelContextLimitsEvidence());

        var connection = reopened.Database.GetDbConnection();
        await connection.OpenAsync();
        await using (var count = connection.CreateCommand())
        {
            count.CommandText = "SELECT COUNT(*) FROM attempts WHERE AgentModelContextLimitsSnapshot IS NOT NULL";
            Assert.Equal(0L, (long)(await count.ExecuteScalarAsync())!);
        }

        await using var column = connection.CreateCommand();
        column.CommandText = "SELECT type, \"notnull\", dflt_value FROM pragma_table_info('attempts') WHERE name = 'AgentModelContextLimitsSnapshot'";
        await using var reader = await column.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("TEXT", reader.GetString(0));
        Assert.Equal(0L, reader.GetInt64(1));
        Assert.True(reader.IsDBNull(2));
    }

    [Fact]
    public async Task Limits_recorded_after_the_upgrade_round_trip_exactly_in_ordinal_order_and_survive_a_restart()
    {
        var now = new DateTimeOffset(2026, 10, 4, 10, 0, 0, TimeSpan.Zero);
        var unordered = new[]
        {
            new AgentModelContextLimit("claude-b", 1000000, 64000),
            new AgentModelContextLimit("Claude-Z", 200000, 200000),
            new AgentModelContextLimit("claude-a", 200000, 32000),
        };
        var evidence = AgentModelContextLimitsEvidence.Create(Source, unordered);
        Guid implementationId;
        Guid reviewId;
        Guid correctionId;

        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
            var project = Project.Register(Guid.NewGuid(), "Limits round trip", $@"C:\repos\limits-round-trip-{Guid.NewGuid():N}", now);
            var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Round trip limits", now);
            context.Projects.Add(project);
            context.Runs.Add(run);

            var implementation = Attempt.ClaimAgentImplementation(
                Guid.NewGuid(), run.Id, 1, Guid.NewGuid(), Guid.NewGuid(), "fingerprint", Guid.NewGuid(),
                TimeSpan.FromMinutes(20), 1024, 2048, now, 1);
            implementation.MarkAgentDispatched(now);
            implementation.CompleteImplementation(
                AgentOutcome.ProviderInvocationFailed, null, now.AddMinutes(20),
                AgentProcessExecutionEvidence.Create(ProcessOutcome.Exited, 1, TimeSpan.FromSeconds(9)), null, evidence);

            var review = Attempt.ClaimAgentCriticalReview(
                Guid.NewGuid(), run.Id, 2, Guid.NewGuid(), Guid.NewGuid(), "fingerprint", Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 1024, 2048, now, 2);
            review.MarkAgentDispatched(now);
            review.CompleteAgent(
                AgentOutcome.InvalidStructuredOutput, null, now.AddMinutes(1),
                AgentProcessExecutionEvidence.Create(ProcessOutcome.Exited, 0, TimeSpan.FromSeconds(1)),
                AgentTokenUsageEvidence.Create(500, 60, null, null, "claude-cli-usage-v1"),
                AgentModelContextLimitsEvidence.Create(Source, [new AgentModelContextLimit("claude-only", 100, 100)]));

            var correction = Attempt.ClaimAgentReviewCorrectionWithModelRequest(
                Guid.NewGuid(), run.Id, 3, Guid.NewGuid(), Guid.NewGuid(), "fingerprint", Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 1024, 2048, now, null, null, 3, null);
            correction.MarkAgentDispatched(now);
            correction.CompleteReviewCorrection(
                AgentOutcome.ProviderInvocationFailed, null, now.AddMinutes(2),
                AgentProcessExecutionEvidence.Create(ProcessOutcome.Exited, 1, TimeSpan.FromSeconds(1)));

            context.Attempts.AddRange(implementation, review, correction);
            await context.SaveChangesAsync();
            implementationId = implementation.Id;
            reviewId = review.Id;
            correctionId = correction.Id;
        }

        // A process restart: every pooled connection is gone and a brand-new context reads the file.
        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_databasePath}"));
        await using var reopened = CreateContext();

        var persisted = await reopened.Attempts.AsNoTracking().SingleAsync(attempt => attempt.Id == implementationId);
        Assert.Equal(AgentOutcome.ProviderInvocationFailed, persisted.AgentOutcome);
        Assert.Equal(
            "{\"version\":1,\"source\":\"claude-cli-model-usage-v1\",\"models\":[{\"modelId\":\"Claude-Z\",\"contextWindowTokens\":200000,"
            + "\"maxOutputTokens\":200000},{\"modelId\":\"claude-a\",\"contextWindowTokens\":200000,\"maxOutputTokens\":32000},"
            + "{\"modelId\":\"claude-b\",\"contextWindowTokens\":1000000,\"maxOutputTokens\":64000}]}",
            persisted.AgentModelContextLimitsSnapshot);
        var read = persisted.GetAgentModelContextLimitsEvidence();
        Assert.NotNull(read);
        Assert.Equal(Source, read.Source);
        Assert.Equal(
            [
                new AgentModelContextLimit("Claude-Z", 200000, 200000),
                new AgentModelContextLimit("claude-a", 200000, 32000),
                new AgentModelContextLimit("claude-b", 1000000, 64000),
            ],
            read.Models.AsEnumerable());
        Assert.Null(persisted.GetAgentTokenUsageEvidence());

        var persistedReview = await reopened.Attempts.AsNoTracking().SingleAsync(attempt => attempt.Id == reviewId);
        Assert.Equal(AgentOutcome.InvalidStructuredOutput, persistedReview.AgentOutcome);
        Assert.Equal([new AgentModelContextLimit("claude-only", 100, 100)], persistedReview.GetAgentModelContextLimitsEvidence()!.Models.AsEnumerable());
        Assert.Equal(500, persistedReview.GetAgentTokenUsageEvidence()!.InputTokens);

        var persistedCorrection = await reopened.Attempts.AsNoTracking().SingleAsync(attempt => attempt.Id == correctionId);
        Assert.Null(persistedCorrection.AgentModelContextLimitsSnapshot);
        Assert.Null(persistedCorrection.GetAgentModelContextLimitsEvidence());
    }

    [Fact]
    public async Task Recording_is_immutable_the_snapshot_is_never_replaced_by_a_second_completion_or_a_second_report()
    {
        var now = new DateTimeOffset(2026, 10, 4, 11, 0, 0, TimeSpan.Zero);
        Guid attemptId;
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
            var project = Project.Register(Guid.NewGuid(), "Limits immutable", $@"C:\repos\limits-immutable-{Guid.NewGuid():N}", now);
            var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Immutable limits", now);
            context.Projects.Add(project);
            context.Runs.Add(run);
            var attempt = Attempt.ClaimAgentCriticalReview(
                Guid.NewGuid(), run.Id, 1, Guid.NewGuid(), Guid.NewGuid(), "fingerprint", Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 1024, 2048, now, 1);
            attempt.MarkAgentDispatched(now);
            attempt.CompleteAgent(
                AgentOutcome.ProviderInvocationFailed, null, now,
                AgentProcessExecutionEvidence.Create(ProcessOutcome.Exited, 1, TimeSpan.FromSeconds(1)), null,
                AgentModelContextLimitsEvidence.Create(Source, [new AgentModelContextLimit("first", 10, 10)]));
            context.Attempts.Add(attempt);
            await context.SaveChangesAsync();
            attemptId = attempt.Id;
        }

        await using (var context = CreateContext())
        {
            var attempt = await context.Attempts.SingleAsync(candidate => candidate.Id == attemptId);
            Assert.Throws<InvalidOperationException>(() => attempt.CompleteAgent(
                AgentOutcome.ProviderInvocationFailed, null, now,
                AgentProcessExecutionEvidence.Create(ProcessOutcome.Exited, 1, TimeSpan.FromSeconds(1)), null,
                AgentModelContextLimitsEvidence.Create(Source, [new AgentModelContextLimit("second", 20, 20)])));
            await context.SaveChangesAsync();
        }

        await using var reopened = CreateContext();
        var persisted = await reopened.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == attemptId);
        Assert.Equal([new AgentModelContextLimit("first", 10, 10)], persisted.GetAgentModelContextLimitsEvidence()!.Models.AsEnumerable());
    }

    [Fact]
    public async Task A_row_already_holding_a_snapshot_refuses_a_second_report_instead_of_replacing_it()
    {
        var now = new DateTimeOffset(2026, 10, 4, 11, 30, 0, TimeSpan.Zero);
        var attemptId = Guid.NewGuid();
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
            var run = await SeedRunAsync(context, now);
            await InsertAgentRowAsync(
                context, attemptId, run, AttemptStatus.Running, AgentProvider.ClaudeCode, dispatched: now, healthyStored: Healthy);
        }

        await using var reopened = CreateContext();
        var attempt = await reopened.Attempts.SingleAsync(candidate => candidate.Id == attemptId);
        Assert.Null(attempt.GetAgentModelContextLimitsEvidence());
        var exception = Assert.Throws<InvalidOperationException>(() => attempt.CompleteAgent(
            AgentOutcome.ProviderInvocationFailed, null, now,
            AgentProcessExecutionEvidence.Create(ProcessOutcome.Exited, 1, TimeSpan.FromSeconds(1)), null,
            AgentModelContextLimitsEvidence.Create(Source, [new AgentModelContextLimit("replacement", 20, 20)])));
        Assert.Contains("write-once", exception.Message, StringComparison.Ordinal);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Equal(Healthy, attempt.AgentModelContextLimitsSnapshot);
    }

    [Fact]
    public async Task A_rolled_back_completion_leaves_the_attempt_running_with_no_limits_recorded()
    {
        var now = new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
        Guid attemptId;
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
            var project = Project.Register(Guid.NewGuid(), "Limits rollback", $@"C:\repos\limits-rollback-{Guid.NewGuid():N}", now);
            var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Rollback limits", now);
            context.Projects.Add(project);
            context.Runs.Add(run);
            var attempt = Attempt.ClaimAgentCriticalReview(
                Guid.NewGuid(), run.Id, 1, Guid.NewGuid(), Guid.NewGuid(), "fingerprint", Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 1024, 2048, now, 1);
            attempt.MarkAgentDispatched(now);
            context.Attempts.Add(attempt);
            await context.SaveChangesAsync();
            attemptId = attempt.Id;
        }

        await using (var context = CreateContext())
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            var attempt = await context.Attempts.SingleAsync(candidate => candidate.Id == attemptId);
            attempt.CompleteAgent(
                AgentOutcome.ProviderInvocationFailed, null, now,
                AgentProcessExecutionEvidence.Create(ProcessOutcome.Exited, 1, TimeSpan.FromSeconds(1)), null,
                AgentModelContextLimitsEvidence.Create(Source, [new AgentModelContextLimit("rolled-back", 10, 10)]));
            await context.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        await using var reopened = CreateContext();
        var persisted = await reopened.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == attemptId);
        Assert.Equal(AttemptStatus.Running, persisted.Status);
        Assert.Null(persisted.AgentOutcome);
        Assert.Null(persisted.AgentModelContextLimitsSnapshot);
        Assert.Null(persisted.AgentProcessOutcome);
    }

    public static TheoryData<string?> UntrustedStoredText => new()
    {
        // Not JSON, the wrong JSON shape, or empty.
        "not json", "[]", "null", "\"text\"", "{}", string.Empty, "   ",
        // Another snapshot version, source, or a missing/extra/duplicated member.
        Healthy.Replace("\"version\":1", "\"version\":2"), Healthy.Replace("\"version\":1", "\"version\":\"1\""),
        Healthy.Replace("\"version\":1", "\"version\":1.0"), Healthy.Replace(Source, "claude-cli-model-usage-v2"),
        Healthy.Replace(Source, "codex-cli-model-usage-v1"), Healthy.Replace("\"source\":\"claude-cli-model-usage-v1\",", string.Empty),
        Healthy.Replace("{\"version\":1,", "{\"version\":1,\"extra\":true,"), Healthy.Replace("{\"version\":1,", "{\"version\":1,\"version\":1,"),
        Healthy.Replace("\"models\":[", "\"models\":null,\"x\":["),
        // Whitespace or any other non-canonical spelling of otherwise valid evidence.
        Healthy.Replace(",\"source\"", ", \"source\""), " " + Healthy, Healthy + "\n", Healthy.Replace("claude-a", "\\u0063laude-a"),
        // Reordered, duplicated, empty, or missing entries.
        "{\"version\":1,\"source\":\"claude-cli-model-usage-v1\",\"models\":[{\"modelId\":\"claude-b\",\"contextWindowTokens\":1,\"maxOutputTokens\":1},"
        + "{\"modelId\":\"claude-a\",\"contextWindowTokens\":1,\"maxOutputTokens\":1}]}",
        "{\"version\":1,\"source\":\"claude-cli-model-usage-v1\",\"models\":[{\"modelId\":\"a\",\"contextWindowTokens\":1,\"maxOutputTokens\":1},"
        + "{\"modelId\":\"a\",\"contextWindowTokens\":1,\"maxOutputTokens\":1}]}",
        "{\"version\":1,\"source\":\"claude-cli-model-usage-v1\",\"models\":[]}", "{\"version\":1,\"source\":\"claude-cli-model-usage-v1\"}",
        // Invalid members: identifier, limits, and contradiction.
        "{\"version\":1,\"source\":\"claude-cli-model-usage-v1\",\"models\":[{\"modelId\":\"bad id\",\"contextWindowTokens\":1,\"maxOutputTokens\":1}]}",
        "{\"version\":1,\"source\":\"claude-cli-model-usage-v1\",\"models\":[{\"modelId\":\"-a\",\"contextWindowTokens\":1,\"maxOutputTokens\":1}]}",
        "{\"version\":1,\"source\":\"claude-cli-model-usage-v1\",\"models\":[{\"modelId\":\"a\",\"contextWindowTokens\":0,\"maxOutputTokens\":1}]}",
        "{\"version\":1,\"source\":\"claude-cli-model-usage-v1\",\"models\":[{\"modelId\":\"a\",\"contextWindowTokens\":1,\"maxOutputTokens\":0}]}",
        "{\"version\":1,\"source\":\"claude-cli-model-usage-v1\",\"models\":[{\"modelId\":\"a\",\"contextWindowTokens\":1,\"maxOutputTokens\":2}]}",
        "{\"version\":1,\"source\":\"claude-cli-model-usage-v1\",\"models\":[{\"modelId\":\"a\",\"contextWindowTokens\":2147483648,\"maxOutputTokens\":1}]}",
        "{\"version\":1,\"source\":\"claude-cli-model-usage-v1\",\"models\":[{\"modelId\":\"a\",\"contextWindowTokens\":1.5,\"maxOutputTokens\":1}]}",
        "{\"version\":1,\"source\":\"claude-cli-model-usage-v1\",\"models\":[{\"modelId\":\"a\",\"contextWindowTokens\":\"1\",\"maxOutputTokens\":1}]}",
        "{\"version\":1,\"source\":\"claude-cli-model-usage-v1\",\"models\":[{\"modelId\":\"a\",\"contextWindowTokens\":1,\"maxOutputTokens\":1,\"contextWindowTokens\":1}]}",
        // Over the entry bound, and over the 4 KiB bound even when every entry is valid and ordered.
        OversizedByEntries(), OversizedByBytes(),
    };

    private static string OversizedByEntries() =>
        "{\"version\":1,\"source\":\"claude-cli-model-usage-v1\",\"models\":["
        + string.Join(",", Enumerable.Range(0, 17).Select(index => $"{{\"modelId\":\"m{index:00}\",\"contextWindowTokens\":1,\"maxOutputTokens\":1}}"))
        + "]}";

    private static string OversizedByBytes() =>
        "{\"version\":1,\"source\":\"claude-cli-model-usage-v1\",\"models\":["
        + string.Join(",", Enumerable.Range(0, 16).Select(index =>
            $"{{\"modelId\":\"m{index:00}{new string('x', 124)}\",\"contextWindowTokens\":2147483647,\"maxOutputTokens\":2147483647}}"))
        + "],\"padding\":\"" + new string('p', 1000) + "\"}";

    [Theory]
    [MemberData(nameof(UntrustedStoredText))]
    public async Task Stored_text_that_is_not_exactly_the_canonical_valid_snapshot_is_unknown_without_disturbing_a_healthy_sibling(string? stored)
    {
        var now = new DateTimeOffset(2026, 10, 4, 13, 0, 0, TimeSpan.Zero);
        var tamperedId = Guid.NewGuid();
        var siblingId = Guid.NewGuid();
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
            var run = await SeedRunAsync(context, now);
            await InsertAgentRowAsync(context, tamperedId, run, AttemptStatus.Failed, AgentProvider.ClaudeCode, dispatched: now, healthyStored: stored, number: 1);
            await InsertAgentRowAsync(context, siblingId, run, AttemptStatus.Failed, AgentProvider.ClaudeCode, dispatched: now, healthyStored: Healthy, number: 2);
        }

        await using var reopened = CreateContext();
        var attempts = await reopened.Attempts.AsNoTracking().OrderBy(attempt => attempt.AttemptNumber).ToListAsync();
        Assert.Equal(2, attempts.Count);
        Assert.Null(attempts[0].GetAgentModelContextLimitsEvidence());
        var sibling = attempts[1].GetAgentModelContextLimitsEvidence();
        Assert.NotNull(sibling);
        Assert.Equal(2, sibling.Models.Length);
        Assert.Equal(Healthy, sibling.Serialize());
    }

    [Fact]
    public async Task A_well_formed_snapshot_is_never_trusted_for_a_running_undispatched_wrong_provider_or_non_agent_attempt()
    {
        var now = new DateTimeOffset(2026, 10, 4, 14, 0, 0, TimeSpan.Zero);
        var ids = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray();
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
            var run = await SeedRunAsync(context, now);
            await InsertAgentRowAsync(context, ids[0], run, AttemptStatus.Running, AgentProvider.ClaudeCode, dispatched: now, healthyStored: Healthy, number: 1);
            await InsertAgentRowAsync(context, ids[1], run, AttemptStatus.Failed, AgentProvider.ClaudeCode, dispatched: null, healthyStored: Healthy, number: 2);
            await InsertAgentRowAsync(context, ids[2], run, AttemptStatus.Failed, AgentProvider.Codex, dispatched: now, healthyStored: Healthy, number: 3);
            await InsertAgentRowAsync(context, ids[3], run, AttemptStatus.Interrupted, AgentProvider.ClaudeCode, dispatched: now, healthyStored: Healthy, number: 4);
            await InsertAgentRowAsync(context, ids[4], run, AttemptStatus.Completed, AgentProvider.ClaudeCode, dispatched: now, healthyStored: Healthy, number: 5);
            await context.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO attempts
                       (Id, RunId, AttemptNumber, Kind, Status, ClaimedAtUtc, CompletedAtUtc, ProcessArguments, AgentModelContextLimitsSnapshot)
                   VALUES
                       ({ids[5]}, {run}, {6}, {nameof(AttemptKind.Process)}, {nameof(AttemptStatus.Completed)}, {now}, {now}, {""}, {Healthy})");
        }

        await using var reopened = CreateContext();
        var byId = (await reopened.Attempts.AsNoTracking().ToListAsync()).ToDictionary(attempt => attempt.Id);
        Assert.All(ids[..3], id => Assert.Null(byId[id].GetAgentModelContextLimitsEvidence()));
        // A concluded (Interrupted or Completed), dispatched Claude attempt is the only coherent shape, as for token usage.
        Assert.NotNull(byId[ids[3]].GetAgentModelContextLimitsEvidence());
        Assert.NotNull(byId[ids[4]].GetAgentModelContextLimitsEvidence());
        Assert.Null(byId[ids[5]].GetAgentModelContextLimitsEvidence());
        Assert.Equal(Healthy, byId[ids[0]].AgentModelContextLimitsSnapshot);
    }

    private static async Task<Guid> SeedRunAsync(DevalCopilotDbContext context, DateTimeOffset now)
    {
        var project = Project.Register(Guid.NewGuid(), "Limits guard", $@"C:\repos\limits-guard-{Guid.NewGuid():N}", now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Stored limits", now);
        context.Projects.Add(project);
        context.Runs.Add(run);
        await context.SaveChangesAsync();
        return run.Id;
    }

    private static Task InsertAgentRowAsync(
        DevalCopilotDbContext context, Guid attemptId, Guid runId, AttemptStatus status, AgentProvider provider,
        DateTimeOffset? dispatched, string? healthyStored, int number = 1)
    {
        var now = new DateTimeOffset(2026, 10, 4, 13, 0, 0, TimeSpan.Zero);
        DateTimeOffset? completed = status == AttemptStatus.Running ? null : now;
        return context.Database.ExecuteSqlInterpolatedAsync(
            $@"INSERT INTO attempts
                   (Id, RunId, AttemptNumber, Kind, Status, ClaimedAtUtc, CompletedAtUtc, ProcessArguments, AgentProvider,
                    AgentResponseContract, AgentDispatchedAtUtc, AgentModelContextLimitsSnapshot)
               VALUES
                   ({attemptId}, {runId}, {number}, {nameof(AttemptKind.Agent)}, {status.ToString()}, {now}, {completed}, {""},
                    {provider.ToString()}, {nameof(AgentResponseContract.CriticalReview)}, {dispatched}, {healthyStored})");
    }

    private DevalCopilotDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<DevalCopilotDbContext>().UseSqlite($"Data Source={_databasePath}").Options);
}
