using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// ADR-0062 persistence against the real file-backed SQLite schema: the additive migration (historical sequences stay Immediate, receive only a
/// stable order and no queue authority; the counter starts at their count), its reverse (a waiting cycle closes as a restart does, nothing
/// else is lost), every named constraint, and the guards of the queue bound, the admission lifecycle, the step-to-admission link, the order
/// counter and the waiting state's unrelated-start fence with its single result-checkpoint exception.
/// </summary>
public sealed class LocalExecutionQueueMigrationTests : LocalExecutionSequenceMigrationTestBase
{
    private static readonly string[] AdmissionTriggerNames =
    [
        "trg_local_execution_admission_immutable",
        "trg_local_execution_admission_insert_bound",
        "trg_local_execution_admission_insert_shape",
    ];

    private static Dictionary<string, string> HistoricalSequence(Scene scene, DateTimeOffset armedAt)
    {
        var row = RawSequence(scene);
        row.Remove("AdmissionMode");
        row.Remove("CoordinationNumber");
        row["ArmedAtUtc"] = Sql(armedAt);
        row["AdvancementDeadlineUtc"] = Sql(armedAt.AddHours(2));
        row["UpdatedAtUtc"] = Sql(armedAt);
        return row;
    }

    private static Dictionary<string, string> QueuedSequence(Scene scene, string state = "WaitingForAdmission", Guid? id = null)
    {
        var row = RawSequence(scene, id);
        row["AdmissionMode"] = "'WaitForAdmission'";
        row["State"] = Quote(state);
        return row;
    }

    private static Dictionary<string, string> WaitingAdmission(Scene scene, Guid sequenceId, long number = 2, int ordinal = 0, Guid? id = null) => new()
    {
        ["Id"] = Q(id ?? Guid.NewGuid()),
        ["SequenceId"] = Q(sequenceId),
        ["AdmissionNumber"] = number.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["Ordinal"] = ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["Kind"] = "'Implementation'",
        ["PassNumber"] = "0",
        ["RecipeOrdinal"] = "NULL",
        ["PredecessorStepId"] = "NULL",
        ["SourceCheckpointId"] = Q(scene.Checkpoint.Id),
        ["SourceFingerprintSha256"] = Quote(Fingerprint),
        ["ReportMessageId"] = "NULL",
        ["SubjectAttemptId"] = "NULL",
        ["State"] = "'Waiting'",
        ["StepId"] = "NULL",
        ["WaitingReason"] = "'AwaitingAdmissionCheck'",
        ["WaitingReasonRecordedAtUtc"] = Sql(Now),
        ["EnqueuedAtUtc"] = Sql(Now),
        ["ReasonCode"] = "NULL",
        ["Revision"] = "1",
        ["UpdatedAtUtc"] = Sql(Now),
    };

    private async Task<(Scene Scene, Guid SequenceId)> QueuedAsync()
    {
        await using var seed = CreateContext();
        var scene = await SeedSceneAsync(seed);
        await seed.SaveChangesAsync();
        var sequenceId = Guid.NewGuid();
        await ExecuteAsync(Insert("local_execution_sequences", QueuedSequence(scene, id: sequenceId)));
        for (var ordinal = 0; ordinal < 2; ordinal++)
        {
            await ExecuteAsync(Insert("local_execution_sequence_recipes", RawRecipe(sequenceId, ordinal)));
        }

        return (scene, sequenceId);
    }

    private async Task<Scene> NewSceneAsync(int runNumber)
    {
        await using var seed = CreateContext();
        var scene = await SeedSceneAsync(seed, runNumber);
        await seed.SaveChangesAsync();
        return scene;
    }

