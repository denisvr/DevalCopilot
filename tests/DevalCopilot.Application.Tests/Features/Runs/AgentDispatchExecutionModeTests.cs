using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>The authoritative last gate before any provider is invoked: the dispatch marker commits only while the
/// run's stored mode admits Agent work, whatever a long-lived tracked context believes.</summary>
public sealed class AgentDispatchExecutionModeTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 13, 0, 0, TimeSpan.Zero);

    private const int Legacy = (int)RunExecutionMode.Legacy;
    private const int Simulated = (int)RunExecutionMode.Simulated;
    private const int ManualAgent = (int)RunExecutionMode.ManualAgent;
    private const int Undefined = RunExecutionModeTestSupport.UndefinedMode;

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    public static TheoryData<string> AgentPaths() =>
        ["planning", "critical-review", "challenge-resolution", "implementation", "code-review", "review-correction"];

    private async Task<bool> IsDispatchedAsync(Guid attemptId)
    {
        await using var verify = _fixture.CreateContext();
        return (await verify.Attempts.AsNoTracking().SingleAsync(attempt => attempt.Id == attemptId)).AgentDispatchedAtUtc.HasValue;
    }

    [Theory]
    [MemberData(nameof(AgentPaths))]
    public async Task Manual_and_legacy_runs_dispatch_normally_for_every_path(string path)
    {
        foreach (var storedMode in new[] { ManualAgent, Legacy })
        {
            var (runId, attemptId) = await AgentAttemptModeSeed.SeedClaimedAsync(_fixture, path, storedMode);
            await using var dbContext = _fixture.CreateContext();

            var result = await new MarkAgentAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now))
                .HandleAsync(new MarkAgentAttemptDispatchedCommand(runId, attemptId), CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);

            Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : string.Empty);
            Assert.True(await IsDispatchedAsync(attemptId));
        }
    }

    [Theory]
    [MemberData(nameof(AgentPaths))]
    public async Task A_simulated_or_undefined_mode_refuses_dispatch_for_every_path(string path)
    {
        foreach (var storedMode in new[] { Simulated, Undefined })
        {
            var (runId, attemptId) = await AgentAttemptModeSeed.SeedClaimedAsync(_fixture, path, storedMode);
            await using var dbContext = _fixture.CreateContext();

            var result = await new MarkAgentAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now))
                .HandleAsync(new MarkAgentAttemptDispatchedCommand(runId, attemptId), CancellationToken.None);

            Assert.Equal(CurrentRunExecutionMode.NotAdmittedCode, Assert.Single(result.Errors).Code);
            Assert.False(await IsDispatchedAsync(attemptId));
        }
    }

    [Fact]
    public async Task A_tracked_stale_run_never_confers_dispatch_authority()
    {
        var (runId, attemptId) = await AgentAttemptModeSeed.SeedClaimedAsync(_fixture, "planning", ManualAgent);
        await using var dbContext = _fixture.CreateContext();
        var tracked = await dbContext.Runs.SingleAsync(run => run.Id == runId);
        Assert.Equal(RunExecutionMode.ManualAgent, tracked.ExecutionMode);
        await RunExecutionModeTestSupport.SetStoredModeAsync(dbContext, runId, Simulated);

        var result = await new MarkAgentAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now))
            .HandleAsync(new MarkAgentAttemptDispatchedCommand(runId, attemptId), CancellationToken.None);

        Assert.Equal(CurrentRunExecutionMode.NotAdmittedCode, Assert.Single(result.Errors).Code);
        Assert.False(await IsDispatchedAsync(attemptId));
    }

    [Fact]
    public async Task A_mode_change_between_the_gate_and_the_commit_rolls_the_dispatch_marker_back()
    {
        var (runId, attemptId) = await AgentAttemptModeSeed.SeedClaimedAsync(_fixture, "planning", ManualAgent);
        await using var inner = _fixture.CreateContext();
        var faulting = new FaultInjectingDbContext(inner)
        {
            BeforeSaveChanges = _ => RunExecutionModeTestSupport.SetStoredModeAsync(_fixture, runId, Simulated),
        };
        var result = await new MarkAgentAttemptDispatchedCommandHandler(faulting, new FixedTimeProvider(Now))
            .HandleAsync(new MarkAgentAttemptDispatchedCommand(runId, attemptId), CancellationToken.None);
        Assert.True(result.IsSuccess);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => faulting.SaveChangesAsync(CancellationToken.None));

        Assert.False(await IsDispatchedAsync(attemptId));
        Assert.Equal(Simulated, await RunExecutionModeTestSupport.ReadStoredModeAsync(_fixture, runId));
    }
}
