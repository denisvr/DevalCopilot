using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionQueue;

/// <summary>
/// Queue intake (ADR-0062) through the production availability and arming handlers over real SQLite: the separately versioned confirmation,
/// mode-bound replay and conflict, the busy and capacity readings intake may wait for, the readings it refuses, the absence of any Git
/// observation, work or reservation at intake and the 64-cycle bound.
/// </summary>
public sealed class LocalExecutionQueueIntakeTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    private async Task<(LocalExecutionSequenceTestScene Scene, LocalExecutionSequenceTestScene.SeededPlan Plan)> NewSceneAsync(int recipeCount = 2)
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture, recipeCount);
        return (scene, await scene.AddAcceptedRootPlanAsync());
    }

    [Fact]
    public async Task The_queue_confirmation_is_separately_versioned_and_never_the_immediate_one()
    {
        var (scene, plan) = await NewSceneAsync();

        var immediate = await scene.ReadAvailabilityAsync(plan.ProposalId);
        var queued = await scene.ReadQueueAvailabilityAsync(plan.ProposalId);

        Assert.True(immediate.Available);
        Assert.True(queued.Available);
        Assert.Equal(LocalExecutionAdmissionMode.Immediate, immediate.AdmissionMode);
        Assert.Equal(LocalExecutionAdmissionMode.WaitForAdmission, queued.AdmissionMode);
        Assert.NotEqual(immediate.ConfirmationSha256, queued.ConfirmationSha256);
        Assert.Equal(immediate.FingerprintSha256, queued.FingerprintSha256);
        Assert.Equal(immediate.CheckpointId, queued.CheckpointId);

        // The same candidate yields a stable value for each mode: neither is a function of time or of the other.
        Assert.Equal(queued.ConfirmationSha256, (await scene.ReadQueueAvailabilityAsync(plan.ProposalId)).ConfirmationSha256);
        Assert.Equal(immediate.ConfirmationSha256, (await scene.ReadAvailabilityAsync(plan.ProposalId)).ConfirmationSha256);
    }

    [Fact]
    public async Task An_immediate_confirmation_cannot_queue_and_a_queue_confirmation_cannot_arm_immediate_execution()
    {
        var (scene, plan) = await NewSceneAsync();
        var immediate = await scene.ReadAvailabilityAsync(plan.ProposalId);
        var queued = await scene.ReadQueueAvailabilityAsync(plan.ProposalId);

        var queueWithImmediate = await scene.TryArmAsync(scene.CommandFrom(immediate) with { Mode = LocalExecutionAdmissionMode.WaitForAdmission });
        var armWithQueue = await scene.TryArmAsync(scene.CommandFrom(queued));

        Assert.True(queueWithImmediate.IsFailure);
        Assert.Equal(LocalExecutionSequenceErrors.ConfirmationStaleCode, queueWithImmediate.Errors[0].Code);
        Assert.True(armWithQueue.IsFailure);
        Assert.Equal(LocalExecutionSequenceErrors.ConfirmationStaleCode, armWithQueue.Errors[0].Code);
        Assert.Null(await scene.FindSequenceAsync());
        Assert.Empty(await scene.ReadAllAdmissionsAsync());
    }

    [Fact]
    public async Task Queue_intake_records_one_waiting_sequence_with_its_pins_and_first_admission_and_observes_no_git_or_work()
    {
        var (scene, plan) = await NewSceneAsync(recipeCount: 3);
        var work = await scene.CountWorkAsync();

        var receipt = await scene.QueueAsync(plan.ProposalId);

        Assert.Equal(work, await scene.CountWorkAsync());
        Assert.Equal(work.Captures, scene.Evidence.Captures);
        Assert.False(receipt.Replayed);
        Assert.Equal(LocalExecutionSequenceState.WaitingForAdmission, receipt.State);
        Assert.Equal(1, receipt.Revision);
        Assert.NotNull(receipt.Queue);
        Assert.Equal(LocalExecutionAdmissionState.Waiting, receipt.Queue.AdmissionState);
        Assert.Equal(LocalExecutionStepKind.Implementation, receipt.Queue.Kind);
        Assert.Equal(LocalExecutionWaitingReason.AwaitingAdmissionCheck, receipt.Queue.WaitingReason);
        Assert.Equal(receipt.ArmedAtUtc.AddHours(2), receipt.AdvancementDeadlineUtc);
        Assert.Equal(receipt.AdvancementDeadlineUtc, receipt.Queue.AdvancementDeadlineUtc);

        var sequence = await scene.ReadSequenceAsync();
        Assert.Equal(LocalExecutionAdmissionMode.WaitForAdmission, sequence.AdmissionMode);
        Assert.Equal(3, sequence.RecipeCount);
        Assert.Equal(3, (await scene.ReadPinnedRecipesAsync()).Count);
        Assert.Equal(scene.CheckpointId, sequence.CheckpointId);
        Assert.Equal(LocalExecutionSequenceTestScene.StartFingerprint, sequence.FingerprintSha256);

        var admission = Assert.Single(await scene.ReadAdmissionsAsync());
        Assert.Equal(LocalExecutionAdmissionState.Waiting, admission.State);
        Assert.Equal(0, admission.Ordinal);
        Assert.Equal(scene.CheckpointId, admission.SourceCheckpointId);
        Assert.Null(admission.StepId);

        // Waiting creates no operation step, token, attempt, verification, seal or budget use; exactly one Human event records the intake.
        Assert.Empty(await scene.ReadStepsAsync());
        Assert.Equal(1, await scene.CountEventsAsync(RunEventType.LocalExecutionSequenceArmed));
        Assert.Equal(0, await scene.CountEventsAsync(RunEventType.LocalExecutionSequenceStepReserved));
    }

    [Fact]
    public async Task An_identical_queue_tuple_replays_its_one_receipt_even_after_termination_and_another_tuple_or_mode_conflicts()
    {
        var (scene, plan) = await NewSceneAsync();
        var command = await scene.QueueCommandAsync(plan.ProposalId);
        var first = await scene.TryArmAsync(command);
        Assert.True(first.IsSuccess);

        var replay = await scene.TryArmAsync(command);
        Assert.True(replay.IsSuccess);
        Assert.True(replay.Value.Replayed);
        Assert.Equal(first.Value.SequenceId, replay.Value.SequenceId);
        Assert.Equal(first.Value.ArmedAtUtc, replay.Value.ArmedAtUtc);
        Assert.Single(await scene.ReadAllAdmissionsAsync());

        var stopped = await scene.StopAsync(first.Value.SequenceId);
        Assert.True(stopped.IsSuccess);
        var afterStop = await scene.TryArmAsync(command);
        Assert.True(afterStop.IsSuccess);
        Assert.True(afterStop.Value.Replayed);
        Assert.Equal(LocalExecutionSequenceState.Stopped, afterStop.Value.State);
        Assert.Null(afterStop.Value.Queue);

        var otherMode = await scene.TryArmAsync(command with { Mode = LocalExecutionAdmissionMode.Immediate });
        Assert.True(otherMode.IsFailure);
        Assert.Equal(LocalExecutionSequenceErrors.TupleConflictCode, otherMode.Errors[0].Code);
        var otherTuple = await scene.TryArmAsync(command with { ConfirmationSha256 = new string('9', 64) });
        Assert.True(otherTuple.IsFailure);
        Assert.Equal(LocalExecutionSequenceErrors.TupleConflictCode, otherTuple.Errors[0].Code);
        Assert.Single(await scene.ReadAllAdmissionsAsync());
    }

    [Fact]
    public async Task An_old_immediate_sequence_is_never_adopted_into_the_queue_and_one_sequence_per_run_stays_the_rule()
    {
        var (scene, plan) = await NewSceneAsync();
        var queueCommand = await scene.QueueCommandAsync(plan.ProposalId);
        var immediate = await scene.ArmAsync(plan.ProposalId);

        var availability = await scene.ReadQueueAvailabilityAsync(plan.ProposalId);
        Assert.False(availability.Available);
        Assert.Equal(LocalExecutionSequenceErrors.AlreadyArmedCode, availability.RefusalCode);

        var queue = await scene.TryArmAsync(queueCommand);
        Assert.True(queue.IsFailure);
        Assert.Equal(LocalExecutionSequenceErrors.TupleConflictCode, queue.Errors[0].Code);
        Assert.Equal(LocalExecutionAdmissionMode.Immediate, (await scene.ReadSequenceAsync()).AdmissionMode);
        Assert.Equal(immediate.SequenceId, (await scene.ReadSequenceAsync()).Id);
        Assert.Empty(await scene.ReadAllAdmissionsAsync());
    }

    [Theory]
    [InlineData(OwnerFamily.Agent)]
    [InlineData(OwnerFamily.Verification)]
    public async Task A_coherent_busy_workspace_is_admitted_to_the_queue_but_never_to_immediate_arming(OwnerFamily owner)
    {
        var (scene, plan) = await NewSceneAsync();
        await scene.AddWorkspaceOwnerAsync(owner);
        var before = await scene.CountWorkAsync();

        var immediate = await scene.ReadAvailabilityAsync(plan.ProposalId);
        var queued = await scene.ReadQueueAvailabilityAsync(plan.ProposalId);

        Assert.False(immediate.Available);
        Assert.True(queued.Available, queued.RefusalCode);
        var receipt = await scene.TryArmAsync(scene.QueueCommandFrom(queued));
        Assert.True(receipt.IsSuccess, receipt.IsFailure ? receipt.Errors[0].Code : null);
        Assert.Equal(before, await scene.CountWorkAsync());
        Assert.Empty(await scene.ReadStepsAsync());
    }

    [Fact]
    public async Task A_workspace_held_for_a_local_commit_is_not_ready_and_is_refused_by_the_queue_like_any_unready_workspace()
    {
        var (scene, plan) = await NewSceneAsync();
        await scene.AddWorkspaceOwnerAsync(OwnerFamily.LocalCommit);

        var queued = await scene.ReadQueueAvailabilityAsync(plan.ProposalId);

        Assert.False(queued.Available);
        Assert.Equal(LocalExecutionSequenceErrors.WorkspaceNotReadyCode, queued.RefusalCode);
    }

    [Fact]
    public async Task Two_recorded_owners_of_one_workspace_are_conflicted_and_never_admitted_to_the_queue()
    {
        var (scene, plan) = await NewSceneAsync();
        var first = await scene.AddWorkspaceOwnerAsync(OwnerFamily.Agent);
        await scene.AddWorkspaceOwnerAsync(OwnerFamily.Agent, onto: first);

        var queued = await scene.ReadQueueAvailabilityAsync(plan.ProposalId);

        Assert.False(queued.Available);
        Assert.Equal(LocalExecutionSequenceErrors.WorkspaceOwnedCode, queued.RefusalCode);
        Assert.Null(await scene.FindSequenceAsync());
    }

    [Fact]
    public async Task A_global_limit_reached_reading_is_waited_for_by_the_queue_and_refused_by_immediate_arming()
    {
        var (scene, plan) = await NewSceneAsync();
        await ExecutionCapacitySeed.SetLimitAsync(fixture, 1);
        await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.Agent);

        var immediate = await scene.ReadAvailabilityAsync(plan.ProposalId);
        var queued = await scene.ReadQueueAvailabilityAsync(plan.ProposalId);

        Assert.False(immediate.Available);
        Assert.Equal(LocalExecutionSequenceErrors.CapacityUnavailableCode, immediate.RefusalCode);
        Assert.True(queued.Available, queued.RefusalCode);
    }

    [Fact]
    public async Task A_project_blocker_whose_cause_is_not_the_workspace_owner_refuses_the_queue_instead_of_being_ignored()
    {
        var (scene, plan) = await NewSceneAsync();
        // An unresolved attempt of the project in an OLDER workspace blocks the project but does not own the latest workspace.
        await scene.AddNewerIdleWorkspaceWithOlderOwnerAsync();

        var queued = await scene.ReadQueueAvailabilityAsync(plan.ProposalId);

        Assert.False(queued.Available);
        Assert.Equal(LocalExecutionSequenceErrors.ProjectBusyCode, queued.RefusalCode);
    }

    [Fact]
    public async Task At_most_sixty_four_cycles_are_live_and_an_ended_cycle_frees_a_place()
    {
        var scenes = new List<(LocalExecutionSequenceTestScene Scene, Guid Plan)>();
        for (var index = 0; index < 64; index++)
        {
            var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture, 1);
            scenes.Add((scene, (await scene.AddAcceptedRootPlanAsync()).ProposalId));
        }

        foreach (var (scene, plan) in scenes)
        {
            await scene.QueueAsync(plan);
        }

        var extra = await LocalExecutionSequenceTestScene.CreateAsync(fixture, 1);
        var extraPlan = (await extra.AddAcceptedRootPlanAsync()).ProposalId;
        var refused = await extra.ReadQueueAvailabilityAsync(extraPlan);
        Assert.False(refused.Available);
        Assert.Equal(LocalExecutionSequenceErrors.QueueFullCode, refused.RefusalCode);

        var command = await BuildForcedCommandAsync(extra, extraPlan);
        var queued = await extra.TryArmAsync(command);
        Assert.True(queued.IsFailure);
        Assert.Equal(LocalExecutionSequenceErrors.QueueFullCode, queued.Errors[0].Code);
        Assert.Null(await extra.FindSequenceAsync());

        var firstSequence = await scenes[0].Scene.ReadSequenceAsync();
        Assert.True((await scenes[0].Scene.StopAsync(firstSequence.Id)).IsSuccess);

        var freed = await extra.ReadQueueAvailabilityAsync(extraPlan);
        Assert.True(freed.Available, freed.RefusalCode);
        Assert.True((await extra.TryArmAsync(extra.QueueCommandFrom(freed))).IsSuccess);
    }

    [Fact]
    public async Task The_database_bound_refuses_a_sixty_fifth_live_cycle_even_when_the_application_reads_are_bypassed()
    {
        for (var index = 0; index < 64; index++)
        {
            var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture, 1);
            await scene.QueueAsync((await scene.AddAcceptedRootPlanAsync()).ProposalId);
        }

        var extra = await LocalExecutionSequenceTestScene.CreateAsync(fixture, 1);
        var plan = (await extra.AddAcceptedRootPlanAsync()).ProposalId;
        await using var context = fixture.CreateContext();
        var counter = await context.LocalExecutionOrderCounters.SingleAsync();
        var sequence = LocalExecutionSequence.Arm(
            Guid.NewGuid(), extra.RunId, extra.ProjectId, extra.WorkspaceId, extra.CheckpointId, LocalExecutionSequenceTestScene.StartFingerprint,
            plan, LocalExecutionPlanAuthority.AcceptedRoot, new string('b', 64), new string('c', 64), "v1;settings", 1, 2,
            LocalExecutionSequenceTestScene.Start, counter.Allocate(), LocalExecutionAdmissionMode.WaitForAdmission);
        context.LocalExecutionSequences.Add(sequence);

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        Assert.Contains("local_execution_sequence.queue_full", exception.GetBaseException().Message, StringComparison.Ordinal);
    }

    private static async Task<DevalCopilot.Application.Features.Runs.Commands.ArmLocalExecutionSequence.ArmLocalExecutionSequenceCommand>
        BuildForcedCommandAsync(LocalExecutionSequenceTestScene scene, Guid plan)
    {
        // The availability refuses at the bound; a client that kept an earlier candidate still submits its tuple.
        var immediate = await scene.ReadAvailabilityAsync(plan);
        return scene.CommandFrom(immediate) with { Mode = LocalExecutionAdmissionMode.WaitForAdmission };
    }
}
