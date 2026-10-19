using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// ADR-0056 persistence against the real file-backed SQLite schema and production migrations, through independent connections: the
/// additive upgrade creates one table, its indexes and two guards and gives no historical attempt an observation, the reverse drops
/// exactly them, and the real constraints and triggers refuse what the Application reads only describe (an observation of a terminal,
/// foreign or non-Agent attempt, a second observation of an attempt, a changed identity, a backward or repeated phase, a deadline
/// extended by a heartbeat, a replaced first attention, any change to a closed observation, a deleted attempt or run). The receipt and
/// its events commit in one transaction, two writers can never both apply a decision made from the same read, and everything survives
/// a reopen.
/// </summary>
public sealed class AgentSupervisionMigrationTests : IAsyncLifetime
{
    private const string PriorMigration = "20261019113314_AddPlanningSequence";

    private static readonly DateTimeOffset Now = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);
    private static readonly string[] TriggerNames = ["trg_agent_supervision_guard_insert", "trg_agent_supervision_immutable"];

    private readonly string databasePath =
        Path.Combine(Path.GetTempPath(), $"devalcopilot-agent-supervision-migration-{Guid.NewGuid():N}.db");

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={databasePath}"));
        if (File.Exists(databasePath))
        {
            File.Delete(databasePath);
        }

        return Task.CompletedTask;
    }

    private DevalCopilotDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<DevalCopilotDbContext>().UseSqlite($"Data Source={databasePath}").Options);

    private async Task MigrateToAsync(string? target)
    {
        await using var context = CreateContext();
        await context.Database.GetService<IMigrator>().MigrateAsync(target);
    }

    private async Task<List<string>> ReadNamesAsync(string type, string? like = null)
    {
        await using var probe = new SqliteConnection($"Data Source={databasePath}");
        await probe.OpenAsync();
        await using var command = probe.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = $type AND name LIKE $like ORDER BY name;";
        command.Parameters.AddWithValue("$type", type);
        command.Parameters.AddWithValue("$like", like ?? "%");
        await using var reader = await command.ExecuteReaderAsync();
        var names = new List<string>();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var probe = new SqliteConnection($"Data Source={databasePath};Foreign Keys=True;Pooling=False");
        await probe.OpenAsync();
        await using var command = probe.CreateCommand();
#pragma warning disable CA2100 // Test-only SQL assembled from constants and identifiers this test generated.
        command.CommandText = sql;
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync();
    }

    private sealed record Scene(Run Run, Attempt Attempt);

    private static async Task<Scene> SeedAsync(
        DevalCopilotDbContext context, int executionNumber = 1, AttemptKind kind = AttemptKind.Agent, bool terminal = false)
    {
        var project = Project.Register(Guid.NewGuid(), "Project", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordClassifiedIntent(Guid.NewGuid(), project.Id, executionNumber, RunExecutionMode.ManualAgent, "Objective", Now);
        var attempt = kind == AttemptKind.Agent
            ? Attempt.ClaimAgent(
                Guid.NewGuid(), run.Id, 1, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(10), 1024, 2048, Now, 1)
            : Attempt.Claim(Guid.NewGuid(), run.Id, 1, Now);
        if (terminal)
        {
            attempt.Fail(Now.AddSeconds(1));
        }

        context.Projects.Add(project);
        context.Runs.Add(run);
        context.Attempts.Add(attempt);
        await context.SaveChangesAsync();
        return new Scene(run, attempt);
    }

    private static AgentSupervision Register(Scene scene, DateTimeOffset? at = null) => AgentSupervision.Register(
        Guid.NewGuid(), scene.Run.Id, scene.Attempt.Id, Guid.NewGuid(), Guid.NewGuid(), at ?? Now);

    private static string Quote(Guid id) => id.ToString().ToUpperInvariant();

    private static string FirstMessage(Exception exception) => exception.GetBaseException().Message;

    private async Task<(Scene Scene, Guid ReceiptId)> SeedObservedAsync()
    {
        await MigrateToAsync(null);
        await using var seed = CreateContext();
        var scene = await SeedAsync(seed);
        var receipt = Register(scene);
        seed.AgentSupervisions.Add(receipt);
        await seed.SaveChangesAsync();
        return (scene, receipt.Id);
    }

    [Fact]
    public async Task The_upgrade_adds_one_table_its_indexes_and_two_guards_and_gives_no_historical_attempt_an_observation()
    {
        await MigrateToAsync(PriorMigration);
        Guid attemptId;
        await using (var seed = CreateContext())
        {
            attemptId = (await SeedAsync(seed)).Attempt.Id;
        }

        Assert.Empty(await ReadNamesAsync("table", "agent_supervisions"));

        await MigrateToAsync(null);

        Assert.Equal(["agent_supervisions"], await ReadNamesAsync("table", "agent_supervisions"));
        Assert.Equal(TriggerNames, await ReadNamesAsync("trigger", "trg_agent_supervision%"));
        Assert.Contains("ix_agent_supervisions_attempt", await ReadNamesAsync("index", "ix_agent_supervisions%"));
        Assert.Contains("ix_agent_supervisions_open", await ReadNamesAsync("index", "ix_agent_supervisions%"));
        Assert.Contains("ix_agent_supervisions_run", await ReadNamesAsync("index", "ix_agent_supervisions%"));
        await using var verify = CreateContext();
        Assert.Empty(await verify.AgentSupervisions.AsNoTracking().ToListAsync());
        Assert.Equal(AttemptStatus.Running, (await verify.Attempts.AsNoTracking().SingleAsync(attempt => attempt.Id == attemptId)).Status);
    }

    [Fact]
    public async Task The_reverse_migration_drops_exactly_the_table_and_two_guards_and_keeps_every_other_row()
    {
        var (scene, _) = await SeedObservedAsync();

        await MigrateToAsync(PriorMigration);

        Assert.Empty(await ReadNamesAsync("table", "agent_supervisions"));
        Assert.Empty(await ReadNamesAsync("trigger", "trg_agent_supervision%"));
        Assert.NotEmpty(await ReadNamesAsync("trigger", "trg_planning_sequence%"));
        await using var verify = CreateContext();
        Assert.True(await verify.Attempts.AsNoTracking().AnyAsync(candidate => candidate.Id == scene.Attempt.Id));

        await MigrateToAsync(null);
        Assert.Equal(TriggerNames, await ReadNamesAsync("trigger", "trg_agent_supervision%"));
    }

    [Fact]
    public async Task The_model_has_no_pending_changes_after_the_migration()
    {
        await MigrateToAsync(null);
        await using var context = CreateContext();

        Assert.False(context.GetService<IMigrationsAssembly>().ModelSnapshot is null);
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task An_observation_persists_exactly_across_a_reopen_with_every_time_and_identity_intact()
    {
        var (scene, receiptId) = await SeedObservedAsync();
        await using (var writer = CreateContext())
        {
            var receipt = await writer.AgentSupervisions.SingleAsync(candidate => candidate.Id == receiptId);
            receipt.RecordHeartbeat(Now.AddSeconds(5).AddTicks(1234567));
            receipt.RecordAttention(AgentSupervisionAttentionReason.PhaseDeadlineElapsed, Now.AddSeconds(6));
            receipt.AdvancePhase(AgentSupervisionPhase.AwaitingAdapterResult, Now.AddSeconds(7), Now.AddSeconds(7).AddMinutes(12));
            await writer.SaveChangesAsync();
        }

        SqliteConnection.ClearAllPools();
        await using var reopened = CreateContext();
        var stored = await reopened.AgentSupervisions.AsNoTracking().SingleAsync(candidate => candidate.Id == receiptId);

        Assert.Equal((scene.Run.Id, scene.Attempt.Id), (stored.RunId, stored.AttemptId));
        Assert.Equal(AgentSupervisionPhase.AwaitingAdapterResult, stored.Phase);
        Assert.Equal(Now, stored.StartedAtUtc);
        Assert.Equal(Now.AddSeconds(7), stored.LastHeartbeatAtUtc);
        Assert.Equal(Now.AddSeconds(7), stored.PhaseEnteredAtUtc);
        Assert.Equal(Now.AddSeconds(7).AddMinutes(12), stored.PhaseDeadlineUtc);
        Assert.Equal(AgentSupervisionAttentionReason.PhaseDeadlineElapsed, stored.AttentionReason);
        Assert.Equal(Now.AddSeconds(6), stored.AttentionAtUtc);
        Assert.Equal(4, stored.Revision);
        Assert.Null(stored.ClosedAtUtc);
    }

    [Theory]
    [InlineData("terminal")]
    [InlineData("simulated")]
    [InlineData("missing_attempt")]
    [InlineData("foreign_run")]
    public async Task An_observation_of_an_attempt_that_is_not_a_running_agent_attempt_of_its_run_is_refused(string flaw)
    {
        await MigrateToAsync(null);
        await using var seed = CreateContext();
        var scene = flaw switch
        {
            "terminal" => await SeedAsync(seed, terminal: true),
            "simulated" => await SeedAsync(seed, kind: AttemptKind.Simulated),
            _ => await SeedAsync(seed),
        };
        var other = flaw == "foreign_run" ? await SeedAsync(seed, 2) : null;
        var receipt = flaw switch
        {
            "missing_attempt" => AgentSupervision.Register(
                Guid.NewGuid(), scene.Run.Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now),
            "foreign_run" => AgentSupervision.Register(
                Guid.NewGuid(), other!.Run.Id, scene.Attempt.Id, Guid.NewGuid(), Guid.NewGuid(), Now),
            _ => Register(scene),
        };

        await using var refused = CreateContext();
        refused.AgentSupervisions.Add(receipt);
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => refused.SaveChangesAsync());

        var message = FirstMessage(exception);
        Assert.True(
            message.Contains("agent_supervision.tuple_incoherent", StringComparison.Ordinal)
            || message.Contains("FOREIGN KEY", StringComparison.OrdinalIgnoreCase),
            message);
    }

    [Fact]
    public async Task A_second_observation_of_the_same_attempt_is_refused_whatever_its_identity()
    {
        var (scene, _) = await SeedObservedAsync();

        await using var competing = CreateContext();
        competing.AgentSupervisions.Add(Register(scene));
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => competing.SaveChangesAsync());

        Assert.Contains("UNIQUE", FirstMessage(exception), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Two_independent_writers_racing_to_register_leave_exactly_one_observation()
    {
        await MigrateToAsync(null);
        Scene scene;
        await using (var seed = CreateContext())
        {
            scene = await SeedAsync(seed);
        }

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
        {
            await using var writer = CreateContext();
            writer.AgentSupervisions.Add(Register(scene));
            try
            {
                await writer.SaveChangesAsync();
                return true;
            }
            catch (DbUpdateException)
            {
                return false;
            }
        })));

        Assert.Equal(1, outcomes.Count(won => won));
        await using var verify = CreateContext();
        Assert.Equal(1, await verify.AgentSupervisions.CountAsync());
    }

    private static string InsertSql(
        Scene scene,
        Guid receiptId,
        int revision = 1,
        string phase = "Preparing",
        string heartbeat = "2026-10-19 09:00:00+00:00",
        string? attention = null,
        string? closure = null)
    {
        var attentionColumns = attention is null ? "NULL, NULL" : $"'{attention}', '2026-10-19 09:00:30+00:00'";
        var closureColumns = closure is null ? "NULL, NULL" : $"'{closure}', '2026-10-19 09:00:00+00:00'";
        return "INSERT INTO agent_supervisions (\"Id\", \"RunId\", \"AttemptId\", \"HostInstanceId\", \"ObserverId\", \"Revision\", "
            + "\"StartedAtUtc\", \"LastHeartbeatAtUtc\", \"Phase\", \"PhaseEnteredAtUtc\", \"PhaseDeadlineUtc\", \"AttentionReason\", "
            + "\"AttentionAtUtc\", \"ClosureReason\", \"ClosedAtUtc\") VALUES "
            + $"('{Quote(receiptId)}', '{Quote(scene.Run.Id)}', '{Quote(scene.Attempt.Id)}', '{Quote(Guid.NewGuid())}', "
            + $"'{Quote(Guid.NewGuid())}', {revision}, '2026-10-19 09:00:00+00:00', '{heartbeat}', '{phase}', "
            + $"'2026-10-19 09:00:00+00:00', '2026-10-19 09:02:00+00:00', {attentionColumns}, {closureColumns})";
    }

    [Fact]
    public async Task The_insert_guard_accepts_the_coherent_observation_the_domain_would_register()
    {
        await MigrateToAsync(null);
        Scene scene;
        await using (var seed = CreateContext())
        {
            scene = await SeedAsync(seed);
        }

        await ExecuteAsync(InsertSql(scene, Guid.NewGuid()));

        await using var verify = CreateContext();
        Assert.Equal(1, await verify.AgentSupervisions.CountAsync());
    }

    [Theory]
    [InlineData("revision")]
    [InlineData("phase")]
    [InlineData("heartbeat")]
    [InlineData("attention")]
    [InlineData("closure")]
    public async Task The_insert_guard_refuses_an_observation_that_does_not_start_open_at_Preparing_revision_one(string flaw)
    {
        await MigrateToAsync(null);
        Scene scene;
        await using (var seed = CreateContext())
        {
            scene = await SeedAsync(seed);
        }

        var sql = flaw switch
        {
            "revision" => InsertSql(scene, Guid.NewGuid(), revision: 2),
            "phase" => InsertSql(scene, Guid.NewGuid(), phase: "AwaitingAdapterResult"),
            "heartbeat" => InsertSql(scene, Guid.NewGuid(), heartbeat: "2026-10-19 09:00:05+00:00"),
            "attention" => InsertSql(scene, Guid.NewGuid(), attention: "stale_heartbeat"),
            _ => InsertSql(scene, Guid.NewGuid(), closure: "attempt_terminal"),
        };

        var exception = await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(sql));

        Assert.Contains("agent_supervision.tuple_incoherent", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_identity_of_an_observation_never_changes()
    {
        var (_, receiptId) = await SeedObservedAsync();
        var id = Quote(receiptId);

        foreach (var set in new[]
                 {
                     $"\"HostInstanceId\" = '{Quote(Guid.NewGuid())}'",
                     $"\"ObserverId\" = '{Quote(Guid.NewGuid())}'",
                     $"\"RunId\" = '{Quote(Guid.NewGuid())}'",
                     $"\"AttemptId\" = '{Quote(Guid.NewGuid())}'",
                     "\"StartedAtUtc\" = '2026-10-19 08:59:00+00:00'",
                 })
        {
            var exception = await Assert.ThrowsAsync<SqliteException>(
                () => ExecuteAsync($"UPDATE agent_supervisions SET {set} WHERE \"Id\" = '{id}'"));
            Assert.Contains("agent_supervision.immutable", exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_phase_moves_forward_only_and_its_deadline_is_fixed_while_it_stays()
    {
        var (_, receiptId) = await SeedObservedAsync();
        var id = Quote(receiptId);

        var extended = await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(
            $"UPDATE agent_supervisions SET \"PhaseDeadlineUtc\" = '2026-10-19 09:30:00+00:00', \"Revision\" = 2 WHERE \"Id\" = '{id}'"));
        var reentered = await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(
            $"UPDATE agent_supervisions SET \"PhaseEnteredAtUtc\" = '2026-10-19 09:00:10+00:00', \"Revision\" = 2 WHERE \"Id\" = '{id}'"));
        Assert.Contains("agent_supervision.immutable", extended.Message, StringComparison.Ordinal);
        Assert.Contains("agent_supervision.immutable", reentered.Message, StringComparison.Ordinal);

        await ExecuteAsync(
            "UPDATE agent_supervisions SET \"Phase\" = 'AwaitingAdapterResult', \"PhaseEnteredAtUtc\" = '2026-10-19 09:00:10+00:00', "
            + $"\"PhaseDeadlineUtc\" = '2026-10-19 09:12:10+00:00', \"LastHeartbeatAtUtc\" = '2026-10-19 09:00:10+00:00', \"Revision\" = 2 WHERE \"Id\" = '{id}'");
        foreach (var phase in new[] { "Preparing", "AwaitingAdapterResult" })
        {
            var backward = await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(
                $"UPDATE agent_supervisions SET \"Phase\" = '{phase}', \"PhaseEnteredAtUtc\" = '2026-10-19 09:00:20+00:00', "
                + $"\"PhaseDeadlineUtc\" = '2026-10-19 09:02:20+00:00', \"Revision\" = 3 WHERE \"Id\" = '{id}'"));
            Assert.Contains("agent_supervision.immutable", backward.Message, StringComparison.Ordinal);
        }

        await ExecuteAsync(
            "UPDATE agent_supervisions SET \"Phase\" = 'RecordingResult', \"PhaseEnteredAtUtc\" = '2026-10-19 09:00:30+00:00', "
            + $"\"PhaseDeadlineUtc\" = '2026-10-19 09:02:30+00:00', \"Revision\" = 3 WHERE \"Id\" = '{id}'");
    }

    [Fact]
    public async Task The_revision_never_decreases_and_a_heartbeat_update_is_allowed_to_advance_it()
    {
        var (_, receiptId) = await SeedObservedAsync();
        var id = Quote(receiptId);

        await ExecuteAsync($"UPDATE agent_supervisions SET \"LastHeartbeatAtUtc\" = '2026-10-19 09:00:05+00:00', \"Revision\" = 2 WHERE \"Id\" = '{id}'");
        var backward = await Assert.ThrowsAsync<SqliteException>(
            () => ExecuteAsync($"UPDATE agent_supervisions SET \"Revision\" = 1 WHERE \"Id\" = '{id}'"));

        Assert.Contains("agent_supervision.immutable", backward.Message, StringComparison.Ordinal);
        await ExecuteAsync($"UPDATE agent_supervisions SET \"Revision\" = \"Revision\" WHERE \"Id\" = '{id}'");
    }

    [Fact]
    public async Task The_first_attention_is_retained_and_a_closed_observation_never_changes_again_not_even_by_a_no_op()
    {
        var (_, receiptId) = await SeedObservedAsync();
        var id = Quote(receiptId);
        await ExecuteAsync(
            "UPDATE agent_supervisions SET \"AttentionReason\" = 'stale_heartbeat', \"AttentionAtUtc\" = '2026-10-19 09:00:30+00:00', "
            + $"\"Revision\" = 2 WHERE \"Id\" = '{id}'");

        foreach (var set in new[]
                 {
                     "\"AttentionReason\" = 'host_interrupted', \"AttentionAtUtc\" = '2026-10-19 09:00:30+00:00'",
                     "\"AttentionAtUtc\" = '2026-10-19 09:00:31+00:00'",
                     "\"AttentionReason\" = NULL, \"AttentionAtUtc\" = NULL",
                 })
        {
            var exception = await Assert.ThrowsAsync<SqliteException>(
                () => ExecuteAsync($"UPDATE agent_supervisions SET {set}, \"Revision\" = 3 WHERE \"Id\" = '{id}'"));
            Assert.True(
                exception.Message.Contains("agent_supervision.immutable", StringComparison.Ordinal)
                || exception.Message.Contains("ck_agent_supervisions_attention", StringComparison.Ordinal),
                exception.Message);
        }

        await ExecuteAsync(
            "UPDATE agent_supervisions SET \"ClosureReason\" = 'observer_ended', \"ClosedAtUtc\" = '2026-10-19 09:00:40+00:00', "
            + $"\"Revision\" = 3 WHERE \"Id\" = '{id}'");
        foreach (var set in new[] { "\"Revision\" = \"Revision\"", "\"LastHeartbeatAtUtc\" = '2026-10-19 09:00:41+00:00'", "\"ClosureReason\" = 'host_interrupted'" })
        {
            var exception = await Assert.ThrowsAsync<SqliteException>(
                () => ExecuteAsync($"UPDATE agent_supervisions SET {set} WHERE \"Id\" = '{id}'"));
            Assert.Contains("agent_supervision.immutable", exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task The_check_constraints_refuse_an_unknown_phase_reason_an_empty_identity_and_a_zero_revision()
    {
        var (_, receiptId) = await SeedObservedAsync();
        var id = Quote(receiptId);

        var phase = await Assert.ThrowsAsync<SqliteException>(
            () => ExecuteAsync($"UPDATE agent_supervisions SET \"Phase\" = 'Running' WHERE \"Id\" = '{id}'"));
        var revision = await Assert.ThrowsAsync<SqliteException>(
            () => ExecuteAsync($"UPDATE agent_supervisions SET \"Revision\" = 0 WHERE \"Id\" = '{id}'"));
        var attention = await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(
            $"UPDATE agent_supervisions SET \"AttentionReason\" = 'working', \"AttentionAtUtc\" = '2026-10-19 09:00:30+00:00', \"Revision\" = 2 WHERE \"Id\" = '{id}'"));
        var half = await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(
            $"UPDATE agent_supervisions SET \"AttentionReason\" = 'stale_heartbeat', \"Revision\" = 2 WHERE \"Id\" = '{id}'"));
        var closure = await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(
            $"UPDATE agent_supervisions SET \"ClosureReason\" = 'finished', \"ClosedAtUtc\" = '2026-10-19 09:00:30+00:00', \"Revision\" = 2 WHERE \"Id\" = '{id}'"));

        Assert.Contains("ck_agent_supervisions_phase", phase.Message, StringComparison.Ordinal);
        // The immutable guard fires first (a revision never decreases); the check constraint is the second line behind it.
        Assert.True(
            revision.Message.Contains("agent_supervision.immutable", StringComparison.Ordinal)
            || revision.Message.Contains("ck_agent_supervisions_revision", StringComparison.Ordinal),
            revision.Message);
        Assert.Contains("ck_agent_supervisions_attention", attention.Message, StringComparison.Ordinal);
        Assert.Contains("ck_agent_supervisions_attention", half.Message, StringComparison.Ordinal);
        Assert.Contains("ck_agent_supervisions_closure", closure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_observed_attempt_or_run_can_never_be_deleted_so_no_observation_is_orphaned_or_silently_dropped()
    {
        var (scene, _) = await SeedObservedAsync();

        var attempt = await Assert.ThrowsAsync<SqliteException>(
            () => ExecuteAsync($"DELETE FROM attempts WHERE \"Id\" = '{Quote(scene.Attempt.Id)}'"));
        var run = await Assert.ThrowsAsync<SqliteException>(
            () => ExecuteAsync($"DELETE FROM runs WHERE \"Id\" = '{Quote(scene.Run.Id)}'"));

        Assert.Contains("FOREIGN KEY", attempt.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FOREIGN KEY", run.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Two_writers_that_read_the_same_revision_can_never_both_apply_their_decision()
    {
        var (_, receiptId) = await SeedObservedAsync();
        await using var first = CreateContext();
        await using var second = CreateContext();
        var a = await first.AgentSupervisions.SingleAsync(candidate => candidate.Id == receiptId);
        var b = await second.AgentSupervisions.SingleAsync(candidate => candidate.Id == receiptId);
        a.RecordHeartbeat(Now.AddSeconds(5));
        b.RecordAttention(AgentSupervisionAttentionReason.StaleHeartbeat, Now.AddSeconds(30));

        await first.SaveChangesAsync();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        await using var verify = CreateContext();
        var stored = await verify.AgentSupervisions.AsNoTracking().SingleAsync(candidate => candidate.Id == receiptId);
        Assert.Equal(2, stored.Revision);
        Assert.Null(stored.AttentionReason);
        Assert.Equal(Now.AddSeconds(5), stored.LastHeartbeatAtUtc);
    }

    [Fact]
    public async Task A_receipt_change_and_its_event_commit_atomically_so_a_rejected_event_rolls_the_receipt_back()
    {
        var (scene, receiptId) = await SeedObservedAsync();
        var duplicateEventId = Guid.NewGuid();
        await using (var seed = CreateContext())
        {
            seed.Events.Add(RunEvent.Record(
                duplicateEventId, scene.Run.Id, null, RunEventType.AgentSupervisionAttention, ParticipantIdentity.ForOrchestrator(), "{}", Now));
            await seed.SaveChangesAsync();
        }

        await using (var writer = CreateContext())
        {
            var receipt = await writer.AgentSupervisions.SingleAsync(candidate => candidate.Id == receiptId);
            receipt.RecordAttention(AgentSupervisionAttentionReason.StaleHeartbeat, Now.AddSeconds(30));
            writer.Events.Add(RunEvent.Record(
                duplicateEventId, scene.Run.Id, scene.Attempt.Id, RunEventType.AgentSupervisionAttention, ParticipantIdentity.ForOrchestrator(), "{}", Now));
            await Assert.ThrowsAsync<DbUpdateException>(() => writer.SaveChangesAsync());
        }

        await using var verify = CreateContext();
        var stored = await verify.AgentSupervisions.AsNoTracking().SingleAsync(candidate => candidate.Id == receiptId);
        Assert.Null(stored.AttentionReason);
        Assert.Equal(1, stored.Revision);
        Assert.Equal(1, await verify.Events.CountAsync(candidate => candidate.RunId == scene.Run.Id));
    }

    [Fact]
    public async Task An_unrelated_run_and_its_observation_are_never_affected_by_another_runs_transitions()
    {
        await MigrateToAsync(null);
        Scene first;
        Scene second;
        Guid secondReceipt;
        await using (var seed = CreateContext())
        {
            first = await SeedAsync(seed, 1);
            second = await SeedAsync(seed, 2);
            seed.AgentSupervisions.Add(Register(first));
            var other = Register(second);
            secondReceipt = other.Id;
            seed.AgentSupervisions.Add(other);
            await seed.SaveChangesAsync();
        }

        await ExecuteAsync(
            "UPDATE agent_supervisions SET \"ClosureReason\" = 'host_interrupted', \"AttentionReason\" = 'host_interrupted', "
            + $"\"AttentionAtUtc\" = '2026-10-19 09:00:01+00:00', \"ClosedAtUtc\" = '2026-10-19 09:00:01+00:00', \"Revision\" = 2 "
            + $"WHERE \"RunId\" = '{Quote(first.Run.Id)}'");

        await using var verify = CreateContext();
        var untouched = await verify.AgentSupervisions.AsNoTracking().SingleAsync(candidate => candidate.Id == secondReceipt);
        Assert.Equal(1, untouched.Revision);
        Assert.Null(untouched.ClosedAtUtc);
    }
}
