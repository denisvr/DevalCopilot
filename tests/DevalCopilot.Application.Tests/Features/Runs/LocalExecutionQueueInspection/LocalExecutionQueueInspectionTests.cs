using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionQueueInspection;
using DevalCopilot.Application.Tests.Features.Runs.LocalExecutionAdmissionHistory;
using DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Runs;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.LocalExecutionQueueInspection.LocalExecutionQueueInspectionHarness;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionQueueInspection;

/// <summary>
/// The recorded outstanding queue admissions of the whole database (ADR-0064) over real file-backed SQLite, every cycle created and
/// moved by the production commands in projects of its own: the closed Empty and Recorded answers; Waiting, Reserved and ended
/// Unresolved membership in recorded admission order across projects; the exclusion of Consumed and Retired admissions and of
/// Immediate cycles; recorded deadlines that passed without changing membership or state; each entry equal to the proved tail of the
/// per-run ADR-0063 ledger of its run; and a read-only, one-statement, no-forbidden-column proof. Bounds, damage and races have classes
/// of their own.
/// </summary>
public sealed class LocalExecutionQueueInspectionTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    // ---- empty is not idle ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_empty_database_and_a_project_without_a_queued_cycle_are_the_closed_empty_answer()
    {
        var harness = new LocalExecutionQueueInspectionHarness(fixture);
        AssertShowsNothing(await harness.InspectAsync(), LocalExecutionQueueInspectionStatus.Empty);

        await LocalExecutionSequenceTestScene.CreateAsync(fixture);

        AssertShowsNothing(await harness.InspectAsync(), LocalExecutionQueueInspectionStatus.Empty);
    }

    [Fact]
    public async Task An_immediate_cycle_has_no_admission_and_is_never_listed_even_while_it_owns_the_workspace()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture);
        var plan = await scene.AddAcceptedRootPlanAsync();
        var armed = await scene.ArmAsync(plan.ProposalId);
        var harness = new LocalExecutionQueueInspectionHarness(fixture);

        AssertShowsNothing(await harness.InspectAsync(), LocalExecutionQueueInspectionStatus.Empty);

        await scene.DriveAsync(armed.SequenceId, new LocalExecutionStepScript());
        AssertShowsNothing(await harness.InspectAsync(), LocalExecutionQueueInspectionStatus.Empty);
    }

    // ---- Waiting ---------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_waiting_cycle_is_one_recorded_entry_with_its_project_run_header_and_admission_equal_to_its_per_run_ledger()
    {
        var harness = new LocalExecutionQueueInspectionHarness(fixture);
        var (cycle, sequenceId) = await harness.AddCycleAsync(projectName: "Alpha platform");

        var result = await harness.InspectAsync();

        Assert.Equal(LocalExecutionQueueInspectionStatus.Recorded, result.Status);
        var entry = Assert.Single(result.Entries);
        Assert.Equal("Alpha platform", entry.ProjectName);
        Assert.Equal(sequenceId, entry.Sequence.SequenceId);
        Assert.Equal(LocalExecutionSequenceState.WaitingForAdmission, entry.Sequence.RecordedState);
        Assert.Equal(LocalExecutionAdmissionMode.WaitForAdmission, entry.Sequence.AdmissionMode);
        Assert.Equal(LocalExecutionAdmissionState.Waiting, entry.Admission.RecordedState);
        Assert.Equal(LocalExecutionStepKind.Implementation, entry.Admission.Kind);
        Assert.Equal(LocalExecutionWaitingReason.AwaitingAdmissionCheck, entry.Admission.WaitingReason);
        Assert.Null(entry.Admission.StepId);
        await AssertMatchesPerRunLedgerAsync(entry, cycle);
    }

    [Fact]
    public async Task Cycles_of_different_projects_are_listed_in_recorded_admission_order_whatever_the_project_or_run_order()
    {
        var harness = new LocalExecutionQueueInspectionHarness(fixture);
        var (second, _) = await harness.AddCycleAsync(projectName: "Zulu");
        var (first, _) = await harness.AddCycleAsync(projectName: "Alpha");
        var (third, _) = await harness.AddCycleAsync(projectName: "Mike");

        var result = await harness.InspectAsync();

        Assert.Equal(LocalExecutionQueueInspectionStatus.Recorded, result.Status);
        Assert.Equal(["Zulu", "Alpha", "Mike"], result.Entries.Select(entry => entry.ProjectName));
        Assert.Equal(
            [second.Scene.RunId, first.Scene.RunId, third.Scene.RunId],
            result.Entries.Select(entry => entry.RunId));
        Assert.Equal(
            result.Entries.Select(entry => entry.Admission.AdmissionNumber).Order(),
            result.Entries.Select(entry => entry.Admission.AdmissionNumber));
        Assert.Equal(3, result.Entries.Select(entry => entry.ProjectId).Distinct().Count());
        foreach (var (entry, cycle) in result.Entries.Zip([second, first, third]))
        {
            await AssertMatchesPerRunLedgerAsync(entry, cycle);
        }
    }

    [Fact]
    public async Task A_recorded_deadline_that_has_passed_neither_removes_the_cycle_nor_changes_its_stored_state()
    {
        var harness = new LocalExecutionQueueInspectionHarness(fixture);
        var (cycle, _) = await harness.AddCycleAsync();
        var before = await harness.ChangeFingerprintAsync();
        cycle.Scene.Clock.Advance(TimeSpan.FromHours(5));

        var result = await harness.InspectAsync();

        var entry = Assert.Single(result.Entries);
        Assert.True(entry.Sequence.AdvancementDeadlineUtc < cycle.Scene.Clock.GetUtcNow());
        Assert.Equal(LocalExecutionSequenceState.WaitingForAdmission, entry.Sequence.RecordedState);
        Assert.Equal(LocalExecutionAdmissionState.Waiting, entry.Admission.RecordedState);
        Assert.Equal(before, await harness.ChangeFingerprintAsync());
    }

    [Fact]
    public async Task A_changed_waiting_observation_shows_the_recorded_reason_and_revision_without_a_sequence_revision_change()
    {
        var harness = new LocalExecutionQueueInspectionHarness(fixture);
        var (cycle, _) = await harness.AddCycleAsync();
        var before = Assert.Single((await harness.InspectAsync()).Entries);
        cycle.Scene.Clock.Advance(TimeSpan.FromMinutes(5));
        await cycle.Scene.AddWorkspaceOwnerAsync(OwnerFamily.Agent);
        await cycle.Scene.ObserveWholeQueueAsync();

        var after = Assert.Single((await harness.InspectAsync()).Entries);

        Assert.Equal(LocalExecutionWaitingReason.WaitingForWorkspace, after.Admission.WaitingReason);
        Assert.True(after.Admission.WaitingReasonRecordedAtUtc > before.Admission.WaitingReasonRecordedAtUtc);
        Assert.Equal(before.Admission.Revision + 1, after.Admission.Revision);
        Assert.Equal(before.Sequence.SequenceRevision, after.Sequence.SequenceRevision);
        await AssertMatchesPerRunLedgerAsync(after, cycle);
    }

    // ---- Reserved --------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_reservation_is_listed_as_reserved_with_its_recorded_step_link_and_the_preparing_header()
    {
        var harness = new LocalExecutionQueueInspectionHarness(fixture);
        var (cycle, _) = await harness.AddCycleAsync();
        var (other, _) = await harness.AddCycleAsync();
        var ticket = (await cycle.Scene.ReserveAsync())!;

        var result = await harness.InspectAsync();

        Assert.Equal(LocalExecutionQueueInspectionStatus.Recorded, result.Status);
        Assert.Equal(2, result.Entries.Count);
        var reserved = result.Entries[0];
        Assert.Equal(LocalExecutionSequenceState.PreparingStep, reserved.Sequence.RecordedState);
        Assert.Equal(LocalExecutionAdmissionState.Reserved, reserved.Admission.RecordedState);
        Assert.Equal(ticket.StepId, reserved.Admission.StepId);
        Assert.Equal(LocalExecutionAdmissionState.Waiting, result.Entries[1].Admission.RecordedState);
        await AssertMatchesPerRunLedgerAsync(reserved, cycle);
        await AssertMatchesPerRunLedgerAsync(result.Entries[1], other);
    }

    // ---- exclusions ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_consumed_tail_is_excluded_and_only_the_cycles_next_waiting_admission_is_listed_again()
    {
        var harness = new LocalExecutionQueueInspectionHarness(fixture);
        var (cycle, sequenceId) = await harness.AddCycleAsync();
        var (other, _) = await harness.AddCycleAsync();
        var ticket = (await cycle.Scene.ReserveAsync())!;
        await cycle.Scene.DriveTicketAsync(ticket, new LocalExecutionStepScript());

        var claimed = await harness.InspectAsync();

        var onlyOther = Assert.Single(claimed.Entries);
        Assert.Equal(other.Scene.RunId, onlyOther.RunId);
        Assert.Equal(LocalExecutionSequenceState.AwaitingStepResult, (await cycle.Scene.ReadSequenceAsync()).State);
        Assert.Equal(LocalExecutionAdmissionState.Consumed, (await cycle.LastAdmissionAsync()).State);

        // The claimed operation concluded; observing it records the cycle's one waiting successor, which is outstanding again.
        Assert.True((await cycle.Scene.ObserveAsync(sequenceId)).IsSuccess);

        var successor = await harness.InspectAsync();

        Assert.Equal(2, successor.Entries.Count);
        var listed = Assert.Single(successor.Entries, entry => entry.RunId == cycle.Scene.RunId);
        Assert.Equal(LocalExecutionAdmissionState.Waiting, listed.Admission.RecordedState);
        Assert.Equal(1, listed.Admission.Ordinal);
        await AssertMatchesPerRunLedgerAsync(listed, cycle);
    }

    [Fact]
    public async Task A_retired_wait_and_a_retired_reservation_are_excluded()
    {
        var harness = new LocalExecutionQueueInspectionHarness(fixture);
        var (waiting, waitingSequence) = await harness.AddCycleAsync();
        var (reserved, reservedSequence) = await harness.AddCycleAsync();
        Assert.True((await waiting.Scene.StopAsync(waitingSequence)).IsSuccess);
        Assert.NotNull(await reserved.Scene.ReserveAsync());
        Assert.True((await reserved.Scene.StopAsync(reservedSequence)).IsSuccess);
        var (live, _) = await harness.AddCycleAsync();

        var result = await harness.InspectAsync();

        Assert.Equal(LocalExecutionAdmissionState.Retired, (await waiting.LastAdmissionAsync()).State);
        Assert.Equal(LocalExecutionAdmissionState.Retired, (await reserved.LastAdmissionAsync()).State);
        var entry = Assert.Single(result.Entries);
        Assert.Equal(live.Scene.RunId, entry.RunId);
    }

    [Fact]
    public async Task A_whole_cycle_that_ended_with_a_consumed_tail_is_excluded()
    {
        var harness = new LocalExecutionQueueInspectionHarness(fixture);
        var (ended, _) = await harness.AddCycleAsync();
        var queueHost = new LocalExecutionQueueHost((ended.Scene, new LocalExecutionStepScript()));
        await queueHost.RunUntilAllEndedAsync();
        Assert.False(LocalExecutionSequencePolicy.IsAuthorityLive((await ended.Scene.ReadSequenceAsync()).State));

        AssertShowsNothing(await harness.InspectAsync(), LocalExecutionQueueInspectionStatus.Empty);
    }

    // ---- ended Unresolved --------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_unresolved_reservation_on_an_ended_sequence_is_listed_with_its_ended_header_and_its_unresolved_step_link()
    {
        var harness = new LocalExecutionQueueInspectionHarness(fixture);
        var (held, _) = await harness.AddCycleAsync(projectName: "Held reservation");
        var ticket = (await held.Scene.ReserveAsync())!;
        var resolved = await held.Scene.ResolveAsync(
            ticket, "attempts.persistence_failed", new ConfigurableLocalExecutionStepProbe(LocalExecutionStepProbeResult.Unresolved));
        Assert.True(resolved.IsSuccess);
        var (waiting, _) = await harness.AddCycleAsync(projectName: "Waiting behind it");

        var result = await harness.InspectAsync();

        Assert.Equal(LocalExecutionQueueInspectionStatus.Recorded, result.Status);
        Assert.Equal(["Held reservation", "Waiting behind it"], result.Entries.Select(entry => entry.ProjectName));
        var entry = result.Entries[0];
        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, entry.Sequence.RecordedState);
        Assert.Equal(LocalExecutionSequenceReason.PreparationUnresolved, entry.Sequence.ReasonCode);
        Assert.NotNull(entry.Sequence.EndedAtUtc);
        Assert.Equal(LocalExecutionAdmissionState.Unresolved, entry.Admission.RecordedState);
        Assert.Equal(ticket.StepId, entry.Admission.StepId);
        Assert.Equal(LocalExecutionSequenceReason.PreparationUnresolved, entry.Admission.ReasonCode);
        await AssertMatchesPerRunLedgerAsync(entry, held);
        await AssertMatchesPerRunLedgerAsync(result.Entries[1], waiting);
    }

    // ---- read only -------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_read_is_one_statement_that_names_every_joined_table_selects_no_forbidden_column_and_changes_nothing()
    {
        var harness = new LocalExecutionQueueInspectionHarness(fixture);
        var (cycle, sequenceId) = await harness.AddCycleAsync(recipeCount: 2);
        await cycle.DriveStepsThenWaitAsync(sequenceId, 3);
        await harness.AddCycleAsync();
        var before = await harness.ChangeFingerprintAsync();
        var recorder = new CommandRecorder();

        var result = await harness.InspectAsync(recorder);

        Assert.Equal(LocalExecutionQueueInspectionStatus.Recorded, result.Status);
        var statement = Assert.Single(recorder.Commands);
        Assert.Equal(before, await harness.ChangeFingerprintAsync());
        foreach (var table in new[]
        {
            "local_execution_admissions", "local_execution_sequences", "runs", "projects", "local_execution_sequence_steps", "git_workspaces",
            "git_checkpoints", "collaboration_messages", "local_execution_sequence_recipes",
        })
        {
            Assert.Contains($"\"{table}\"", statement, StringComparison.Ordinal);
        }

        Assert.Contains("LIMIT", statement, StringComparison.Ordinal);
        foreach (var forbidden in new[]
        {
            "PreparationOwnerToken", "SettingsSnapshot", "ConfirmationSha256", "PlanInputsSha256", "CanonicalPath", "RegistrationIdentityKey",
            "WorkspacePath", "ExecutablePath", "ArgumentsJson", "DetailCode", "Objective", "FinalReportMessageId", "PlanProposalMessageId",
            "RepositoryMutationLease", "attempts", "verification_executions",
        })
        {
            Assert.DoesNotContain(forbidden, statement, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_cycle_with_a_longer_ledger_and_a_waiting_tail_is_the_proved_tail_of_every_earlier_consumed_admission()
    {
        var harness = new LocalExecutionQueueInspectionHarness(fixture);
        var (cycle, sequenceId) = await harness.AddCycleAsync(recipeCount: 2);
        await cycle.DriveStepsThenWaitAsync(sequenceId, 7, LocalExecutionAdmissionHistoryHarness.MixedOriginsScript());

        var result = await harness.InspectAsync();

        var entry = Assert.Single(result.Entries);
        Assert.Equal(7, entry.Admission.Ordinal);
        Assert.Equal(LocalExecutionAdmissionState.Waiting, entry.Admission.RecordedState);
        await AssertMatchesPerRunLedgerAsync(entry, cycle);
    }
}
