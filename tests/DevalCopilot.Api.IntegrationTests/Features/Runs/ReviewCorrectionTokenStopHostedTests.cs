using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.CreateReviewCorrectionAttempt;
using DevalCopilot.Application.Features.Runs.Commands.SetTokenStopThreshold;
using DevalCopilot.Application.Features.Runs.Queries.GetRunCockpit;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>
/// The token-activity stop through the real mediator, EF transactions, supervisor, and a process
/// restart. Only the provider adapter is a deterministic test boundary; the stop is derived purely
/// from persisted state, so a refused claim after a restart never reaches a provider.
/// </summary>
public sealed partial class ReviewCorrectionSupervisorHostedTests
{
    [Fact]
    public async Task A_correction_claimed_before_the_stop_dispatches_and_finishes_and_the_next_claim_is_then_refused()
    {
        var evidence = new SequencedEvidence(call => call == 1 ? Matching() : Changed());
        var adapter = new GatedAdapter(_artifactStore);
        await using var provider = BuildProvider(evidence, adapter, new TestNotifier());
        var seed = await SeedAsync(provider);
        adapter.FindingId = seed.FindingId;
        Guid reviewId;
        await using (var scope = provider.CreateAsyncScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();
            var set = await mediator.SendAsync(new SetTokenStopThresholdCommand(seed.RunId, "ClaudeCode", 1), CancellationToken.None);
            Assert.True(set.IsSuccess, string.Join(",", set.Errors.Select(e => e.Code)));
            var db = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            reviewId = (await db.Attempts.AsNoTracking().SingleAsync(a => a.RunId == seed.RunId && a.AgentRole == AgentRole.CodeReviewer)).Id;
        }

        // The already committed claim is unaffected by a stop set afterwards: it dispatches and finishes.
        var supervisor = CreateSupervisor(provider, adapter, evidence);
        await supervisor.StartAsync(CancellationToken.None);
        try
        {
            await adapter.Invoked.Task.WaitAsync(TimeSpan.FromSeconds(5));
            adapter.Release();
            await WaitForStatusAsync(provider, seed.CorrectionId, AttemptStatus.Completed);
        }
        finally
        {
            await supervisor.StopAsync(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);
        }

        Assert.Equal(1, adapter.InvocationCount);
        await using var claimScope = provider.CreateAsyncScope();
        var refused = await claimScope.ServiceProvider.GetRequiredService<IApplicationMediator>()
            .SendAsync(new CreateReviewCorrectionAttemptCommand(seed.RunId, reviewId), CancellationToken.None);
        Assert.True(refused.IsFailure);
        Assert.Contains(
            Assert.Single(refused.Errors).Code,
            new[] { AgentTokenStopGate.ReachedCode, AgentTokenStopGate.EvidenceIndeterminateCode });
        Assert.Equal(1, adapter.InvocationCount);
    }

    [Fact]
    public async Task A_reached_stop_refuses_the_claim_after_a_restart_from_persisted_state_without_a_provider_call()
    {
        var firstAdapter = new GatedAdapter(_artifactStore);
        Guid runId;
        Guid reviewId;
        await using (var firstProvider = BuildProvider(new SequencedEvidence(_ => Matching()), firstAdapter, new TestNotifier()))
        {
            var seed = await SeedAsync(firstProvider);
            runId = seed.RunId;
            await using var scope = firstProvider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            var now = DateTimeOffset.UtcNow;

            // The seeded correction concludes with recorded usage, so it is the only counted Claude attempt.
            var correction = await db.Attempts.SingleAsync(a => a.Id == seed.CorrectionId);
            correction.MarkAgentDispatched(now);
            correction.CompleteAgent(
                AgentOutcome.ProviderInvocationFailed, Fingerprint, now,
                tokenUsage: AgentTokenUsageEvidence.Create(500, 50, 5, 60, AgentTokenUsageEvidencePolicy.ClaudeCliSchemaVersion));
            await db.SaveChangesAsync();
            reviewId = (await db.Attempts.AsNoTracking().SingleAsync(a => a.RunId == runId && a.AgentRole == AgentRole.CodeReviewer)).Id;

            var set = await scope.ServiceProvider.GetRequiredService<IApplicationMediator>()
                .SendAsync(new SetTokenStopThresholdCommand(runId, "ClaudeCode", 615), CancellationToken.None);
            Assert.True(set.IsSuccess, string.Join(",", set.Errors.Select(e => e.Code)));
        }

        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_databasePath}"));

        // A process restart: new container and adapter, nothing in memory.
        var adapter = new GatedAdapter(_artifactStore);
        await using var restartedProvider = BuildProvider(new SequencedEvidence(_ => Matching()), adapter, new TestNotifier());
        await using var restartedScope = restartedProvider.CreateAsyncScope();
        var mediator = restartedScope.ServiceProvider.GetRequiredService<IApplicationMediator>();

        var cockpit = await mediator.SendAsync(new GetRunCockpitQuery(runId), CancellationToken.None);
        Assert.True(cockpit.IsSuccess);
        var claude = Assert.Single(cockpit.Value.TokenStops!, entry => entry.Provider == AgentProvider.ClaudeCode);
        Assert.Equal(AgentTokenStopState.ThresholdReached, claude.State);
        Assert.Equal(615, claude.KnownTokenCount);
        Assert.True(claude.BlocksClaim);

        var refused = await mediator.SendAsync(new CreateReviewCorrectionAttemptCommand(runId, reviewId), CancellationToken.None);

        Assert.True(refused.IsFailure);
        Assert.Equal(AgentTokenStopGate.ReachedCode, Assert.Single(refused.Errors).Code);
        Assert.Equal(0, adapter.InvocationCount);
        await using var verify = restartedProvider.GetRequiredService<IDbContextFactory<DevalCopilotDbContext>>().CreateDbContext();
        Assert.Equal(5, await verify.Attempts.CountAsync(a => a.RunId == runId));
        Assert.Empty(verify.ReviewCorrectionEscalations);
    }
}
