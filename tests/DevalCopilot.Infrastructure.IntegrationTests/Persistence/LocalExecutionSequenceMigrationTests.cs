using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// ADR-0060 persistence against the real file-backed SQLite schema and production migrations, through independent connections: the
/// additive upgrade creates only three tables and fifteen guards and gives no historical row a sequence or a permission, the reverse
/// drops exactly them, and the real constraints, unique indexes and write-seam triggers refuse what the Application reads only
/// describe (a second sequence for a run, an impure or incoherent arming, malformed values, a duplicate or second outstanding step, a
/// step past its bounds, mutation of an authorization, a pinned recipe, a concluded step or an ended sequence).
/// </summary>
public sealed class LocalExecutionSequenceMigrationTests : LocalExecutionSequenceMigrationTestBase
{
    private static readonly string[] TableNames =
    [
        "local_execution_admissions",
        "local_execution_order_counters",
        "local_execution_sequence_recipes",
        "local_execution_sequence_steps",
        "local_execution_sequences",
    ];

    [Fact]
    public async Task The_upgrade_adds_five_tables_and_twenty_two_guards_and_gives_no_historical_row_a_sequence()
    {
        await MigrateToAsync(PriorMigration);
        Guid runId;
        await using (var seed = CreateContext())
        {
            var scene = await SeedSceneAsync(seed);
            runId = scene.Run.Id;
            seed.Attempts.Add(ImplementationAttempt(scene, Guid.NewGuid()));
            await seed.SaveChangesAsync();
        }

        Assert.Empty(await ReadNamesAsync("table", "local_execution%"));
        Assert.Empty(await ReadNamesAsync("trigger", "trg_local_execution%"));

        await MigrateToAsync(QueueMigration);

        Assert.Equal(TableNames, await ReadNamesAsync("table", "local_execution%"));
        Assert.Equal(TriggerNames, await ReadNamesAsync("trigger", "trg_local_execution%"));
        Assert.Equal(22, TriggerNames.Length);
        await using var verify = CreateContext();
        Assert.Empty(await verify.LocalExecutionSequences.AsNoTracking().ToListAsync());
        Assert.Empty(await verify.LocalExecutionSequenceRecipes.AsNoTracking().ToListAsync());
        Assert.Empty(await verify.LocalExecutionSequenceSteps.AsNoTracking().ToListAsync());
        Assert.Equal(2, await verify.Attempts.AsNoTracking().CountAsync(attempt => attempt.RunId == runId));
        var run = await verify.Runs.AsNoTracking().SingleAsync(candidate => candidate.Id == runId);
        Assert.Equal(RunLifecycle.Running, run.Lifecycle);
        Assert.NotEmpty(await ReadNamesAsync("trigger", "trg_planning_sequence%"));
    }

    [Fact]
    public async Task The_reverse_migration_drops_exactly_the_five_tables_and_twenty_two_guards_and_keeps_every_other_row()
    {
        await MigrateToAsync(QueueMigration);
        var tablesBefore = await ReadNamesAsync("table");
        var triggersBefore = await ReadNamesAsync("trigger");
        Guid runId;
        await using (var seed = CreateContext())
        {
            var scene = await SeedSceneAsync(seed);
            runId = scene.Run.Id;
            AddArmed(seed, scene);
            await seed.SaveChangesAsync();
        }

        await MigrateToAsync(PriorMigration);

        Assert.Empty(await ReadNamesAsync("table", "local_execution%"));
        Assert.Empty(await ReadNamesAsync("trigger", "trg_local_execution%"));
        Assert.Equal(tablesBefore.Except(TableNames).OrderBy(name => name), await ReadNamesAsync("table"));
        Assert.Equal(triggersBefore.Except(TriggerNames).OrderBy(name => name), await ReadNamesAsync("trigger"));
        Assert.NotEmpty(await ReadNamesAsync("trigger", "trg_local_commit%"));
        Assert.NotEmpty(await ReadNamesAsync("trigger", "trg_planning_sequence%"));
        await using (var verify = CreateContext())
        {
            Assert.True(await verify.Runs.AsNoTracking().AnyAsync(candidate => candidate.Id == runId));
            Assert.Equal(2, await verify.VerificationCommands.AsNoTracking().CountAsync());
        }

        await MigrateToAsync(QueueMigration);
        Assert.Equal(TriggerNames, await ReadNamesAsync("trigger", "trg_local_execution%"));
        Assert.Equal(TableNames, await ReadNamesAsync("table", "local_execution%"));
    }

