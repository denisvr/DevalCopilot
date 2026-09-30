using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.CreateReviewCorrectionAttempt;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// The run-scoped Claude mutation turn-limit request on the review-correction claim: snapshotted
/// immutably under the version 2 contract, read late and guarded at the durable claim boundary, and an
/// invalid stored value refused before any correction authorization is consumed.
/// </summary>
public sealed partial class CreateReviewCorrectionAttemptCommandHandlerTests
{
    private async Task AssertNoCorrectionClaimPersistedAsync(Seed seed, int attemptsBefore, long eventsBefore)
    {
        await using var verify = _fixture.CreateContext();
        Assert.Equal(attemptsBefore, await verify.Attempts.CountAsync(a => a.RunId == seed.Run.Id));
        Assert.Empty(await verify.Attempts
            .Where(a => a.RunId == seed.Run.Id && a.AgentResponseContract == AgentResponseContract.ReviewCorrection
                && a.AgentAdapterContractVersion == "claude-review-correction-v2")
            .ToListAsync());
        Assert.Empty(await verify.Artifacts.Where(a => a.RunId == seed.Run.Id).ToListAsync());
        Assert.Empty(await verify.AttemptInputMessages.Where(m => verify.Attempts.All(a => a.Id != m.AttemptId)).ToListAsync());
        Assert.Equal(eventsBefore, await ClaudeMutationTurnLimitTestSupport.CountEventsAsync(_fixture, seed.Run.Id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(37)]
    [InlineData(100)]
    public async Task Claim_snapshots_the_runs_turn_limit_under_the_v2_contract_immutably(int? limit)
    {
        await using var seedContext = _fixture.CreateContext();
        var seed = await SeedAsync(seedContext);
        if (limit is not null)
        {
            await ClaudeMutationTurnLimitTestSupport.SetLimitAsync(_fixture, seed.Run.Id, limit);
        }

        await using var handlerContext = _fixture.CreateContext();
        var result = await Handler(handlerContext).HandleAsync(Command(seed), CancellationToken.None);

        var created = Assert.IsType<CreateReviewCorrectionAttemptCommandResult.AttemptCreated>(result.Value);
        Assert.Equal(limit, await ClaudeMutationTurnLimitTestSupport.ReadAttemptLimitAsync(_fixture, created.AttemptId));
        Assert.Equal(
            "claude-review-correction-v2",
            await ClaudeMutationTurnLimitTestSupport.ReadAttemptContractVersionAsync(_fixture, created.AttemptId));

        await ClaudeMutationTurnLimitTestSupport.SetLimitAsync(_fixture, seed.Run.Id, limit == 50 ? 51 : 50);
        Assert.Equal(limit, await ClaudeMutationTurnLimitTestSupport.ReadAttemptLimitAsync(_fixture, created.AttemptId));
        await ClaudeMutationTurnLimitTestSupport.SetLimitAsync(_fixture, seed.Run.Id, null);
        Assert.Equal(limit, await ClaudeMutationTurnLimitTestSupport.ReadAttemptLimitAsync(_fixture, created.AttemptId));
        await using var verify = _fixture.CreateContext();
        var attempt = await verify.Attempts.AsNoTracking().SingleAsync(a => a.Id == created.AttemptId);
        Assert.Equal(AgentPermissionProfile.WorkspaceEditOnly, attempt.AgentPermissionProfile);
        Assert.Equal(
            limit is null ? ClaudeMutationTurnLimitEvidence.NotRequested : ClaudeMutationTurnLimitEvidence.Requested,
            attempt.GetMutationTurnLimitEvidence());
    }

    [Fact]
    public async Task Claim_snapshots_a_turn_limit_committed_during_external_evidence_capture()
    {
        await using var seedContext = _fixture.CreateContext();
        var seed = await SeedAsync(seedContext);
        var evidence = new RecordingEvidenceReader(
            seed.Evidence, _ => ClaudeMutationTurnLimitTestSupport.SetLimitAsync(_fixture, seed.Run.Id, 9));
        await using var handlerContext = _fixture.CreateContext();

        var result = await StopHandler(handlerContext, evidence).HandleAsync(Command(seed), CancellationToken.None);

        var created = Assert.IsType<CreateReviewCorrectionAttemptCommandResult.AttemptCreated>(result.Value);
        Assert.Equal(9, await ClaudeMutationTurnLimitTestSupport.ReadAttemptLimitAsync(_fixture, created.AttemptId));
    }

    [Theory]
    [InlineData(null, 5)]
    [InlineData(5, 6)]
    [InlineData(5, null)]
    public async Task Claim_persists_nothing_when_the_turn_limit_changes_between_snapshot_and_commit(int? initial, int? competing)
    {
        await using var seedContext = _fixture.CreateContext();
        var seed = await SeedAsync(seedContext);
        await ClaudeMutationTurnLimitTestSupport.SetLimitAsync(_fixture, seed.Run.Id, initial);
        var attemptsBefore = await StopAttemptCountAsync(seed.Run.Id);
        var eventsBefore = await ClaudeMutationTurnLimitTestSupport.CountEventsAsync(_fixture, seed.Run.Id);
        var store = new TestArtifactStore();
        await using var handlerContext = _fixture.CreateContext(new BeforeFirstSaveInterceptor(
            () => ClaudeMutationTurnLimitTestSupport.SetLimitAsync(_fixture, seed.Run.Id, competing)));

        var result = await StopHandler(handlerContext, new RecordingEvidenceReader(seed.Evidence), store)
            .HandleAsync(Command(seed), CancellationToken.None);

        AssertCode(result, "agent_attempts.run_changed_during_claim");
        Assert.Single(store.DeletedSealedFiles);
        await AssertNoCorrectionClaimPersistedAsync(seed, attemptsBefore, eventsBefore);
        Assert.Equal(competing, await ClaudeMutationTurnLimitTestSupport.ReadRunLimitAsync(_fixture, seed.Run.Id));
    }

    [Fact]
    public async Task Claim_persists_nothing_when_the_run_lifecycle_changes_between_snapshot_and_commit()
    {
        await using var seedContext = _fixture.CreateContext();
        var seed = await SeedAsync(seedContext);
        await ClaudeMutationTurnLimitTestSupport.SetLimitAsync(_fixture, seed.Run.Id, 5);
        var attemptsBefore = await StopAttemptCountAsync(seed.Run.Id);
        var eventsBefore = await ClaudeMutationTurnLimitTestSupport.CountEventsAsync(_fixture, seed.Run.Id);
        var store = new TestArtifactStore();
        await using var handlerContext = _fixture.CreateContext(new BeforeFirstSaveInterceptor(async () =>
        {
            await using var competing = _fixture.CreateContext();
            var run = await competing.Runs.SingleAsync(r => r.Id == seed.Run.Id);
            run.Complete(Now.AddMinutes(1));
            await competing.SaveChangesAsync();
        }));

        var result = await StopHandler(handlerContext, new RecordingEvidenceReader(seed.Evidence), store)
            .HandleAsync(Command(seed), CancellationToken.None);

        AssertCode(result, "agent_attempts.run_changed_during_claim");
        Assert.Single(store.DeletedSealedFiles);
        await AssertNoCorrectionClaimPersistedAsync(seed, attemptsBefore, eventsBefore);
    }

    [Fact]
    public async Task A_turn_limit_change_at_the_claim_commit_leaves_an_available_authorization_unconsumed()
    {
        var (seed, authorizationId) = await SeedAuthorizedAsync();
        var attemptsBefore = await StopAttemptCountAsync(seed.Run.Id);
        var eventsBefore = await ClaudeMutationTurnLimitTestSupport.CountEventsAsync(_fixture, seed.Run.Id);
        var store = new TestArtifactStore();
        await using var context = _fixture.CreateContext(new BeforeFirstSaveInterceptor(
            () => ClaudeMutationTurnLimitTestSupport.SetLimitAsync(_fixture, seed.Run.Id, 12)));

        var result = await StopHandler(context, new RecordingEvidenceReader(seed.Evidence), store, Now.AddMinutes(2))
            .HandleAsync(Command(seed), CancellationToken.None);

        AssertCode(result, "agent_attempts.run_changed_during_claim");
        Assert.Single(store.DeletedSealedFiles);
        await AssertNoCorrectionClaimPersistedAsync(seed, attemptsBefore, eventsBefore);
        await using var verify = _fixture.CreateContext();
        var authorization = await verify.ReviewCorrectionAuthorizations.AsNoTracking().SingleAsync(a => a.Id == authorizationId);
        Assert.Null(authorization.ConsumedByAttemptId);
        Assert.True(authorization.IsAvailable);

        // A retry re-reads the new request and consumes the authorization exactly once.
        await using var retryContext = _fixture.CreateContext();
        var retry = await StopHandler(retryContext, new RecordingEvidenceReader(seed.Evidence), at: Now.AddMinutes(2))
            .HandleAsync(Command(seed), CancellationToken.None);
        var created = Assert.IsType<CreateReviewCorrectionAttemptCommandResult.AttemptCreated>(retry.Value);
        Assert.Equal(12, await ClaudeMutationTurnLimitTestSupport.ReadAttemptLimitAsync(_fixture, created.AttemptId));
    }

    [Fact]
    public async Task A_successful_claim_with_a_turn_limit_consumes_the_authorization_exactly_once()
    {
        var (seed, authorizationId) = await SeedAuthorizedAsync();
        await ClaudeMutationTurnLimitTestSupport.SetLimitAsync(_fixture, seed.Run.Id, 12);
        await using var context = _fixture.CreateContext();

        var result = await StopHandler(context, new RecordingEvidenceReader(seed.Evidence), at: Now.AddMinutes(2))
            .HandleAsync(Command(seed), CancellationToken.None);

        var created = Assert.IsType<CreateReviewCorrectionAttemptCommandResult.AttemptCreated>(result.Value);
        Assert.Equal(12, await ClaudeMutationTurnLimitTestSupport.ReadAttemptLimitAsync(_fixture, created.AttemptId));
        await using var verify = _fixture.CreateContext();
        Assert.Equal(
            created.AttemptId,
            (await verify.ReviewCorrectionAuthorizations.AsNoTracking().SingleAsync(a => a.Id == authorizationId)).ConsumedByAttemptId);
    }

    public static IEnumerable<object[]> StorageClassCases =>
        [[new byte[] { 0x37 }], ["blob:37"], ["b:37"], [double.PositiveInfinity]];

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(101)]
    [InlineData(3.5)]
    [InlineData(4294967297L)]
    [InlineData("abc")]
    [MemberData(nameof(StorageClassCases))]
    public async Task Claim_refuses_an_invalid_stored_turn_limit_without_clamping_and_removes_the_orphan_manifest(object stored)
    {
        await using var seedContext = _fixture.CreateContext();
        var seed = await SeedAsync(seedContext);
        await ClaudeMutationTurnLimitTestSupport.SetRawRunLimitAsync(_fixture, seed.Run.Id, stored);
        var attemptsBefore = await StopAttemptCountAsync(seed.Run.Id);
        var eventsBefore = await ClaudeMutationTurnLimitTestSupport.CountEventsAsync(_fixture, seed.Run.Id);
        var store = new TestArtifactStore();
        await using var context = _fixture.CreateContext();

        var result = await StopHandler(context, new RecordingEvidenceReader(seed.Evidence), store)
            .HandleAsync(Command(seed), CancellationToken.None);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("agent_attempts.claude_turn_limit_invalid", error.Code);
        Assert.DoesNotContain(ClaudeMutationTurnLimitTestSupport.StoredText(stored), error.Description, StringComparison.Ordinal);
        Assert.Single(store.DeletedSealedFiles);
        await AssertNoCorrectionClaimPersistedAsync(seed, attemptsBefore, eventsBefore);
        Assert.Equal(ClaudeMutationTurnLimitTestSupport.StoredText(stored), await ClaudeMutationTurnLimitTestSupport.ReadRunRawAsync(_fixture, seed.Run.Id));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    [InlineData(3.5)]
    [InlineData(4294967297L)]
    [InlineData("abc")]
    [MemberData(nameof(StorageClassCases))]
    public async Task An_invalid_stored_turn_limit_leaves_an_available_authorization_unconsumed(object stored)
    {
        var (seed, authorizationId) = await SeedAuthorizedAsync();
        await ClaudeMutationTurnLimitTestSupport.SetRawRunLimitAsync(_fixture, seed.Run.Id, stored);
        var attemptsBefore = await StopAttemptCountAsync(seed.Run.Id);
        var eventsBefore = await ClaudeMutationTurnLimitTestSupport.CountEventsAsync(_fixture, seed.Run.Id);
        var store = new TestArtifactStore();
        await using var context = _fixture.CreateContext();

        var result = await StopHandler(context, new RecordingEvidenceReader(seed.Evidence), store, Now.AddMinutes(2))
            .HandleAsync(Command(seed), CancellationToken.None);

        AssertCode(result, "agent_attempts.claude_turn_limit_invalid");
        Assert.Single(store.DeletedSealedFiles);
        await AssertNoCorrectionClaimPersistedAsync(seed, attemptsBefore, eventsBefore);
        await using var verify = _fixture.CreateContext();
        var authorization = await verify.ReviewCorrectionAuthorizations.AsNoTracking().SingleAsync(a => a.Id == authorizationId);
        Assert.Null(authorization.ConsumedByAttemptId);
        Assert.True(authorization.IsAvailable);
    }

    [Fact]
    public async Task Claim_only_conflicts_on_the_exact_claimed_runs_turn_limit()
    {
        await using var seedContext = _fixture.CreateContext();
        var seed = await SeedAsync(seedContext);
        await using var otherContext = _fixture.CreateContext();
        var other = await SeedAsync(otherContext, providerObserved: false);
        await using var handlerContext = _fixture.CreateContext(new BeforeFirstSaveInterceptor(
            () => ClaudeMutationTurnLimitTestSupport.SetLimitAsync(_fixture, other.Run.Id, 77)));

        var result = await StopHandler(handlerContext, new RecordingEvidenceReader(seed.Evidence))
            .HandleAsync(Command(seed), CancellationToken.None);

        Assert.IsType<CreateReviewCorrectionAttemptCommandResult.AttemptCreated>(result.Value);
        Assert.Equal(77, await ClaudeMutationTurnLimitTestSupport.ReadRunLimitAsync(_fixture, other.Run.Id));
    }


    [Fact]
    public async Task A_turn_limit_does_not_weaken_the_running_attempt_gate()
    {
        await using var seedContext = _fixture.CreateContext();
        var seed = await SeedAsync(seedContext, runningAttempt: true);
        await ClaudeMutationTurnLimitTestSupport.SetLimitAsync(_fixture, seed.Run.Id, 5);
        await using var context = _fixture.CreateContext();

        var result = await Handler(context).HandleAsync(Command(seed), CancellationToken.None);

        AssertCode(result, "attempts.run_has_active_attempt");
    }

    [Fact]
    public async Task A_turn_limit_does_not_weaken_the_run_wide_count_gate()
    {
        await using var seedContext = _fixture.CreateContext();
        var seed = await SeedAsync(seedContext, maximumAgentAttempts: 4);
        await ClaudeMutationTurnLimitTestSupport.SetLimitAsync(_fixture, seed.Run.Id, 5);
        await using var context = _fixture.CreateContext();

        var result = await Handler(context).HandleAsync(Command(seed), CancellationToken.None);

        AssertCode(result, "agent_attempts.budget_exhausted");
        Assert.Equal(4, await StopAttemptCountAsync(seed.Run.Id));
    }
}
