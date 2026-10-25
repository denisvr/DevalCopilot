using DevalCopilot.Domain.Features.Runs;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// ADR-0065 persistence over a real file-backed SQLite database and the real production migrations: the additive migration adds exactly
/// three append-only fact tables and recreates the nine live-fence triggers with the release predicate, nothing is backfilled, constraints
/// and unique associations refuse what the Application only describes, and the reverse migration is refused BEFORE any change once any
/// witness, observation or release exists while an unused feature reverses to exactly the previous guards.
/// </summary>
public sealed class LocalExecutionOwnershipReleaseMigrationTests : LocalExecutionSequenceMigrationTestBase
{
    private const string ReleaseMigration = "20261023211312_AddLocalExecutionOwnershipRelease";

    private static readonly string[] FactTables =
    [
        "local_execution_dispatch_witnesses",
        "local_execution_ownership_releases",
        "local_execution_termination_observations",
    ];

    private static readonly string[] AppendOnlyTriggers = FactTables
        .SelectMany(table => new[] { $"trg_{table}_no_delete", $"trg_{table}_no_update" })
        .Order(StringComparer.Ordinal)
        .ToArray();

    private static readonly string[] LiveTriggers =
    [
        "trg_local_execution_guard_attempts",
        "trg_local_execution_guard_checkpoints",
        "trg_local_execution_guard_local_commit_operations",
        "trg_local_execution_guard_run_abandonment",
        "trg_local_execution_guard_run_settings",
        "trg_local_execution_guard_verification_commands_delete",
        "trg_local_execution_guard_verification_commands_insert",
        "trg_local_execution_guard_verification_commands_update",
        "trg_local_execution_guard_verification_executions",
    ];

    private sealed record Chain(
        Scene Scene,
        LocalExecutionSequence Sequence,
        LocalExecutionSequenceStep Step,
        Attempt Attempt,
        LocalExecutionDispatchWitness Witness,
        LocalExecutionTerminationObservation Observation);

    /// <summary>A coherent, ended, dispatched Agent operation with its witness and observation, written through the production entities.</summary>
    private async Task<Chain> SeedObservedAsync()
    {
        await using var context = CreateContext();
        var scene = await SeedSceneAsync(context);
        var sequence = AddArmed(context, scene);
        var operationId = Guid.NewGuid();
        var step = ImplementationStep(sequence.Id, scene, operationId);
        Claim(step);
        context.LocalExecutionSequenceSteps.Add(step);
        await context.SaveChangesAsync();
        var attempt = ImplementationAttempt(scene, operationId);
        attempt.MarkAgentDispatched(Now.AddSeconds(11));
        context.Attempts.Add(attempt);
        await context.SaveChangesAsync();
        var witness = LocalExecutionDispatchWitness.RecordAgent(
            Guid.NewGuid(), sequence, step, operationId, AgentResponseContract.ImplementationReport, null, Now.AddSeconds(11), Guid.NewGuid(),
            Guid.NewGuid());
        context.LocalExecutionDispatchWitnesses.Add(witness);
        await context.SaveChangesAsync();
        var observation = LocalExecutionTerminationObservation.Record(
            Guid.NewGuid(), witness, ProcessOutcome.TimedOut, null, TimeSpan.FromSeconds(5), 137, TimeSpan.FromSeconds(4), Now.AddSeconds(20));
        context.LocalExecutionTerminationObservations.Add(observation);
        await context.SaveChangesAsync();
        return new Chain(scene, sequence, step, attempt, witness, observation);
    }

    /// <summary>The coherent facts of a release of the chain's Agent attempt that ended Failed with a timed-out process.</summary>
    private static LocalExecutionOwnershipReleaseFacts FactsOf(Chain chain) => new(
        chain.Witness.Id,
        chain.Observation.Id,
        chain.Witness.OperationFamily,
        chain.Witness.OperationId,
        chain.Witness.SequenceId,
        chain.Witness.RunId,
        chain.Witness.ProjectId,
        chain.Witness.WorkspaceId,
        LocalExecutionSequenceState.NeedsAttention,
        LocalExecutionAdmissionMode.Immediate,
        null,
        chain.Witness.StepId,
        chain.Witness.Kind,
        chain.Witness.Ordinal,
        chain.Witness.PassNumber,
        chain.Witness.RecipeOrdinal,
        chain.Witness.PredecessorStepId,
        chain.Witness.SourceCheckpointId,
        chain.Witness.SourceFingerprintSha256,
        chain.Witness.ReportMessageId,
        chain.Witness.SubjectAttemptId,
        chain.Witness.AgentResponseContract,
        chain.Witness.RecipeCommandId,
        "Failed",
        AgentOutcome.ProviderInvocationFailed,
        null,
        chain.Observation.ProcessOutcome,
        chain.Observation.SemanticExitCode,
        chain.Observation.ProcessDuration.Ticks,
        chain.Observation.PhysicalExitCode,
        chain.Observation.ObservedElapsed.Ticks,
        chain.Witness.DispatchedAtUtc,
        chain.Observation.RecordedAtUtc);

