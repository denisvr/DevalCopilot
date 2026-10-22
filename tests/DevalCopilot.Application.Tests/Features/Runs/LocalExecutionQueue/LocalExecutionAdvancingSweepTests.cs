using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;
using DevalCopilot.Application.Features.Runs.Queries.GetAdvanceableLocalExecutionSequences;
using DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionQueue;

/// <summary>LCQ-R3 (ADR-0062): the advancing-sequence feed carries a fixed high-water through a sweep, finishes that finite range and only
/// then starts a new sweep with a fresh high-water. Continuous arrivals therefore keep neither an old record from its revisit nor its
/// deadline from being observed, driven with the same cursor class the hosted coordinator uses over real SQLite.</summary>
public sealed class LocalExecutionAdvancingSweepTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    private async Task<(LocalExecutionSequenceTestScene Scene, Guid SequenceId)> ArmedAsync()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture, 1);
        var plan = await scene.AddAcceptedRootPlanAsync();
        return (scene, (await scene.ArmAsync(plan.ProposalId)).SequenceId);
    }

    private async Task<AdvanceableLocalExecutionSequencesPage> PageAsync(LocalExecutionAdvancingSweep sweep)
    {
        await using var context = fixture.CreateContext();
        var page = await new GetAdvanceableLocalExecutionSequencesQueryHandler(context).HandleAsync(sweep.NextQuery(), CancellationToken.None);
        sweep.Accept(page);
        return page;
    }

    [Fact]
    public async Task Ongoing_arrivals_never_keep_an_old_record_from_its_revisit_and_its_deadline_is_observed()
    {
        var old = new List<(LocalExecutionSequenceTestScene Scene, Guid SequenceId)>();
        for (var index = 0; index < 34; index++)
        {
            old.Add(await ArmedAsync());
        }

        var sweep = new LocalExecutionAdvancingSweep();
        var first = await PageAsync(sweep);
        Assert.Equal(32, first.Items.Count);
        Assert.False(first.SweepFinished);
        Assert.Contains(old[0].SequenceId, first.Items.Select(item => item.SequenceId));
        var firstHighWater = first.HighWaterCoordinationNumber;

        // Arrivals that would keep every later page full under a lower bound alone: thirty-two of them after the fixed high-water.
        var arrivals = new List<(LocalExecutionSequenceTestScene Scene, Guid SequenceId)>();
        for (var index = 0; index < 32; index++)
        {
            arrivals.Add(await ArmedAsync());
        }

        var second = await PageAsync(sweep);
        Assert.Equal(2, second.Items.Count);
        Assert.True(second.SweepFinished);
        Assert.Equal(firstHighWater, second.HighWaterCoordinationNumber);
        Assert.All(second.Items, item => Assert.True(item.CoordinationNumber <= firstHighWater));
        Assert.DoesNotContain(second.Items, item => arrivals.Any(arrival => arrival.SequenceId == item.SequenceId));

        // More arrivals during the next sweep; the finished sweep restarted, so the old record is visited again on its very next page.
        var third = await PageAsync(sweep);
        Assert.Equal(old[0].SequenceId, third.Items[0].SequenceId);
        Assert.True(third.HighWaterCoordinationNumber > firstHighWater);
        for (var index = 0; index < 32; index++)
        {
            await ArmedAsync();
        }

        // Its two-hour advancement deadline elapsed meanwhile: the revisit's observation ends it, which only an actual revisit can do.
        old[0].Scene.Clock.Advance(TimeSpan.FromHours(2) + TimeSpan.FromMinutes(1));
        Assert.True((await old[0].Scene.ObserveAsync(old[0].SequenceId)).IsSuccess);
        var ended = await old[0].Scene.ReadSequenceAsync();
        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, ended.State);
        Assert.Equal(LocalExecutionSequenceReason.AdvancementDeadlineElapsed, ended.ReasonCode);

        // Every old record is revisited by the end of the range of its sweep, whatever arrived meanwhile.
        var seen = new HashSet<Guid>(first.Items.Select(item => item.SequenceId));
        seen.UnionWith(second.Items.Select(item => item.SequenceId));
        Assert.Equal(34, seen.Count(id => old.Any(entry => entry.SequenceId == id)));
    }

    [Fact]
    public async Task An_empty_feed_starts_a_fresh_sweep_with_no_stale_high_water()
    {
        var sweep = new LocalExecutionAdvancingSweep();
        var empty = await PageAsync(sweep);
        Assert.Empty(empty.Items);
        Assert.True(empty.SweepFinished);

        var (_, sequenceId) = await ArmedAsync();
        var next = await PageAsync(sweep);

        Assert.Equal(sequenceId, Assert.Single(next.Items).SequenceId);
        Assert.True(next.SweepFinished);
    }

    [Fact]
    public async Task A_finite_range_larger_than_one_page_is_finished_exactly_once_per_sweep()
    {
        for (var index = 0; index < 40; index++)
        {
            await ArmedAsync();
        }

        var sweep = new LocalExecutionAdvancingSweep();
        var visited = 0;
        var pages = 0;
        AdvanceableLocalExecutionSequencesPage page;
        do
        {
            page = await PageAsync(sweep);
            visited += page.Items.Count;
            pages++;
        }
        while (!page.SweepFinished && pages < 5);

        Assert.Equal(40, visited);
        Assert.Equal(2, pages);
    }
}
