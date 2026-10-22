using DevalCopilot.Application.Features.Runs.Queries.GetAdvanceableLocalExecutionSequences;
using DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionQueue;

/// <summary>LCQ-V1 (ADR-0062): startup never resumes queued authority. A waiting cycle and a reserved one are both closed by the startup
/// closure itself (not merely by retiring their admission), and afterwards no observation, reservation or advancing feed can pick them up,
/// even with every workspace and capacity condition favorable.</summary>
public sealed class LocalExecutionQueueStartupNoResumeTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    private async Task<(LocalExecutionSequenceTestScene Scene, Guid SequenceId)> QueuedAsync()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture, 1);
        var plan = await scene.AddAcceptedRootPlanAsync();
        return (scene, (await scene.QueueAsync(plan.ProposalId)).SequenceId);
    }

    private async Task AssertNothingAdvancesAsync(LocalExecutionSequenceTestScene scene, Guid sequenceId)
    {
        var sequence = await scene.ReadSequenceAsync();
        Assert.Equal(LocalExecutionSequenceState.Interrupted, sequence.State);
        Assert.Equal(LocalExecutionSequenceReason.HostRestarted, sequence.ReasonCode);
        Assert.False(LocalExecutionSequencePolicy.IsAuthorityLive(sequence.State));

        await using (var context = fixture.CreateContext())
        {
            var feed = await new GetAdvanceableLocalExecutionSequencesQueryHandler(context).HandleAsync(
                new GetAdvanceableLocalExecutionSequencesQuery(), CancellationToken.None);
            Assert.DoesNotContain(feed.Items, item => item.SequenceId == sequenceId);
        }

        Assert.Equal(0, (await scene.ObserveQueueAsync()).Visited);
        Assert.Null(await scene.ReserveAsync());
        var after = await scene.ReadSequenceAsync();
        Assert.Equal(LocalExecutionSequenceState.Interrupted, after.State);
        Assert.All(await scene.ReadStepsAsync(), step => Assert.Equal(LocalExecutionSequenceStepState.Revoked, step.State));
        Assert.DoesNotContain(await scene.ReadAdmissionsAsync(), admission => admission.IsHeld);
    }

    [Fact]
    public async Task A_waiting_cycle_is_closed_by_startup_and_is_never_reserved_observed_or_advanced_afterwards()
    {
        var (scene, sequenceId) = await QueuedAsync();

        await scene.InterruptAsync();

        await AssertNothingAdvancesAsync(scene, sequenceId);
        Assert.Equal(LocalExecutionAdmissionState.Retired, Assert.Single(await scene.ReadAdmissionsAsync()).State);
    }

    [Fact]
    public async Task A_reserved_cycle_is_closed_by_startup_its_unused_reservation_retired_and_it_is_never_resumed()
    {
        var (scene, sequenceId) = await QueuedAsync();
        Assert.NotNull(await scene.ReserveAsync());

        await scene.InterruptAsync();

        await AssertNothingAdvancesAsync(scene, sequenceId);
    }
}
