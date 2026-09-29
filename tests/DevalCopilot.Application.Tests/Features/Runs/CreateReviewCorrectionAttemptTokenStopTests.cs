using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.AuthorizeReviewCorrection;
using DevalCopilot.Application.Features.Runs.Commands.CreateReviewCorrectionAttempt;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// The run-scoped Claude token-activity stop on the review-correction claim: refused before the
/// review-correction budget, escalation, human-authorization consumption, and any external work, and
/// guarded against a concurrent stop-policy change at the durable claim boundary.
/// </summary>
public sealed partial class CreateReviewCorrectionAttemptCommandHandlerTests
{
    private static readonly GitWorkspaceEvidenceResult StopEvidence =
        new(GitWorkspaceEvidenceOutcome.Success, new string('a', 40), Fingerprint, [], null);

    private static CreateReviewCorrectionAttemptCommandHandler StopHandler(
        DevalCopilotDbContext context, IGitWorkspaceEvidenceReader reader, TestArtifactStore? store = null,
        DateTimeOffset? at = null) =>
        new(context, reader, store ?? new TestArtifactStore(), new FixedTimeProvider(at ?? Now));

    private async Task AddClaudeHistoryAsync(Seed seed, AgentTokenUsageEvidence? usage) =>
        await TokenStopTestSupport.AddHistoryAsync(
            _fixture, seed.Run.Id, seed.Workspace.Id, seed.Checkpoint!.Id, AgentProvider.ClaudeCode, usage);

    private async Task<int> StopAttemptCountAsync(Guid runId)
    {
        await using var verify = _fixture.CreateContext();
        return await verify.Attempts.CountAsync(a => a.RunId == runId);
    }