    [Fact]
    public async Task A_reopened_database_keeps_the_sequence_recipes_and_steps_exactly()
    {
        await MigrateToAsync(null);
        Guid sequenceId;
        Guid stepId;
        await using (var seed = CreateContext())
        {
            var scene = await SeedSceneAsync(seed);
            var sequence = AddArmed(seed, scene);
            sequenceId = sequence.Id;
            var step = ImplementationStep(sequence.Id, scene);
            stepId = step.Id;
            seed.LocalExecutionSequenceSteps.Add(step);
            await seed.SaveChangesAsync();
        }

        await MigrateToAsync(null);

        await using var reopened = CreateContext();
        var sequenceRow = await reopened.LocalExecutionSequences.AsNoTracking().SingleAsync();
        Assert.Equal(sequenceId, sequenceRow.Id);
        Assert.Equal(LocalExecutionSequenceState.Armed, sequenceRow.State);
        Assert.Equal(new string('b', 64), sequenceRow.PlanInputsSha256);
        Assert.Equal(2, sequenceRow.RecipeCount);
        Assert.Equal(["0", "1"], (await reopened.LocalExecutionSequenceRecipes.AsNoTracking().OrderBy(recipe => recipe.Ordinal).ToListAsync())
            .Select(recipe => recipe.Ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var step2 = await reopened.LocalExecutionSequenceSteps.AsNoTracking().SingleAsync();
        Assert.Equal(stepId, step2.Id);
        Assert.Equal(LocalExecutionSequenceStepState.Reserved, step2.State);
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
            var first = AddArmed(seed, scene);
            await seed.SaveChangesAsync();
            first.End(LocalExecutionSequenceState.Stopped, LocalExecutionSequenceReason.StoppedBeforeClaim, null, Now);
            await seed.SaveChangesAsync();
        }

        await using var refused = CreateContext();
        refused.LocalExecutionSequences.Add(Arm(scene));
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => refused.SaveChangesAsync());
        Assert.Contains("UNIQUE", FirstMessage(exception), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RunId", FirstMessage(exception), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("State", "'Running'", "ck_local_execution_sequences_state")]
    [InlineData("PlanAuthority", "'Other'", "ck_local_execution_sequences_authority")]
    [InlineData("CorrectionAllowance", "3", "ck_local_execution_sequences_allowance")]
    [InlineData("CorrectionAllowance", "-1", "ck_local_execution_sequences_allowance")]
    [InlineData("CorrectionAllowance", "'x'", "ck_local_execution_sequences_allowance")]
    [InlineData("RecipeCount", "0", "ck_local_execution_sequences_recipes")]
    [InlineData("RecipeCount", "33", "ck_local_execution_sequences_recipes")]
    [InlineData("RecipeCount", "'abc'", "ck_local_execution_sequences_recipes")]
    [InlineData("Revision", "0", "ck_local_execution_sequences_revision")]
    [InlineData("FingerprintSha256", "'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA'", "ck_local_execution_sequences_fingerprint")]
    [InlineData("FingerprintSha256", "'aaaa'", "ck_local_execution_sequences_fingerprint")]
    [InlineData("PlanInputsSha256", "'zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz'", "ck_local_execution_sequences_plan_inputs")]
    [InlineData("PlanInputsSha256", "''", "ck_local_execution_sequences_plan_inputs")]
    [InlineData("ConfirmationSha256", "'ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789'", "ck_local_execution_sequences_confirmation")]
    [InlineData("ConfirmationSha256", "'ab'", "ck_local_execution_sequences_confirmation")]
    public async Task The_real_constraints_refuse_a_malformed_sequence_value(string column, string value, string constraint)
    {
        await MigrateToAsync(null);
        Scene scene;
        await using (var seed = CreateContext())
        {
            scene = await SeedSceneAsync(seed);
        }

        var values = RawSequence(scene);
        values[column] = value;

        var exception = await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(Insert("local_execution_sequences", values)));
        Assert.Contains(constraint, exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM local_execution_sequences"));
    }

    [Fact]
    public async Task A_well_formed_raw_sequence_is_admitted_so_the_malformed_cases_are_the_only_cause_of_refusal()
    {
        await MigrateToAsync(null);
        Scene scene;
        await using (var seed = CreateContext())
        {
            scene = await SeedSceneAsync(seed);
        }

        await ExecuteAsync(Insert("local_execution_sequences", RawSequence(scene)));

        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM local_execution_sequences"));
    }

    [Theory]
    [InlineData("ReadyForHumanApproval", "NULL", "NULL", "NULL")]
    [InlineData("Armed", "'review'", "NULL", "NULL")]
    [InlineData("HumanEscalation", "NULL", "'report'", "NULL")]
    [InlineData("HumanEscalation", "NULL", "NULL", "'escalation'")]
    [InlineData("Stopped", "NULL", "'report'", "NULL")]
    public async Task The_final_references_exist_only_for_an_approved_or_escalated_end_and_exactly_as_the_state_requires(
        string state, string review, string report, string escalation)
    {
        await MigrateToAsync(null);
        Scene scene;
        await using (var seed = CreateContext())
        {
            scene = await SeedSceneAsync(seed);
        }

        var values = RawSequence(scene);
        values["State"] = Quote(state);
        values["FinalReviewAttemptId"] = review == "'review'" ? Q(scene.PlannerAttemptId) : review;
        values["FinalReportMessageId"] = report == "'report'" ? Q(scene.PlanMessageId) : report;
        values["FinalEscalationMessageId"] = escalation == "'escalation'" ? Q(scene.PlanMessageId) : escalation;

        var exception = await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(Insert("local_execution_sequences", values)));
        Assert.Contains("ck_local_execution_sequences_final", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Ordinal", "32", "ck_local_execution_sequence_recipes_ordinal")]
    [InlineData("Ordinal", "-1", "ck_local_execution_sequence_recipes_ordinal")]
    [InlineData("TimeoutSeconds", "0", "ck_local_execution_sequence_recipes_timeout")]
    [InlineData("TimeoutSeconds", "901", "ck_local_execution_sequence_recipes_timeout")]
    [InlineData("IsEnabled", "0", "ck_local_execution_sequence_recipes_enabled")]
    public async Task The_real_constraints_refuse_a_malformed_recipe_value(string column, string value, string constraint)
    {
        await MigrateToAsync(null);
        Guid sequenceId;
        await using (var seed = CreateContext())
        {
            var scene = await SeedSceneAsync(seed);
            sequenceId = AddArmed(seed, scene, recipeCount: 1).Id;
            await seed.SaveChangesAsync();
        }

        var values = RawRecipe(sequenceId, ordinal: 5);
        values[column] = value;

        var exception = await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(Insert("local_execution_sequence_recipes", values)));
        Assert.Contains(constraint, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_recipe_identity_ordinal_and_command_number_are_unique_within_a_sequence()
    {
        await MigrateToAsync(null);
        Scene scene;
        Guid sequenceId;
        await using (var seed = CreateContext())
        {
            scene = await SeedSceneAsync(seed);
            sequenceId = AddArmed(seed, scene).Id;
            await seed.SaveChangesAsync();
        }

        var duplicateOrdinal = RawRecipe(sequenceId, ordinal: 0);
        duplicateOrdinal["CommandNumber"] = "9";
        var ordinal = await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(Insert("local_execution_sequence_recipes", duplicateOrdinal)));
        Assert.Contains("UNIQUE", ordinal.Message, StringComparison.Ordinal);
        Assert.Contains("Ordinal", ordinal.Message, StringComparison.Ordinal);

        var duplicateNumber = RawRecipe(sequenceId, ordinal: 2);
        duplicateNumber["CommandNumber"] = "1";
        var number = await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(Insert("local_execution_sequence_recipes", duplicateNumber)));
        Assert.Contains("UNIQUE", number.Message, StringComparison.Ordinal);
        Assert.Contains("CommandNumber", number.Message, StringComparison.Ordinal);

        var duplicateCommand = RawRecipe(sequenceId, ordinal: 2);
        duplicateCommand["CommandNumber"] = "7";
        duplicateCommand["VerificationCommandId"] = Q(scene.Commands[0].Id);
        var command = await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(Insert("local_execution_sequence_recipes", duplicateCommand)));
        Assert.Contains("UNIQUE", command.Message, StringComparison.Ordinal);
        Assert.Contains("VerificationCommandId", command.Message, StringComparison.Ordinal);

        Assert.Equal(2, await CountAsync("SELECT COUNT(*) FROM local_execution_sequence_recipes"));
    }

    [Fact]
    public async Task A_pinned_recipe_is_never_updated_by_any_write()
    {
        await MigrateToAsync(null);
        Guid recipeId;
        await using (var seed = CreateContext())
        {
            var scene = await SeedSceneAsync(seed);
            AddArmed(seed, scene);
            await seed.SaveChangesAsync();
            recipeId = seed.LocalExecutionSequenceRecipes.Local.First().Id;
        }

        var edit = await Assert.ThrowsAsync<SqliteException>(
            () => ExecuteAsync($"UPDATE local_execution_sequence_recipes SET \"TimeoutSeconds\" = 5 WHERE \"Id\" = {Q(recipeId)}"));
        Assert.Contains(ImmutableMessage, edit.Message, StringComparison.Ordinal);
        var noop = await Assert.ThrowsAsync<SqliteException>(
            () => ExecuteAsync($"UPDATE local_execution_sequence_recipes SET \"CommandName\" = \"CommandName\" WHERE \"Id\" = {Q(recipeId)}"));
        Assert.Contains(ImmutableMessage, noop.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Created")]
    [InlineData("RunningAttempt")]
    [InlineData("ImplementationAttempt")]
    [InlineData("CorrectionAttempt")]
    [InlineData("LocalCommit")]
    [InlineData("LivePlanningSequence")]
    public async Task Arming_is_refused_by_the_database_for_a_run_that_is_not_eligible(string impurity)
    {
        await MigrateToAsync(null);
        Scene scene;
        await using (var seed = CreateContext())
        {
            scene = await SeedSceneAsync(seed, running: impurity != "Created");
            switch (impurity)
            {
                case "RunningAttempt":
                    seed.Attempts.Add(PlannerAttempt(scene, Guid.NewGuid()));
                    break;
                case "ImplementationAttempt":
                    var implemented = ImplementationAttempt(scene, Guid.NewGuid());
                    implemented.CompleteLocalExecutionSequenceDispatchRefused(Now);
                    seed.Attempts.Add(implemented);
                    break;
                case "CorrectionAttempt":
                    var correction = Attempt.ClaimAgentReviewCorrection(
                        Guid.NewGuid(), scene.Run.Id, NextAttemptNumber(), scene.Workspace.Id, scene.Checkpoint.Id, Fingerprint, Guid.NewGuid(),
                        TimeSpan.FromMinutes(20), 1024, 2048, Now, agentBudgetSlot: 5);
                    correction.CompleteLocalExecutionSequenceDispatchRefused(Now);
                    seed.Attempts.Add(correction);
                    break;
                case "LivePlanningSequence":
                    await seed.SaveChangesAsync();
                    break;
                default:
                    break;
            }

            await seed.SaveChangesAsync();
        }

        if (impurity == "LocalCommit")
        {
            await InsertRawLocalCommitAsync(scene);
        }

        if (impurity == "LivePlanningSequence")
        {
            // The planning arming guard would itself refuse this run; the coexisting state is fabricated to prove this guard alone.
            await ExecuteAsync("DROP TRIGGER trg_planning_sequence_guard_arming;");
            await ExecuteAsync("DROP TRIGGER trg_planning_sequence_guard_tuple;");
            await using var planning = CreateContext();
            planning.PlanningSequences.Add(PlanningSequence.Arm(
                Guid.NewGuid(), scene.Run.Id, scene.Run.ProjectId, scene.Workspace.Id, scene.Checkpoint.Id, Fingerprint, new string('b', 64), Now));
            await planning.SaveChangesAsync();
        }

        await using var refused = CreateContext();
        refused.LocalExecutionSequences.Add(Arm(scene));
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => refused.SaveChangesAsync());
        Assert.Contains(NotEligibleMessage, FirstMessage(exception), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Arming_is_admitted_for_a_running_run_with_only_a_finished_read_only_attempt_and_an_ended_planning_sequence()
    {
        await MigrateToAsync(null);
        await using var seed = CreateContext();
        var scene = await SeedSceneAsync(seed);
        seed.LocalExecutionSequences.Add(Arm(scene));

        await seed.SaveChangesAsync();

        Assert.Equal(1, await seed.LocalExecutionSequences.CountAsync());
    }

    [Fact]
    public async Task Arming_is_refused_for_a_workspace_checkpoint_or_plan_message_that_does_not_belong_to_the_run()
    {
        await MigrateToAsync(null);
        Scene scene;
        Scene other;
        await using (var seed = CreateContext())
        {
            scene = await SeedSceneAsync(seed, 1);
            other = await SeedSceneAsync(seed, 2);
        }

        async Task AssertRefusedAsync(Func<LocalExecutionSequence> build)
        {
            await using var context = CreateContext();
            context.LocalExecutionSequences.Add(build());
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
            Assert.Contains(TupleMessage, FirstMessage(exception), StringComparison.Ordinal);
        }

        await AssertRefusedAsync(() => LocalExecutionSequence.Arm(
            Guid.NewGuid(), scene.Run.Id, scene.Run.ProjectId, other.Workspace.Id, other.Checkpoint.Id, Fingerprint, scene.PlanMessageId,
            LocalExecutionPlanAuthority.AcceptedRoot, new string('b', 64), new string('c', 64), "s", 1, 2, Now, NextCoordination()));
        await AssertRefusedAsync(() => LocalExecutionSequence.Arm(
            Guid.NewGuid(), scene.Run.Id, scene.Run.ProjectId, scene.Workspace.Id, other.Checkpoint.Id, Fingerprint, scene.PlanMessageId,
            LocalExecutionPlanAuthority.AcceptedRoot, new string('b', 64), new string('c', 64), "s", 1, 2, Now, NextCoordination()));
        await AssertRefusedAsync(() => LocalExecutionSequence.Arm(
            Guid.NewGuid(), scene.Run.Id, scene.Run.ProjectId, scene.Workspace.Id, scene.Checkpoint.Id, Fingerprint, other.PlanMessageId,
            LocalExecutionPlanAuthority.AcceptedRoot, new string('b', 64), new string('c', 64), "s", 1, 2, Now, NextCoordination()));
        await AssertRefusedAsync(() => LocalExecutionSequence.Arm(
            Guid.NewGuid(), scene.Run.Id, other.Run.ProjectId, scene.Workspace.Id, scene.Checkpoint.Id, Fingerprint, scene.PlanMessageId,
            LocalExecutionPlanAuthority.AcceptedRoot, new string('b', 64), new string('c', 64), "s", 1, 2, Now, NextCoordination()));
    }

    [Theory]
    [InlineData("WorkspaceId")]
    [InlineData("RunId")]
    [InlineData("ProjectId")]
    [InlineData("CheckpointId")]
    [InlineData("PlanProposalMessageId")]
    public async Task The_authorization_tuple_cannot_be_retargeted(string column)
    {
        await MigrateToAsync(null);
        Guid sequenceId;
        await using (var seed = CreateContext())
        {
            var scene = await SeedSceneAsync(seed);
            sequenceId = AddArmed(seed, scene).Id;
            await seed.SaveChangesAsync();
        }

        var exception = await Assert.ThrowsAsync<SqliteException>(
            () => ExecuteAsync($"UPDATE local_execution_sequences SET \"{column}\" = {Q(Guid.NewGuid())} WHERE \"Id\" = {Q(sequenceId)}", foreignKeys: false));
        Assert.Contains(ImmutableMessage, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("FingerprintSha256", "'cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc'")]
    [InlineData("PlanInputsSha256", "'cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc'")]
    [InlineData("ConfirmationSha256", "'dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd'")]
    [InlineData("SettingsSnapshot", "'v1;other'")]
    [InlineData("PlanAuthority", "'AcceptedRevision'")]
    [InlineData("RecipeCount", "1")]
    [InlineData("CorrectionAllowance", "1")]
    [InlineData("ArmedAtUtc", "'2026-10-20 10:00:00'")]
    [InlineData("AdvancementDeadlineUtc", "'2026-10-20 12:00:00'")]
    public async Task The_pinned_authorization_facts_are_immutable(string column, string value)
    {
        await MigrateToAsync(null);
        Guid sequenceId;
        await using (var seed = CreateContext())
        {
            var scene = await SeedSceneAsync(seed);
            sequenceId = AddArmed(seed, scene).Id;
            await seed.SaveChangesAsync();
        }

        var exception = await Assert.ThrowsAsync<SqliteException>(
            () => ExecuteAsync($"UPDATE local_execution_sequences SET \"{column}\" = {value} WHERE \"Id\" = {Q(sequenceId)}"));
        Assert.Contains(ImmutableMessage, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(LocalExecutionSequenceState.Stopped, LocalExecutionSequenceReason.StoppedBeforeClaim)]
    [InlineData(LocalExecutionSequenceState.NeedsAttention, LocalExecutionSequenceReason.AuthorityChanged)]
    [InlineData(LocalExecutionSequenceState.Interrupted, LocalExecutionSequenceReason.HostRestarted)]
    public async Task An_ended_sequence_never_changes_again_not_even_by_a_no_op_write(LocalExecutionSequenceState endState, string reason)
    {
        await MigrateToAsync(null);
        Guid sequenceId;
        await using (var seed = CreateContext())
        {
            var scene = await SeedSceneAsync(seed);
            var sequence = AddArmed(seed, scene);
            sequenceId = sequence.Id;
            await seed.SaveChangesAsync();
            sequence.End(endState, reason, null, Now.AddMinutes(1));
            await seed.SaveChangesAsync();
        }

        var id = Q(sequenceId);
        var reopen = await Assert.ThrowsAsync<SqliteException>(
            () => ExecuteAsync($"UPDATE local_execution_sequences SET \"State\" = 'Armed' WHERE \"Id\" = {id}"));
        Assert.Contains(ImmutableMessage, reopen.Message, StringComparison.Ordinal);
        var noop = await Assert.ThrowsAsync<SqliteException>(
            () => ExecuteAsync($"UPDATE local_execution_sequences SET \"Revision\" = \"Revision\" WHERE \"Id\" = {id}"));
        Assert.Contains(ImmutableMessage, noop.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_live_sequence_advances_its_guarded_state_and_revision_through_the_production_entity()
    {
        await MigrateToAsync(null);
        await using var context = CreateContext();
        var scene = await SeedSceneAsync(context);
        var sequence = AddArmed(context, scene);
        await context.SaveChangesAsync();

        sequence.BeginPreparing(Now.AddSeconds(1));
        sequence.MarkAwaitingResult(Now.AddSeconds(2));
        await context.SaveChangesAsync();

        await using var verify = CreateContext();
        var stored = await verify.LocalExecutionSequences.AsNoTracking().SingleAsync();
        Assert.Equal(LocalExecutionSequenceState.AwaitingStepResult, stored.State);
        Assert.Equal(3, stored.Revision);
    }

    [Theory]
    [InlineData("Kind", "'Other'", "ck_local_execution_sequence_steps_kind")]
    [InlineData("State", "'Running'", "ck_local_execution_sequence_steps_state")]
    [InlineData("Revision", "0", "ck_local_execution_sequence_steps_revision")]
    [InlineData("PassNumber", "4", "ck_local_execution_sequence_steps_pass")]
    [InlineData("PassNumber", "-1", "ck_local_execution_sequence_steps_pass")]
    [InlineData("Ordinal", "-1", "ck_local_execution_sequence_steps_ordinal")]
    public async Task The_real_constraints_refuse_a_malformed_step_value(string column, string value, string constraint)
    {
        await MigrateToAsync(null);
        Scene scene;
        Guid sequenceId;
        await using (var seed = CreateContext())
        {
            scene = await SeedSceneAsync(seed);
            sequenceId = AddArmed(seed, scene).Id;
            await seed.SaveChangesAsync();
        }

        var values = With(RawStep(scene, sequenceId, 0, LocalExecutionStepKind.Implementation), column, value);

        var exception = await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(Insert("local_execution_sequence_steps", values)));
        Assert.Contains(constraint, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ImplementationWithPredecessor")]
    [InlineData("ImplementationWithReport")]
    [InlineData("ImplementationWithRecipe")]
    [InlineData("ImplementationWithSubject")]
    [InlineData("ImplementationInPassOne")]
    [InlineData("ReviewInPassZero")]
    [InlineData("ReviewWithoutPredecessor")]
    [InlineData("ReviewWithoutReport")]
    [InlineData("ReviewWithSubject")]
    [InlineData("ReviewWithRecipe")]
    [InlineData("VerificationWithoutRecipe")]
    [InlineData("CorrectionWithoutSubject")]
    [InlineData("DiagnosisCorrectionWithoutSubject")]
    [InlineData("NonImplementationAtOrdinalZero")]
    public async Task The_step_shape_constraint_refuses_a_value_that_does_not_belong_to_the_kind(string shape)
    {
        await MigrateToAsync(null);
        Scene scene;
        Guid sequenceId;
        Guid predecessor;
        await using (var seed = CreateContext())
        {
            scene = await SeedSceneAsync(seed);
            var sequence = AddArmed(seed, scene);
            sequenceId = sequence.Id;
            var first = ImplementationStep(sequence.Id, scene);
            predecessor = first.Id;
            seed.LocalExecutionSequenceSteps.Add(first);
            await seed.SaveChangesAsync();
            first.Revoke(LocalExecutionSequenceReason.StoppedBeforeClaim, Now);
            await seed.SaveChangesAsync();
        }

        var values = shape switch
        {
            "ImplementationWithPredecessor" => With(RawStep(scene, sequenceId, 0, LocalExecutionStepKind.Implementation), "PredecessorStepId", Q(predecessor)),
            "ImplementationWithReport" => With(RawStep(scene, sequenceId, 0, LocalExecutionStepKind.Implementation), "ReportMessageId", Q(scene.PlanMessageId)),
            "ImplementationWithRecipe" => With(RawStep(scene, sequenceId, 0, LocalExecutionStepKind.Implementation), "RecipeOrdinal", "0"),
            "ImplementationWithSubject" => With(RawStep(scene, sequenceId, 0, LocalExecutionStepKind.Implementation), "SubjectAttemptId", Q(scene.PlannerAttemptId)),
            "ImplementationInPassOne" => With(RawStep(scene, sequenceId, 0, LocalExecutionStepKind.Implementation), "PassNumber", "1"),
            "ReviewInPassZero" => With(RawStep(scene, sequenceId, 3, predecessor: predecessor), "PassNumber", "0"),
            "ReviewWithoutPredecessor" => With(RawStep(scene, sequenceId, 3, predecessor: predecessor), "PredecessorStepId", "NULL"),
            "ReviewWithoutReport" => With(RawStep(scene, sequenceId, 3, predecessor: predecessor), "ReportMessageId", "NULL"),
            "ReviewWithSubject" => With(RawStep(scene, sequenceId, 3, predecessor: predecessor), "SubjectAttemptId", Q(scene.PlannerAttemptId)),
            "ReviewWithRecipe" => With(RawStep(scene, sequenceId, 3, predecessor: predecessor), "RecipeOrdinal", "0"),
            "VerificationWithoutRecipe" => With(RawStep(scene, sequenceId, 1, LocalExecutionStepKind.Verification, predecessor), "RecipeOrdinal", "NULL"),
            "CorrectionWithoutSubject" => With(RawStep(scene, sequenceId, 4, LocalExecutionStepKind.ReviewCorrection, predecessor), "SubjectAttemptId", "NULL"),
            "DiagnosisCorrectionWithoutSubject" => With(RawStep(scene, sequenceId, 4, LocalExecutionStepKind.DiagnosisCorrection, predecessor), "SubjectAttemptId", "NULL"),
            _ => With(RawStep(scene, sequenceId, 0, LocalExecutionStepKind.CodeReview, predecessor), "Ordinal", "0"),
        };
        var exception = await Assert.ThrowsAsync<SqliteException>(
            () => ExecuteAsync(Insert("local_execution_sequence_steps", values), foreignKeys: false));
        Assert.Contains("ck_local_execution_sequence_steps_shape", exception.Message, StringComparison.Ordinal);
    }

    private static Dictionary<string, string> With(Dictionary<string, string> values, string column, string value)
    {
        values[column] = value;
        return values;
    }

    [Theory]
    [InlineData("Reserved", "'00000000-0000-0000-0000-000000000001'", "NULL", "NULL")]
    [InlineData("Claimed", "NULL", "NULL", "NULL")]
    [InlineData("Concluded", "NULL", "'2026-10-20 09:00:10'", "'2026-10-20 09:30:00'")]
    [InlineData("Revoked", "'op'", "NULL", "NULL")]
    [InlineData("Unresolved", "NULL", "'2026-10-20 09:00:10'", "NULL")]
    [InlineData("Concluded", "'other'", "'2026-10-20 09:00:10'", "'2026-10-20 09:30:00'")]
    [InlineData("Concluded", "'op'", "NULL", "'2026-10-20 09:30:00'")]
    [InlineData("Concluded", "'op'", "'2026-10-20 09:00:10'", "NULL")]
    public async Task The_operation_link_constraint_ties_the_link_to_a_claimed_or_concluded_step_and_its_reserved_identity(
        string state, string operation, string claimedAt, string deadline)
    {
        await MigrateToAsync(null);
        Scene scene;
        Guid sequenceId;
        await using (var seed = CreateContext())
        {
            scene = await SeedSceneAsync(seed);
            sequenceId = AddArmed(seed, scene).Id;
            await seed.SaveChangesAsync();
        }

        var values = RawStep(scene, sequenceId, 0, LocalExecutionStepKind.Implementation);
        values["State"] = Quote(state);
        values["OperationId"] = operation switch
        {
            "op" => values["ReservedOperationId"],
            "other" => Q(Guid.NewGuid()),
            _ => operation,
        };
        values["ClaimedAtUtc"] = claimedAt;
        values["ResultDeadlineUtc"] = deadline;

        var exception = await Assert.ThrowsAsync<SqliteException>(
            () => ExecuteAsync(Insert("local_execution_sequence_steps", values), foreignKeys: false));
        Assert.Contains("ck_local_execution_sequence_steps_operation_link", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_step_has_a_unique_ordinal_even_after_the_first_step_is_no_longer_outstanding()
    {
        await MigrateToAsync(null);
        Scene scene;
        Guid sequenceId;
        await using (var seed = CreateContext())
        {
            scene = await SeedSceneAsync(seed);
            var sequence = AddArmed(seed, scene);
            sequenceId = sequence.Id;
            var step = ImplementationStep(sequence.Id, scene);
            seed.LocalExecutionSequenceSteps.Add(step);
            await seed.SaveChangesAsync();
            step.Revoke(LocalExecutionSequenceReason.StoppedBeforeClaim, Now);
            await seed.SaveChangesAsync();
        }

        await using var duplicate = CreateContext();
        duplicate.LocalExecutionSequenceSteps.Add(ImplementationStep(sequenceId, scene));
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
        Assert.Contains("UNIQUE", FirstMessage(exception), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Ordinal", FirstMessage(exception), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_sequence_has_at_most_one_outstanding_step()
    {
        await MigrateToAsync(null);
        Scene scene;
        Guid sequenceId;
        Guid firstId;
        await using (var seed = CreateContext())
        {
            scene = await SeedSceneAsync(seed);
            var sequence = AddArmed(seed, scene);
            sequenceId = sequence.Id;
            var step = ImplementationStep(sequence.Id, scene);
            firstId = step.Id;
            seed.LocalExecutionSequenceSteps.Add(step);
            await seed.SaveChangesAsync();
        }

        await using (var second = CreateContext())
        {
            second.LocalExecutionSequenceSteps.Add(ReviewStep(sequenceId, scene, firstId, ordinal: 3));
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
            Assert.Contains("UNIQUE", FirstMessage(exception), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("SequenceId", FirstMessage(exception), StringComparison.Ordinal);
        }

        await using (var claim = CreateContext())
        {
            var step = await claim.LocalExecutionSequenceSteps.SingleAsync(candidate => candidate.Id == firstId);
            Claim(step);
            await claim.SaveChangesAsync();
        }

        // A Claimed step is still outstanding; once Concluded the next step may be reserved.
        await using (var stillOutstanding = CreateContext())
        {
            stillOutstanding.LocalExecutionSequenceSteps.Add(VerificationStep(sequenceId, scene, firstId, ordinal: 1));
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => stillOutstanding.SaveChangesAsync());
            Assert.Contains("UNIQUE", FirstMessage(exception), StringComparison.OrdinalIgnoreCase);
        }

        await using (var conclude = CreateContext())
        {
            var step = await conclude.LocalExecutionSequenceSteps.SingleAsync(candidate => candidate.Id == firstId);
            step.Conclude(Now.AddMinutes(1));
            await conclude.SaveChangesAsync();
        }

        await using var next = CreateContext();
        next.LocalExecutionSequenceSteps.Add(VerificationStep(sequenceId, scene, firstId, ordinal: 1));
        await next.SaveChangesAsync();
        Assert.Equal(2, await next.LocalExecutionSequenceSteps.CountAsync());
    }

    [Fact]
    public async Task A_verification_pass_and_recipe_pair_is_unique_within_a_sequence()
    {
        await MigrateToAsync(null);
        Scene scene;
        Guid sequenceId;
        Guid firstId;
        await using (var seed = CreateContext())
        {
            scene = await SeedSceneAsync(seed);
            var sequence = AddArmed(seed, scene);
            sequenceId = sequence.Id;
            var first = ImplementationStep(sequence.Id, scene);
            firstId = first.Id;
            seed.LocalExecutionSequenceSteps.Add(first);
            await seed.SaveChangesAsync();
            Claim(first);
            await seed.SaveChangesAsync();
            first.Conclude(Now.AddMinutes(1));
            await seed.SaveChangesAsync();
        }

        await ExecuteAsync(Insert("local_execution_sequence_steps", With(RawStep(scene, sequenceId, 1, LocalExecutionStepKind.Verification, firstId), "Revision", "1")));

        var duplicate = await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(
            Insert("local_execution_sequence_steps", RawStep(scene, sequenceId, 2, LocalExecutionStepKind.Verification, firstId))));
        Assert.Contains("UNIQUE", duplicate.Message, StringComparison.Ordinal);
        Assert.Contains("PassNumber", duplicate.Message, StringComparison.Ordinal);

        // The same recipe in another pass is a different member.
        var otherPass = RawStep(scene, sequenceId, 2, LocalExecutionStepKind.Verification, firstId);
        otherPass["PassNumber"] = "2";
        await ExecuteAsync(Insert("local_execution_sequence_steps", otherPass));
    }

    [Fact]
    public async Task A_reserved_operation_identity_belongs_to_exactly_one_step()
    {
        await MigrateToAsync(null);
        Scene scene;
        Guid sequenceId;
        Guid firstId;
        Guid reserved;
        await using (var seed = CreateContext())
        {
            scene = await SeedSceneAsync(seed);
            var sequence = AddArmed(seed, scene);
            sequenceId = sequence.Id;
            var first = ImplementationStep(sequence.Id, scene);
            firstId = first.Id;
            reserved = first.ReservedOperationId;
            seed.LocalExecutionSequenceSteps.Add(first);
            await seed.SaveChangesAsync();
            Claim(first);
            await seed.SaveChangesAsync();
            first.Conclude(Now);
            await seed.SaveChangesAsync();
        }

        var reuse = RawStep(scene, sequenceId, 1, LocalExecutionStepKind.Verification, firstId);
        reuse["ReservedOperationId"] = Q(reserved);
        var exception = await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(Insert("local_execution_sequence_steps", reuse)));
        Assert.Contains("UNIQUE", exception.Message, StringComparison.Ordinal);
        Assert.Contains("OperationId", exception.Message, StringComparison.Ordinal);

        // A concluded step's operation link is unique as well, even if the reservation differs.
        var link = Concluded(RawStep(scene, sequenceId, 2, LocalExecutionStepKind.Verification, firstId));
        link["OperationId"] = Q(reserved);
        var linkException = await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(Insert("local_execution_sequence_steps", link)));
        Assert.True(
            linkException.Message.Contains("UNIQUE", StringComparison.Ordinal)
            || linkException.Message.Contains("ck_local_execution_sequence_steps_operation_link", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Step_ordinals_stay_below_three_times_the_recipe_count_plus_six()
    {
        await MigrateToAsync(null);
        Scene scene;
        Guid sequenceId;
        await using (var seed = CreateContext())
        {
            scene = await SeedSceneAsync(seed, commandCount: 1);
            sequenceId = AddArmed(seed, scene, recipeCount: 1).Id;
            await seed.SaveChangesAsync();
        }

        // One recipe allows ordinals zero to eight.
        await ExecuteAsync(Insert("local_execution_sequence_steps", RawStep(scene, sequenceId, 8)), foreignKeys: false);
        var nine = await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(Insert("local_execution_sequence_steps", RawStep(scene, sequenceId, 9)), foreignKeys: false));
        Assert.Contains(BoundMessage, nine.Message, StringComparison.Ordinal);
        var hard = await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(Insert("local_execution_sequence_steps", RawStep(scene, sequenceId, 101)), foreignKeys: false));
        Assert.Contains(BoundMessage, hard.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_verification_step_cannot_name_a_recipe_beyond_the_pinned_count()
    {
        await MigrateToAsync(null);
        Scene scene;
        Guid sequenceId;
        await using (var seed = CreateContext())
        {
            scene = await SeedSceneAsync(seed);
            sequenceId = AddArmed(seed, scene).Id;
            await seed.SaveChangesAsync();
        }

        var inside = With(RawStep(scene, sequenceId, 1, LocalExecutionStepKind.Verification), "RecipeOrdinal", "1");
        await ExecuteAsync(Insert("local_execution_sequence_steps", inside), foreignKeys: false);
        var beyond = With(RawStep(scene, sequenceId, 2, LocalExecutionStepKind.Verification), "RecipeOrdinal", "2");
        var exception = await Assert.ThrowsAsync<SqliteException>(
            () => ExecuteAsync(Insert("local_execution_sequence_steps", beyond), foreignKeys: false));
        Assert.Contains(BoundMessage, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task At_most_six_non_verification_non_revoked_steps_are_admitted_and_a_revoked_step_never_counts()
    {
        await MigrateToAsync(null);
        Scene scene;
        Guid sequenceId;
        await using (var seed = CreateContext())
        {
            scene = await SeedSceneAsync(seed, commandCount: 4);
            sequenceId = AddArmed(seed, scene, recipeCount: 4).Id;
            await seed.SaveChangesAsync();
        }

        for (var ordinal = 0; ordinal < 6; ordinal++)
        {
            var kind = ordinal == 0 ? LocalExecutionStepKind.Implementation : LocalExecutionStepKind.CodeReview;
            await ExecuteAsync(Insert("local_execution_sequence_steps", Concluded(RawStep(scene, sequenceId, ordinal, kind))), foreignKeys: false);
        }

        var seventh = await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(
            Insert("local_execution_sequence_steps", RawStep(scene, sequenceId, 6)), foreignKeys: false));
        Assert.Contains(BoundMessage, seventh.Message, StringComparison.Ordinal);

        // A verification step is not an Agent claim.
        await ExecuteAsync(Insert("local_execution_sequence_steps", RawStep(scene, sequenceId, 6, LocalExecutionStepKind.Verification)), foreignKeys: false);

        // A revoked step consumed nothing: free one slot by deleting a counted row and fill it with a revoked one is not enough,
        // so prove a revoked row is excluded by counting in a separate sequence.
        Guid second;
        await using (var seed = CreateContext())
        {
            var other = await SeedSceneAsync(seed, runNumber: 2, commandCount: 4);
            second = AddArmed(seed, other, recipeCount: 4).Id;
            await seed.SaveChangesAsync();
            for (var ordinal = 1; ordinal <= 8; ordinal++)
            {
                await ExecuteAsync(
                    Insert("local_execution_sequence_steps", RawStep(other, second, ordinal, LocalExecutionStepKind.CodeReview)), foreignKeys: false);
            }
        }

        Assert.Equal(8, await CountAsync($"SELECT COUNT(*) FROM local_execution_sequence_steps WHERE \"SequenceId\" = {Q(second)}"));
    }

    [Fact]
    public async Task Corrections_beyond_the_pinned_allowance_are_refused_and_a_revoked_correction_does_not_count()
    {
        await MigrateToAsync(null);
        Scene scene;
        Guid sequenceId;
        await using (var seed = CreateContext())
        {
            scene = await SeedSceneAsync(seed);
            sequenceId = AddArmed(seed, scene, allowance: 1).Id;
            await seed.SaveChangesAsync();
        }

        var revoked = RawStep(scene, sequenceId, 3, LocalExecutionStepKind.ReviewCorrection);
        await ExecuteAsync(Insert("local_execution_sequence_steps", revoked), foreignKeys: false);
        await ExecuteAsync(
            Insert("local_execution_sequence_steps", Concluded(RawStep(scene, sequenceId, 4, LocalExecutionStepKind.DiagnosisCorrection))),
            foreignKeys: false);

        var second = await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(
            Insert("local_execution_sequence_steps", RawStep(scene, sequenceId, 5, LocalExecutionStepKind.ReviewCorrection)), foreignKeys: false));
        Assert.Contains(BoundMessage, second.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_sequence_with_no_correction_allowance_admits_no_correction_step()
    {
        await MigrateToAsync(null);
        Scene scene;
        Guid sequenceId;
        await using (var seed = CreateContext())
        {
            scene = await SeedSceneAsync(seed);
            sequenceId = AddArmed(seed, scene, allowance: 0).Id;
            await seed.SaveChangesAsync();
        }

        var exception = await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(
            Insert("local_execution_sequence_steps", RawStep(scene, sequenceId, 3, LocalExecutionStepKind.DiagnosisCorrection)), foreignKeys: false));
        Assert.Contains(BoundMessage, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Ordinal", "7")]
    [InlineData("PassNumber", "2")]
    [InlineData("SourceFingerprintSha256", "'cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc'")]
    [InlineData("Kind", "'CodeReview'")]
    [InlineData("PreparationOwnerToken", "'11111111-1111-1111-1111-111111111111'")]
    [InlineData("ReservedOperationId", "'22222222-2222-2222-2222-222222222222'")]
    [InlineData("SequenceId", "'33333333-3333-3333-3333-333333333333'")]
    [InlineData("PreparationDeadlineUtc", "'2026-10-20 10:00:00'")]
    [InlineData("PreparationReservedAtUtc", "'2026-10-20 10:00:00'")]
    [InlineData("ReportMessageId", "'44444444-4444-4444-4444-444444444444'")]
    [InlineData("PredecessorStepId", "'55555555-5555-5555-5555-555555555555'")]
    [InlineData("SourceCheckpointId", "'66666666-6666-6666-6666-666666666666'")]
    public async Task A_step_identity_fact_is_immutable(string column, string value)
    {
        await MigrateToAsync(null);
        Guid stepId;
        await using (var seed = CreateContext())
        {
            var scene = await SeedSceneAsync(seed);
            var sequence = AddArmed(seed, scene);
            await seed.SaveChangesAsync();
            var step = ImplementationStep(sequence.Id, scene);
            stepId = step.Id;
            seed.LocalExecutionSequenceSteps.Add(step);
            await seed.SaveChangesAsync();
        }

        var exception = await Assert.ThrowsAsync<SqliteException>(
            () => ExecuteAsync($"UPDATE local_execution_sequence_steps SET \"{column}\" = {value} WHERE \"Id\" = {Q(stepId)}", foreignKeys: false));
        Assert.Contains(ImmutableMessage, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_step_moves_only_along_the_legal_transitions_and_a_final_step_never_changes_again()
    {
        await MigrateToAsync(null);
        Scene scene;
        Guid sequenceId;
        LocalExecutionSequenceStep step;
        await using (var seed = CreateContext())
        {
            scene = await SeedSceneAsync(seed);
            var sequence = AddArmed(seed, scene);
            sequenceId = sequence.Id;
            await seed.SaveChangesAsync();
            step = ImplementationStep(sequence.Id, scene);
            seed.LocalExecutionSequenceSteps.Add(step);
            await seed.SaveChangesAsync();
        }

        var id = Q(step.Id);
        var operation = Q(step.ReservedOperationId);

        // Reserved cannot jump straight to Concluded.
        var jump = await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(
            $"UPDATE local_execution_sequence_steps SET \"State\" = 'Concluded', \"OperationId\" = \"ReservedOperationId\", \"ClaimedAtUtc\" = '2026-10-20 09:00:10', "
            + $"\"ResultDeadlineUtc\" = '2026-10-20 09:30:00' WHERE \"Id\" = {id}"));
        Assert.Contains(ImmutableMessage, jump.Message, StringComparison.Ordinal);

        await using (var claim = CreateContext())
        {
            var tracked = await claim.LocalExecutionSequenceSteps.SingleAsync(candidate => candidate.Id == step.Id);
            Claim(tracked);
            await claim.SaveChangesAsync();
        }

        // A claimed step moves only to Concluded and its link, claim time and result deadline never change.
        var revoke = await Assert.ThrowsAsync<SqliteException>(
            () => ExecuteAsync($"UPDATE local_execution_sequence_steps SET \"State\" = 'Revoked', \"OperationId\" = NULL WHERE \"Id\" = {id}"));
        Assert.Contains(ImmutableMessage, revoke.Message, StringComparison.Ordinal);
        var unlink = await Assert.ThrowsAsync<SqliteException>(
            () => ExecuteAsync($"UPDATE local_execution_sequence_steps SET \"OperationId\" = NULL WHERE \"Id\" = {id}"));
        Assert.Contains(ImmutableMessage, unlink.Message, StringComparison.Ordinal);
        var deadline = await Assert.ThrowsAsync<SqliteException>(
            () => ExecuteAsync($"UPDATE local_execution_sequence_steps SET \"ResultDeadlineUtc\" = '2026-10-20 12:00:00' WHERE \"Id\" = {id}"));
        Assert.Contains(ImmutableMessage, deadline.Message, StringComparison.Ordinal);
        var relink = await Assert.ThrowsAsync<SqliteException>(
            () => ExecuteAsync($"UPDATE local_execution_sequence_steps SET \"ClaimedAtUtc\" = '2026-10-20 09:05:00' WHERE \"Id\" = {id}"));
        Assert.Contains(ImmutableMessage, relink.Message, StringComparison.Ordinal);
        Assert.NotNull(operation);

        await using (var conclude = CreateContext())
        {
            var tracked = await conclude.LocalExecutionSequenceSteps.SingleAsync(candidate => candidate.Id == step.Id);
            tracked.Conclude(Now.AddMinutes(1));
            await conclude.SaveChangesAsync();
        }

        var after = await Assert.ThrowsAsync<SqliteException>(
            () => ExecuteAsync($"UPDATE local_execution_sequence_steps SET \"ReasonCode\" = 'operation_failed' WHERE \"Id\" = {id}"));
        Assert.Contains(ImmutableMessage, after.Message, StringComparison.Ordinal);
        var noop = await Assert.ThrowsAsync<SqliteException>(
            () => ExecuteAsync($"UPDATE local_execution_sequence_steps SET \"Revision\" = \"Revision\" WHERE \"Id\" = {id}"));
        Assert.Contains(ImmutableMessage, noop.Message, StringComparison.Ordinal);
        Assert.NotEqual(Guid.Empty, sequenceId);
    }

    [Theory]
    [InlineData("Revoke")]
    [InlineData("Unresolved")]
    public async Task A_revoked_or_unresolved_step_never_changes_again(string transition)
    {
        await MigrateToAsync(null);
        Guid stepId;
        await using (var seed = CreateContext())
        {
            var scene = await SeedSceneAsync(seed);
            var sequence = AddArmed(seed, scene);
            await seed.SaveChangesAsync();
            var step = ImplementationStep(sequence.Id, scene);
            stepId = step.Id;
            seed.LocalExecutionSequenceSteps.Add(step);
            await seed.SaveChangesAsync();
            if (transition == "Revoke")
            {
                step.Revoke(LocalExecutionSequenceReason.StoppedBeforeClaim, Now);
            }
            else
            {
                step.MarkUnresolved(LocalExecutionSequenceReason.PreparationUnresolved, Now);
            }

            await seed.SaveChangesAsync();
        }

        var exception = await Assert.ThrowsAsync<SqliteException>(
            () => ExecuteAsync($"UPDATE local_execution_sequence_steps SET \"State\" = 'Reserved' WHERE \"Id\" = {Q(stepId)}"));
        Assert.Contains(ImmutableMessage, exception.Message, StringComparison.Ordinal);
        var other = await Assert.ThrowsAsync<SqliteException>(
            () => ExecuteAsync($"UPDATE local_execution_sequence_steps SET \"ReasonCode\" = 'operation_failed' WHERE \"Id\" = {Q(stepId)}"));
        Assert.Contains(ImmutableMessage, other.Message, StringComparison.Ordinal);
    }

    private async Task InsertRawLocalCommitAsync(Scene scene)
    {
        await using var probe = new SqliteConnection($"Data Source={DatabasePath};Foreign Keys=False;Pooling=False");
        await probe.OpenAsync();
        await using var columns = probe.CreateCommand();
        columns.CommandText = "SELECT name, type, \"notnull\" FROM pragma_table_info('local_commit_operations');";
        var names = new List<(string Name, string Type, bool NotNull)>();
        await using (var reader = await columns.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                names.Add((reader.GetString(0), reader.GetString(1), reader.GetInt32(2) == 1));
            }
        }

        var values = names.Where(column => column.NotNull).ToDictionary(
            column => column.Name,
            column => column.Name switch
            {
                "Id" => Q(Guid.NewGuid()),
                "RunId" => Q(scene.Run.Id),
                "ProjectId" => Q(scene.Run.ProjectId),
                "GitWorkspaceId" => Q(scene.Workspace.Id),
                _ => column.Type.Contains("INT", StringComparison.OrdinalIgnoreCase) ? "1" : "''",
            });
        await using var ignore = probe.CreateCommand();
        ignore.CommandText = "PRAGMA ignore_check_constraints = ON;";
        await ignore.ExecuteNonQueryAsync();
        await using var insert = probe.CreateCommand();
#pragma warning disable CA2100 // Test-only SQL assembled from constants and identifiers this test generated.
        insert.CommandText = Insert("local_commit_operations", values);
#pragma warning restore CA2100
        await insert.ExecuteNonQueryAsync();
    }
}
