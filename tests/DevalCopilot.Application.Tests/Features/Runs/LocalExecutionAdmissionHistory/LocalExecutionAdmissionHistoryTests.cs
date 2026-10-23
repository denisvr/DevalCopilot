using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionAdmissionHistory;
using DevalCopilot.Application.Tests.Features.Runs.LocalExecutionInspection;
using DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences.ScriptedResultKind;
using Step = DevalCopilot.Domain.Features.Runs.LocalExecutionStepKind;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionAdmissionHistory;

/// <summary>
/// The recorded admission ledger of one queued local execution cycle (ADR-0063) over real file-backed SQLite, every admission created and
/// moved by the production commands: the closed NotRecorded, NotQueued, Recorded and Unavailable answers; every admission state with its
/// exact link (a waiting or retired-while-waiting admission has no step); both correction origins and the recipe order of whole cycles;
/// the recorded timestamps and reasons; stability after later legitimate changes; and a read-only, no-forbidden-column, one-statement
/// proof.
/// </summary>
public sealed class LocalExecutionAdmissionHistoryTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    public static TheoryData<string> Scenarios =>
    [
        "approval",
        "review-correction",
        "diagnosis-correction",
        "mixed-origins",
        "review-exhaustion",
        "diagnosis-exhaustion",
        "provider-escalation",
        "implementation-without-exit",
        "verification-timed-out",
        "review-failed",
    ];

    private static (int Recipes, int Corrections, LocalExecutionStepScript Script) Scenario(string name) => name switch
    {
        "approval" => (2, 2, new LocalExecutionStepScript()),
        "review-correction" => (2, 2, new LocalExecutionStepScript { Findings = 3 }.On(Step.CodeReview, 1, ReviewChangesRequested)),
        "diagnosis-correction" => (3, 2, new LocalExecutionStepScript { Findings = 2 }.Verify(1, 0, VerificationFailed)),
        "mixed-origins" => (1, 2, LocalExecutionAdmissionHistoryHarness.MixedOriginsScript()),
        "review-exhaustion" => (1, 2, new LocalExecutionStepScript().On(Step.CodeReview, ReviewChangesRequested)),
        "diagnosis-exhaustion" => (1, 1, new LocalExecutionStepScript().Verify(1, 0, VerificationFailed).Verify(2, 0, VerificationFailed)),
        "provider-escalation" => (2, 2, new LocalExecutionStepScript().Verify(1, 1, VerificationFailed).On(Step.VerificationDiagnosis, DiagnosisEscalated)),
        "implementation-without-exit" => (2, 2, new LocalExecutionStepScript().On(Step.Implementation, ProviderFailedNoExit)),
        "verification-timed-out" => (2, 2, new LocalExecutionStepScript().Verify(1, 0, VerificationFailed).Verify(1, 1, VerificationTimedOut)),
        "review-failed" => (1, 2, new LocalExecutionStepScript().On(Step.CodeReview, ProviderFailedWithExit)),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    // ---- absence and mode ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_unknown_run_is_one_fixed_not_found()
    {
        var (harness, _) = await LocalExecutionAdmissionHistoryHarness.QueuedAsync(fixture);

        var result = await harness.TryHistoryAsync(Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(LocalExecutionInspectionErrors.NotFoundCode, result.Errors[0].Code);
    }

    [Theory]
    [InlineData(RunExecutionMode.ManualAgent)]
    [InlineData(RunExecutionMode.Legacy)]
    public async Task A_known_run_without_a_sequence_is_not_recorded_with_no_header_and_no_admissions(RunExecutionMode mode)
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture, mode: mode);

        var history = await new LocalExecutionAdmissionHistoryHarness(scene).HistoryAsync();

        Assert.Equal(scene.RunId, history.RunId);
        LocalExecutionAdmissionHistoryHarness.AssertShowsNothing(history, LocalExecutionAdmissionHistoryStatus.NotRecorded);
    }

    [Fact]
    public async Task An_immediate_sequence_is_not_queued_before_and_after_its_whole_cycle_and_never_an_empty_ledger()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture);
        var plan = await scene.AddAcceptedRootPlanAsync();
        var armed = await scene.ArmAsync(plan.ProposalId);
        var harness = new LocalExecutionAdmissionHistoryHarness(scene);

        LocalExecutionAdmissionHistoryHarness.AssertShowsNothing(await harness.HistoryAsync(), LocalExecutionAdmissionHistoryStatus.NotQueued);

        await scene.DriveAsync(armed.SequenceId, LocalExecutionInspectionHarness.AllKindsScript());

        Assert.NotEmpty(await scene.ReadStepsAsync());
        Assert.Empty(await scene.ReadAdmissionsAsync());
        LocalExecutionAdmissionHistoryHarness.AssertShowsNothing(await harness.HistoryAsync(), LocalExecutionAdmissionHistoryStatus.NotQueued);
    }

    // ---- every admission state -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_waiting_admission_with_no_step_is_recorded_with_its_exact_header_and_no_link()
    {
        var (harness, sequenceId) = await LocalExecutionAdmissionHistoryHarness.QueuedAsync(fixture, recipeCount: 3);

        var history = await harness.HistoryAsync();

        await harness.AssertMirrorsStorageAsync(history);
        Assert.Equal(sequenceId, history.Sequence!.SequenceId);
        Assert.Equal(LocalExecutionSequenceState.WaitingForAdmission, history.Sequence.RecordedState);
        Assert.Null(history.Sequence.ReasonCode);
        Assert.Null(history.Sequence.EndedAtUtc);
        var waiting = Assert.Single(history.Admissions);
        Assert.Equal(LocalExecutionAdmissionState.Waiting, waiting.RecordedState);
        Assert.Equal(Step.Implementation, waiting.Kind);
        Assert.Equal(0, waiting.PassNumber);
        Assert.Equal(LocalExecutionWaitingReason.AwaitingAdmissionCheck, waiting.WaitingReason);
        Assert.Null(waiting.StepId);
        Assert.Null(waiting.ReasonCode);
        Assert.Empty(await harness.Scene.ReadStepsAsync());
    }

    [Fact]
    public async Task A_changed_waiting_observation_updates_the_admission_but_not_the_sequence_and_the_ledger_shows_the_recorded_reason()
    {
        var (harness, _) = await LocalExecutionAdmissionHistoryHarness.QueuedAsync(fixture);
        var before = await harness.HistoryAsync();
        var sequenceBefore = await harness.Scene.ReadSequenceAsync();
        harness.Scene.Clock.Advance(TimeSpan.FromMinutes(5));
        await harness.Scene.AddWorkspaceOwnerAsync(OwnerFamily.Agent);

        await harness.Scene.ObserveWholeQueueAsync();

        var after = await harness.HistoryAsync();
        await harness.AssertMirrorsStorageAsync(after);
        var sequenceAfter = await harness.Scene.ReadSequenceAsync();
        Assert.Equal(sequenceBefore.Revision, sequenceAfter.Revision);
        Assert.Equal(before.Sequence!.SequenceRevision, after.Sequence!.SequenceRevision);
        var member = Assert.Single(after.Admissions);
        Assert.Equal(LocalExecutionWaitingReason.WaitingForWorkspace, member.WaitingReason);
        Assert.Equal(Assert.Single(before.Admissions).Revision + 1, member.Revision);
        Assert.True(member.WaitingReasonRecordedAtUtc > Assert.Single(before.Admissions).WaitingReasonRecordedAtUtc);
        Assert.Null(member.StepId);
    }

    [Fact]
    public async Task A_reserved_admission_links_its_exact_reserved_step_and_a_claimed_one_is_consumed_with_its_claimed_step()
    {
        var (harness, sequenceId) = await LocalExecutionAdmissionHistoryHarness.QueuedAsync(fixture);
        var ticket = (await harness.Scene.ReserveAsync())!;

        var reserved = await harness.HistoryAsync();

        await harness.AssertMirrorsStorageAsync(reserved);
        Assert.Equal(LocalExecutionSequenceState.PreparingStep, reserved.Sequence!.RecordedState);
        var member = Assert.Single(reserved.Admissions);
        Assert.Equal(LocalExecutionAdmissionState.Reserved, member.RecordedState);
        Assert.Equal(ticket.StepId, member.StepId);
        Assert.Null(member.ReasonCode);

        var claim = await harness.Scene.ClaimAsync(ticket);
        Assert.True(claim.IsSuccess);
        var consumed = await harness.HistoryAsync();

        await harness.AssertMirrorsStorageAsync(consumed);
        Assert.Equal(LocalExecutionSequenceState.AwaitingStepResult, consumed.Sequence!.RecordedState);
        var claimed = Assert.Single(consumed.Admissions);
        Assert.Equal(LocalExecutionAdmissionState.Consumed, claimed.RecordedState);
        Assert.Equal(ticket.StepId, claimed.StepId);
        Assert.Equal(member.AdmissionId, claimed.AdmissionId);
        Assert.Equal(member.AdmissionNumber, claimed.AdmissionNumber);
        Assert.Equal(member.Revision + 1, claimed.Revision);
        Assert.Equal(sequenceId, consumed.Sequence.SequenceId);
    }

    [Fact]
    public async Task A_concluded_step_and_the_waiting_successor_it_recorded_are_both_in_the_ledger()
    {
        var (harness, sequenceId) = await LocalExecutionAdmissionHistoryHarness.QueuedAsync(fixture);
        await harness.DriveStepsThenWaitAsync(sequenceId, steps: 1);

        var history = await harness.HistoryAsync();

        await harness.AssertMirrorsStorageAsync(history);
        Assert.Equal(LocalExecutionSequenceState.WaitingForAdmission, history.Sequence!.RecordedState);
        Assert.Equal(LocalExecutionAdmissionState.Consumed, history.Admissions[0].RecordedState);
        Assert.NotNull(history.Admissions[0].StepId);
        var tail = history.Admissions[^1];
        Assert.Equal(LocalExecutionAdmissionState.Waiting, tail.RecordedState);
        Assert.Null(tail.StepId);
        Assert.Equal(Step.Verification, tail.Kind);
        Assert.Equal(1, tail.PassNumber);
        Assert.True(tail.AdmissionNumber > history.Admissions[0].AdmissionNumber);
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task A_whole_queued_cycle_lists_one_consumed_admission_per_step_in_order_with_every_exact_link(string name)
    {
        var (recipes, corrections, script) = Scenario(name);
        var (harness, _) = await LocalExecutionAdmissionHistoryHarness.EndedAsync(fixture, recipes, script, corrections);

        var history = await harness.HistoryAsync();

        await harness.AssertMirrorsStorageAsync(history);
        var steps = await harness.Scene.ReadStepsAsync();
        Assert.Equal(steps.Count, history.Admissions.Count);
        for (var position = 0; position < steps.Count; position++)
        {
            Assert.Equal(LocalExecutionAdmissionState.Consumed, history.Admissions[position].RecordedState);
            Assert.Equal(steps[position].Id, history.Admissions[position].StepId);
            Assert.Equal(steps[position].Kind, history.Admissions[position].Kind);
            Assert.Equal(steps[position].PassNumber, history.Admissions[position].PassNumber);
            Assert.Equal(steps[position].RecipeOrdinal, history.Admissions[position].RecipeOrdinal);
        }

        Assert.True(history.Admissions.Zip(history.Admissions.Skip(1), (earlier, later) => earlier.AdmissionNumber < later.AdmissionNumber).All(ordered => ordered));
        Assert.Equal((await harness.Scene.ReadSequenceAsync()).State, history.Sequence!.RecordedState);
    }

    [Fact]
    public async Task Both_correction_origins_and_every_recipe_in_its_pinned_order_are_recorded_in_one_queued_cycle()
    {
        var script = new LocalExecutionStepScript().Verify(1, 1, VerificationFailed).On(Step.CodeReview, 2, ReviewChangesRequested);
        var (harness, _) = await LocalExecutionAdmissionHistoryHarness.EndedAsync(fixture, 3, script);

        var history = await harness.HistoryAsync();

        await harness.AssertMirrorsStorageAsync(history);
        Assert.Contains(history.Admissions, member => member.Kind == Step.DiagnosisCorrection);
        Assert.Contains(history.Admissions, member => member.Kind == Step.ReviewCorrection);
        var firstPass = history.Admissions.Where(member => member.Kind == Step.Verification && member.PassNumber == 1)
            .Select(member => (member.RecipeOrdinal, member.RecipeName)).ToArray();
        var pinned = await harness.Scene.ReadPinnedRecipesAsync();
        Assert.Equal(pinned.Select(recipe => ((int?)recipe.Ordinal, (string?)recipe.CommandName)), firstPass);
        Assert.Equal(3, pinned.Count);
        Assert.Equal(LocalExecutionSequenceState.ReadyForHumanApproval, history.Sequence!.RecordedState);
        Assert.Equal(LocalExecutionSequenceReason.ReviewApproved, history.Sequence.ReasonCode);
    }

    // ---- retired, unresolved and the restart closure ---------------------------------------------------------------------------

    [Fact]
    public async Task A_stop_while_waiting_retires_the_admission_with_no_step_and_both_ledger_and_header_say_so()
    {
        var (harness, sequenceId) = await LocalExecutionAdmissionHistoryHarness.QueuedAsync(fixture);
        Assert.True((await harness.Scene.StopAsync(sequenceId)).IsSuccess);

        var history = await harness.HistoryAsync();

        await harness.AssertMirrorsStorageAsync(history);
        Assert.Equal(LocalExecutionSequenceState.Stopped, history.Sequence!.RecordedState);
        Assert.Equal(LocalExecutionSequenceReason.StoppedBeforeClaim, history.Sequence.ReasonCode);
        var retired = Assert.Single(history.Admissions);
        Assert.Equal(LocalExecutionAdmissionState.Retired, retired.RecordedState);
        Assert.Equal(LocalExecutionSequenceReason.StoppedBeforeClaim, retired.ReasonCode);
        Assert.Null(retired.StepId);
        Assert.Empty(await harness.Scene.ReadStepsAsync());
    }

    [Fact]
    public async Task A_wait_retired_after_earlier_consumed_steps_keeps_the_whole_history_and_has_no_step_of_its_own()
    {
        var (harness, sequenceId) = await LocalExecutionAdmissionHistoryHarness.QueuedAsync(fixture);
        await harness.DriveStepsThenWaitAsync(sequenceId, steps: 1);
        Assert.True((await harness.Scene.StopAsync(sequenceId)).IsSuccess);

        var history = await harness.HistoryAsync();

        await harness.AssertMirrorsStorageAsync(history);
        Assert.Equal(LocalExecutionAdmissionState.Consumed, history.Admissions[0].RecordedState);
        var retired = history.Admissions[^1];
        Assert.Equal(LocalExecutionAdmissionState.Retired, retired.RecordedState);
        Assert.Null(retired.StepId);
        Assert.Equal(LocalExecutionSequenceReason.StoppedBeforeClaim, retired.ReasonCode);
        Assert.Equal((await harness.Scene.ReadStepsAsync()).Count, history.Admissions.Count(member => member.StepId is not null));
    }

    [Fact]
    public async Task A_proved_unused_reservation_retired_by_a_stop_links_its_exact_revoked_step()
    {
        var (harness, sequenceId) = await LocalExecutionAdmissionHistoryHarness.QueuedAsync(fixture);
        var ticket = (await harness.Scene.ReserveAsync())!;
        Assert.True((await harness.Scene.StopAsync(sequenceId)).IsSuccess);

        var history = await harness.HistoryAsync();

        await harness.AssertMirrorsStorageAsync(history);
        var retired = Assert.Single(history.Admissions);
        Assert.Equal(LocalExecutionAdmissionState.Retired, retired.RecordedState);
        Assert.Equal(ticket.StepId, retired.StepId);
        Assert.Equal(LocalExecutionSequenceStepState.Revoked, Assert.Single(await harness.Scene.ReadStepsAsync()).State);
        Assert.Equal(LocalExecutionSequenceState.Stopped, history.Sequence!.RecordedState);
    }

    [Fact]
    public async Task An_unresolved_reservation_is_retained_on_the_ended_sequence_and_links_its_unresolved_step()
    {
        var (harness, sequenceId) = await LocalExecutionAdmissionHistoryHarness.QueuedAsync(fixture);
        var ticket = (await harness.Scene.ReserveAsync())!;
        await harness.Scene.PlantReservedOperationAsync(ticket);
        Assert.True((await harness.Scene.StopAsync(sequenceId)).IsSuccess);

        var history = await harness.HistoryAsync();

        await harness.AssertMirrorsStorageAsync(history);
        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, history.Sequence!.RecordedState);
        Assert.Equal(LocalExecutionSequenceReason.PreparationUnresolved, history.Sequence.ReasonCode);
        var held = Assert.Single(history.Admissions);
        Assert.Equal(LocalExecutionAdmissionState.Unresolved, held.RecordedState);
        Assert.Equal(ticket.StepId, held.StepId);
        Assert.Equal(LocalExecutionSequenceReason.PreparationUnresolved, held.ReasonCode);
        Assert.Equal(LocalExecutionSequenceStepState.Unresolved, Assert.Single(await harness.Scene.ReadStepsAsync()).State);
    }

    [Fact]
    public async Task An_unanswered_independent_probe_holds_the_reservation_unresolved_and_the_ledger_shows_it()
    {
        var (harness, _) = await LocalExecutionAdmissionHistoryHarness.QueuedAsync(fixture);
        var ticket = (await harness.Scene.ReserveAsync())!;
        var resolved = await harness.Scene.ResolveAsync(
            ticket, "attempts.persistence_failed", new ConfigurableLocalExecutionStepProbe(LocalExecutionStepProbeResult.Unresolved));
        Assert.True(resolved.IsSuccess);

        var history = await harness.HistoryAsync();

        await harness.AssertMirrorsStorageAsync(history);
        Assert.Equal(LocalExecutionAdmissionState.Unresolved, Assert.Single(history.Admissions).RecordedState);
        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, history.Sequence!.RecordedState);
    }

    [Fact]
    public async Task A_restart_closes_a_waiting_a_reserved_and_a_claimed_cycle_without_resuming_and_each_reads_its_stored_end()
    {
        // The reservation and the startup closure are global to one database, so each cycle lives in a database of its own.
        await using var reservedDatabase = new SqliteDatabaseFixture();
        await reservedDatabase.InitializeAsync();
        await using var claimedDatabase = new SqliteDatabaseFixture();
        await claimedDatabase.InitializeAsync();
        var (waiting, _) = await LocalExecutionAdmissionHistoryHarness.QueuedAsync(fixture);
        var (reserved, _) = await LocalExecutionAdmissionHistoryHarness.QueuedAsync(reservedDatabase);
        var (claimed, _) = await LocalExecutionAdmissionHistoryHarness.QueuedAsync(claimedDatabase);
        var claimedTicket = (await claimed.Scene.ReserveAsync())!;
        Assert.True((await claimed.Scene.ClaimAsync(claimedTicket)).IsSuccess);
        var reservedTicket = (await reserved.Scene.ReserveAsync())!;

        await waiting.Scene.InterruptAsync();
        await reserved.Scene.InterruptAsync();
        await claimed.Scene.InterruptAsync();

        var waitingHistory = await waiting.HistoryAsync();
        await waiting.AssertMirrorsStorageAsync(waitingHistory);
        Assert.Equal(LocalExecutionSequenceState.Interrupted, waitingHistory.Sequence!.RecordedState);
        Assert.Equal(LocalExecutionSequenceReason.HostRestarted, waitingHistory.Sequence.ReasonCode);
        var retired = Assert.Single(waitingHistory.Admissions);
        Assert.Equal(LocalExecutionAdmissionState.Retired, retired.RecordedState);
        Assert.Null(retired.StepId);

        var reservedHistory = await reserved.HistoryAsync();
        await reserved.AssertMirrorsStorageAsync(reservedHistory);
        var revoked = Assert.Single(reservedHistory.Admissions);
        Assert.Equal(LocalExecutionAdmissionState.Retired, revoked.RecordedState);
        Assert.Equal(reservedTicket.StepId, revoked.StepId);

        var claimedHistory = await claimed.HistoryAsync();
        await claimed.AssertMirrorsStorageAsync(claimedHistory);
        var drained = Assert.Single(claimedHistory.Admissions);
        Assert.Equal(LocalExecutionAdmissionState.Consumed, drained.RecordedState);
        Assert.Equal(claimedTicket.StepId, drained.StepId);
        Assert.Equal(LocalExecutionSequenceStepState.Claimed, Assert.Single(await claimed.Scene.ReadStepsAsync()).State);
    }

    [Fact]
    public async Task An_expired_preparation_and_the_elapsed_advancement_deadline_retire_their_admissions_and_each_is_recorded()
    {
        var (expired, expiredSequence) = await LocalExecutionAdmissionHistoryHarness.QueuedAsync(fixture);
        var ticket = (await expired.Scene.ReserveAsync())!;
        expired.Scene.Clock.Advance(TimeSpan.FromMinutes(3));
        Assert.True((await expired.Scene.ObserveAsync(expiredSequence)).IsSuccess);

        var expiredHistory = await expired.HistoryAsync();

        await expired.AssertMirrorsStorageAsync(expiredHistory);
        Assert.Equal(LocalExecutionSequenceReason.PreparationExpired, expiredHistory.Sequence!.ReasonCode);
        var revoked = Assert.Single(expiredHistory.Admissions);
        Assert.Equal(LocalExecutionAdmissionState.Retired, revoked.RecordedState);
        Assert.Equal(ticket.StepId, revoked.StepId);
        Assert.Equal(LocalExecutionSequenceReason.PreparationExpired, revoked.ReasonCode);

        var (late, _) = await LocalExecutionAdmissionHistoryHarness.QueuedAsync(fixture);
        late.Scene.Clock.Advance(TimeSpan.FromHours(2) + TimeSpan.FromSeconds(1));
        await late.Scene.ObserveWholeQueueAsync();

        var lateHistory = await late.HistoryAsync();

        await late.AssertMirrorsStorageAsync(lateHistory);
        Assert.Equal(LocalExecutionSequenceReason.AdvancementDeadlineElapsed, lateHistory.Sequence!.ReasonCode);
        var closed = Assert.Single(lateHistory.Admissions);
        Assert.Equal(LocalExecutionAdmissionState.Retired, closed.RecordedState);
        Assert.Null(closed.StepId);
        Assert.True(lateHistory.Sequence.UpdatedAtUtc >= lateHistory.Sequence.AdvancementDeadlineUtc);
    }

    // ---- stability and effects --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Historical_members_survive_newer_checkpoints_recipe_edits_new_recipes_and_a_newer_run()
    {
        var (harness, _) = await LocalExecutionAdmissionHistoryHarness.EndedAsync(fixture, 2, LocalExecutionAdmissionHistoryHarness.MixedOriginsScript());
        var before = await harness.HistoryAsync();
        var recipes = await harness.Scene.ReadRecipesAsync();
        await harness.Scene.EditRecipeAsync(recipes[0].Id, executablePath: @"C:\tools\renamed.exe", timeoutSeconds: 120);
        await harness.Scene.AddRecipeAsync(9, "A later recipe");
        await using (var context = fixture.CreateContext())
        {
            var workspace = await context.GitWorkspaces.FindAsync(harness.Scene.WorkspaceId);
            var number = workspace!.ReserveCheckpointNumber();
            context.GitCheckpoints.Add(GitCheckpoint.Capture(
                Guid.NewGuid(), workspace.Id, number, LocalExecutionSequenceTestScene.Start.AddHours(1),
                LocalExecutionSequenceTestScene.Head, new string('9', 64), []));
            await context.SaveChangesAsync();
        }

        var later = await LocalExecutionSequenceTestScene.CreateAsync(fixture);
        await harness.Scene.ReleaseLeaseAsync();
        var after = await harness.HistoryAsync();

        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.Sequence, after.Sequence);
        Assert.Equal(before.Admissions, after.Admissions);
        Assert.NotEqual(later.RunId, after.RunId);
    }

    [Fact]
    public async Task Reading_the_history_runs_exactly_one_read_only_statement_naming_every_joined_table_and_changes_nothing()
    {
        var (harness, _) = await LocalExecutionAdmissionHistoryHarness.EndedAsync(fixture, 2, LocalExecutionAdmissionHistoryHarness.MixedOriginsScript());
        var before = await harness.ChangeFingerprintAsync();
        var admissionsBefore = string.Join('|', (await harness.Scene.ReadAdmissionsAsync()).Select(row => $"{row.Revision}:{row.State}"));
        var log = new StatementLog();

        var history = await harness.HistoryAsync(null, log);

        Assert.Equal(LocalExecutionAdmissionHistoryStatus.Recorded, history.Status);
        Assert.Equal(before, await harness.ChangeFingerprintAsync());
        Assert.Equal(admissionsBefore, string.Join('|', (await harness.Scene.ReadAdmissionsAsync()).Select(row => $"{row.Revision}:{row.State}")));
        var statement = Assert.Single(log.Statements);
        log.AssertReadOnly();
        foreach (var table in new[]
        {
            "\"runs\"", "\"local_execution_sequences\"", "\"local_execution_admissions\"", "\"local_execution_sequence_steps\"",
            "\"git_workspaces\"", "\"git_checkpoints\"", "\"collaboration_messages\"", "\"local_execution_sequence_recipes\"",
        })
        {
            Assert.Contains(table, statement, StringComparison.Ordinal);
        }

        log.AssertNeverSelects(
            "\"PreparationOwnerToken\"", "\"SettingsSnapshot\"", "\"ConfirmationSha256\"", "\"PlanInputsSha256\"", "\"DetailCode\"",
            "\"ReservedOperationId\" AS", "\"ExecutablePath\"", "\"ArgumentsJson\"", "\"Arguments\"", "\"WorkspacePath\"",
            "\"RelativeStoragePath\"", "\"ContentHash\"", "\"StructuredContentJson\"", "\"Summary\"", "\"FinalReportMessageId\"");
        Assert.DoesNotContain("PreparationOwnerToken", statement, StringComparison.Ordinal);
        Assert.Contains("LIMIT", statement, StringComparison.Ordinal);
    }
}
