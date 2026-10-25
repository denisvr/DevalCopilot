using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionOwnershipRelease;
using DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Runs;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences.ScriptedResultKind;
using Rig = DevalCopilot.Application.Tests.Features.Runs.LocalExecutionOwnershipReleases.LocalExecutionOwnershipReleaseRig;
using Step = DevalCopilot.Domain.Features.Runs.LocalExecutionStepKind;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionOwnershipReleases;

/// <summary>
/// ADR-0065 across a host restart: a committed coherent observation is read by the restarted host for a new explicit Human release
/// without adopting any process or result, an interrupted cycle is releasable like any other ended one, and a restart can neither
/// invent the physical fact of a still-running operation nor resume, redispatch or release anything by itself.
/// </summary>
public sealed class LocalExecutionOwnershipReleaseRestartTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_restarted_host_releases_an_observation_the_original_host_committed(bool queued)
    {
        var ended = await Rig.EndAsync(fixture, Step.Implementation, queued);
        var scene = ended.Scene;
        var observation = Assert.Single(await scene.ReadObservationsAsync());
        var witnessBefore = Assert.Single(await scene.ReadWitnessesAsync());

        scene.RestartHost();
        await using (var reopened = fixture.CreateContext())
        {
            // A fresh connection reads the same committed facts: nothing was held in memory.
            Assert.Equal(observation.Id, Assert.Single(reopened.LocalExecutionTerminationObservations).Id);
        }

        var read = await scene.ReadReleaseAsync(ended.Target.Id);
        Assert.Equal(LocalExecutionOwnershipReleaseStatus.Available, read.Status);
        Assert.Equal(witnessBefore.Id, read.Candidate!.Facts.WitnessId);
        var released = await scene.ReleaseAsync(scene.ConfirmedCommand(read));

        Assert.True(released.IsSuccess, released.IsFailure ? released.Errors[0].Code : null);
        // No process or result was adopted: the witness still names the ORIGINAL launch, and nothing about the result changed.
        Assert.Equal(witnessBefore.HostLaunchId, Assert.Single(await scene.ReadWitnessesAsync()).HostLaunchId);
        Assert.NotEqual(scene.HostLaunch.Id, witnessBefore.HostLaunchId);
        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, (await scene.ReadSequenceAsync()).State);
    }

    [Fact]
    public async Task An_interrupted_cycle_with_a_recorded_observation_is_releasable_like_any_ended_one()
    {
        // The result and its physical fact were recorded, but the host restarted before the coordinator classified the step.
        var ended = await Rig.DriveAsync(
            fixture, new LocalExecutionStepScript().On(Step.Implementation, ProviderTimedOutTerminated), maximumTicks: 1);
        var scene = ended.Scene;
        Assert.Equal(LocalExecutionSequenceState.AwaitingStepResult, ended.Sequence.State);
        scene.RestartHost();
        await scene.InterruptAsync();
        Assert.Equal(LocalExecutionSequenceState.Interrupted, (await scene.ReadSequenceAsync()).State);

        var read = await scene.ReadReleaseAsync(ended.Target.Id);
        Assert.Equal(LocalExecutionOwnershipReleaseStatus.Available, read.Status);
        Assert.Equal(LocalExecutionSequenceState.Interrupted, read.Candidate!.Facts.SequenceState);

        var released = await scene.ReleaseAsync(scene.ConfirmedCommand(read));
        Assert.True(released.IsSuccess, released.IsFailure ? released.Errors[0].Code : null);
        Assert.Equal(
            Application.Features.Projects.Policies.WorkspaceExecutions.WorkspaceExecutionStatus.Idle,
            (await Rig.ReadOwnershipAsync(fixture, scene)).Workspace);
    }

    [Fact]
    public async Task A_restart_cannot_invent_the_physical_fact_of_a_still_running_operation_and_keeps_it_held()
    {
        var ended = await Rig.DriveAsync(
            fixture, new LocalExecutionStepScript().On(Step.Implementation, LeaveRunning), maximumTicks: 1);
        var scene = ended.Scene;
        scene.RestartHost();
        await scene.InterruptAsync();

        var attempt = await scene.ReadAttemptAsync(ended.Target.OperationId!.Value);
        Assert.NotNull(attempt.AgentDispatchedAtUtc);
        Assert.Empty(await scene.ReadObservationsAsync());
        var read = await scene.ReadReleaseAsync(ended.Target.Id);

        Assert.Contains(read.Status, new[] { LocalExecutionOwnershipReleaseStatus.NotEligible, LocalExecutionOwnershipReleaseStatus.Unavailable });
        Assert.Null(read.Candidate);
        Assert.NotEqual(
            Application.Features.Projects.Policies.WorkspaceExecutions.WorkspaceExecutionStatus.Idle,
            (await Rig.ReadOwnershipAsync(fixture, scene)).Workspace);
        Assert.Empty(await scene.ReadReleasesAsync());
        Assert.Equal(0, await scene.CountEventsAsync(RunEventType.LocalExecutionOwnershipReleased));
    }
}
