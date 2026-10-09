using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Runs.Commands.AbandonManualRun;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>Shared, deterministic helpers for proving both orders of the race between an explicit abandonment (ADR-0031) and the Agent
/// claims. Every race is reproduced by construction at a real seam, never by timing: the abandonment commits from an independent
/// context exactly where a claim has already decided, or a claim commits exactly where the abandonment has not yet taken its write lock.</summary>
internal static class AbandonmentRaceSupport
{
    public const string Reason = "The objective was replaced by a different approach.";

    public static readonly DateTimeOffset AbandonedAt = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The recognized stored mode of a manual Agent run.</summary>
    public const int ManualAgentMode = (int)RunExecutionMode.ManualAgent;

    /// <summary>Abandons through a fresh, independent context: a change committed by another request.</summary>
    public static async Task<Result<AbandonManualRunCommandResult>> AbandonAsync(
        SqliteDatabaseFixture fixture, Guid runId, string reason = Reason, DateTimeOffset? nowUtc = null)
    {
        await using var context = fixture.CreateContext();
        return await new AbandonManualRunCommandHandler(context, new FixedTimeProvider(nowUtc ?? AbandonedAt))
            .HandleAsync(new AbandonManualRunCommand(runId, reason), CancellationToken.None);
    }

    /// <summary>Abandons while a competing claim commits immediately before the abandonment takes its write-locked transaction: the
    /// abandonment's earlier untracked state is stale and only its in-transaction reads can see the claim.</summary>
    public static async Task<Result<AbandonManualRunCommandResult>> AbandonWhileClaimCommitsAsync(
        SqliteDatabaseFixture fixture, Guid runId, Func<CancellationToken, Task> claim)
    {
        await using var inner = fixture.CreateContext();
        var racing = new FaultInjectingDbContext(inner) { BeforeBeginTransaction = claim };
        return await new AbandonManualRunCommandHandler(racing, new FixedTimeProvider(AbandonedAt))
            .HandleAsync(new AbandonManualRunCommand(runId, Reason), CancellationToken.None);
    }

    /// <summary>The abandonment lost: the run is exactly as it was, no event, no reason, no time.</summary>
    public static async Task AssertNotAbandonedAsync(
        SqliteDatabaseFixture fixture, Guid runId, RunLifecycle expectedLifecycle)
    {
        await using var verify = fixture.CreateContext();
        var run = await verify.Runs.AsNoTracking().SingleAsync(candidate => candidate.Id == runId);
        Assert.Equal(expectedLifecycle, run.Lifecycle);
        Assert.Null(run.AbandonmentReason);
        Assert.Null(run.AbandonedAtUtc);
        Assert.DoesNotContain(verify.Events.AsNoTracking().Where(candidate => candidate.RunId == runId), candidate =>
            candidate.EventType == RunEventType.RunAbandoned);
    }

    /// <summary>The abandonment won: coherent Abandoned facts, exactly one Human event, and no attempt of the race's losing claim.</summary>
    public static async Task AssertAbandonedWithoutAttemptsAsync(
        SqliteDatabaseFixture fixture, Guid runId, int expectedAttempts = 0)
    {
        await using var verify = fixture.CreateContext();
        var run = await verify.Runs.AsNoTracking().SingleAsync(candidate => candidate.Id == runId);
        Assert.Equal(RunLifecycle.Abandoned, run.Lifecycle);
        Assert.Equal(Reason, run.AbandonmentReason);
        Assert.Equal(AbandonedAt, run.AbandonedAtUtc);
        var recorded = Assert.Single(
            verify.Events.AsNoTracking().Where(candidate => candidate.RunId == runId && candidate.EventType == RunEventType.RunAbandoned));
        Assert.Equal(ParticipantIdentity.ForHuman(), recorded.Actor);
        Assert.Equal(expectedAttempts, await verify.Attempts.CountAsync(attempt => attempt.RunId == runId));
        Assert.Empty(verify.Attempts.Where(attempt => attempt.RunId == runId && attempt.Status == AttemptStatus.Running));
        Assert.Equal(expectedAttempts, await verify.Artifacts.CountAsync(artifact => artifact.RunId == runId));
    }
}
