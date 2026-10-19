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
/// ADR-0055 persistence against the real file-backed SQLite schema and production migrations, through independent connections: the
/// additive upgrade creates only two tables and eight guards and gives no historical row a sequence or a permission, the reverse drops
/// exactly them, the real constraints and write-seam triggers refuse what the Application reads only describe (a second sequence for a
/// run, an impure run, an incoherent tuple, a second outstanding step, an attempt or abandonment past a live sequence, mutation of an
/// authorization, an ended sequence or a concluded step), and an unrelated project is never affected.
/// </summary>
public sealed class PlanningSequenceMigrationTests : IAsyncLifetime
{
    private const string PriorMigration = "20261014215118_AddGitHubBranchPublicationApproval";
    private const string BlockedMessage = "planning_sequence.manual_control_blocked";

    private static readonly DateTimeOffset Now = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);
    private static readonly string[] TriggerNames =
    [
        "trg_planning_sequence_guard_arming",
        "trg_planning_sequence_guard_attempts",
        "trg_planning_sequence_guard_local_commit_operations",
        "trg_planning_sequence_guard_run_abandonment",
        "trg_planning_sequence_guard_tuple",
        "trg_planning_sequence_guard_verification_executions",
        "trg_planning_sequence_immutable",
        "trg_planning_sequence_step_immutable",
    ];

    private readonly string databasePath =
        Path.Combine(Path.GetTempPath(), $"devalcopilot-planning-sequence-migration-{Guid.NewGuid():N}.db");

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

    private sealed record Scene(Run Run, GitWorkspace Workspace, GitCheckpoint Checkpoint);

    private static async Task<Scene> SeedSceneAsync(DevalCopilotDbContext context, int executionNumber = 1)
    {
        var project = Project.Register(Guid.NewGuid(), "Project", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordClassifiedIntent(Guid.NewGuid(), project.Id, executionNumber, RunExecutionMode.ManualAgent, "Objective", Now);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", Now);
        workspace.MarkReady();
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, workspace.ReserveCheckpointNumber(), Now, new string('a', 40), Fingerprint, []);
        context.Projects.Add(project);
        context.Runs.Add(run);
        context.GitWorkspaces.Add(workspace);
        context.GitCheckpoints.Add(checkpoint);
        await context.SaveChangesAsync();
        return new Scene(run, workspace, checkpoint);
    }

    private static PlanningSequence Arm(Scene scene) => PlanningSequence.Arm(
        Guid.NewGuid(), scene.Run.Id, scene.Run.ProjectId, scene.Workspace.Id, scene.Checkpoint.Id, Fingerprint, new string('b', 64), Now);

    private static Attempt Planner(Guid runId, Guid attemptId, int number = 1) => Attempt.ClaimAgent(
        attemptId, runId, number, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(10), 1024, 2048, Now, number);

    private static string FirstMessage(Exception exception) => exception.GetBaseException().Message;

    [Fact]
    public async Task The_upgrade_adds_two_tables_and_eight_guards_and_gives_no_historical_row_a_sequence()
    {
        await MigrateToAsync(PriorMigration);
        Guid runId;
        await using (var seed = CreateContext())
        {
            var scene = await SeedSceneAsync(seed);
            runId = scene.Run.Id;
            seed.Attempts.Add(Planner(scene.Run.Id, Guid.NewGuid()));
            await seed.SaveChangesAsync();
        }

        Assert.Empty(await ReadNamesAsync("table", "planning_sequence%"));

        await MigrateToAsync(null);

        Assert.Equal(["planning_sequence_steps", "planning_sequences"], await ReadNamesAsync("table", "planning_sequence%"));
        Assert.Equal(TriggerNames, await ReadNamesAsync("trigger", "trg_planning_sequence%"));
        await using var verify = CreateContext();
        Assert.Empty(await verify.PlanningSequences.AsNoTracking().ToListAsync());
        Assert.Empty(await verify.PlanningSequenceSteps.AsNoTracking().ToListAsync());
        Assert.Single(await verify.Attempts.AsNoTracking().Where(attempt => attempt.RunId == runId).ToListAsync());
        var run = await verify.Runs.AsNoTracking().SingleAsync(candidate => candidate.Id == runId);
        Assert.Equal(RunLifecycle.Created, run.Lifecycle);
    }

    [Fact]
    public async Task The_reverse_migration_drops_exactly_the_two_tables_and_eight_guards_and_keeps_every_other_row()
    {
        await MigrateToAsync(null);
        Guid runId;
        await using (var seed = CreateContext())
        {
            var scene = await SeedSceneAsync(seed);
            runId = scene.Run.Id;
            seed.PlanningSequences.Add(Arm(scene));
            await seed.SaveChangesAsync();
        }

        await MigrateToAsync(PriorMigration);

        Assert.Empty(await ReadNamesAsync("table", "planning_sequence%"));
        Assert.Empty(await ReadNamesAsync("trigger", "trg_planning_sequence%"));
        Assert.NotEmpty(await ReadNamesAsync("trigger", "trg_local_commit%"));
        await using var verify = CreateContext();
        Assert.True(await verify.Runs.AsNoTracking().AnyAsync(candidate => candidate.Id == runId));

        await MigrateToAsync(null);
        Assert.Equal(TriggerNames, await ReadNamesAsync("trigger", "trg_planning_sequence%"));
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
    public async Task A_run_can_never_have_a_second_sequence_even_after_the_first_ended()
    {
        await MigrateToAsync(null);
        Scene scene;
        await using (var seed = CreateContext())
        {
            scene = await SeedSceneAsync(seed);
            var first = Arm(scene);
            seed.PlanningSequences.Add(first);
            await seed.SaveChangesAsync();
            first.End(PlanningSequenceState.Stopped, PlanningSequenceReason.StoppedBeforeClaim, null, Now);
            await seed.SaveChangesAsync();
        }

        await using var refused = CreateContext();
        refused.PlanningSequences.Add(Arm(scene));
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => refused.SaveChangesAsync());
        Assert.Contains("UNIQUE", FirstMessage(exception), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("attempt")]
    [InlineData("message")]
    [InlineData("not_created")]
    public async Task Arming_is_refused_by_the_database_for_a_run_that_is_not_pristine(string impurity)
    {
        await MigrateToAsync(null);
        Scene scene;
        await using (var seed = CreateContext())
        {
            scene = await SeedSceneAsync(seed);
            switch (impurity)
            {
                case "attempt":
                    seed.Attempts.Add(Planner(scene.Run.Id, Guid.NewGuid()));
                    break;
                case "message":
                    seed.Attempts.Add(Planner(scene.Run.Id, Guid.NewGuid()));
                    await seed.SaveChangesAsync();
                    seed.CollaborationMessages.Add(CollaborationMessage.Record(
                        Guid.NewGuid(), scene.Run.Id, seed.Attempts.Local.Single().Id, CollaborationMessage.ProtocolVersionOne,
                        ParticipantIdentity.ForAgent(AgentRole.Planner, AgentProvider.Codex),
                        ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), CollaborationMessageType.Proposal, null,
                        "Plan.", "{\"scope\":\"s\",\"implementationSteps\":\"i\",\"risks\":\"r\",\"verificationPlan\":\"v\",\"escalationPoints\":\"e\"}",
                        CollaborationMessageProvenance.ProviderObserved, Now));
                    break;
                default:
                    scene.Run.Claim(Now);
                    break;
            }

            await seed.SaveChangesAsync();
        }

        await using var refused = CreateContext();
        refused.PlanningSequences.Add(Arm(scene));
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => refused.SaveChangesAsync());
        Assert.Contains("planning_sequence.run_not_pristine", FirstMessage(exception), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Arming_is_refused_for_a_workspace_or_checkpoint_that_does_not_belong_to_the_runs_project()
    {
        await MigrateToAsync(null);
        Scene scene;
        Scene other;
        await using (var seed = CreateContext())
        {
            scene = await SeedSceneAsync(seed, 1);
            other = await SeedSceneAsync(seed, 2);
        }

        await using (var foreignWorkspace = CreateContext())
        {
            foreignWorkspace.PlanningSequences.Add(PlanningSequence.Arm(
                Guid.NewGuid(), scene.Run.Id, scene.Run.ProjectId, other.Workspace.Id, other.Checkpoint.Id, Fingerprint, new string('b', 64), Now));
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => foreignWorkspace.SaveChangesAsync());
            Assert.Contains("planning_sequence.tuple_incoherent", FirstMessage(exception), StringComparison.Ordinal);
        }

        await using var foreignCheckpoint = CreateContext();
        foreignCheckpoint.PlanningSequences.Add(PlanningSequence.Arm(
            Guid.NewGuid(), scene.Run.Id, scene.Run.ProjectId, scene.Workspace.Id, other.Checkpoint.Id, Fingerprint, new string('b', 64), Now));
        var checkpointException = await Assert.ThrowsAsync<DbUpdateException>(() => foreignCheckpoint.SaveChangesAsync());
        Assert.Contains("planning_sequence.tuple_incoherent", FirstMessage(checkpointException), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_check_constraints_refuse_an_unknown_state_a_non_lowercase_fingerprint_and_a_zero_revision()
    {
        await MigrateToAsync(null);
        Guid sequenceId;
        await using (var seed = CreateContext())
        {
            var scene = await SeedSceneAsync(seed);
            var sequence = Arm(scene);
            sequenceId = sequence.Id;
            seed.PlanningSequences.Add(sequence);
            await seed.SaveChangesAsync();
        }

        var state = await Assert.ThrowsAsync<SqliteException>(
            () => ExecuteAsync($"UPDATE planning_sequences SET \"State\" = 'Running' WHERE \"Id\" = '{sequenceId.ToString().ToUpperInvariant()}'"));
        Assert.Contains("ck_planning_sequences_state", state.Message, StringComparison.Ordinal);
        var revision = await Assert.ThrowsAsync<SqliteException>(
            () => ExecuteAsync($"UPDATE planning_sequences SET \"Revision\" = 0 WHERE \"Id\" = '{sequenceId.ToString().ToUpperInvariant()}'"));
        Assert.Contains("ck_planning_sequences_revision", revision.Message, StringComparison.Ordinal);

        await using var upper = CreateContext();
        var scene2 = await SeedSceneAsync(upper, 2);
        upper.PlanningSequences.Add(PlanningSequence.Arm(
            Guid.NewGuid(), scene2.Run.Id, scene2.Run.ProjectId, scene2.Workspace.Id, scene2.Checkpoint.Id, Fingerprint, new string('b', 64), Now));
        await upper.SaveChangesAsync();
        await using var forged = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await forged.OpenAsync();
        await using var command = forged.CreateCommand();
#pragma warning disable CA2100 // Test-only SQL assembled from constants and identifiers this test generated.
        command.CommandText = $"UPDATE planning_sequences SET \"FingerprintSha256\" = '{new string('A', 64)}' WHERE \"RunId\" = '{scene2.Run.Id.ToString().ToUpperInvariant()}'";
#pragma warning restore CA2100
        var fingerprint = await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync());
        Assert.Contains("planning_sequence.immutable", fingerprint.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_authorization_tuple_is_immutable_and_an_ended_sequence_never_changes_again()
    {
        await MigrateToAsync(null);
        Guid sequenceId;
        Scene scene;
        await using (var seed = CreateContext())
        {
            scene = await SeedSceneAsync(seed);
            var sequence = Arm(scene);
            sequenceId = sequence.Id;
            seed.PlanningSequences.Add(sequence);
            await seed.SaveChangesAsync();
        }

        var id = sequenceId.ToString().ToUpperInvariant();
        var retarget = await Assert.ThrowsAsync<SqliteException>(
            () => ExecuteAsync($"UPDATE planning_sequences SET \"WorkspaceId\" = '{Guid.NewGuid().ToString().ToUpperInvariant()}' WHERE \"Id\" = '{id}'"));
        Assert.Contains("planning_sequence.immutable", retarget.Message, StringComparison.Ordinal);
        var rebind = await Assert.ThrowsAsync<SqliteException>(
            () => ExecuteAsync($"UPDATE planning_sequences SET \"ObjectiveSha256\" = '{new string('c', 64)}' WHERE \"Id\" = '{id}'"));
        Assert.Contains("planning_sequence.immutable", rebind.Message, StringComparison.Ordinal);

        await using (var end = CreateContext())
        {
            var sequence = await end.PlanningSequences.SingleAsync(candidate => candidate.Id == sequenceId);
            sequence.End(PlanningSequenceState.NeedsAttention, PlanningSequenceReason.AuthorityChanged, null, Now);
            await end.SaveChangesAsync();
        }

        // No rearm, resume or reopen: every later write to an ended sequence is refused, including a no-op write.
        var reopen = await Assert.ThrowsAsync<SqliteException>(
            () => ExecuteAsync($"UPDATE planning_sequences SET \"State\" = 'Armed' WHERE \"Id\" = '{id}'"));
        Assert.Contains("planning_sequence.immutable", reopen.Message, StringComparison.Ordinal);
        var noop = await Assert.ThrowsAsync<SqliteException>(
            () => ExecuteAsync($"UPDATE planning_sequences SET \"Revision\" = \"Revision\" WHERE \"Id\" = '{id}'"));
        Assert.Contains("planning_sequence.immutable", noop.Message, StringComparison.Ordinal);
    }

    private static PlanningSequenceStep Reserve(Guid sequenceId, int ordinal, Guid? predecessor = null, Guid? input = null) =>
        PlanningSequenceStep.Reserve(
            Guid.NewGuid(), sequenceId, ordinal, ordinal == 0 ? null : predecessor ?? Guid.NewGuid(), ordinal == 0 ? null : input ?? Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Now);

    [Fact]
    public async Task A_sequence_has_at_most_one_outstanding_step_one_step_per_ordinal_and_only_ordinals_zero_to_four()
    {
        await MigrateToAsync(null);
        Guid sequenceId;
        await using (var seed = CreateContext())
        {
            var scene = await SeedSceneAsync(seed);
            var sequence = Arm(scene);
            sequenceId = sequence.Id;
            seed.PlanningSequences.Add(sequence);
            await seed.SaveChangesAsync();
            seed.PlanningSequenceSteps.Add(Reserve(sequence.Id, 0));
            await seed.SaveChangesAsync();
        }

        // A second Reserved step of the same sequence is refused by the filtered unique index (never two outstanding).
        await using (var second = CreateContext())
        {
            var attemptId = Guid.NewGuid();
            second.PlanningSequenceSteps.Add(PlanningSequenceStep.Reserve(
                Guid.NewGuid(), sequenceId, 1, attemptId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now));
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
            Assert.Contains("UNIQUE", FirstMessage(exception), StringComparison.OrdinalIgnoreCase);
        }

        // The same ordinal twice is refused even when the first step is no longer outstanding.
        await using (var revoke = CreateContext())
        {
            var step = await revoke.PlanningSequenceSteps.SingleAsync(candidate => candidate.SequenceId == sequenceId);
            step.Revoke(PlanningSequenceReason.StoppedBeforeClaim, Now);
            await revoke.SaveChangesAsync();
        }

        await using (var duplicate = CreateContext())
        {
            duplicate.PlanningSequenceSteps.Add(Reserve(sequenceId, 0));
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
            Assert.Contains("UNIQUE", FirstMessage(exception), StringComparison.OrdinalIgnoreCase);
        }

        // There is no sixth step: the ordinal check refuses a raw insert (the domain would throw first).
        var sixth = await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(
            "INSERT INTO planning_sequence_steps (\"Id\", \"SequenceId\", \"Ordinal\", \"Role\", \"State\", \"PredecessorAttemptId\", "
            + "\"InputProposalMessageId\", \"ReservedAttemptId\", \"AttemptId\", \"PreparationOwnerToken\", \"PreparationReservedAtUtc\", "
            + $"\"PreparationDeadlineUtc\", \"ReasonCode\", \"Revision\", \"UpdatedAtUtc\") VALUES ('{Guid.NewGuid()}', '{sequenceId.ToString().ToUpperInvariant()}', 5, "
            + $"'Planner', 'Revoked', NULL, NULL, '{Guid.NewGuid()}', NULL, '{Guid.NewGuid()}', '2026-10-19 09:00:00', '2026-10-19 09:02:00', NULL, 1, '2026-10-19 09:00:00')"));
        Assert.Contains("ck_planning_sequence_steps_ordinal", sixth.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_step_role_must_match_its_ordinal_and_a_link_requires_a_claimed_or_concluded_state()
    {
        await MigrateToAsync(null);
        Guid sequenceId;
        await using (var seed = CreateContext())
        {
            var scene = await SeedSceneAsync(seed);
            var sequence = Arm(scene);
            sequenceId = sequence.Id;
            seed.PlanningSequences.Add(sequence);
            await seed.SaveChangesAsync();
        }

        string Insert(int ordinal, string role, string state, string? attemptId) =>
            "INSERT INTO planning_sequence_steps (\"Id\", \"SequenceId\", \"Ordinal\", \"Role\", \"State\", \"PredecessorAttemptId\", "
            + "\"InputProposalMessageId\", \"ReservedAttemptId\", \"AttemptId\", \"PreparationOwnerToken\", \"PreparationReservedAtUtc\", "
            + $"\"PreparationDeadlineUtc\", \"ReasonCode\", \"Revision\", \"UpdatedAtUtc\") VALUES ('{Guid.NewGuid()}', '{sequenceId.ToString().ToUpperInvariant()}', {ordinal}, "
            + $"'{role}', '{state}', NULL, NULL, '{Guid.NewGuid()}', {(attemptId is null ? "NULL" : $"'{attemptId}'")}, '{Guid.NewGuid()}', "
            + "'2026-10-19 09:00:00', '2026-10-19 09:02:00', NULL, 1, '2026-10-19 09:00:00')";

        var role = await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(Insert(0, "Resolver", "Revoked", null)));
        Assert.Contains("ck_planning_sequence_steps_role", role.Message, StringComparison.Ordinal);
        var state = await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(Insert(0, "Planner", "Claimed", null)));
        Assert.Contains("ck_planning_sequence_steps_attempt_link", state.Message, StringComparison.Ordinal);
        var predecessor = await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(Insert(1, "CriticalReviewer", "Revoked", null)));
        Assert.Contains("ck_planning_sequence_steps_predecessor", predecessor.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_step_identity_is_immutable_and_a_concluded_step_never_changes_again()
    {
        await MigrateToAsync(null);
        PlanningSequenceStep step;
        await using (var seed = CreateContext())
        {
            var scene = await SeedSceneAsync(seed);
            var sequence = Arm(scene);
            seed.PlanningSequences.Add(sequence);
            await seed.SaveChangesAsync();
            step = Reserve(sequence.Id, 0);
            seed.PlanningSequenceSteps.Add(step);
            await seed.SaveChangesAsync();
            seed.Attempts.Add(Planner(scene.Run.Id, step.ReservedAttemptId));
            await seed.SaveChangesAsync();
        }

        var id = step.Id.ToString().ToUpperInvariant();
        var token = await Assert.ThrowsAsync<SqliteException>(
            () => ExecuteAsync($"UPDATE planning_sequence_steps SET \"PreparationOwnerToken\" = '{Guid.NewGuid()}' WHERE \"Id\" = '{id}'"));
        Assert.Contains("planning_sequence.immutable", token.Message, StringComparison.Ordinal);

        // Reserved cannot jump straight to Concluded, and a claimed step moves only to Concluded.
        var jump = await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(
            $"UPDATE planning_sequence_steps SET \"State\" = 'Concluded', \"AttemptId\" = \"ReservedAttemptId\" WHERE \"Id\" = '{id}'"));
        Assert.Contains("planning_sequence.immutable", jump.Message, StringComparison.Ordinal);

        await using (var claim = CreateContext())
        {
            var tracked = await claim.PlanningSequenceSteps.SingleAsync(candidate => candidate.Id == step.Id);
            tracked.MarkClaimed(tracked.ReservedAttemptId, Now);
            await claim.SaveChangesAsync();
            tracked.Conclude(Now.AddMinutes(1));
            await claim.SaveChangesAsync();
        }

        var after = await Assert.ThrowsAsync<SqliteException>(
            () => ExecuteAsync($"UPDATE planning_sequence_steps SET \"ReasonCode\" = 'step_failed' WHERE \"Id\" = '{id}'"));
        Assert.Contains("planning_sequence.immutable", after.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_live_sequence_refuses_every_attempt_of_its_run_except_the_exact_reserved_attempt_of_a_reserved_step()
    {
        await MigrateToAsync(null);
        Scene scene;
        PlanningSequenceStep step;
        Scene unrelated;
        await using (var seed = CreateContext())
        {
            scene = await SeedSceneAsync(seed, 1);
            unrelated = await SeedSceneAsync(seed, 2);
            var sequence = Arm(scene);
            seed.PlanningSequences.Add(sequence);
            await seed.SaveChangesAsync();
            step = Reserve(sequence.Id, 0);
            seed.PlanningSequenceSteps.Add(step);
            await seed.SaveChangesAsync();
        }

        await using (var manual = CreateContext())
        {
            manual.Attempts.Add(Planner(scene.Run.Id, Guid.NewGuid()));
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => manual.SaveChangesAsync());
            Assert.Contains(BlockedMessage, FirstMessage(exception), StringComparison.Ordinal);
        }

        // An unrelated project's run is never blocked.
        await using (var other = CreateContext())
        {
            other.Attempts.Add(Planner(unrelated.Run.Id, Guid.NewGuid()));
            await other.SaveChangesAsync();
        }

        // The exact reserved attempt of the still-Reserved step is admitted (the claim then links it in the same transaction).
        await using (var claim = CreateContext())
        {
            claim.Attempts.Add(Planner(scene.Run.Id, step.ReservedAttemptId));
            await claim.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task An_ended_sequence_releases_its_run_unless_a_linked_attempt_is_still_running()
    {
        await MigrateToAsync(null);
        Scene scene;
        PlanningSequenceStep step;
        await using (var seed = CreateContext())
        {
            scene = await SeedSceneAsync(seed);
            var sequence = Arm(scene);
            seed.PlanningSequences.Add(sequence);
            await seed.SaveChangesAsync();
            step = Reserve(sequence.Id, 0);
            seed.PlanningSequenceSteps.Add(step);
            await seed.SaveChangesAsync();
            seed.Attempts.Add(Planner(scene.Run.Id, step.ReservedAttemptId));
            await seed.SaveChangesAsync();
        }

        await using (var link = CreateContext())
        {
            var sequence = await link.PlanningSequences.SingleAsync(candidate => candidate.RunId == scene.Run.Id);
            var tracked = await link.PlanningSequenceSteps.SingleAsync(candidate => candidate.Id == step.Id);
            sequence.BeginPreparing(Now);
            sequence.MarkAwaitingResult(Now);
            tracked.MarkClaimed(tracked.ReservedAttemptId, Now);
            await link.SaveChangesAsync();
            sequence.End(PlanningSequenceState.NeedsAttention, PlanningSequenceReason.ResultDeadlineElapsed, null, Now);
            await link.SaveChangesAsync();
        }

        // The sequence ended as NeedsAttention but its linked attempt is still Running: the run stays held.
        await using (var blocked = CreateContext())
        {
            var run = await blocked.Runs.SingleAsync(candidate => candidate.Id == scene.Run.Id);
            run.Claim(Now);
            await blocked.SaveChangesAsync();
            run.Abandon("Stop here.", Now.AddMinutes(1));
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => blocked.SaveChangesAsync());
            Assert.Contains(BlockedMessage, FirstMessage(exception), StringComparison.Ordinal);
        }

        // Once that attempt is no longer Running the fence releases and abandonment is admitted.
        await using (var resolve = CreateContext())
        {
            var attempt = await resolve.Attempts.SingleAsync(candidate => candidate.Id == step.ReservedAttemptId);
            attempt.CompletePlanningSequenceDispatchRefused(Now.AddMinutes(2));
            await resolve.SaveChangesAsync();
        }

        await using var released = CreateContext();
        var abandon = await released.Runs.SingleAsync(candidate => candidate.Id == scene.Run.Id);
        abandon.Abandon("Stop here.", Now.AddMinutes(3));
        await released.SaveChangesAsync();
    }
}
