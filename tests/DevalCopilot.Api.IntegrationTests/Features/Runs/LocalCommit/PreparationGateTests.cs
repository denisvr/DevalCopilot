using DevalCopilot.Application.Features.Runs.Ports;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>The test-owned parking gate: a completion is freed only by releasing that exact captured sequence (or by the
/// failure-safe release of everything), parking is bounded, and an early release is not lost.</summary>
public sealed class PreparationGateTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    private static LocalCommitPreparationCompletion Completion(int sequence) => new(
        sequence,
        new LocalCommitPreparationRequest(
            Guid.NewGuid(), "main", "workspace", "branch", new string('a', 40), new string('f', 64), [], "message",
            new LocalCommitOwnership(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1UL, "00"), DateTimeOffset.UnixEpoch),
        new LocalCommitPreparationResult(LocalCommitPreparationOutcome.CheckpointNotCurrent, null));

    [Fact]
    public async Task A_parked_completion_is_freed_only_by_its_own_release()
    {
        var gate = new PreparationGate(Bound);
        var first = Completion(1);
        var second = Completion(2);
        var parkedFirst = gate.ParkAsync(first);
        var parkedSecond = gate.ParkAsync(second);

        gate.Release(second);
        await parkedSecond.WaitAsync(Bound);

        Assert.False(parkedFirst.IsCompleted, "releasing another completion freed this one");
        gate.Release(first);
        await parkedFirst.WaitAsync(Bound);
    }

    [Fact]
    public async Task A_release_before_the_completion_parks_is_not_lost()
    {
        var gate = new PreparationGate(Bound);
        var completion = Completion(1);

        gate.Release(completion);

        await gate.ParkAsync(completion).WaitAsync(Bound);
    }

    [Fact]
    public async Task The_failure_safe_release_frees_every_parked_completion_and_any_that_park_afterwards()
    {
        var gate = new PreparationGate(Bound);
        var parked = gate.ParkAsync(Completion(1));

        gate.ReleaseAll();

        await parked.WaitAsync(Bound);
        await gate.ParkAsync(Completion(2)).WaitAsync(Bound);
    }

    [Fact]
    public async Task Parking_is_bounded_when_nothing_releases_it()
    {
        var gate = new PreparationGate(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAsync<TimeoutException>(() => gate.ParkAsync(Completion(1)));
    }
}