    private static LocalExecutionOwnershipRelease RecordRelease(
        Chain chain, Guid eventId, DateTimeOffset releasedAt, DateTimeOffset expiry)
    {
        var facts = FactsOf(chain);
        return LocalExecutionOwnershipRelease.Record(
            Guid.NewGuid(), chain.Witness, chain.Observation, eventId, releasedAt, 1,
            LocalExecutionOwnershipReleaseConfirmation.Digest(facts, expiry), expiry, facts);
    }

    private async Task<LocalExecutionOwnershipRelease> AddReleaseAsync(Chain chain)
    {
        await using var context = CreateContext();
        var runEvent = RunEvent.Record(
            Guid.NewGuid(), chain.Scene.Run.Id, chain.Attempt.Id, RunEventType.LocalExecutionOwnershipReleased, ParticipantIdentity.ForHuman(),
            "{}", Now.AddMinutes(1));
        context.Events.Add(runEvent);
        var release = RecordRelease(chain, runEvent.Id, Now.AddMinutes(1), Now.AddMinutes(2));
        context.LocalExecutionOwnershipReleases.Add(release);
        await context.SaveChangesAsync();
        return release;
    }

    private async Task<Dictionary<string, string>> ReadTriggerSqlAsync(string like)
    {
        await using var probe = new SqliteConnection($"Data Source={DatabasePath};Pooling=False");
        await probe.OpenAsync();
        await using var command = probe.CreateCommand();
        command.CommandText = "SELECT name, sql FROM sqlite_master WHERE type = 'trigger' AND name LIKE $like ORDER BY name;";
        command.Parameters.AddWithValue("$like", like);
        await using var reader = await command.ExecuteReaderAsync();
        var result = new Dictionary<string, string>();
        while (await reader.ReadAsync())
        {
            result[reader.GetString(0)] = reader.GetString(1);
        }

        return result;
    }

