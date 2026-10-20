using DevalCopilot.Application.Features.Projects.Queries.GetEligibleVerificationExecutions;
using DevalCopilot.Application.Features.Runs.Queries.GetEligibleAgentAttempts;
using DevalCopilot.Application.Features.Runs.Queries.GetEligibleChallengeResolutionAttempts;
using DevalCopilot.Application.Features.Runs.Queries.GetEligibleClaudeCriticalReviewAttempts;
using DevalCopilot.Application.Features.Runs.Queries.GetEligibleCodeReviewAttempts;
using DevalCopilot.Application.Features.Runs.Queries.GetEligibleImplementationAttempts;
using DevalCopilot.Application.Features.Runs.Queries.GetEligibleLocalCommitOperations;
using DevalCopilot.Application.Features.Runs.Queries.GetEligibleReviewCorrectionAttempts;
using DevalCopilot.Application.Tests.Features.Projects;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// ADR-0058's advisory use by the eligibility feeds of potentially mutating work: a pending owner the global ceiling would not let
/// dispatch is not proposed to its supervisor, while every ReadOnly feed is untouched at any occupancy. A feed only filters: it
/// terminalizes, cancels and releases nothing, and the single-use gate still decides.
/// </summary>
public sealed class ExecutionCapacityFeedTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<IReadOnlyList<Guid>> AgentFeedAsync(string feed)
    {
        await using var db = _fixture.CreateContext();
        var ct = CancellationToken.None;
        return feed switch
        {
            "planning" => (await new GetEligibleAgentAttemptsQueryHandler(db).HandleAsync(new GetEligibleAgentAttemptsQuery(), ct)).Select(a => a.AttemptId).ToArray(),
            "critical-review" => (await new GetEligibleClaudeCriticalReviewAttemptsQueryHandler(db).HandleAsync(new GetEligibleClaudeCriticalReviewAttemptsQuery(), ct)).Select(a => a.AttemptId).ToArray(),
            "challenge-resolution" => (await new GetEligibleChallengeResolutionAttemptsQueryHandler(db).HandleAsync(new GetEligibleChallengeResolutionAttemptsQuery(), ct)).Select(a => a.AttemptId).ToArray(),
            "implementation" => (await new GetEligibleImplementationAttemptsQueryHandler(db).HandleAsync(new GetEligibleImplementationAttemptsQuery(), ct)).Select(a => a.AttemptId).ToArray(),
            "code-review" => (await new GetEligibleCodeReviewAttemptsQueryHandler(db).HandleAsync(new GetEligibleCodeReviewAttemptsQuery(), ct)).Select(a => a.AttemptId).ToArray(),
            "review-correction" => (await new GetEligibleReviewCorrectionAttemptsQueryHandler(db).HandleAsync(new GetEligibleReviewCorrectionAttemptsQuery(), ct)).Select(a => a.AttemptId).ToArray(),
            _ => throw new ArgumentOutOfRangeException(nameof(feed)),
        };
    }

    public static TheoryData<string> MutatingFeeds() => ["implementation", "review-correction"];

    public static TheoryData<string> ReadOnlyFeeds() => ["planning", "critical-review", "challenge-resolution", "code-review"];

    [Theory]
    [MemberData(nameof(MutatingFeeds))]
    public async Task A_pending_mutating_attempt_is_proposed_beside_one_other_owner_and_not_beside_two(string feed)
    {
        var (_, attemptId) = await AgentAttemptModeSeed.SeedClaimedAsync(_fixture, feed, AbandonmentRaceSupport.ManualAgentMode);
        var other = await ExecutionCapacitySeed.AddAsync(_fixture, OwnerFamily.Verification);

        Assert.Contains(attemptId, await AgentFeedAsync(feed));

        await ExecutionCapacitySeed.AddAsync(_fixture, OwnerFamily.Agent);

        Assert.DoesNotContain(attemptId, await AgentFeedAsync(feed));

        await ExecutionCapacitySeed.FinishAsync(_fixture, other);

        Assert.Contains(attemptId, await AgentFeedAsync(feed));
        await using var verify = _fixture.CreateContext();
        var attempt = await verify.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == attemptId);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Null(attempt.AgentDispatchedAtUtc);
    }

    [Theory]
    [MemberData(nameof(ReadOnlyFeeds))]
    public async Task Every_ReadOnly_feed_proposes_its_attempt_while_the_database_is_over_capacity(string feed)
    {
        var (_, attemptId) = await AgentAttemptModeSeed.SeedClaimedAsync(_fixture, feed, AbandonmentRaceSupport.ManualAgentMode);
        await ExecutionCapacitySeed.AddAsync(_fixture, OwnerFamily.Agent, OwnerFamily.Verification, OwnerFamily.LocalCommit);

        Assert.Contains(attemptId, await AgentFeedAsync(feed));
    }

    [Fact]
    public async Task A_pending_verification_execution_is_proposed_beside_one_other_owner_and_not_beside_two()
    {
        Guid executionId;
        await using (var db = _fixture.CreateContext())
        {
            var scene = await WorkspaceExecutionSeed.SeedAsync(db);
            executionId = await WorkspaceExecutionSeed.AddVerificationExecutionAsync(db, scene, dispatched: false);
        }

        await ExecutionCapacitySeed.AddAsync(_fixture, OwnerFamily.LocalCommit);
        Assert.Contains(executionId, await VerificationFeedAsync());

        await ExecutionCapacitySeed.AddAsync(_fixture, OwnerFamily.Agent);

        Assert.DoesNotContain(executionId, await VerificationFeedAsync());
        await using var verify = _fixture.CreateContext();
        var execution = await verify.VerificationExecutions.AsNoTracking().SingleAsync(candidate => candidate.Id == executionId);
        Assert.Equal(VerificationExecutionStatus.Running, execution.Status);
        Assert.Null(execution.DispatchedAtUtc);
    }

    private async Task<IReadOnlyList<Guid>> VerificationFeedAsync()
    {
        await using var db = _fixture.CreateContext();
        return (await new GetEligibleVerificationExecutionsQueryHandler(db)
            .HandleAsync(new GetEligibleVerificationExecutionsQuery(), CancellationToken.None))
            .Select(candidate => candidate.VerificationExecutionId).ToArray();
    }

    [Fact]
    public async Task A_prepared_local_commit_is_proposed_beside_one_other_owner_and_not_beside_two_and_is_never_terminalized()
    {
        var commit = await ExecutionCapacitySeed.AddAsync(_fixture, OwnerFamily.LocalCommit);
        await ExecutionCapacitySeed.AddAsync(_fixture, OwnerFamily.Verification);
        Assert.Contains(commit.Id, await CommitFeedAsync());

        await ExecutionCapacitySeed.AddAsync(_fixture, OwnerFamily.Agent);

        Assert.DoesNotContain(commit.Id, await CommitFeedAsync());
        await using var verify = _fixture.CreateContext();
        Assert.Equal(LocalCommitStatus.Prepared, (await verify.LocalCommitOperations.AsNoTracking().SingleAsync(o => o.Id == commit.Id)).Status);
        Assert.Equal(
            WorkspaceStatus.Committing, (await verify.GitWorkspaces.AsNoTracking().SingleAsync(w => w.Id == commit.WorkspaceId)).Status);
    }

    private async Task<IReadOnlyList<Guid>> CommitFeedAsync()
    {
        await using var db = _fixture.CreateContext();
        var result = await new GetEligibleLocalCommitOperationsQueryHandler(db)
            .HandleAsync(new GetEligibleLocalCommitOperationsQuery(), CancellationToken.None);
        return result.Value;
    }
}
