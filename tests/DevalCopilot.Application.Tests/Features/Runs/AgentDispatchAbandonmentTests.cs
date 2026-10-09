using DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>ADR-0031: a claimed Agent attempt keeps an abandonment from being recorded, so no dispatch can follow one. These controls
/// prove the last gate before a provider is invoked also refuses a dispatch against an Abandoned run, and loses to an abandonment that
/// commits between its decision and its save, for every claim path.</summary>
public sealed class AgentDispatchAbandonmentTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 13, 0, 0, TimeSpan.Zero);

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
    public async Task A_dispatch_against_an_abandoned_run_is_refused_and_never_reaches_a_provider(string path)
    {
        var (runId, attemptId) = await AgentAttemptModeSeed.SeedClaimedAsync(_fixture, path, AbandonmentRaceSupport.ManualAgentMode);
        await using (var setup = _fixture.CreateContext())
        {
            await setup.Database.ExecuteSqlRawAsync("UPDATE runs SET Lifecycle = 'Abandoned' WHERE Id = {0}", runId);
        }

        await using var dbContext = _fixture.CreateContext();
        var result = await new MarkAgentAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now))
            .HandleAsync(new MarkAgentAttemptDispatchedCommand(runId, attemptId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.False(await IsDispatchedAsync(attemptId));
    }

    [Theory]
    [MemberData(nameof(AgentPaths))]
    public async Task A_dispatch_that_decided_before_an_abandonment_loses_to_the_lifecycle_token_at_its_save(string path)
    {
        var (runId, attemptId) = await AgentAttemptModeSeed.SeedClaimedAsync(_fixture, path, AbandonmentRaceSupport.ManualAgentMode);
        await using var dbContext = _fixture.CreateContext(new BeforeFirstSaveInterceptor(async () =>
        {
            await using var other = _fixture.CreateContext();
            await other.Database.ExecuteSqlRawAsync("UPDATE runs SET Lifecycle = 'Abandoned' WHERE Id = {0}", runId);
        }));

        var result = await new MarkAgentAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now))
            .HandleAsync(new MarkAgentAttemptDispatchedCommand(runId, attemptId), CancellationToken.None);
        Assert.True(result.IsSuccess);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => dbContext.SaveChangesAsync(CancellationToken.None));
        Assert.False(await IsDispatchedAsync(attemptId));
    }
}