    /// <summary>Two failed corrections exhaust the review-correction budget; the escalation and one
    /// human authorization are then created through the real handlers.</summary>
    private async Task<(Seed Seed, Guid AuthorizationId)> SeedAuthorizedAsync()
    {
        await using var context = _fixture.CreateContext();
        var seed = await SeedAsync(context, maximumAgentInvocationTime: TimeSpan.FromHours(24));
        for (var number = 5; number <= 6; number++)
        {
            var priorCorrection = Attempt.ClaimAgentReviewCorrection(
                Guid.NewGuid(), seed.Run.Id, number, seed.Workspace.Id, seed.Checkpoint!.Id, Fingerprint, Guid.NewGuid(),
                TimeSpan.FromMinutes(20), 262144, 524288, Now, number);
            priorCorrection.MarkAgentDispatched(Now);
            priorCorrection.CompleteAgent(AgentOutcome.ProviderInvocationFailed, null, Now);
            context.Attempts.Add(priorCorrection);
        }

        await context.SaveChangesAsync(CancellationToken.None);
        var escalation = Assert.IsType<CreateReviewCorrectionAttemptCommandResult.Escalated>(
            (await Handler(context).HandleAsync(Command(seed), CancellationToken.None)).Value);
        var authorized = await new AuthorizeReviewCorrectionCommandHandler(
                context, new RecordingEvidenceReader(seed.Evidence), new FixedTimeProvider(Now.AddMinutes(1)))
            .HandleAsync(new AuthorizeReviewCorrectionCommand(seed.Run.Id, escalation.EscalationId), CancellationToken.None);
        Assert.True(authorized.IsSuccess);
        return (seed, (await context.ReviewCorrectionAuthorizations.SingleAsync()).Id);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_reached_claude_stop_refuses_the_correction_before_any_external_work(bool providerObserved)
    {
        await using var seedContext = _fixture.CreateContext();
        var seed = await SeedAsync(seedContext, providerObserved: providerObserved);
        await AddClaudeHistoryAsync(seed, TokenStopTestSupport.ClaudeUsage(500, 50, 5, 60));
        await TokenStopTestSupport.SetStopAsync(_fixture, seed.Run.Id, AgentProvider.ClaudeCode, 615);
        var attemptsBefore = await StopAttemptCountAsync(seed.Run.Id);
        var captures = 0;
        var store = new TestArtifactStore();
        await using var context = _fixture.CreateContext();

        var result = await StopHandler(context, new RecordingEvidenceReader(seed.Evidence, _ => { captures++; return Task.CompletedTask; }), store)
            .HandleAsync(Command(seed), CancellationToken.None);

        AssertCode(result, AgentTokenStopGate.ReachedCode);
        Assert.Equal(0, captures);
        Assert.Empty(store.DeletedSealedFiles);
        Assert.Equal(attemptsBefore, await StopAttemptCountAsync(seed.Run.Id));
        await using var verify = _fixture.CreateContext();
        Assert.Empty(await verify.Artifacts.Where(a => a.RunId == seed.Run.Id).ToListAsync());
    }

    [Fact]
    public async Task An_indeterminate_claude_stop_refuses_the_correction_before_any_external_work()
    {
        await using var seedContext = _fixture.CreateContext();
        var seed = await SeedAsync(seedContext, providerObserved: false);
        await AddClaudeHistoryAsync(seed, usage: null);
        await TokenStopTestSupport.SetStopAsync(_fixture, seed.Run.Id, AgentProvider.ClaudeCode, 1_000_000);
        var captures = 0;
        await using var context = _fixture.CreateContext();

        var result = await StopHandler(context, new RecordingEvidenceReader(seed.Evidence, _ => { captures++; return Task.CompletedTask; }))
            .HandleAsync(Command(seed), CancellationToken.None);

        AssertCode(result, AgentTokenStopGate.EvidenceIndeterminateCode);
        Assert.Equal(0, captures);
    }

    [Fact]
    public async Task The_stop_never_creates_an_escalation_or_consumes_a_human_authorization()
    {
        // Correction budget exhausted with no escalation yet: a stopped run must not create one.
        await using var context = _fixture.CreateContext();
        var seed = await SeedAsync(context, maximumAgentInvocationTime: TimeSpan.FromHours(24));
        for (var number = 5; number <= 6; number++)
        {
            var priorCorrection = Attempt.ClaimAgentReviewCorrection(
                Guid.NewGuid(), seed.Run.Id, number, seed.Workspace.Id, seed.Checkpoint!.Id, Fingerprint, Guid.NewGuid(),
                TimeSpan.FromMinutes(20), 262144, 524288, Now, number);
            priorCorrection.MarkAgentDispatched(Now);
            priorCorrection.CompleteAgent(AgentOutcome.ProviderInvocationFailed, null, Now);
            context.Attempts.Add(priorCorrection);
        }

        await context.SaveChangesAsync(CancellationToken.None);
        await AddClaudeHistoryAsync(seed, TokenStopTestSupport.ClaudeUsage(500, 50, 5, 60));
        await TokenStopTestSupport.SetStopAsync(_fixture, seed.Run.Id, AgentProvider.ClaudeCode, 615);
        await using var handlerContext = _fixture.CreateContext();

        var result = await Handler(handlerContext).HandleAsync(Command(seed), CancellationToken.None);

        AssertCode(result, AgentTokenStopGate.ReachedCode);
        await using var verify = _fixture.CreateContext();
        Assert.Empty(await verify.ReviewCorrectionEscalations.ToListAsync());
        Assert.Empty(await verify.ReviewCorrectionAuthorizations.ToListAsync());
    }

    [Fact]
    public async Task A_stop_refusal_leaves_an_available_authorization_unconsumed_and_a_later_claim_consumes_it_once()
    {
        var (seed, authorizationId) = await SeedAuthorizedAsync();
        await AddClaudeHistoryAsync(seed, TokenStopTestSupport.ClaudeUsage(500, 50, 5, 60));
        await TokenStopTestSupport.SetStopAsync(_fixture, seed.Run.Id, AgentProvider.ClaudeCode, 615);
        await using (var refusedContext = _fixture.CreateContext())
        {
            var refused = await StopHandler(refusedContext, new RecordingEvidenceReader(seed.Evidence), at: Now.AddMinutes(2))
                .HandleAsync(Command(seed), CancellationToken.None);
            AssertCode(refused, AgentTokenStopGate.ReachedCode);
        }

        await using (var verify = _fixture.CreateContext())
        {
            var authorization = await verify.ReviewCorrectionAuthorizations.AsNoTracking().SingleAsync(a => a.Id == authorizationId);
            Assert.Null(authorization.ConsumedByAttemptId);
            Assert.True(authorization.IsAvailable);
        }

        await TokenStopTestSupport.SetStopAsync(_fixture, seed.Run.Id, AgentProvider.ClaudeCode, null);
        await using var claimContext = _fixture.CreateContext();
        var claimed = await StopHandler(claimContext, new RecordingEvidenceReader(seed.Evidence), at: Now.AddMinutes(2))
            .HandleAsync(Command(seed), CancellationToken.None);
        var created = Assert.IsType<CreateReviewCorrectionAttemptCommandResult.AttemptCreated>(claimed.Value);
        await using var after = _fixture.CreateContext();
        Assert.Equal(created.AttemptId, (await after.ReviewCorrectionAuthorizations.AsNoTracking().SingleAsync(a => a.Id == authorizationId)).ConsumedByAttemptId);
    }

    [Fact]
    public async Task A_stop_policy_change_at_the_claim_commit_rolls_back_the_attempt_and_leaves_the_authorization_unconsumed()
    {
        var (seed, authorizationId) = await SeedAuthorizedAsync();
        await AddClaudeHistoryAsync(seed, TokenStopTestSupport.ClaudeUsage(500, 50, 5, 60));
        var attemptsBefore = await StopAttemptCountAsync(seed.Run.Id);
        var store = new TestArtifactStore();
        var reader = new RecordingEvidenceReader(
            seed.Evidence, _ => TokenStopTestSupport.SetStopAsync(_fixture, seed.Run.Id, AgentProvider.ClaudeCode, 1));
        await using var context = _fixture.CreateContext();

        var result = await StopHandler(context, reader, store, Now.AddMinutes(2)).HandleAsync(Command(seed), CancellationToken.None);

        AssertCode(result, CurrentTokenStopPolicy.PolicyChangedDuringClaimCode);
        Assert.Single(store.DeletedSealedFiles);
        await using var verify = _fixture.CreateContext();
        Assert.Equal(attemptsBefore, await verify.Attempts.CountAsync(a => a.RunId == seed.Run.Id));
        Assert.Empty(await verify.Artifacts.Where(a => a.RunId == seed.Run.Id).ToListAsync());
        var authorization = await verify.ReviewCorrectionAuthorizations.AsNoTracking().SingleAsync(a => a.Id == authorizationId);
        Assert.Null(authorization.ConsumedByAttemptId);
        Assert.True(authorization.IsAvailable);
    }

    [Fact]
    public async Task A_stop_policy_change_at_the_claim_commit_is_retryable_and_the_retry_re_decides()
    {
        await using var seedContext = _fixture.CreateContext();
        var seed = await SeedAsync(seedContext);
        await AddClaudeHistoryAsync(seed, TokenStopTestSupport.ClaudeUsage(500, 50, 5, 60));
        var reader = new RecordingEvidenceReader(
            seed.Evidence, _ => TokenStopTestSupport.SetStopAsync(_fixture, seed.Run.Id, AgentProvider.ClaudeCode, 1));
        await using var context = _fixture.CreateContext();

        var result = await StopHandler(context, reader).HandleAsync(Command(seed), CancellationToken.None);

        AssertCode(result, CurrentTokenStopPolicy.PolicyChangedDuringClaimCode);
        await using var retryContext = _fixture.CreateContext();
        AssertCode(await Handler(retryContext).HandleAsync(Command(seed), CancellationToken.None), AgentTokenStopGate.ReachedCode);
    }

    [Fact]
    public async Task A_change_to_the_other_providers_stop_at_the_claim_commit_is_also_a_policy_change()
    {
        await using var seedContext = _fixture.CreateContext();
        var seed = await SeedAsync(seedContext);
        var reader = new RecordingEvidenceReader(
            seed.Evidence, _ => TokenStopTestSupport.SetStopAsync(_fixture, seed.Run.Id, AgentProvider.Codex, 5));
        await using var context = _fixture.CreateContext();

        var result = await StopHandler(context, reader).HandleAsync(Command(seed), CancellationToken.None);

        AssertCode(result, CurrentTokenStopPolicy.PolicyChangedDuringClaimCode);
    }

    [Fact]
    public async Task An_unconfigured_claude_stop_and_a_codex_stop_leave_the_claim_unchanged()
    {
        await using var seedContext = _fixture.CreateContext();
        var seed = await SeedAsync(seedContext);
        await AddClaudeHistoryAsync(seed, TokenStopTestSupport.ClaudeUsage(500, 50, 5, 60));
        await TokenStopTestSupport.SetStopAsync(_fixture, seed.Run.Id, AgentProvider.Codex, 1);
        await using var context = _fixture.CreateContext();

        var result = await Handler(context).HandleAsync(Command(seed), CancellationToken.None);

        Assert.IsType<CreateReviewCorrectionAttemptCommandResult.AttemptCreated>(result.Value);
    }

    [Fact]
    public async Task The_run_wide_count_budget_takes_precedence_over_the_stop()
    {
        await using var context = _fixture.CreateContext();
        var seed = await SeedAsync(context, maximumAgentAttempts: 4);
        await TokenStopTestSupport.SetStopAsync(_fixture, seed.Run.Id, AgentProvider.ClaudeCode, 1);

        AssertCode(await Handler(context).HandleAsync(Command(seed), CancellationToken.None), "agent_attempts.budget_exhausted");
    }

    [Fact]
    public async Task The_run_wide_time_budget_takes_precedence_over_the_stop()
    {
        await using var context = _fixture.CreateContext();
        var seed = await SeedAsync(context, maximumAgentInvocationTime: TimeSpan.FromMinutes(90));
        await TokenStopTestSupport.SetStopAsync(_fixture, seed.Run.Id, AgentProvider.ClaudeCode, 1);

        AssertCode(await Handler(context).HandleAsync(Command(seed), CancellationToken.None), "agent_attempts.time_budget_exceeded");
    }

    [Fact]
    public async Task A_stop_set_after_the_claim_commits_is_prospective_and_leaves_the_claimed_attempt()
    {
        await using var seedContext = _fixture.CreateContext();
        var seed = await SeedAsync(seedContext);
        await AddClaudeHistoryAsync(seed, TokenStopTestSupport.ClaudeUsage(500, 50, 5, 60));
        await using var context = _fixture.CreateContext();
        var created = Assert.IsType<CreateReviewCorrectionAttemptCommandResult.AttemptCreated>(
            (await Handler(context).HandleAsync(Command(seed), CancellationToken.None)).Value);

        await TokenStopTestSupport.SetStopAsync(_fixture, seed.Run.Id, AgentProvider.ClaudeCode, 1);

        await using var verify = _fixture.CreateContext();
        Assert.Equal(AttemptStatus.Running, (await verify.Attempts.AsNoTracking().SingleAsync(a => a.Id == created.AttemptId)).Status);
    }
}