    // ---- upgrade and reopen ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_upgrade_adds_three_tables_six_append_only_guards_recreates_the_nine_live_guards_and_backfills_nothing()
    {
        await MigrateToAsync(QueueMigration);
        Guid operationId;
        await using (var seed = CreateContext())
        {
            var scene = await SeedSceneAsync(seed);
            var sequence = AddArmed(seed, scene);
            operationId = Guid.NewGuid();
            var step = ImplementationStep(sequence.Id, scene, operationId);
            Claim(step);
            seed.LocalExecutionSequenceSteps.Add(step);
            await seed.SaveChangesAsync();
            var attempt = ImplementationAttempt(scene, operationId);
            attempt.MarkAgentDispatched(Now.AddSeconds(11));
            seed.Attempts.Add(attempt);
            await seed.SaveChangesAsync();
        }

        var liveBefore = await ReadTriggerSqlAsync("trg_local_execution_guard_%");
        Assert.Equal(LiveTriggers, liveBefore.Keys.Where(name => LiveTriggers.Contains(name)).Order(StringComparer.Ordinal));

        await MigrateToAsync(ReleaseMigration);

        Assert.Equal(FactTables, (await ReadNamesAsync("table", "local_execution%")).Where(name => FactTables.Contains(name)));
        Assert.Equal(AppendOnlyTriggers, (await ReadNamesAsync("trigger", "trg_local_execution_%")).Where(name => AppendOnlyTriggers.Contains(name)));
        var liveAfter = await ReadTriggerSqlAsync("trg_local_execution_guard_%");
        Assert.Equal(liveBefore.Keys, liveAfter.Keys);
        foreach (var name in LiveTriggers)
        {
            Assert.NotEqual(liveBefore[name], liveAfter[name]);
            Assert.Contains("local_execution_ownership_releases", liveAfter[name], StringComparison.Ordinal);
        }

        // Historical rows gain no witness, observation or release: nothing is backfilled and no release path exists for them.
        foreach (var table in FactTables)
        {
            Assert.Equal(0, await CountAsync($"SELECT COUNT(*) FROM {table}"));
        }

        Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM attempts WHERE \"Id\" = {Q(operationId)}"));
    }

    [Fact]
    public async Task A_reopened_database_keeps_the_three_facts_exactly()
    {
        await MigrateToAsync(null);
        var chain = await SeedObservedAsync();
        var release = await AddReleaseAsync(chain);
        SqliteConnection.ClearAllPools();

        await using var reopened = CreateContext();
        await reopened.Database.MigrateAsync();

        var witness = await reopened.LocalExecutionDispatchWitnesses.AsNoTracking().SingleAsync();
        var observation = await reopened.LocalExecutionTerminationObservations.AsNoTracking().SingleAsync();
        var stored = await reopened.LocalExecutionOwnershipReleases.AsNoTracking().SingleAsync();
        Assert.Equal(chain.Witness.Id, witness.Id);
        Assert.Equal(chain.Witness.Nonce, witness.Nonce);
        Assert.Equal(chain.Witness.HostLaunchId, witness.HostLaunchId);
        Assert.Equal(chain.Witness.DispatchedAtUtc, witness.DispatchedAtUtc);
        Assert.Equal(chain.Observation.Id, observation.Id);
        Assert.Equal(ProcessOutcome.TimedOut, observation.ProcessOutcome);
        Assert.Null(observation.SemanticExitCode);
        Assert.Equal(137, observation.PhysicalExitCode);
        Assert.Equal(TimeSpan.FromSeconds(5), observation.ProcessDuration);
        Assert.Equal(TimeSpan.FromSeconds(4), observation.ObservedElapsed);
        Assert.Equal(release.Id, stored.Id);
        Assert.Equal(release.ConfirmationSha256, stored.ConfirmationSha256);
        Assert.Equal(release.ConfirmationExpiresAtUtc, stored.ConfirmationExpiresAtUtc);
    }

    // ---- append-only and constraints ------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("local_execution_dispatch_witnesses", "UPDATE local_execution_dispatch_witnesses SET \"Ordinal\" = \"Ordinal\"")]
    [InlineData("local_execution_dispatch_witnesses", "DELETE FROM local_execution_dispatch_witnesses")]
    [InlineData("local_execution_termination_observations", "UPDATE local_execution_termination_observations SET \"PhysicalExitCode\" = \"PhysicalExitCode\"")]
    [InlineData("local_execution_termination_observations", "DELETE FROM local_execution_termination_observations")]
    [InlineData("local_execution_ownership_releases", "UPDATE local_execution_ownership_releases SET \"ConfirmationVersion\" = \"ConfirmationVersion\"")]
    [InlineData("local_execution_ownership_releases", "DELETE FROM local_execution_ownership_releases")]
    public async Task Every_fact_is_append_only_even_for_a_statement_that_would_change_nothing(string table, string statement)
    {
        await MigrateToAsync(null);
        var chain = await SeedObservedAsync();
        await AddReleaseAsync(chain);

        var refusal = await Assert.ThrowsAnyAsync<Exception>(() => ExecuteAsync(statement));

        Assert.Contains(ImmutableMessage, refusal.ToString(), StringComparison.Ordinal);
        Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM {table}"));
    }

    [Fact]
    public async Task One_witness_per_step_and_operation_one_observation_and_one_release_per_witness_and_event()
    {
        await MigrateToAsync(null);
        var chain = await SeedObservedAsync();
        var release = await AddReleaseAsync(chain);

        await using (var duplicateWitness = CreateContext())
        {
            duplicateWitness.LocalExecutionDispatchWitnesses.Add(LocalExecutionDispatchWitness.RecordAgent(
                Guid.NewGuid(), chain.Sequence, chain.Step, chain.Attempt.Id, AgentResponseContract.ImplementationReport, null, Now.AddSeconds(12),
                Guid.NewGuid(), Guid.NewGuid()));
            await Assert.ThrowsAsync<DbUpdateException>(() => duplicateWitness.SaveChangesAsync());
        }

        await using (var duplicateObservation = CreateContext())
        {
            duplicateObservation.LocalExecutionTerminationObservations.Add(LocalExecutionTerminationObservation.Record(
                Guid.NewGuid(), chain.Witness, ProcessOutcome.Cancelled, null, TimeSpan.FromSeconds(5), 1, TimeSpan.FromSeconds(1), Now.AddSeconds(30)));
            await Assert.ThrowsAsync<DbUpdateException>(() => duplicateObservation.SaveChangesAsync());
        }

        await using (var duplicateRelease = CreateContext())
        {
            duplicateRelease.LocalExecutionOwnershipReleases.Add(RecordRelease(chain, release.EventId, Now.AddMinutes(1), Now.AddMinutes(2)));
            await Assert.ThrowsAsync<DbUpdateException>(() => duplicateRelease.SaveChangesAsync());
        }

        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM local_execution_dispatch_witnesses"));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM local_execution_termination_observations"));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM local_execution_ownership_releases"));
    }

    [Fact]
    public async Task Restrictive_foreign_keys_name_a_real_witness_attempt_execution_step_and_observation()
    {
        await MigrateToAsync(null);
        var chain = await SeedObservedAsync();

        // Each statement is complete and valid except for one parent that does not exist.
        var orphanObservation = $"INSERT INTO local_execution_termination_observations (\"Id\", \"Version\", \"WitnessId\", \"ProcessOutcome\", "
            + "\"SemanticExitCode\", \"ProcessDuration\", \"PhysicalExitCode\", \"ObservedElapsed\", \"RecordedAtUtc\") "
            + $"VALUES ({Q(Guid.NewGuid())}, 1, {Q(Guid.NewGuid())}, 'TimedOut', NULL, 50000000, 137, 40000000, {Sql(Now)});";
        var orphanRelease = Insert("local_execution_ownership_releases", new Dictionary<string, string>(RawRelease())
        {
            ["WitnessId"] = Q(chain.Witness.Id),
            ["StepId"] = Q(chain.Step.Id),
        });

        Assert.Contains("FOREIGN KEY", (await Assert.ThrowsAnyAsync<Exception>(() => ExecuteAsync(orphanObservation))).ToString(), StringComparison.Ordinal);
        Assert.Contains("FOREIGN KEY", (await Assert.ThrowsAnyAsync<Exception>(() => ExecuteAsync(orphanRelease))).ToString(), StringComparison.Ordinal);
        Assert.Contains("FOREIGN KEY", (await Assert.ThrowsAnyAsync<Exception>(() => ExecuteAsync(
            $"DELETE FROM attempts WHERE \"Id\" = {Q(chain.Attempt.Id)};"))).ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ck_local_execution_termination_observations_durations", "ObservedElapsed", "60000000")]
    [InlineData("ck_local_execution_termination_observations_durations", "ObservedElapsed", "-1")]
    [InlineData("ck_local_execution_termination_observations_durations", "ProcessDuration", "'5000'")]
    [InlineData("ck_local_execution_termination_observations_exit_codes", "SemanticExitCode", "1")]
    [InlineData("ck_local_execution_termination_observations_exit_codes", "PhysicalExitCode", "4294967296")]
    [InlineData("ck_local_execution_termination_observations_exit_codes", "PhysicalExitCode", "1.5")]
    [InlineData("ck_local_execution_termination_observations_outcome", "ProcessOutcome", "'Failed'")]
    [InlineData("ck_local_execution_termination_observations_version", "Version", "2")]
    [InlineData("ck_local_execution_termination_observations_time", "RecordedAtUtc", "X'4142'")]
    public async Task Stored_physical_facts_are_exact_integers_in_their_closed_vocabulary(string constraint, string column, string value)
    {
        await MigrateToAsync(null);
        var valid = new Dictionary<string, string>
        {
            ["Id"] = Q(Guid.NewGuid()),
            ["Version"] = "1",
            ["WitnessId"] = Q(Guid.NewGuid()),
            ["ProcessOutcome"] = "'TimedOut'",
            ["SemanticExitCode"] = "NULL",
            ["ProcessDuration"] = "50000000",
            ["PhysicalExitCode"] = "137",
            ["ObservedElapsed"] = "40000000",
            ["RecordedAtUtc"] = Sql(Now),
        };
        var violating = new Dictionary<string, string>(valid) { [column] = value };

        // Foreign keys are suspended only so the closed shape (never a missing parent) decides the refusal.
        var exception = await Assert.ThrowsAnyAsync<Exception>(() => ExecuteAsync(Insert("local_execution_termination_observations", violating), foreignKeys: false));

        Assert.Contains(constraint, exception.ToString(), StringComparison.Ordinal);
        await ExecuteAsync(Insert("local_execution_termination_observations", valid), foreignKeys: false);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM local_execution_termination_observations"));
    }

    /// <summary>The stored form of a UTC time: <c>yyyy-MM-dd HH:mm:ss</c>, the fraction with trailing zeros trimmed (and no point when it is
    /// zero) and <c>+00:00</c>, exactly what the entity framework writes for the host's DateTimeOffset values.</summary>
    private static string Stored(DateTimeOffset value) => $"'{value.UtcDateTime:yyyy-MM-dd HH:mm:ss.FFFFFFF}+00:00'";

    private static Dictionary<string, string> RawRelease(bool verification = false, DateTimeOffset? released = null, DateTimeOffset? expires = null)
    {
        var at = released ?? Now.AddMinutes(1);
        var expiry = expires ?? at.AddMinutes(1);
        return new Dictionary<string, string>
        {
            ["Id"] = Q(Guid.NewGuid()),
            ["Version"] = "1",
            ["WitnessId"] = Q(Guid.NewGuid()),
            ["ObservationId"] = Q(Guid.NewGuid()),
            ["StepId"] = Q(Guid.NewGuid()),
            ["EventId"] = Q(Guid.NewGuid()),
            ["ReleasedAtUtc"] = Stored(at),
            ["ConfirmationVersion"] = "1",
            ["ConfirmationSha256"] = "'" + new string('d', 64) + "'",
            ["ConfirmationExpiresAtUtc"] = Stored(expiry),
            ["IntentVersion"] = "1",
            ["IntentFamily"] = verification ? "'Verification'" : "'Agent'",
            ["IntentOperationId"] = Q(Guid.NewGuid()),
            ["IntentSequenceId"] = Q(Guid.NewGuid()),
            ["IntentRunId"] = Q(Guid.NewGuid()),
            ["IntentProjectId"] = Q(Guid.NewGuid()),
            ["IntentWorkspaceId"] = Q(Guid.NewGuid()),
            ["IntentSequenceState"] = "'NeedsAttention'",
            ["IntentAdmissionMode"] = "'Immediate'",
            ["IntentAdmissionId"] = "NULL",
            ["IntentKind"] = verification ? "'Verification'" : "'Implementation'",
            ["IntentOrdinal"] = verification ? "2" : "0",
            ["IntentPassNumber"] = verification ? "1" : "0",
            ["IntentRecipeOrdinal"] = verification ? "0" : "NULL",
            ["IntentPredecessorStepId"] = verification ? Q(Guid.NewGuid()) : "NULL",
            ["IntentSourceCheckpointId"] = Q(Guid.NewGuid()),
            ["IntentSourceFingerprintSha256"] = "'" + new string('a', 64) + "'",
            ["IntentReportMessageId"] = verification ? Q(Guid.NewGuid()) : "NULL",
            ["IntentSubjectAttemptId"] = "NULL",
            ["IntentAgentResponseContract"] = verification ? "NULL" : "'ImplementationReport'",
            ["IntentRecipeCommandId"] = verification ? Q(Guid.NewGuid()) : "NULL",
            ["IntentOperationStatus"] = verification ? "'TimedOut'" : "'Failed'",
            ["IntentAgentOutcome"] = verification ? "NULL" : "'ProviderInvocationFailed'",
            ["IntentCompletionFingerprintSha256"] = verification ? "'" + new string('9', 64) + "'" : "NULL",
            ["IntentProcessOutcome"] = "'TimedOut'",
            ["IntentSemanticExitCode"] = "NULL",
            ["IntentProcessDuration"] = "50000000",
            ["IntentPhysicalExitCode"] = "137",
            ["IntentObservedElapsed"] = "40000000",
            ["IntentDispatchedAtUtc"] = Stored(Now),
            ["IntentObservationRecordedAtUtc"] = Stored(Now.AddSeconds(5)),
            ["IntentConfirmationVersion"] = "1",
            ["IntentConfirmationExpiresAtUtc"] = Stored(expiry),
        };
    }

    [Theory]
    [InlineData(0L, 1_200_000_000L, true)]
    [InlineData(0L, 1_199_999_999L, true)]
    [InlineData(0L, 1_200_000_001L, false)]
    [InlineData(0L, 2_400_000_000L, false)]
    [InlineData(1L, 1_200_000_000L, true)]
    [InlineData(1L, 1_200_000_001L, false)]
    [InlineData(1_000_000L, 1_200_000_000L, true)]
    [InlineData(1_000_000L, 1_200_000_001L, false)]
    [InlineData(1_234_567L, 1_200_000_000L, true)]
    [InlineData(1_234_567L, 1_199_999_999L, true)]
    [InlineData(1_234_567L, 1_200_000_001L, false)]
    [InlineData(9_999_999L, 1_200_000_000L, true)]
    [InlineData(9_999_999L, 1_200_000_001L, false)]
    [InlineData(5_000_000L, 1_200_000_000L, true)]
    [InlineData(5_000_000L, 1_200_000_001L, false)]
    [InlineData(0L, 0L, true)]
    [InlineData(0L, -1L, true)]
    public async Task A_receipt_confirmation_lifetime_is_bounded_exactly_at_tick_precision(long fractionTicks, long lifetimeTicks, bool accepted)
    {
        await MigrateToAsync(null);
        var released = Now.AddMinutes(1).AddTicks(fractionTicks);
        var row = RawRelease(released: released, expires: released.AddTicks(lifetimeTicks));

        // Foreign keys are suspended only so the closed shape (never a missing parent) decides the outcome. Only the UPPER bound is this
        // constraint's concern: an expiry that is not after the release is refused by the shared relational proof, never by storage.
        if (accepted)
        {
            await ExecuteAsync(Insert("local_execution_ownership_releases", row), foreignKeys: false);
            Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM local_execution_ownership_releases"));
        }
        else
        {
            var exception = await Assert.ThrowsAnyAsync<Exception>(() => ExecuteAsync(Insert("local_execution_ownership_releases", row), foreignKeys: false));
            Assert.Contains("ck_local_execution_ownership_releases_time", exception.ToString(), StringComparison.Ordinal);
            Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM local_execution_ownership_releases"));
        }
    }

    [Theory]
    [InlineData("2026-10-23T23:59:59.9999999Z", 1_200_000_000L, true)]
    [InlineData("2026-10-23T23:59:59.9999999Z", 1_200_000_001L, false)]
    [InlineData("2026-10-24T00:00:00.0000000Z", 1_200_000_000L, true)]
    [InlineData("2026-10-24T00:00:00.0000000Z", 1_200_000_001L, false)]
    [InlineData("2026-12-31T23:58:59.9999999Z", 1_200_000_000L, true)]
    [InlineData("2026-12-31T23:58:59.9999999Z", 1_200_000_001L, false)]
    [InlineData("2028-02-28T23:59:30.5000000Z", 1_200_000_000L, true)]
    [InlineData("2028-02-28T23:59:30.5000000Z", 1_200_000_001L, false)]
    public async Task The_exact_lifetime_bound_holds_across_second_day_month_and_year_boundaries(string releasedText, long lifetimeTicks, bool accepted)
    {
        await MigrateToAsync(null);
        var released = DateTimeOffset.Parse(releasedText, System.Globalization.CultureInfo.InvariantCulture);
        var row = RawRelease(released: released, expires: released.AddTicks(lifetimeTicks));

        if (accepted)
        {
            await ExecuteAsync(Insert("local_execution_ownership_releases", row), foreignKeys: false);
            Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM local_execution_ownership_releases"));
        }
        else
        {
            var exception = await Assert.ThrowsAnyAsync<Exception>(() => ExecuteAsync(Insert("local_execution_ownership_releases", row), foreignKeys: false));
            Assert.Contains("ck_local_execution_ownership_releases_time", exception.ToString(), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(false, "ck_local_execution_ownership_releases_intent_version", "IntentVersion", "2")]
    [InlineData(false, "ck_local_execution_ownership_releases_intent_version", "IntentConfirmationVersion", "2")]
    [InlineData(false, "ck_local_execution_ownership_releases_intent_family", "IntentCompletionFingerprintSha256", "'9999999999999999999999999999999999999999999999999999999999999999'")]
    [InlineData(false, "ck_local_execution_ownership_releases_intent_family", "IntentAgentOutcome", "NULL")]
    [InlineData(false, "ck_local_execution_ownership_releases_intent_family", "IntentRecipeCommandId", "'11111111-1111-1111-1111-111111111111'")]
    [InlineData(true, "ck_local_execution_ownership_releases_intent_family", "IntentCompletionFingerprintSha256", "NULL")]
    [InlineData(true, "ck_local_execution_ownership_releases_intent_family", "IntentAgentOutcome", "'SourceChanged'")]
    [InlineData(true, "ck_local_execution_ownership_releases_intent_family", "IntentRecipeCommandId", "NULL")]
    [InlineData(false, "ck_local_execution_ownership_releases_intent_vocabulary", "IntentAgentOutcome", "'Implemented'")]
    [InlineData(false, "ck_local_execution_ownership_releases_intent_vocabulary", "IntentAgentOutcome", "'Unrecognized'")]
    [InlineData(false, "ck_local_execution_ownership_releases_intent_vocabulary", "IntentProcessOutcome", "'Exited'")]
    [InlineData(false, "ck_local_execution_ownership_releases_intent_vocabulary", "IntentSequenceState", "'AwaitingStepResult'")]
    [InlineData(false, "ck_local_execution_ownership_releases_intent_vocabulary", "IntentAdmissionMode", "'WaitForAdmission'")]
    [InlineData(false, "ck_local_execution_ownership_releases_intent_vocabulary", "IntentOperationStatus", "''")]
    [InlineData(false, "ck_local_execution_ownership_releases_intent_numbers", "IntentOrdinal", "102")]
    [InlineData(false, "ck_local_execution_ownership_releases_intent_numbers", "IntentPassNumber", "4")]
    [InlineData(false, "ck_local_execution_ownership_releases_intent_numbers", "IntentSemanticExitCode", "0")]
    [InlineData(false, "ck_local_execution_ownership_releases_intent_numbers", "IntentPhysicalExitCode", "4294967296")]
    [InlineData(false, "ck_local_execution_ownership_releases_intent_numbers", "IntentPhysicalExitCode", "1.5")]
    [InlineData(false, "ck_local_execution_ownership_releases_intent_numbers", "IntentObservedElapsed", "60000000")]
    [InlineData(false, "ck_local_execution_ownership_releases_intent_numbers", "IntentObservedElapsed", "-1")]
    [InlineData(false, "ck_local_execution_ownership_releases_intent_numbers", "IntentProcessDuration", "'5000'")]
    [InlineData(false, "ck_local_execution_ownership_releases_intent_shape", "IntentPassNumber", "1")]
    [InlineData(false, "ck_local_execution_ownership_releases_intent_shape", "IntentPredecessorStepId", "'11111111-1111-1111-1111-111111111111'")]
    [InlineData(true, "ck_local_execution_ownership_releases_intent_shape", "IntentRecipeOrdinal", "NULL")]
    [InlineData(false, "ck_local_execution_ownership_releases_intent_fingerprints", "IntentSourceFingerprintSha256", "'ABC'")]
    [InlineData(false, "ck_local_execution_ownership_releases_intent_fingerprints", "IntentSourceFingerprintSha256", "'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA'")]
    [InlineData(true, "ck_local_execution_ownership_releases_intent_fingerprints", "IntentCompletionFingerprintSha256", "'not-a-sha256'")]
    [InlineData(true, "ck_local_execution_ownership_releases_intent_fingerprints", "IntentCompletionFingerprintSha256", "'99999999999999999999999999999999999999999999999999999999999999999'")]
    [InlineData(true, "ck_local_execution_ownership_releases_intent_fingerprints", "IntentCompletionFingerprintSha256", "'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA'")]
    [InlineData(false, "ck_local_execution_ownership_releases_intent_times", "IntentObservationRecordedAtUtc", "X'4142'")]
    [InlineData(false, "ck_local_execution_ownership_releases_intent_times", "IntentDispatchedAtUtc", "X'4142'")]
    public async Task The_typed_intent_snapshot_is_refused_by_the_database_when_it_leaves_its_closed_shape(
        bool verification, string constraint, string column, string value)
    {
        await MigrateToAsync(null);
        var valid = RawRelease(verification);
        var violating = new Dictionary<string, string>(valid) { [column] = value };

        // Foreign keys are suspended only so the closed shape (never a missing parent) decides the refusal.
        var exception = await Assert.ThrowsAnyAsync<Exception>(() => ExecuteAsync(Insert("local_execution_ownership_releases", violating), foreignKeys: false));

        Assert.Contains(constraint, exception.ToString(), StringComparison.Ordinal);
        await ExecuteAsync(Insert("local_execution_ownership_releases", valid), foreignKeys: false);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM local_execution_ownership_releases"));
    }

    [Fact]
    public async Task An_exited_observation_carries_its_semantic_exit_code_equal_to_the_physical_one()
    {
        await MigrateToAsync(null);
        Dictionary<string, string> Row(string outcome, string semantic, string physical) => new()
        {
            ["Id"] = Q(Guid.NewGuid()),
            ["Version"] = "1",
            ["WitnessId"] = Q(Guid.NewGuid()),
            ["ProcessOutcome"] = outcome,
            ["SemanticExitCode"] = semantic,
            ["ProcessDuration"] = "50000000",
            ["PhysicalExitCode"] = physical,
            ["ObservedElapsed"] = "40000000",
            ["RecordedAtUtc"] = Sql(Now),
        };

        await ExecuteAsync(Insert("local_execution_termination_observations", Row("'Exited'", "3", "3")), foreignKeys: false);
        await ExecuteAsync(Insert("local_execution_termination_observations", Row("'TimedOut'", "NULL", "-1")), foreignKeys: false);
        foreach (var violating in new[] { Row("'Exited'", "NULL", "3"), Row("'Exited'", "3", "4"), Row("'Cancelled'", "3", "3") })
        {
            var exception = await Assert.ThrowsAnyAsync<Exception>(() => ExecuteAsync(Insert("local_execution_termination_observations", violating), foreignKeys: false));
            Assert.Contains("ck_local_execution_termination_observations_exit_codes", exception.ToString(), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("ck_local_execution_dispatch_witnesses_family", "\"VerificationExecutionId\" = " + "'11111111-1111-1111-1111-111111111111'")]
    [InlineData("ck_local_execution_dispatch_witnesses_contract", "\"AgentResponseContract\" = 'Proposal'")]
    [InlineData("ck_local_execution_dispatch_witnesses_kind", "\"Kind\" = 'Other'")]
    [InlineData("ck_local_execution_dispatch_witnesses_ordinal", "\"Ordinal\" = 102")]
    [InlineData("ck_local_execution_dispatch_witnesses_pass", "\"PassNumber\" = 4")]
    [InlineData("ck_local_execution_dispatch_witnesses_fingerprint", "\"SourceFingerprintSha256\" = 'ABC'")]
    [InlineData("ck_local_execution_dispatch_witnesses_version", "\"Version\" = 2")]
    [InlineData("ck_local_execution_dispatch_witnesses_shape", "\"PredecessorStepId\" = '11111111-1111-1111-1111-111111111111'")]
    public async Task A_witness_row_that_violates_its_closed_shape_is_refused_by_the_database(string constraint, string assignment)
    {
        await MigrateToAsync(null);
        var chain = await SeedObservedAsync();
        var valid = new Dictionary<string, string>
        {
            ["Id"] = Q(Guid.NewGuid()),
            ["Version"] = "1",
            ["SequenceId"] = Q(chain.Sequence.Id),
            ["RunId"] = Q(chain.Scene.Run.Id),
            ["ProjectId"] = Q(chain.Scene.Project.Id),
            ["WorkspaceId"] = Q(chain.Scene.Workspace.Id),
            ["StepId"] = Q(Guid.NewGuid()),
            ["OperationFamily"] = "'Agent'",
            ["AgentAttemptId"] = Q(Guid.NewGuid()),
            ["VerificationExecutionId"] = "NULL",
            ["Kind"] = "'Implementation'",
            ["Ordinal"] = "0",
            ["PassNumber"] = "0",
            ["RecipeOrdinal"] = "NULL",
            ["PredecessorStepId"] = "NULL",
            ["SourceCheckpointId"] = Q(chain.Scene.Checkpoint.Id),
            ["SourceFingerprintSha256"] = Quote(Fingerprint),
            ["ReportMessageId"] = "NULL",
            ["SubjectAttemptId"] = "NULL",
            ["AgentResponseContract"] = "'ImplementationReport'",
            ["RecipeCommandId"] = "NULL",
            ["AdmissionId"] = "NULL",
            ["DispatchedAtUtc"] = Sql(Now),
            ["HostLaunchId"] = Q(Guid.NewGuid()),
            ["Nonce"] = Q(Guid.NewGuid()),
        };
        var column = assignment[..assignment.IndexOf(" = ", StringComparison.Ordinal)].Trim('"');
        var value = assignment[(assignment.IndexOf(" = ", StringComparison.Ordinal) + 3)..];
        var violating = new Dictionary<string, string>(valid) { [column] = value };

        // Foreign keys are suspended only so the closed shape (never a missing parent) decides the refusal.
        var exception = await Assert.ThrowsAnyAsync<Exception>(() => ExecuteAsync(Insert("local_execution_dispatch_witnesses", violating), foreignKeys: false));

        Assert.Contains(constraint, exception.ToString(), StringComparison.Ordinal);
        await ExecuteAsync(Insert("local_execution_dispatch_witnesses", valid), foreignKeys: false);
        Assert.Equal(2, await CountAsync("SELECT COUNT(*) FROM local_execution_dispatch_witnesses"));
    }

    // ---- reverse migration ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_unused_feature_reverses_to_exactly_the_previous_guards_and_removes_only_its_tables()
    {
        await MigrateToAsync(QueueMigration);
        var tablesBefore = await ReadNamesAsync("table");
        var triggersBefore = await ReadTriggerSqlAsync("trg_%");

        await MigrateToAsync(ReleaseMigration);
        Assert.NotEqual(triggersBefore.Count, (await ReadTriggerSqlAsync("trg_%")).Count);
        await MigrateToAsync(QueueMigration);

        Assert.Equal(tablesBefore, await ReadNamesAsync("table"));
        var restored = await ReadTriggerSqlAsync("trg_%");
        Assert.Equal(triggersBefore.Keys, restored.Keys);
        foreach (var (name, definition) in triggersBefore)
        {
            Assert.Equal(definition, restored[name]);
        }

        Assert.Equal(0, await CountAsync($"SELECT COUNT(*) FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '{ReleaseMigration}'"));

        await MigrateToAsync(null);
        Assert.Equal(FactTables, (await ReadNamesAsync("table", "local_execution%")).Where(name => FactTables.Contains(name)));
    }

    [Theory]
    [InlineData("witness")]
    [InlineData("observation")]
    [InlineData("release")]
    public async Task The_reverse_migration_is_refused_before_any_change_once_any_fact_exists_and_destroys_no_provenance_or_decision(string used)
    {
        await MigrateToAsync(null);
        var chain = await SeedObservedAsync();
        if (used == "release")
        {
            await AddReleaseAsync(chain);
        }
        else if (used == "witness")
        {
            // Only the witness remains: the observation is removed with the append-only guard suspended for this fixture setup.
            await ExecuteAsync("DROP TRIGGER trg_local_execution_termination_observations_no_delete;");
            await ExecuteAsync("DELETE FROM local_execution_termination_observations;");
            await ExecuteAsync(
                "CREATE TRIGGER trg_local_execution_termination_observations_no_delete BEFORE DELETE ON local_execution_termination_observations "
                + $"BEGIN SELECT RAISE(ABORT, '{ImmutableMessage}'); END;");
        }

        var tablesBefore = await ReadNamesAsync("table");
        var triggersBefore = await ReadTriggerSqlAsync("trg_%");
        var historyBefore = await CountAsync("SELECT COUNT(*) FROM \"__EFMigrationsHistory\"");
        var witnesses = await CountAsync("SELECT COUNT(*) FROM local_execution_dispatch_witnesses");
        var observations = await CountAsync("SELECT COUNT(*) FROM local_execution_termination_observations");
        var releases = await CountAsync("SELECT COUNT(*) FROM local_execution_ownership_releases");

        var refusal = await Assert.ThrowsAnyAsync<Exception>(() => MigrateToAsync(QueueMigration));

        Assert.Contains("used_release_feature_blocks_downgrade", refusal.ToString(), StringComparison.Ordinal);
        SqliteConnection.ClearAllPools();
        Assert.Equal(tablesBefore, await ReadNamesAsync("table"));
        var triggersAfter = await ReadTriggerSqlAsync("trg_%");
        Assert.Equal(triggersBefore.Keys, triggersAfter.Keys);
        foreach (var (name, definition) in triggersBefore)
        {
            Assert.Equal(definition, triggersAfter[name]);
        }

        Assert.Equal(historyBefore, await CountAsync("SELECT COUNT(*) FROM \"__EFMigrationsHistory\""));
        Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '{ReleaseMigration}'"));
        Assert.Equal(witnesses, await CountAsync("SELECT COUNT(*) FROM local_execution_dispatch_witnesses"));
        Assert.Equal(observations, await CountAsync("SELECT COUNT(*) FROM local_execution_termination_observations"));
        Assert.Equal(releases, await CountAsync("SELECT COUNT(*) FROM local_execution_ownership_releases"));
    }
}