    // ---- upgrade and reverse ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_upgrade_keeps_every_historical_sequence_immediate_orders_them_by_arming_and_grants_no_queue_authority()
    {
        await MigrateToAsync(SequenceMigration);
        var older = await NewSceneAsync(1);
        var younger = await NewSceneAsync(2);
        var olderId = Guid.NewGuid();
        var youngerId = Guid.NewGuid();
        await ExecuteAsync(Insert("local_execution_sequences", HistoricalSequence(younger, Now.AddMinutes(10)).With("Id", Q(youngerId))));
        await ExecuteAsync(Insert("local_execution_sequences", HistoricalSequence(older, Now).With("Id", Q(olderId))));
        for (var ordinal = 0; ordinal < 2; ordinal++)
        {
            await ExecuteAsync(Insert("local_execution_sequence_recipes", RawRecipe(olderId, ordinal)));
        }

        Assert.Empty(await ReadNamesAsync("table", "local_execution_admissions"));

        await MigrateToAsync(QueueMigration);

        Assert.Equal(["local_execution_admissions", "local_execution_order_counters"], await ReadNamesAsync("table", "local_execution_a%").ContinueWith(
            names => names.Result.Concat(ReadNamesAsync("table", "local_execution_o%").Result).ToList()));
        Assert.Equal(TriggerNames, await ReadNamesAsync("trigger", "trg_local_execution%"));
        Assert.Equal(2, await CountAsync("SELECT COUNT(*) FROM local_execution_sequences WHERE \"AdmissionMode\" = 'Immediate'"));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM local_execution_sequences WHERE \"AdmissionMode\" <> 'Immediate'"));
        Assert.Equal(1, await CountAsync($"SELECT \"CoordinationNumber\" FROM local_execution_sequences WHERE \"Id\" = {Q(olderId)}"));
        Assert.Equal(2, await CountAsync($"SELECT \"CoordinationNumber\" FROM local_execution_sequences WHERE \"Id\" = {Q(youngerId)}"));
        Assert.Equal(2, await CountAsync("SELECT \"LastNumber\" FROM local_execution_order_counters WHERE \"Id\" = 1"));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM local_execution_admissions"));
        Assert.Equal(2, await CountAsync("SELECT COUNT(*) FROM local_execution_sequence_recipes"));
        Assert.Equal(2, await CountAsync("SELECT COUNT(*) FROM local_execution_sequences WHERE \"State\" = 'Armed'"));

        await using var verify = CreateContext();
        var read = await verify.LocalExecutionSequences.AsNoTracking().OrderBy(sequence => sequence.CoordinationNumber).ToListAsync();
        Assert.Equal([olderId, youngerId], read.Select(sequence => sequence.Id));
        Assert.All(read, sequence => Assert.Equal(LocalExecutionAdmissionMode.Immediate, sequence.AdmissionMode));
        Assert.Equal(LocalExecutionSequenceState.Armed, read[0].State);
    }

    [Fact]
    public async Task The_reverse_migration_closes_a_waiting_cycle_like_a_restart_and_restores_exactly_the_previous_guards_and_rows()
    {
        await MigrateToAsync(QueueMigration);
        var (scene, queuedId) = await QueuedAsync();
        var immediate = await NewSceneAsync(2);
        var immediateId = Guid.NewGuid();
        await ExecuteAsync(Insert("local_execution_sequences", RawSequence(immediate, immediateId)));
        await ExecuteAsync(Insert("local_execution_admissions", WaitingAdmission(scene, queuedId)));
        var tablesBefore = await ReadNamesAsync("table");

        await MigrateToAsync(SequenceMigration);

        Assert.Empty(await ReadNamesAsync("table", "local_execution_admissions"));
        Assert.Empty(await ReadNamesAsync("table", "local_execution_order_counters"));
        Assert.Equal(tablesBefore.Except(["local_execution_admissions", "local_execution_order_counters"]).OrderBy(name => name), await ReadNamesAsync("table"));
        Assert.Equal(SequenceTriggerNames, await ReadNamesAsync("trigger", "trg_local_execution%"));
        Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM local_execution_sequences WHERE \"Id\" = {Q(queuedId)} AND \"State\" = 'Interrupted' AND \"ReasonCode\" = 'host_restarted' AND \"EndedAtUtc\" IS NOT NULL"));
        Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM local_execution_sequences WHERE \"Id\" = {Q(immediateId)} AND \"State\" = 'Armed'"));
        Assert.Equal(2, await CountAsync("SELECT COUNT(*) FROM local_execution_sequences"));
        Assert.Equal(2, await CountAsync($"SELECT COUNT(*) FROM local_execution_sequence_recipes WHERE \"SequenceId\" = {Q(queuedId)}"));

        // The previous guard still works after the reverse: an attempt of an Armed sequence's run is refused.
        await using (var refused = CreateContext())
        {
            refused.Attempts.Add(PlannerAttempt(immediate, Guid.NewGuid()));
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => refused.SaveChangesAsync());
            Assert.Contains(BlockedMessage, FirstMessage(exception), StringComparison.Ordinal);
        }

        await MigrateToAsync(QueueMigration);
        Assert.Equal(TriggerNames, await ReadNamesAsync("trigger", "trg_local_execution%"));
        Assert.Equal(2, await CountAsync("SELECT COUNT(*) FROM local_execution_sequences"));
        Assert.Equal(2, await CountAsync("SELECT COUNT(*) FROM local_execution_sequences WHERE \"CoordinationNumber\" >= 1"));
    }

    private async Task<(Guid SequenceId, Guid AdmissionId)> HeldAdmissionAsync(string state)
    {
        var (scene, sequenceId) = await QueuedAsync();
        var admissionId = Guid.NewGuid();
        await ExecuteAsync(Insert("local_execution_admissions", WaitingAdmission(scene, sequenceId, id: admissionId)));
        var step = RawStep(scene, sequenceId, 0, LocalExecutionStepKind.Implementation);
        step["State"] = "'Reserved'";
        step["ReasonCode"] = "NULL";
        step["PredecessorStepId"] = "NULL";
        step["ReportMessageId"] = "NULL";
        await ExecuteAsync(Insert("local_execution_sequence_steps", step));
        await ExecuteAsync($"UPDATE local_execution_admissions SET \"State\" = 'Reserved', \"StepId\" = {step["Id"]} WHERE \"Id\" = {Q(admissionId)}");
        if (state == "Unresolved")
        {
            await ExecuteAsync($"UPDATE local_execution_sequence_steps SET \"State\" = 'Unresolved', \"ReasonCode\" = 'persistence_unresolved' WHERE \"Id\" = {step["Id"]}");
            await ExecuteAsync($"UPDATE local_execution_admissions SET \"State\" = 'Unresolved', \"ReasonCode\" = 'persistence_unresolved' WHERE \"Id\" = {Q(admissionId)}");
        }

        return (sequenceId, admissionId);
    }

    [Theory]
    [InlineData("Reserved")]
    [InlineData("Unresolved")]
    public async Task The_reverse_migration_is_refused_while_a_held_admission_exists_and_changes_nothing(string state)
    {
        await MigrateToAsync(QueueMigration);
        var (sequenceId, admissionId) = await HeldAdmissionAsync(state);
        var tablesBefore = await ReadNamesAsync("table");
        var triggersBefore = await ReadNamesAsync("trigger", "trg_local_execution%");
        var historyBefore = await CountAsync("SELECT COUNT(*) FROM \"__EFMigrationsHistory\"");

        var refusal = await Assert.ThrowsAnyAsync<Exception>(() => MigrateToAsync(SequenceMigration));

        Assert.Contains("held_reservations_block_downgrade", refusal.ToString(), StringComparison.Ordinal);
        SqliteConnection.ClearAllPools();
        Assert.Equal(tablesBefore, await ReadNamesAsync("table"));
        Assert.Equal(triggersBefore, await ReadNamesAsync("trigger", "trg_local_execution%"));
        Assert.Equal(AdmissionTriggerNames.Length, (await ReadNamesAsync("trigger", "trg_local_execution_admission%")).Count());
        Assert.Equal(historyBefore, await CountAsync("SELECT COUNT(*) FROM \"__EFMigrationsHistory\""));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" LIKE '%AddLocalExecutionQueue'"));
        Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM local_execution_admissions WHERE \"Id\" = {Q(admissionId)} AND \"State\" = '{state}'"));
        Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM local_execution_sequence_steps WHERE \"SequenceId\" = {Q(sequenceId)}"));
        Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM local_execution_sequences WHERE \"Id\" = {Q(sequenceId)} AND \"AdmissionMode\" = 'WaitForAdmission' AND \"EndedAtUtc\" IS NULL"));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM local_execution_order_counters"));
    }

    [Fact]
    public async Task The_reverse_migration_is_allowed_again_once_no_admission_is_held()
    {
        await MigrateToAsync(null);
        var (sequenceId, admissionId) = await HeldAdmissionAsync("Reserved");
        await Assert.ThrowsAnyAsync<Exception>(() => MigrateToAsync(SequenceMigration));
        SqliteConnection.ClearAllPools();

        // A normal retirement (the production proof) is the only way a held admission leaves; here its resulting rows are written directly.
        await ExecuteAsync($"UPDATE local_execution_sequence_steps SET \"State\" = 'Revoked', \"ReasonCode\" = 'step_expired' WHERE \"SequenceId\" = {Q(sequenceId)}");
        await ExecuteAsync($"UPDATE local_execution_admissions SET \"State\" = 'Retired', \"ReasonCode\" = 'step_expired' WHERE \"Id\" = {Q(admissionId)}");
        await MigrateToAsync(SequenceMigration);

        Assert.Empty(await ReadNamesAsync("table", "local_execution_admissions"));
    }

    [Fact]
    public async Task A_reopened_database_keeps_the_queued_sequence_its_admission_and_its_counter_exactly()
    {
        await MigrateToAsync(null);
        var (scene, sequenceId) = await QueuedAsync();
        var admissionId = Guid.NewGuid();
        await ExecuteAsync(Insert("local_execution_admissions", WaitingAdmission(scene, sequenceId, id: admissionId)));
        await ExecuteAsync("UPDATE local_execution_order_counters SET \"LastNumber\" = 2, \"Revision\" = 2");
        SqliteConnection.ClearAllPools();

        await using var reopened = CreateContext();
        var sequence = await reopened.LocalExecutionSequences.AsNoTracking().SingleAsync(candidate => candidate.Id == sequenceId);
        var admission = await reopened.LocalExecutionAdmissions.AsNoTracking().SingleAsync(candidate => candidate.Id == admissionId);
        var counter = await reopened.LocalExecutionOrderCounters.AsNoTracking().SingleAsync();

        Assert.Equal(LocalExecutionAdmissionMode.WaitForAdmission, sequence.AdmissionMode);
        Assert.Equal(LocalExecutionSequenceState.WaitingForAdmission, sequence.State);
        Assert.Equal(LocalExecutionAdmissionState.Waiting, admission.State);
        Assert.Equal(2, admission.AdmissionNumber);
        Assert.Equal(LocalExecutionWaitingReason.AwaitingAdmissionCheck, admission.WaitingReason);
        Assert.Equal(2, counter.LastNumber);
        Assert.Equal(1, counter.Id);
    }

    // ---- constraints -----------------------------------------------------------------------------------------------------------

    public static TheoryData<string, string, string> BadAdmissionValues => new()
    {
        { "AdmissionNumber", "0", "ck_local_execution_admissions_number" },
        { "AdmissionNumber", "'x'", "ck_local_execution_admissions_number" },
        { "Ordinal", "102", BoundMessage },
        { "Ordinal", "-1", "ck_local_execution_admissions_ordinal" },
        { "Kind", "'Other'", "ck_local_execution_admissions_kind" },
        { "State", "'Running'", TupleMessage },
        { "WaitingReason", "'Idle'", "ck_local_execution_admissions_waiting_reason" },
        { "SourceFingerprintSha256", "'abc'", "ck_local_execution_admissions_fingerprint" },
        { "PassNumber", "4", "ck_local_execution_admissions_pass" },
        { "PassNumber", "1", "ck_local_execution_admissions_shape" },
        { "RecipeOrdinal", "0", "ck_local_execution_admissions_shape" },
        { "ReasonCode", "'stopped_before_claim'", "ck_local_execution_admissions_reason" },
        { "Revision", "0", "ck_local_execution_admissions_revision" },
        { "State", "'Reserved'", TupleMessage },
    };

    [Theory]
    [MemberData(nameof(BadAdmissionValues))]
    public async Task The_real_constraints_refuse_a_malformed_admission_value(string column, string value, string constraint)
    {
        await MigrateToAsync(null);
        var (scene, sequenceId) = await QueuedAsync();
        var row = WaitingAdmission(scene, sequenceId);
        row[column] = value;

        var exception = await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(Insert("local_execution_admissions", row)));

        Assert.Contains(constraint, exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM local_execution_admissions"));
    }

    public static TheoryData<string, string, string> BadSequenceValues => new()
    {
        { "AdmissionMode", "'Other'", "ck_local_execution_sequences_mode" },
        { "CoordinationNumber", "0", "ck_local_execution_sequences_coordination" },
        { "CoordinationNumber", "'x'", "ck_local_execution_sequences_coordination" },
        { "State", "'WaitingForAdmission'", "ck_local_execution_sequences_queue_state" },
    };

    [Theory]
    [MemberData(nameof(BadSequenceValues))]
    public async Task The_real_constraints_refuse_a_malformed_queue_sequence_value(string column, string value, string constraint)
    {
        await MigrateToAsync(null);
        var scene = await NewSceneAsync(1);
        var row = RawSequence(scene);
        row[column] = value;

        var exception = await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(Insert("local_execution_sequences", row)));

        Assert.Contains(constraint, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task One_pending_admission_per_cycle_unique_orders_and_the_counter_singleton_are_database_rules()
    {
        await MigrateToAsync(null);
        var (scene, sequenceId) = await QueuedAsync();
        await ExecuteAsync(Insert("local_execution_admissions", WaitingAdmission(scene, sequenceId, number: 2)));

        var successor = WaitingAdmission(scene, sequenceId, number: 3, ordinal: 1);
        successor["Kind"] = "'CodeReview'";
        successor["PassNumber"] = "1";
        successor["PredecessorStepId"] = Q(Guid.NewGuid());
        successor["ReportMessageId"] = Q(scene.PlanMessageId);
        var secondPending = await Assert.ThrowsAsync<SqliteException>(
            () => ExecuteAsync(Insert("local_execution_admissions", successor), foreignKeys: false));
        Assert.Contains("UNIQUE", secondPending.Message, StringComparison.Ordinal);
        Assert.Contains("local_execution_admissions.SequenceId", secondPending.Message, StringComparison.Ordinal);

        var other = await QueuedAsync();
        var duplicateNumber = await Assert.ThrowsAsync<SqliteException>(
            () => ExecuteAsync(Insert("local_execution_admissions", WaitingAdmission(other.Scene, other.SequenceId, number: 2))));
        Assert.Contains("UNIQUE", duplicateNumber.Message, StringComparison.Ordinal);

        var secondCounter = await Assert.ThrowsAsync<SqliteException>(
            () => ExecuteAsync("INSERT INTO local_execution_order_counters (\"Id\", \"LastNumber\", \"Revision\") VALUES (2, 0, 0)"));
        Assert.Contains("ck_local_execution_order_counters_singleton", secondCounter.Message, StringComparison.Ordinal);
    }

    // ---- guards: the queue bound, the admission lifecycle, the link and the counter ----------------------------------------------

    [Fact]
    public async Task The_queue_bound_counts_only_live_queued_cycles_and_refuses_the_sixty_fifth()
    {
        await MigrateToAsync(null);
        var scenes = new List<Scene>();
        for (var index = 0; index < 65; index++)
        {
            scenes.Add(await NewSceneAsync(index + 1));
        }

        // Ended and Immediate sequences never count toward the bound.
        await ExecuteAsync(Insert("local_execution_sequences", RawSequence(scenes[64])));
        for (var index = 0; index < 64; index++)
        {
            await ExecuteAsync(Insert("local_execution_sequences", QueuedSequence(scenes[index])));
        }

        var scene65 = await NewSceneAsync(70);
        var refused = await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(Insert("local_execution_sequences", QueuedSequence(scene65))));
        Assert.Contains("local_execution_sequence.queue_full", refused.Message, StringComparison.Ordinal);

        await ExecuteAsync($"UPDATE local_execution_sequences SET \"State\" = 'Stopped', \"ReasonCode\" = 'stopped_before_claim', \"StopRequestedAtUtc\" = {Sql(Now)}, \"EndedAtUtc\" = {Sql(Now)} WHERE \"RunId\" = {Q(scenes[0].Run.Id)}");
        await ExecuteAsync(Insert("local_execution_sequences", QueuedSequence(scene65)));
        Assert.Equal(65, await CountAsync("SELECT COUNT(*) FROM local_execution_sequences WHERE \"AdmissionMode\" = 'WaitForAdmission'"));
    }

    [Fact]
    public async Task An_admission_is_only_queued_for_a_live_queued_sequence_in_a_waiting_state_within_the_step_bound()
    {
        await MigrateToAsync(null);
        var (scene, sequenceId) = await QueuedAsync();
        var immediate = await NewSceneAsync(2);
        var immediateId = Guid.NewGuid();
        await ExecuteAsync(Insert("local_execution_sequences", RawSequence(immediate, immediateId)));

        await AssertRefusedAsync(WaitingAdmission(immediate, immediateId), TupleMessage);
        var reserved = WaitingAdmission(scene, sequenceId);
        reserved["State"] = "'Consumed'";
        reserved["StepId"] = Q(Guid.NewGuid());
        await using (var fk = CreateContext())
        {
            // A non-waiting insert is refused by the guard before its dangling step could matter.
            var exception = await Assert.ThrowsAnyAsync<SqliteException>(() => ExecuteAsync(Insert("local_execution_admissions", reserved), foreignKeys: false));
            Assert.Contains(TupleMessage, exception.Message, StringComparison.Ordinal);
        }

        var beyond = WaitingAdmission(scene, sequenceId, ordinal: 12);
        await AssertRefusedAsync(beyond, BoundMessage);
    }

    private async Task AssertRefusedAsync(Dictionary<string, string> row, string message)
    {
        var exception = await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(Insert("local_execution_admissions", row)));
        Assert.Contains(message, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_admission_only_moves_forward_and_its_identity_pins_and_order_never_change()
    {
        await MigrateToAsync(null);
        var (scene, sequenceId) = await QueuedAsync();
        var admissionId = Guid.NewGuid();
        await ExecuteAsync(Insert("local_execution_admissions", WaitingAdmission(scene, sequenceId, id: admissionId)));

        foreach (var change in new[]
        {
            "\"AdmissionNumber\" = 99",
            "\"Ordinal\" = 5",
            "\"Kind\" = 'Verification'",
            "\"PassNumber\" = 2",
            $"\"SourceCheckpointId\" = {Q(Guid.NewGuid())}",
            "\"SourceFingerprintSha256\" = '" + new string('b', 64) + "'",
            $"\"EnqueuedAtUtc\" = {Sql(Now.AddMinutes(1))}",
            "\"State\" = 'Consumed'",
        })
        {
            var exception = await Assert.ThrowsAnyAsync<SqliteException>(
                () => ExecuteAsync($"UPDATE local_execution_admissions SET {change} WHERE \"Id\" = {Q(admissionId)}", foreignKeys: false));
            Assert.Contains("local_execution_sequence", exception.Message, StringComparison.Ordinal);
        }

        // A changed waiting reason (revision advanced) and a retirement are allowed; a retired admission is final.
        await ExecuteAsync(
            $"UPDATE local_execution_admissions SET \"WaitingReason\" = 'WaitingForWorkspace', \"Revision\" = 2 WHERE \"Id\" = {Q(admissionId)}");
        await ExecuteAsync(
            $"UPDATE local_execution_admissions SET \"State\" = 'Retired', \"ReasonCode\" = 'stopped_before_claim', \"Revision\" = 3 WHERE \"Id\" = {Q(admissionId)}");
        var final = await Assert.ThrowsAnyAsync<SqliteException>(
            () => ExecuteAsync($"UPDATE local_execution_admissions SET \"Revision\" = 4 WHERE \"Id\" = {Q(admissionId)}"));
        Assert.Contains(ImmutableMessage, final.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_queued_sequences_step_exists_only_with_its_exact_pending_admission_while_an_immediate_one_is_unaffected()
    {
        await MigrateToAsync(null);
        var (scene, sequenceId) = await QueuedAsync();
        var admissionId = Guid.NewGuid();
        await ExecuteAsync(Insert("local_execution_admissions", WaitingAdmission(scene, sequenceId, id: admissionId)));

        var orphan = RawStep(scene, sequenceId, 0, LocalExecutionStepKind.Implementation);
        orphan["State"] = "'Reserved'";
        orphan["ReasonCode"] = "NULL";
        orphan["PredecessorStepId"] = "NULL";
        orphan["ReportMessageId"] = "NULL";
        orphan["SourceFingerprintSha256"] = Quote(new string('c', 64));
        var mismatch = await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(Insert("local_execution_sequence_steps", orphan)));
        Assert.Contains(TupleMessage, mismatch.Message, StringComparison.Ordinal);

        var matching = RawStep(scene, sequenceId, 0, LocalExecutionStepKind.Implementation);
        matching["State"] = "'Reserved'";
        matching["ReasonCode"] = "NULL";
        matching["PredecessorStepId"] = "NULL";
        matching["ReportMessageId"] = "NULL";
        await ExecuteAsync(Insert("local_execution_sequence_steps", matching));
        Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM local_execution_sequence_steps WHERE \"SequenceId\" = {Q(sequenceId)}"));

        var immediate = await NewSceneAsync(3);
        var immediateId = Guid.NewGuid();
        await ExecuteAsync(Insert("local_execution_sequences", RawSequence(immediate, immediateId)));
        var immediateStep = RawStep(immediate, immediateId, 0, LocalExecutionStepKind.Implementation);
        immediateStep["State"] = "'Reserved'";
        immediateStep["ReasonCode"] = "NULL";
        immediateStep["PredecessorStepId"] = "NULL";
        immediateStep["ReportMessageId"] = "NULL";
        await ExecuteAsync(Insert("local_execution_sequence_steps", immediateStep));
    }

    [Fact]
    public async Task The_order_counter_never_moves_backwards_changes_identity_or_disappears()
    {
        await MigrateToAsync(null);
        await ExecuteAsync("UPDATE local_execution_order_counters SET \"LastNumber\" = 7, \"Revision\" = 7");

        var backwards = await Assert.ThrowsAnyAsync<SqliteException>(() => ExecuteAsync("UPDATE local_execution_order_counters SET \"LastNumber\" = 3"));
        Assert.Contains(ImmutableMessage, backwards.Message, StringComparison.Ordinal);
        var removed = await Assert.ThrowsAnyAsync<SqliteException>(() => ExecuteAsync("DELETE FROM local_execution_order_counters"));
        Assert.Contains(ImmutableMessage, removed.Message, StringComparison.Ordinal);
        var typed = await Assert.ThrowsAnyAsync<SqliteException>(() => ExecuteAsync("UPDATE local_execution_order_counters SET \"LastNumber\" = 'x'"));
        Assert.Contains("ck_local_execution_order_counters_last", typed.Message, StringComparison.Ordinal);
        Assert.Equal(7, await CountAsync("SELECT \"LastNumber\" FROM local_execution_order_counters"));
    }

    [Fact]
    public async Task The_mode_and_the_coordination_order_of_a_sequence_are_immutable()
    {
        await MigrateToAsync(null);
        var (_, sequenceId) = await QueuedAsync();

        foreach (var change in new[] { "\"AdmissionMode\" = 'Immediate', \"State\" = 'Armed'", "\"CoordinationNumber\" = 999" })
        {
            var exception = await Assert.ThrowsAnyAsync<SqliteException>(
                () => ExecuteAsync($"UPDATE local_execution_sequences SET {change} WHERE \"Id\" = {Q(sequenceId)}"));
            Assert.Contains(ImmutableMessage, exception.Message, StringComparison.Ordinal);
        }
    }

    // ---- the waiting state holds the unrelated-start fence but owns no workspace ------------------------------------------------

    private async Task<(Scene Scene, Guid SequenceId)> WaitingAsync(string state = "WaitingForAdmission")
    {
        await using var seed = CreateContext();
        var scene = await SeedSceneAsync(seed);
        await seed.SaveChangesAsync();
        var sequenceId = Guid.NewGuid();
        await ExecuteAsync(Insert("local_execution_sequences", QueuedSequence(scene, state, sequenceId)));
        return (scene, sequenceId);
    }

    [Fact]
    public async Task A_waiting_queued_sequence_refuses_every_unrelated_start_its_run_workspace_and_project_would_make()
    {
        await MigrateToAsync(null);
        var (scene, _) = await WaitingAsync();

        await using (var attempt = CreateContext())
        {
            attempt.Attempts.Add(PlannerAttempt(scene, Guid.NewGuid()));
            Assert.Contains(BlockedMessage, FirstMessage(await Assert.ThrowsAsync<DbUpdateException>(() => attempt.SaveChangesAsync())), StringComparison.Ordinal);
        }

        await using (var execution = CreateContext())
        {
            execution.VerificationExecutions.Add(Execution(scene, Guid.NewGuid()));
            Assert.Contains(BlockedMessage, FirstMessage(await Assert.ThrowsAsync<DbUpdateException>(() => execution.SaveChangesAsync())), StringComparison.Ordinal);
        }

        Assert.True(await IsRunHeldAsync(scene.Run.Id));
        await using (var recipe = CreateContext())
        {
            var command = await recipe.VerificationCommands.FirstAsync(candidate => candidate.ProjectId == scene.Project.Id);
            command.Update(command.Name, @"C:\tools\other.exe", command.Arguments, command.TimeoutSeconds, command.IsEnabled, Now.AddMinutes(1));
            Assert.Contains(BlockedMessage, FirstMessage(await Assert.ThrowsAsync<DbUpdateException>(() => recipe.SaveChangesAsync())), StringComparison.Ordinal);
        }

        await using (var manualCheckpoint = CreateContext())
        {
            manualCheckpoint.GitCheckpoints.Add(
                GitCheckpoint.Capture(Guid.NewGuid(), scene.Workspace.Id, 50, Now, new string('a', 40), new string('d', 64), []));
            Assert.Contains(BlockedMessage, FirstMessage(await Assert.ThrowsAsync<DbUpdateException>(() => manualCheckpoint.SaveChangesAsync())), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_waiting_cycle_owns_no_workspace_so_the_result_checkpoint_of_an_attempt_already_executing_there_is_admitted_but_a_capture_is_not()
    {
        await MigrateToAsync(null);
        var (scene, _) = await WaitingAsync();

        // Another run's attempt already executes in the workspace; its recorder names the result checkpoint on the attempt first.
        var checkpointId = Guid.NewGuid();
        await using (var owner = CreateContext())
        {
            var other = await SeedSceneAsync(owner, runNumber: 9);
            var attempt = ImplementationAttempt(other, Guid.NewGuid());
            owner.Attempts.Add(attempt);
            await owner.SaveChangesAsync();
            await ExecuteAsync(
                $"UPDATE attempts SET \"AgentGitWorkspaceId\" = {Q(scene.Workspace.Id)}, \"AgentResultGitCheckpointId\" = {Q(checkpointId)} WHERE \"Id\" = {Q(attempt.Id)}",
                foreignKeys: false);
        }

        await using (var result = CreateContext())
        {
            result.GitCheckpoints.Add(GitCheckpoint.Capture(checkpointId, scene.Workspace.Id, 60, Now, new string('a', 40), new string('e', 64), []));
            await result.SaveChangesAsync();
        }

        await using var capture = CreateContext();
        capture.GitCheckpoints.Add(GitCheckpoint.Capture(Guid.NewGuid(), scene.Workspace.Id, 61, Now, new string('a', 40), new string('f', 64), []));
        Assert.Contains(BlockedMessage, FirstMessage(await Assert.ThrowsAsync<DbUpdateException>(() => capture.SaveChangesAsync())), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_owning_immediate_sequence_still_refuses_even_the_result_checkpoint_of_an_unlinked_attempt()
    {
        await MigrateToAsync(null);
        await using var seed = CreateContext();
        var scene = await SeedSceneAsync(seed);
        await seed.SaveChangesAsync();
        await ExecuteAsync(Insert("local_execution_sequences", RawSequence(scene)));
        var checkpointId = Guid.NewGuid();
        var other = await SeedSceneAsync(seed, runNumber: 9);
        var attempt = ImplementationAttempt(other, Guid.NewGuid());
        seed.Attempts.Add(attempt);
        await seed.SaveChangesAsync();
        await ExecuteAsync(
            $"UPDATE attempts SET \"AgentGitWorkspaceId\" = {Q(scene.Workspace.Id)}, \"AgentResultGitCheckpointId\" = {Q(checkpointId)} WHERE \"Id\" = {Q(attempt.Id)}",
            foreignKeys: false);

        await using var result = CreateContext();
        result.GitCheckpoints.Add(GitCheckpoint.Capture(checkpointId, scene.Workspace.Id, 60, Now, new string('a', 40), new string('e', 64), []));

        Assert.Contains(BlockedMessage, FirstMessage(await Assert.ThrowsAsync<DbUpdateException>(() => result.SaveChangesAsync())), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_waiting_cycle_never_fences_an_unrelated_project()
    {
        await MigrateToAsync(null);
        await WaitingAsync();
        await using var other = CreateContext();
        var unrelated = await SeedSceneAsync(other, runNumber: 5);
        other.Attempts.Add(PlannerAttempt(unrelated, Guid.NewGuid()));

        await other.SaveChangesAsync();

        Assert.Equal(2, await CountAsync($"SELECT COUNT(*) FROM attempts WHERE \"RunId\" = {Q(unrelated.Run.Id)}"));
    }
}

