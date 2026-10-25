using DevalCopilot.Application.Features.Projects.Policies.WorkspaceExecutions;
using DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.DiagnosisTestScene;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// ADR-0057 at the single-use Agent dispatch marker, the last gate before a provider is invoked. The gate runs inside its own
/// write-locked transaction (the production pipeline's, reproduced here around the handler), re-reads the workspace's ownership
/// afresh and excludes only the exact attempt being dispatched. A refusal commits no marker, is not a workspace loss and never
/// terminalizes the attempt.
/// </summary>
public sealed class AgentDispatchWorkspaceExecutionTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 13, 0, 0, TimeSpan.Zero);

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    public static TheoryData<string> AgentPaths() =>
        ["planning", "critical-review", "challenge-resolution", "implementation", "code-review", "review-correction"];

    private async Task<Devalente.Shared.Results.Result<MarkAgentAttemptDispatchedCommandResult>> DispatchAsync(
        DevalCopilotDbContext db, Guid runId, Guid attemptId)
    {
        // The EF transaction behavior of every ICommand: the lock is taken before the handler reads, the save and commit follow success.
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

    private async Task AssertStillRunningAndUndispatchedAsync(Guid attemptId)
    {
        await using var verify = _fixture.CreateContext();
        var attempt = await verify.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == attemptId);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Null(attempt.AgentDispatchedAtUtc);
        Assert.Null(attempt.AgentOutcome);
    }

    [Theory]
    [MemberData(nameof(AgentPaths))]
    public async Task The_exact_attempt_alone_in_its_workspace_is_dispatched(string path)
    {
        var (runId, attemptId) = await AgentAttemptModeSeed.SeedClaimedAsync(_fixture, path, AbandonmentRaceSupport.ManualAgentMode);

        await using var db = _fixture.CreateContext();
        var result = await DispatchAsync(db, runId, attemptId);

        Assert.True(result.IsSuccess);
        await using var verify = _fixture.CreateContext();
        Assert.NotNull((await verify.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == attemptId)).AgentDispatchedAtUtc);
    }

    [Theory]
    [InlineData("planning", CompetingOwner.Verification)]
    [InlineData("planning", CompetingOwner.AgentOfAnotherRun)]
    [InlineData("critical-review", CompetingOwner.Verification)]
    [InlineData("critical-review", CompetingOwner.AgentOfAnotherRun)]
    [InlineData("challenge-resolution", CompetingOwner.Verification)]
    [InlineData("challenge-resolution", CompetingOwner.AgentOfAnotherRun)]
    [InlineData("implementation", CompetingOwner.Verification)]
    [InlineData("implementation", CompetingOwner.AgentOfAnotherRun)]
    [InlineData("code-review", CompetingOwner.Verification)]
    [InlineData("code-review", CompetingOwner.AgentOfAnotherRun)]
    [InlineData("review-correction", CompetingOwner.Verification)]
    [InlineData("review-correction", CompetingOwner.AgentOfAnotherRun)]
    public async Task A_competing_owner_that_appears_before_the_lock_refuses_the_dispatch_and_leaves_the_attempt_as_recorded(
        string path, CompetingOwner owner)
    {
        var (runId, attemptId) = await AgentAttemptModeSeed.SeedClaimedAsync(_fixture, path, AbandonmentRaceSupport.ManualAgentMode);
        await using var db = _fixture.CreateContext(new BeforeFirstSaveInterceptor(
            () => WorkspaceExecutionRaceSupport.AddAsync(_fixture, runId, owner)));

        var result = await DispatchAsync(db, runId, attemptId);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal(WorkspaceExecutionRaceSupport.CodeOf(owner), error.Code);
        Assert.NotEqual(MarkAgentAttemptDispatchedCommandHandler.WorkspaceNoLongerEligibleCode, error.Code);
        await AssertStillRunningAndUndispatchedAsync(attemptId);
    }

    [Theory]
    [MemberData(nameof(AgentPaths))]
    public async Task An_owner_recorded_before_the_dispatch_refuses_it_and_conflicting_owners_are_not_resolved(string path)
    {
        var (runId, attemptId) = await AgentAttemptModeSeed.SeedClaimedAsync(_fixture, path, AbandonmentRaceSupport.ManualAgentMode);
        await WorkspaceExecutionRaceSupport.AddAsync(_fixture, runId, CompetingOwner.Verification);

        await using var db = _fixture.CreateContext();
        var single = await DispatchAsync(db, runId, attemptId);
        await WorkspaceExecutionRaceSupport.AddAsync(_fixture, runId, CompetingOwner.AgentOfAnotherRun);
        await using var later = _fixture.CreateContext();
        var conflicted = await DispatchAsync(later, runId, attemptId);

        Assert.Equal(WorkspaceExecutionErrors.VerificationActiveCode, Assert.Single(single.Errors).Code);
        Assert.Equal(WorkspaceExecutionErrors.OwnersConflictedCode, Assert.Single(conflicted.Errors).Code);
        await AssertStillRunningAndUndispatchedAsync(attemptId);
    }

    [Fact]
    public async Task A_verification_execution_with_the_attempts_own_identifier_is_not_the_exact_attempt_and_is_not_excluded()
    {
        var (runId, attemptId) = await AgentAttemptModeSeed.SeedClaimedAsync(_fixture, "implementation", AbandonmentRaceSupport.ManualAgentMode);
        await WorkspaceExecutionRaceSupport.AddAsync(_fixture, runId, CompetingOwner.Verification, identifier: attemptId);

        await using var db = _fixture.CreateContext();
        var result = await DispatchAsync(db, runId, attemptId);

        Assert.Equal(WorkspaceExecutionErrors.VerificationActiveCode, Assert.Single(result.Errors).Code);
        await AssertStillRunningAndUndispatchedAsync(attemptId);
    }

    [Fact]
    public async Task Another_attempt_of_the_same_run_is_never_excluded_only_the_exact_attempt()
    {
        var (runId, attemptId) = await AgentAttemptModeSeed.SeedClaimedAsync(_fixture, "implementation", AbandonmentRaceSupport.ManualAgentMode);
        await using (var db = _fixture.CreateContext())
        {
            var scene = await db.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == attemptId);
            // A second unresolved attempt of the same run on the same workspace: the filtered unique index normally forbids it, so the
            // rows are inserted with the index dropped, exactly like a pre-existing inconsistent database.
            await db.Database.ExecuteSqlRawAsync("DROP INDEX IF EXISTS ix_attempts_run_id_one_running");
            await WorkspaceExecutionSeed_AddSecondRunningAttemptAsync(db, runId, scene);
        }

        await using var dispatchContext = _fixture.CreateContext();
        var result = await DispatchAsync(dispatchContext, runId, attemptId);

        Assert.Equal(WorkspaceExecutionErrors.AgentAttemptActiveCode, Assert.Single(result.Errors).Code);
        await AssertStillRunningAndUndispatchedAsync(attemptId);
    }

    [Fact]
    public async Task An_owner_of_another_repository_never_refuses_the_dispatch()
    {
        var (runId, attemptId) = await AgentAttemptModeSeed.SeedClaimedAsync(_fixture, "implementation", AbandonmentRaceSupport.ManualAgentMode);
        var (otherRunId, _) = await AgentAttemptModeSeed.SeedClaimedAsync(_fixture, "planning", AbandonmentRaceSupport.ManualAgentMode);
        await WorkspaceExecutionRaceSupport.AddAsync(_fixture, otherRunId, CompetingOwner.Verification);

        await using var db = _fixture.CreateContext();
        var result = await DispatchAsync(db, runId, attemptId);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task The_verification_diagnosis_dispatch_is_refused_by_a_competing_Agent_attempt_and_leaves_the_attempt_as_recorded()
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()]);
        var claim = await scene.ClaimAsync();
        Assert.True(claim.IsSuccess);
        var attemptId = claim.Value.AttemptId;
        await using var db = _fixture.CreateContext(new BeforeFirstSaveInterceptor(
            () => WorkspaceExecutionRaceSupport.AddAsync(_fixture, scene.Run.Id, CompetingOwner.AgentOfAnotherRun)));

        var result = await DispatchAsync(db, scene.Run.Id, attemptId);

        // A verification execution would also change the diagnosed evidence, which the dispatch gate classifies with its own code first.
        Assert.Equal(WorkspaceExecutionErrors.AgentAttemptActiveCode, Assert.Single(result.Errors).Code);
        await AssertStillRunningAndUndispatchedAsync(attemptId);
    }

    private static async Task WorkspaceExecutionSeed_AddSecondRunningAttemptAsync(DevalCopilotDbContext db, Guid runId, Attempt existing)
    {
        var second = Attempt.ClaimAgentImplementation(
            Guid.NewGuid(), runId, 2, existing.AgentGitWorkspaceId!.Value, existing.AgentGitCheckpointId!.Value,
            existing.AgentCheckpointFingerprintSha256!, Guid.NewGuid(), TimeSpan.FromMinutes(10), 1024, 2048, Now, 2);
        db.Attempts.Add(second);
        await db.SaveChangesAsync(CancellationToken.None);
    }
}
