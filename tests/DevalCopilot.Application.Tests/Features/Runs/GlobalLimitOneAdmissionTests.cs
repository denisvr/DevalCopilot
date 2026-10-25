using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Projects.Commands.ClaimVerificationExecution;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Projects.Queries.GetEligibleVerificationExecutions;
using DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;
using DevalCopilot.Application.Features.Runs.Commands.SetExecutionCapacityLimit;
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
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// ADR-0059's persisted limit at the shared admission boundary, against real file-backed SQLite through independent connections: with the
/// limit set to one a verification claim, an Agent dispatch and every advisory feed of mutating work are refused beside a single other
/// recorded owner and admitted alone (exact self-exclusion), a ReadOnly attempt keeps its capacity independence, and both orderings of an
/// operator limit change racing a claim are decided by the write boundary. A claim never decides from a limit it observed before its
/// preparation. The same refusals for the remaining entrances live beside their own handlers' regressions.
/// </summary>
public sealed class GlobalLimitOneAdmissionTests : IAsyncLifetime
{
    private const string LimitReached = "execution_capacity.limit_reached";
    private static readonly DateTimeOffset Now = WorkspaceExecutionSeed.Now;
    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    private sealed class FixedReader(Func<Task>? onCapture = null) : IGitWorkspaceEvidenceReader
    {
        public int Captures { get; private set; }

        public async Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken)
        {
            Captures++;
            if (onCapture is not null)
            {
                await onCapture();
            }

            return new GitWorkspaceEvidenceResult(
                GitWorkspaceEvidenceOutcome.Success, new string('a', 40), WorkspaceExecutionSeed.Fingerprint, [], null);
        }
    }

    private async Task<WorkspaceExecutionScene[]> SeedAsync(int count)
    {
        await using var db = fixture.CreateContext();
        var scenes = new List<WorkspaceExecutionScene>(count);
        for (var index = 0; index < count; index++)
        {
            scenes.Add(await WorkspaceExecutionSeed.SeedAsync(db));
        }

        return [.. scenes];
    }

    private async Task<Result<ClaimVerificationExecutionCommandResult>> ClaimAsync(WorkspaceExecutionScene scene, IGitWorkspaceEvidenceReader reader)
    {
        await using var db = fixture.CreateContext();
        return await new ClaimVerificationExecutionCommandHandler(db, reader, new FixedTimeProvider(Now)).HandleAsync(
            new ClaimVerificationExecutionCommand(scene.Project.Id, scene.Recipe.Id, scene.Checkpoint.Id), CancellationToken.None);
    }

    private async Task<Result<SetExecutionCapacityLimitCommandResult>> SetAsync(int limit)
    {
        await using var db = fixture.CreateContext();
        return await new SetExecutionCapacityLimitCommandHandler(db)
            .HandleAsync(new SetExecutionCapacityLimitCommand(limit), CancellationToken.None);
    }

    private async Task AssertProjectUntouchedAsync(WorkspaceExecutionScene scene)
    {
        await using var verify = fixture.CreateContext();
        Assert.Equal(0, await verify.VerificationExecutions.CountAsync(execution => execution.ProjectId == scene.Project.Id));
        Assert.Equal(
            1,
            await verify.Projects.Where(project => project.Id == scene.Project.Id).Select(project => project.NextVerificationExecutionNumber).SingleAsync());
    }

    [Theory]
    [InlineData(OwnerFamily.Agent)]
    [InlineData(OwnerFamily.Verification)]
    [InlineData(OwnerFamily.LocalCommit)]
    public async Task A_verification_claim_is_refused_beside_one_other_owner_at_limit_one_before_any_capture(OwnerFamily other)
    {
        var scenes = await SeedAsync(1);
        await ExecutionCapacitySeed.SetLimitAsync(fixture, 1);
        await ExecutionCapacitySeed.AddAsync(fixture, other);
        var reader = new FixedReader();

        var result = await ClaimAsync(scenes[0], reader);

        Assert.True(result.IsFailure, "The claim was admitted beside an owner at limit one.");
        Assert.Equal(LimitReached, Assert.Single(result.Errors).Code);
        Assert.Equal(0, reader.Captures);
        await AssertProjectUntouchedAsync(scenes[0]);
    }

    [Fact]
    public async Task At_limit_one_the_first_claim_is_admitted_and_the_next_is_refused_until_it_finishes()
    {
        var scenes = await SeedAsync(2);
        await ExecutionCapacitySeed.SetLimitAsync(fixture, 1);

        Assert.True((await ClaimAsync(scenes[0], new FixedReader())).IsSuccess);
        var refused = await ClaimAsync(scenes[1], new FixedReader());
        Assert.Equal(LimitReached, Assert.Single(refused.Errors).Code);

        await using (var db = fixture.CreateContext())
        {
            var executionId = await db.VerificationExecutions.Select(execution => execution.Id).SingleAsync();
            await WorkspaceExecutionSeed.SetExecutionStatusAsync(db, executionId, VerificationExecutionStatus.Passed);
        }

        Assert.True((await ClaimAsync(scenes[1], new FixedReader())).IsSuccess);
    }

    [Fact]
    public async Task Raising_the_limit_back_to_two_admits_a_second_claim_again()
    {
        var scenes = await SeedAsync(2);
        await ExecutionCapacitySeed.SetLimitAsync(fixture, 1);
        Assert.True((await ClaimAsync(scenes[0], new FixedReader())).IsSuccess);
        Assert.True((await ClaimAsync(scenes[1], new FixedReader())).IsFailure);

        Assert.True((await SetAsync(2)).IsSuccess);

        Assert.True((await ClaimAsync(scenes[1], new FixedReader())).IsSuccess);
    }

    [Fact]
    public async Task A_limit_lowered_between_the_early_check_and_the_write_lock_refuses_the_claim_and_consumes_nothing()
    {
        var scenes = await SeedAsync(2);
        Assert.True((await ClaimAsync(scenes[0], new FixedReader())).IsSuccess);
        // Update-first ordering: the operator lowers the limit (one owner is recorded, so it is allowed) after this claim passed its early
        // check at limit two and while it prepares; its final check under the write lock reads the new persisted limit.
        var reader = new FixedReader(async () => Assert.True((await SetAsync(1)).IsSuccess));

        var result = await ClaimAsync(scenes[1], reader);

        Assert.True(result.IsFailure, "The claim was admitted from a limit observed before the change.");
        Assert.Equal(LimitReached, Assert.Single(result.Errors).Code);
        Assert.Equal(1, reader.Captures);
        await AssertProjectUntouchedAsync(scenes[1]);
    }

    [Fact]
    public async Task A_claim_that_commits_first_makes_the_lowering_refuse_because_two_owners_are_recorded()
    {
        var scenes = await SeedAsync(2);
        Assert.True((await ClaimAsync(scenes[0], new FixedReader())).IsSuccess);
        Assert.True((await ClaimAsync(scenes[1], new FixedReader())).IsSuccess);

        var lowering = await SetAsync(1);

        Assert.Equal("execution_capacity.limit_below_recorded_owners", Assert.Single(lowering.Errors).Code);
        await using var db = fixture.CreateContext();
        Assert.Equal(2, await db.VerificationExecutions.CountAsync());
        Assert.Equal(1, await db.ExecutionCapacityConfigurations.CountAsync());
    }

    [Fact]
    public async Task An_owner_taking_the_last_place_at_limit_one_between_the_early_check_and_the_write_lock_refuses_the_claim()
    {
        var scenes = await SeedAsync(2);
        await ExecutionCapacitySeed.SetLimitAsync(fixture, 1);
        var reader = new FixedReader(async () =>
        {
            await using var other = fixture.CreateContext();
            await WorkspaceExecutionSeed.AddAgentAttemptAsync(other, scenes[0].Run.Id, scenes[0].Workspace.Id, scenes[0].Checkpoint.Id);
        });

        var result = await ClaimAsync(scenes[1], reader);

        Assert.Equal(LimitReached, Assert.Single(result.Errors).Code);
        await AssertProjectUntouchedAsync(scenes[1]);
    }

    [Fact]
    public async Task A_missing_configuration_refuses_the_claim_as_unavailable_and_never_defaults_to_two()
    {
        var scenes = await SeedAsync(1);
        await using (var db = fixture.CreateContext())
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM execution_capacity_configurations");
        }

        var reader = new FixedReader();
        var result = await ClaimAsync(scenes[0], reader);

        Assert.Equal("execution_capacity.unavailable", Assert.Single(result.Errors).Code);
        Assert.Equal(0, reader.Captures);
        await AssertProjectUntouchedAsync(scenes[0]);
    }

    // ---- Agent dispatch ---------------------------------------------------------------------------------------------------

    private static async Task<Result<MarkAgentAttemptDispatchedCommandResult>> DispatchAsync(DevalCopilotDbContext db, Guid runId, Guid attemptId)
    {
        await using var transaction = await db.BeginTransactionAsync(CancellationToken.None);
        var result = await new MarkAgentAttemptDispatchedCommandHandler(db, new FixedTimeProvider(Now))
            .HandleAsync(new MarkAgentAttemptDispatchedCommand(runId, attemptId), CancellationToken.None);
        if (result.IsFailure)
        {
            await transaction.RollbackAsync();
            return result;
        }

        await db.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync();
        return result;
    }

    private async Task<Result<MarkAgentAttemptDispatchedCommandResult>> DispatchAsync(Guid runId, Guid attemptId)
    {
        await using var db = fixture.CreateContext();
        return await DispatchAsync(db, runId, attemptId);
    }

    [Theory]
    [InlineData("implementation", OwnerFamily.Agent)]
    [InlineData("implementation", OwnerFamily.Verification)]
    [InlineData("implementation", OwnerFamily.LocalCommit)]
    [InlineData("review-correction", OwnerFamily.Agent)]
    [InlineData("review-correction", OwnerFamily.Verification)]
    [InlineData("review-correction", OwnerFamily.LocalCommit)]
    public async Task At_limit_one_a_mutating_dispatch_is_withheld_by_one_other_owner_and_commits_no_marker(string path, OwnerFamily other)
    {
        var (runId, attemptId) = await AgentAttemptModeSeed.SeedClaimedAsync(fixture, path, AbandonmentRaceSupport.ManualAgentMode);
        await ExecutionCapacitySeed.SetLimitAsync(fixture, 1);
        await ExecutionCapacitySeed.AddAsync(fixture, other);

        var result = await DispatchAsync(runId, attemptId);

        Assert.Equal(LimitReached, Assert.Single(result.Errors).Code);
        await using var verify = fixture.CreateContext();
        var attempt = await verify.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == attemptId);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Null(attempt.AgentDispatchedAtUtc);
    }

    [Theory]
    [InlineData("implementation")]
    [InlineData("review-correction")]
    public async Task At_limit_one_the_only_recorded_owner_still_dispatches_by_excluding_exactly_itself(string path)
    {
        var (runId, attemptId) = await AgentAttemptModeSeed.SeedClaimedAsync(fixture, path, AbandonmentRaceSupport.ManualAgentMode);
        await ExecutionCapacitySeed.SetLimitAsync(fixture, 1);

        Assert.True((await DispatchAsync(runId, attemptId)).IsSuccess);
        await using var verify = fixture.CreateContext();
        Assert.NotNull((await verify.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == attemptId)).AgentDispatchedAtUtc);
    }

    [Theory]
    [InlineData("planning")]
    [InlineData("critical-review")]
    [InlineData("challenge-resolution")]
    [InlineData("code-review")]
    public async Task At_limit_one_a_recognized_ReadOnly_attempt_dispatches_beside_every_recorded_owner(string path)
    {
        var (runId, attemptId) = await AgentAttemptModeSeed.SeedClaimedAsync(fixture, path, AbandonmentRaceSupport.ManualAgentMode);
        await ExecutionCapacitySeed.SetLimitAsync(fixture, 1);
        await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.Agent, OwnerFamily.Verification, OwnerFamily.LocalCommit);

        Assert.True((await DispatchAsync(runId, attemptId)).IsSuccess);
    }


    [Fact]
    public async Task A_limit_lowered_after_a_tracked_read_still_withholds_the_dispatch_that_limit_two_would_have_admitted()
    {
        var (runId, attemptId) = await AgentAttemptModeSeed.SeedClaimedAsync(
            fixture, "implementation", AbandonmentRaceSupport.ManualAgentMode);
        await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.Verification);
        await using var tracked = fixture.CreateContext();
        Assert.Equal(2, (await tracked.ExecutionCapacityConfigurations.ToListAsync()).Single().Limit);
        await ExecutionCapacitySeed.SetLimitAsync(fixture, 1);

        var result = await DispatchAsync(tracked, runId, attemptId);

        Assert.Equal(LimitReached, Assert.Single(result.Errors).Code);
    }

    // ---- Advisory feeds ---------------------------------------------------------------------------------------------------

    private async Task<IReadOnlyList<Guid>> AgentFeedAsync(string feed)
    {
        await using var db = fixture.CreateContext();
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

    [Theory]
    [InlineData("implementation")]
    [InlineData("review-correction")]
    public async Task At_limit_one_a_mutating_feed_proposes_a_pending_attempt_alone_and_not_beside_one_other_owner(string feed)
    {
        var (_, attemptId) = await AgentAttemptModeSeed.SeedClaimedAsync(fixture, feed, AbandonmentRaceSupport.ManualAgentMode);
        await ExecutionCapacitySeed.SetLimitAsync(fixture, 1);
        Assert.Contains(attemptId, await AgentFeedAsync(feed));

        var other = await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.Verification);

        Assert.DoesNotContain(attemptId, await AgentFeedAsync(feed));

        await ExecutionCapacitySeed.FinishAsync(fixture, other);

        Assert.Contains(attemptId, await AgentFeedAsync(feed));
    }

    [Theory]
    [InlineData("planning")]
    [InlineData("critical-review")]
    [InlineData("challenge-resolution")]
    [InlineData("code-review")]
    public async Task At_limit_one_every_ReadOnly_feed_still_proposes_its_attempt(string feed)
    {
        var (_, attemptId) = await AgentAttemptModeSeed.SeedClaimedAsync(fixture, feed, AbandonmentRaceSupport.ManualAgentMode);
        await ExecutionCapacitySeed.SetLimitAsync(fixture, 1);
        await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.Agent, OwnerFamily.Verification, OwnerFamily.LocalCommit);

        Assert.Contains(attemptId, await AgentFeedAsync(feed));
    }

    [Fact]
    public async Task At_limit_one_a_pending_verification_execution_is_proposed_alone_and_not_beside_one_other_owner()
    {
        Guid executionId;
        await using (var db = fixture.CreateContext())
        {
            var scene = await WorkspaceExecutionSeed.SeedAsync(db);
            executionId = await WorkspaceExecutionSeed.AddVerificationExecutionAsync(db, scene, dispatched: false);
        }

        await ExecutionCapacitySeed.SetLimitAsync(fixture, 1);
        Assert.Contains(executionId, await VerificationFeedAsync());

        await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.LocalCommit);

        Assert.DoesNotContain(executionId, await VerificationFeedAsync());
    }

    private async Task<IReadOnlyList<Guid>> VerificationFeedAsync()
    {
        await using var db = fixture.CreateContext();
        return (await new GetEligibleVerificationExecutionsQueryHandler(db)
            .HandleAsync(new GetEligibleVerificationExecutionsQuery(), CancellationToken.None))
            .Select(candidate => candidate.VerificationExecutionId).ToArray();
    }

    [Fact]
    public async Task At_limit_one_a_prepared_local_commit_is_proposed_alone_and_not_beside_one_other_owner()
    {
        var commit = await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.LocalCommit);
        await ExecutionCapacitySeed.SetLimitAsync(fixture, 1);
        Assert.Contains(commit.Id, await CommitFeedAsync());

        await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.Verification);

        Assert.DoesNotContain(commit.Id, await CommitFeedAsync());
    }

    private async Task<IReadOnlyList<Guid>> CommitFeedAsync()
    {
        await using var db = fixture.CreateContext();
        var result = await new GetEligibleLocalCommitOperationsQueryHandler(db)
            .HandleAsync(new GetEligibleLocalCommitOperationsQuery(), CancellationToken.None);
        return result.Value;
    }
}
