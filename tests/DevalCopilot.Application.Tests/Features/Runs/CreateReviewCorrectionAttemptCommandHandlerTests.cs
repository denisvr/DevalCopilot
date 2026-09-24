using System.Security.Cryptography;
using System.Text.Json;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Commands.CreateReviewCorrectionAttempt;
using DevalCopilot.Application.Features.Runs.Commands.AuthorizeReviewCorrection;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed class CreateReviewCorrectionAttemptCommandHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 14, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);
    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public void Correction_result_variants_reject_fabricated_identities()
    {
        Assert.Throws<ArgumentException>(() => new CreateReviewCorrectionAttemptCommandResult.AttemptCreated(Guid.Empty, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CreateReviewCorrectionAttemptCommandResult.AttemptCreated(Guid.NewGuid(), 0));
        Assert.Throws<ArgumentException>(() => new CreateReviewCorrectionAttemptCommandResult.Escalated(Guid.Empty, Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => new CreateReviewCorrectionAttemptCommandResult.Escalated(Guid.NewGuid(), Guid.Empty));
    }

    [Fact]
    public async Task Unknown_run_is_not_found()
    {
        await using var context = _fixture.CreateContext();
        var result = await Handler(context).HandleAsync(new CreateReviewCorrectionAttemptCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);
        AssertCode(result, "runs.not_found");
    }

    [Fact]
    public async Task Non_running_run_is_rejected_before_other_eligibility_checks()
    {
        await using var context = _fixture.CreateContext();
        var seed = await SeedAsync(context, claimRun: false);
        var result = await Handler(context).HandleAsync(Command(seed), CancellationToken.None);
        AssertCode(result, "runs.not_running");
    }

    [Fact]
    public async Task Any_running_attempt_blocks_the_claim()
    {
        await using var context = _fixture.CreateContext();
        var seed = await SeedAsync(context, runningAttempt: true);
        var result = await new CreateReviewCorrectionAttemptCommandHandler(context, new RecordingEvidenceReader(seed.Evidence), new TestArtifactStore(), new FixedTimeProvider(Now))
            .HandleAsync(Command(seed), CancellationToken.None);
        AssertCode(result, "attempts.run_has_active_attempt");
    }

    [Fact]
    public async Task Run_wide_budget_exhaustion_is_checked_before_the_review_correction_specific_budget_and_never_consumes_an_authorization()
    {
        await using var context = _fixture.CreateContext();
        // The seeded Planner/CriticalReviewer/Implementer/CodeReviewer chain already occupies
        // all four of the run's budget slots — the review-correction-specific budget (default 2)
        // is nowhere near exhausted, proving the run-wide check runs first and independently.
        var seed = await SeedAsync(context, maximumAgentAttempts: 4);
        var handler = Handler(context);

        var result = await handler.HandleAsync(Command(seed), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.budget_exhausted", Assert.Single(result.Errors).Code);
        // Never consumes a review-correction authorization or creates any escalation/attempt.
        Assert.Empty(await context.ReviewCorrectionEscalations.ToListAsync());
        Assert.Empty(await context.ReviewCorrectionAuthorizations.ToListAsync());
        Assert.Equal(4, await context.Attempts.CountAsync(item => item.RunId == seed.Run.Id));
    }

    // Deterministically simulates a concurrent request winning the race for the exact
    // AgentBudgetSlot this handler independently computes, injected strictly between this
    // handler's own pre-check and its own final SaveChangesAsync. Below the maximum, losing this
    // race is a safe, retryable conflict — never misreported as budget exhaustion.
    [Fact]
    public async Task HandleAsync_classifies_a_persisted_competing_slot_below_the_maximum_as_a_safe_conflict()
    {
        await using var seedContext = _fixture.CreateContext();
        var seed = await SeedAsync(seedContext, maximumAgentAttempts: 8);

        await using var raceContext = _fixture.CreateContext();
        // The seeded Planner/CriticalReviewer/Implementer/CodeReviewer chain already occupies
        // slots 1-4, so slot 5 is the exact value this handler will independently compute for
        // itself.
        var evidence = new RecordingEvidenceReader(seed.Evidence, async cancellationToken =>
        {
            var competing = Attempt.ClaimAgentImplementation(
                Guid.NewGuid(), seed.Run.Id, 5, seed.Workspace.Id, seed.Checkpoint!.Id, Fingerprint, Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 262144, 524288, Now, agentBudgetSlot: 5);
            competing.Fail(Now);
            raceContext.Attempts.Add(competing);
            await raceContext.SaveChangesAsync(cancellationToken);
        });

        await using var handlerContext = _fixture.CreateContext();
        var result = await new CreateReviewCorrectionAttemptCommandHandler(handlerContext, evidence, new TestArtifactStore(), new FixedTimeProvider(Now))
            .HandleAsync(Command(seed), CancellationToken.None);

        AssertCode(result, "agent_attempts.budget_slot_conflict");

        await using var verify = _fixture.CreateContext();
        Assert.Equal(5, await verify.Attempts.CountAsync(item => item.RunId == seed.Run.Id));
    }

    // Companion to the fact above: this time the exact slot the handler computes is also the
    // run's last available slot, so losing the race genuinely does mean the budget is now
    // exhausted.
    [Fact]
    public async Task HandleAsync_classifies_a_persisted_competing_slot_at_the_maximum_as_exhausted()
    {
        await using var seedContext = _fixture.CreateContext();
        var seed = await SeedAsync(seedContext, maximumAgentAttempts: 5);

        await using var raceContext = _fixture.CreateContext();
        var evidence = new RecordingEvidenceReader(seed.Evidence, async cancellationToken =>
        {
            var competing = Attempt.ClaimAgentImplementation(
                Guid.NewGuid(), seed.Run.Id, 5, seed.Workspace.Id, seed.Checkpoint!.Id, Fingerprint, Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 262144, 524288, Now, agentBudgetSlot: 5);
            competing.Fail(Now);
            raceContext.Attempts.Add(competing);
            await raceContext.SaveChangesAsync(cancellationToken);
        });

        await using var handlerContext = _fixture.CreateContext();
        var result = await new CreateReviewCorrectionAttemptCommandHandler(handlerContext, evidence, new TestArtifactStore(), new FixedTimeProvider(Now))
            .HandleAsync(Command(seed), CancellationToken.None);

        AssertCode(result, "agent_attempts.budget_exhausted");

        await using var verify = _fixture.CreateContext();
        Assert.Equal(5, await verify.Attempts.CountAsync(item => item.RunId == seed.Run.Id));
    }

    [Theory]
    [InlineData(false, true, true, true, "agent_attempts.workspace_not_ready")]
    [InlineData(true, false, true, true, "agent_attempts.lease_not_active")]
    [InlineData(true, true, false, true, "agent_attempts.checkpoint_missing")]
    [InlineData(true, true, true, false, "agent_attempts.provider_not_observed")]
    public async Task Workspace_checkpoint_and_provider_eligibility_is_fail_closed(
        bool workspaceReady, bool leaseActive, bool checkpointExists, bool providerObserved, string code)
    {
        await using var context = _fixture.CreateContext();
        var seed = await SeedAsync(context, workspaceReady, leaseActive, checkpointExists, providerObserved);
        var result = await Handler(context).HandleAsync(Command(seed), CancellationToken.None);
        AssertCode(result, code);
    }

    [Fact]
    public async Task Source_drift_is_rejected_before_claim_persistence()
    {
        await using var context = _fixture.CreateContext();
        var seed = await SeedAsync(context, evidence: new GitWorkspaceEvidenceResult(
            GitWorkspaceEvidenceOutcome.Success, new string('b', 40), new string('b', 64), [new GitWorkspaceChangedPath("src/Changed.cs", null, " M", "")], "diff"));
        var result = await new CreateReviewCorrectionAttemptCommandHandler(context, new RecordingEvidenceReader(seed.Evidence), new TestArtifactStore(), new FixedTimeProvider(Now))
            .HandleAsync(Command(seed), CancellationToken.None);
        Assert.True(result.IsFailure, string.Join("; ", result.Errors.Select(error => error.Code)));
        Assert.Equal("agent_attempts.checkpoint_not_current", Assert.Single(result.Errors).Code);
        Assert.Empty(await context.Attempts.Where(item => item.AgentResponseContract == AgentResponseContract.ReviewCorrection).ToListAsync());
    }

    [Theory]
    [InlineData(false, false, "agent_attempts.review_not_applicable")]
    [InlineData(true, false, "agent_attempts.review_findings_invalid")]
    public async Task Review_identity_and_findings_are_validated(
        bool completedReview, bool includeFindings, string code)
    {
        await using var context = _fixture.CreateContext();
        var seed = await SeedAsync(context, completedReview: completedReview, includeFindings: includeFindings);
        var result = await Handler(context).HandleAsync(Command(seed), CancellationToken.None);
        AssertCode(result, code);
    }

    [Fact]
    public async Task Successful_claim_persists_exact_ordered_inputs_bounded_manifest_and_atomic_rows()
    {
        await using var context = _fixture.CreateContext();
        var seed = await SeedAsync(context);
        var evidenceReader = new RecordingEvidenceReader(seed.Evidence);
        var handler = new CreateReviewCorrectionAttemptCommandHandler(context, evidenceReader, new TestArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(Command(seed), CancellationToken.None);

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Code)));
        var created = Assert.IsType<CreateReviewCorrectionAttemptCommandResult.AttemptCreated>(result.Value);
        var attempt = await context.Attempts.SingleAsync(item => item.Id == created.AttemptId);
        Assert.Equal(AgentRole.Implementer, attempt.AgentRole);
        Assert.Equal(AgentResponseContract.ReviewCorrection, attempt.AgentResponseContract);
        Assert.Equal(AgentProvider.ClaudeCode, attempt.AgentProvider);
        Assert.Equal([seed.ExecutionReport.Id, .. seed.Findings.Select(item => item.Id)],
            await context.AttemptInputMessages.Where(item => item.AttemptId == attempt.Id).OrderBy(item => item.Sequence).Select(item => item.CollaborationMessageId).ToListAsync());
        var manifest = await context.Artifacts.SingleAsync(item => item.AttemptId == attempt.Id && item.Purpose == ArtifactPurpose.AgentContextManifest);
        Assert.Equal(ArtifactCaptureOutcome.Captured, manifest.CaptureOutcome);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
    }

    [Fact]
    public async Task Third_claim_creates_one_durable_escalation_without_attempt_or_manifest()
    {
        await using var context = _fixture.CreateContext();
        var seed = await SeedAsync(context);
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
        var handler = Handler(context);

        var result = await handler.HandleAsync(Command(seed), CancellationToken.None);
        var repeated = await handler.HandleAsync(Command(seed), CancellationToken.None);

        var escalated = Assert.IsType<CreateReviewCorrectionAttemptCommandResult.Escalated>(result.Value);
        var repeatedEscalated = Assert.IsType<CreateReviewCorrectionAttemptCommandResult.Escalated>(repeated.Value);
        Assert.Equal(escalated, repeatedEscalated);
        Assert.Equal(2, await context.Attempts.CountAsync(item => item.AgentResponseContract == AgentResponseContract.ReviewCorrection));
        Assert.Single(await context.ReviewCorrectionEscalations.ToListAsync());
        Assert.Single(await context.CollaborationMessages.Where(item => item.Type == CollaborationMessageType.Escalation).ToListAsync());
        Assert.Empty(await context.Artifacts.Where(item => item.Purpose == ArtifactPurpose.AgentContextManifest).ToListAsync());
    }

    [Fact]
    public async Task Escalation_retry_recovers_exact_committed_event_and_re_notifies_after_post_commit_failure()
    {
        await using var seedContext = _fixture.CreateContext();
        var seed = await SeedAsync(seedContext);
        for (var number = 5; number <= 6; number++)
        {
            var priorCorrection = Attempt.ClaimAgentReviewCorrection(
                Guid.NewGuid(), seed.Run.Id, number, seed.Workspace.Id, seed.Checkpoint!.Id, Fingerprint, Guid.NewGuid(),
                TimeSpan.FromMinutes(20), 262144, 524288, Now, number);
            priorCorrection.MarkAgentDispatched(Now);
            priorCorrection.CompleteAgent(AgentOutcome.ProviderInvocationFailed, null, Now);
            seedContext.Attempts.Add(priorCorrection);
        }

        await seedContext.SaveChangesAsync(CancellationToken.None);
        var throwingNotifier = new RecordingNotifier(throwOnFirstCall: true);
        await using (var firstContext = _fixture.CreateContext())
        {
            var handler = new CreateReviewCorrectionAttemptCommandHandler(
                firstContext, new RecordingEvidenceReader(seed.Evidence), new TestArtifactStore(), new FixedTimeProvider(Now), throwingNotifier);
            await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(Command(seed), CancellationToken.None));
        }

        var expected = await ReadEscalationEventAsync(seed.Run.Id);
        var retryNotifier = new RecordingNotifier();
        CreateReviewCorrectionAttemptCommandResult.Escalated retry;
        await using (var retryContext = _fixture.CreateContext())
        {
            var handler = new CreateReviewCorrectionAttemptCommandHandler(
                retryContext, new RecordingEvidenceReader(seed.Evidence), new TestArtifactStore(), new FixedTimeProvider(Now), retryNotifier);
            retry = Assert.IsType<CreateReviewCorrectionAttemptCommandResult.Escalated>(
                (await handler.HandleAsync(Command(seed), CancellationToken.None)).Value);
        }

        Assert.Equal(expected.EscalationId, retry.EscalationId);
        Assert.Equal(expected.MessageId, retry.EscalationMessageId);
        Assert.Equal(expected.Sequence, retry.LatestEventSequence);
        Assert.Equal([(seed.Run.Id, expected.Sequence)], retryNotifier.Notifications);
        await using var verify = _fixture.CreateContext();
        Assert.Single(await verify.ReviewCorrectionEscalations.Where(item => item.RunId == seed.Run.Id).ToListAsync());
        Assert.Single(await verify.CollaborationMessages.Where(item => item.RunId == seed.Run.Id && item.Type == CollaborationMessageType.Escalation).ToListAsync());
        Assert.Single(await verify.Events.Where(item => item.RunId == seed.Run.Id && item.EventType == RunEventType.CollaborationMessageRecorded).ToListAsync());
    }

    [Fact]
    public async Task One_human_authorization_allows_exactly_one_additional_claim()
    {
        await using var context = _fixture.CreateContext();
        var seed = await SeedAsync(context);
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
        var correctionHandler = Handler(context);
        var escalationResult = await correctionHandler.HandleAsync(Command(seed), CancellationToken.None);
        var escalation = Assert.IsType<CreateReviewCorrectionAttemptCommandResult.Escalated>(escalationResult.Value);

        var authorizationResult = await new AuthorizeReviewCorrectionCommandHandler(
                context, new RecordingEvidenceReader(seed.Evidence), new FixedTimeProvider(Now.AddMinutes(1)))
            .HandleAsync(new AuthorizeReviewCorrectionCommand(seed.Run.Id, escalation.EscalationId), CancellationToken.None);
        Assert.True(authorizationResult.IsSuccess, string.Join("; ", authorizationResult.Errors.Select(error => error.Code)));

        var claimResult = await new CreateReviewCorrectionAttemptCommandHandler(
                context, new RecordingEvidenceReader(seed.Evidence), new TestArtifactStore(), new FixedTimeProvider(Now.AddMinutes(2)))
            .HandleAsync(Command(seed), CancellationToken.None);
        var created = Assert.IsType<CreateReviewCorrectionAttemptCommandResult.AttemptCreated>(claimResult.Value);
        var authorization = await context.ReviewCorrectionAuthorizations.SingleAsync();
        Assert.Equal(created.AttemptId, authorization.ConsumedByAttemptId);
        Assert.False(authorization.IsAvailable);
    }

    [Fact]
    public async Task Authorization_retry_recovers_exact_committed_event_and_re_notifies_after_post_commit_failure()
    {
        await using var seedContext = _fixture.CreateContext();
        var seed = await SeedAsync(seedContext);
        for (var number = 5; number <= 6; number++)
        {
            var priorCorrection = Attempt.ClaimAgentReviewCorrection(
                Guid.NewGuid(), seed.Run.Id, number, seed.Workspace.Id, seed.Checkpoint!.Id, Fingerprint, Guid.NewGuid(),
                TimeSpan.FromMinutes(20), 262144, 524288, Now, number);
            priorCorrection.MarkAgentDispatched(Now);
            priorCorrection.CompleteAgent(AgentOutcome.ProviderInvocationFailed, null, Now);
            seedContext.Attempts.Add(priorCorrection);
        }

        await seedContext.SaveChangesAsync(CancellationToken.None);
        CreateReviewCorrectionAttemptCommandResult.Escalated escalation;
        await using (var escalationContext = _fixture.CreateContext())
        {
            escalation = Assert.IsType<CreateReviewCorrectionAttemptCommandResult.Escalated>(
                (await new CreateReviewCorrectionAttemptCommandHandler(
                    escalationContext, new RecordingEvidenceReader(seed.Evidence), new TestArtifactStore(), new FixedTimeProvider(Now))
                    .HandleAsync(Command(seed), CancellationToken.None)).Value);
        }

        var throwingNotifier = new RecordingNotifier(throwOnFirstCall: true);
        await using (var firstContext = _fixture.CreateContext())
        {
            var handler = new AuthorizeReviewCorrectionCommandHandler(
                firstContext, new RecordingEvidenceReader(seed.Evidence), new FixedTimeProvider(Now.AddMinutes(1)), throwingNotifier);
            await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(
                new AuthorizeReviewCorrectionCommand(seed.Run.Id, escalation.EscalationId), CancellationToken.None));
        }

        var expected = await ReadAuthorizationEventAsync(seed.Run.Id);
        var retryNotifier = new RecordingNotifier();
        AuthorizeReviewCorrectionCommandResult retry;
        await using (var retryContext = _fixture.CreateContext())
        {
            var handler = new AuthorizeReviewCorrectionCommandHandler(
                retryContext, new RecordingEvidenceReader(seed.Evidence), new FixedTimeProvider(Now.AddMinutes(1)), retryNotifier);
            retry = (await handler.HandleAsync(
                new AuthorizeReviewCorrectionCommand(seed.Run.Id, escalation.EscalationId), CancellationToken.None)).Value;
        }

        Assert.Equal(expected.AuthorizationId, retry.AuthorizationId);
        Assert.Equal(expected.MessageId, retry.HumanInstructionMessageId);
        Assert.Equal(expected.Sequence, retry.LatestEventSequence);
        Assert.Equal([(seed.Run.Id, expected.Sequence)], retryNotifier.Notifications);
        await using var verify = _fixture.CreateContext();
        Assert.Single(await verify.ReviewCorrectionAuthorizations.Where(item => item.RunId == seed.Run.Id).ToListAsync());
        Assert.Single(await verify.CollaborationMessages.Where(item => item.RunId == seed.Run.Id && item.Type == CollaborationMessageType.HumanInstruction).ToListAsync());
        Assert.Equal(2, await verify.Events.CountAsync(item => item.RunId == seed.Run.Id && item.EventType == RunEventType.CollaborationMessageRecorded));
    }

    [Fact]
    public async Task Lost_running_attempt_race_returns_conflict_deletes_manifest_and_leaves_no_loser_rows()
    {
        await using var seedContext = _fixture.CreateContext();
        var seed = await SeedAsync(seedContext);
        await using var raceContext = _fixture.CreateContext();
        var store = new TestArtifactStore();
        var evidence = new RecordingEvidenceReader(seed.Evidence, async cancellationToken =>
        {
            raceContext.Attempts.Add(Attempt.Claim(Guid.NewGuid(), seed.Run.Id, 99, Now));
            await raceContext.SaveChangesAsync(cancellationToken);
        });
        await using var handlerContext = _fixture.CreateContext();
        var result = await new CreateReviewCorrectionAttemptCommandHandler(handlerContext, evidence, store, new FixedTimeProvider(Now))
            .HandleAsync(Command(seed), CancellationToken.None);

        AssertCode(result, "attempts.run_has_active_attempt");
        Assert.Single(store.DeletedSealedFiles);
        await using var verify = _fixture.CreateContext();
        Assert.Single(await verify.Attempts.Where(item => item.RunId == seed.Run.Id && item.Status == AttemptStatus.Running).ToListAsync());
        Assert.Empty(await verify.Attempts.Where(item => item.RunId == seed.Run.Id && item.AgentResponseContract == AgentResponseContract.ReviewCorrection).ToListAsync());
        Assert.Empty(await verify.Artifacts.Where(item => item.RunId == seed.Run.Id).ToListAsync());
    }

    [Fact]
    public async Task Unrelated_persistence_failure_is_not_reported_as_a_race()
    {
        await using var context = _fixture.CreateContext();
        var seed = await SeedAsync(context);
        var interceptor = new ThrowOnFirstSaveInterceptor();
        await using var handlerContext = _fixture.CreateContext(interceptor);
        var result = await new CreateReviewCorrectionAttemptCommandHandler(handlerContext, new RecordingEvidenceReader(seed.Evidence), new TestArtifactStore(), new FixedTimeProvider(Now))
            .HandleAsync(Command(seed), CancellationToken.None);
        AssertCode(result, "attempts.persistence_failed");
    }

    private CreateReviewCorrectionAttemptCommandHandler Handler(DevalCopilotDbContext context) =>
        new(context, new RecordingEvidenceReader(new GitWorkspaceEvidenceResult(GitWorkspaceEvidenceOutcome.Success, new string('a', 40), Fingerprint, [], null)), new TestArtifactStore(), new FixedTimeProvider(Now));

    private static CreateReviewCorrectionAttemptCommand Command(Seed seed) => new(seed.Run.Id, seed.Review.Id);

    private static void AssertCode<T>(Devalente.Shared.Results.Result<T> result, string code) =>
        Assert.Equal(code, Assert.Single(result.Errors).Code);

    private static void AssertCode(Devalente.Shared.Results.Result result, string code) =>
        Assert.Equal(code, Assert.Single(result.Errors).Code);

    private async Task<Seed> SeedAsync(
        DevalCopilotDbContext context,
        bool workspaceReady = true,
        bool leaseActive = true,
        bool checkpointExists = true,
        bool providerObserved = true,
        bool claimRun = true,
        bool runningAttempt = false,
        bool completedReview = true,
        bool includeFindings = true,
        GitWorkspaceEvidenceResult? evidence = null,
        int maximumAgentAttempts = 16)
    {
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Correct the implementation", Now, maximumAgentAttempts: maximumAgentAttempts);
        if (claimRun) run.Claim(Now);
        var workspace = GitWorkspace.Prepare(Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", Now);
        if (workspaceReady) workspace.MarkReady();
        var lease = RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, project.Id.ToByteArray(), Now);
        if (!leaseActive) lease.Release(Now);
        GitCheckpoint? checkpoint = checkpointExists ? GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), Fingerprint, []) : null;

        context.Projects.Add(project);
        context.Runs.Add(run);
        context.GitWorkspaces.Add(workspace);
        context.RepositoryMutationLeases.Add(lease);
        if (checkpoint is not null) context.GitCheckpoints.Add(checkpoint);
        if (providerObserved)
        {
            var snapshot = HostCapabilitySnapshot.Seed(Capability.ClaudeCli, Now);
            snapshot.MarkDispatched(Now);
            snapshot.RecordSuccess(CapabilityLaunchKind.DirectExecutable, @"C:\safe\claude.exe", null, "1.0.0", Now, Now.AddMinutes(5));
            context.HostCapabilitySnapshots.Add(snapshot);
        }
        if (runningAttempt && checkpoint is not null)
        {
            context.Attempts.Add(Attempt.Claim(Guid.NewGuid(), run.Id, 99, Now));
        }

        var planningAttempt = Attempt.ClaimAgent(
            Guid.NewGuid(), run.Id, 1, workspace.Id, checkpoint?.Id ?? Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(20), 262144, 524288, Now, 1);
        planningAttempt.MarkAgentDispatched(Now);
        planningAttempt.CompleteAgent(AgentOutcome.Proposed, Fingerprint, Now, processEvidence: TestProcessEvidence.CleanExit);
        context.Attempts.Add(planningAttempt);

        var proposal = CollaborationMessage.Record(
            Guid.NewGuid(), run.Id, planningAttempt.Id, CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForAgent(AgentRole.Planner, AgentProvider.Codex),
            ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), CollaborationMessageType.Proposal, null,
            "Implement the requested correction.", JsonSerializer.Serialize(new { scope = "Correction", implementationSteps = "Apply the review findings.", risks = "None known.", verificationPlan = "Run tests.", escalationPoints = "None." }),
            CollaborationMessageProvenance.ProviderObserved, Now);
        context.CollaborationMessages.Add(proposal);
        var report = CollaborationMessage.Record(
            Guid.NewGuid(), run.Id, null, CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForAgent(AgentRole.Implementer, AgentProvider.ClaudeCode),
            ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex), CollaborationMessageType.ExecutionReport, proposal.Id,
            "Implemented the requested correction.", JsonSerializer.Serialize(new { completedWork = "Updated the implementation.", verification = "Tests passed." }),
            CollaborationMessageProvenance.ProviderObserved, Now);
        var findings = new List<CollaborationMessage>();
        var acceptanceAttempt = Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), run.Id, 2, workspace.Id, checkpoint?.Id ?? Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(20), 262144, 524288, Now, 2);
        acceptanceAttempt.MarkAgentDispatched(Now);
        acceptanceAttempt.CompleteAgent(AgentOutcome.Accepted, Fingerprint, Now, processEvidence: TestProcessEvidence.CleanExit);
        context.Attempts.Add(acceptanceAttempt);
        context.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), acceptanceAttempt.Id, proposal.Id, 0));
        var acceptance = CollaborationMessage.Record(
            Guid.NewGuid(), run.Id, acceptanceAttempt.Id, CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForAgent(AgentRole.CriticalReviewer, AgentProvider.ClaudeCode),
            ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex), CollaborationMessageType.Acceptance, proposal.Id,
            "Accepted the implementation plan.", JsonSerializer.Serialize(new { rationale = "The plan is complete." }),
            CollaborationMessageProvenance.ProviderObserved, Now);
        context.CollaborationMessages.Add(acceptance);

        var review = Attempt.ClaimAgentCodeReview(Guid.NewGuid(), run.Id, 4, workspace.Id, checkpoint?.Id ?? Guid.NewGuid(), Fingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(20), 262144, 524288, Now, 3);
        if (completedReview) { review.MarkAgentDispatched(Now); review.CompleteAgent(AgentOutcome.ReviewChangesRequested, Fingerprint, Now, processEvidence: TestProcessEvidence.CleanExit); }
        else { review.CompleteAgent(AgentOutcome.ProviderInvocationFailed, null, Now); }
        if (checkpoint is not null)
        {
            var implementation = Attempt.ClaimAgentImplementation(Guid.NewGuid(), run.Id, 3, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(20), 262144, 524288, Now, 4);
            implementation.MarkAgentDispatched(Now);
            implementation.CompleteImplementation(AgentOutcome.Implemented, checkpoint.Id, Now, processEvidence: TestProcessEvidence.CleanExit);
            report = CollaborationMessage.Record(Guid.NewGuid(), run.Id, implementation.Id, CollaborationMessage.ProtocolVersionOne,
                ParticipantIdentity.ForAgent(AgentRole.Implementer, AgentProvider.ClaudeCode), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex), CollaborationMessageType.ExecutionReport, proposal.Id,
                "Implemented the requested correction.", JsonSerializer.Serialize(new { completedWork = "Updated the implementation.", verification = "Tests passed." }), CollaborationMessageProvenance.ProviderObserved, Now);
            context.Attempts.Add(implementation);
            context.AttemptInputMessages.AddRange(
                AttemptInputMessage.Record(Guid.NewGuid(), implementation.Id, proposal.Id, 0),
                AttemptInputMessage.Record(Guid.NewGuid(), implementation.Id, acceptance.Id, 1));
        }
        context.Attempts.Add(review);
        context.CollaborationMessages.Add(report);
        context.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), review.Id, report.Id, 0));
        if (includeFindings && checkpoint is not null)
        {
            for (var index = 0; index < 2; index++)
            {
                var finding = CollaborationMessage.Record(Guid.NewGuid(), run.Id, review.Id, CollaborationMessage.ProtocolVersionOne,
                    ParticipantIdentity.ForAgent(AgentRole.CodeReviewer, AgentProvider.Codex), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), CollaborationMessageType.ReviewFinding, report.Id,
                    $"Finding {index + 1}.", JsonSerializer.Serialize(new { severity = "high", category = "correctness", evidence = "The branch is incomplete.", requiredChange = "Complete the branch." }), CollaborationMessageProvenance.ProviderObserved, Now.AddSeconds(index + 1));
                findings.Add(finding);
                context.CollaborationMessages.Add(finding);
            }
        }
        await context.SaveChangesAsync(CancellationToken.None);

        return new Seed(project, run, workspace, checkpoint, review, report, findings, evidence ?? new GitWorkspaceEvidenceResult(GitWorkspaceEvidenceOutcome.Success, new string('a', 40), Fingerprint, [new GitWorkspaceChangedPath("src/Changed.cs", null, " M", "")], "diff"));
    }

    private sealed record Seed(Project Project, Run Run, GitWorkspace Workspace, GitCheckpoint? Checkpoint, Attempt Review, CollaborationMessage ExecutionReport, IReadOnlyList<CollaborationMessage> Findings, GitWorkspaceEvidenceResult Evidence);

    private sealed class RecordingEvidenceReader(GitWorkspaceEvidenceResult result, Func<CancellationToken, Task>? callback = null) : IGitWorkspaceEvidenceReader
    {
        public async Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken)
        {
            if (callback is not null) await callback(cancellationToken);
            return result;
        }
    }

    private sealed class TestArtifactStore : IArtifactStore
    {
        private static readonly string Root = Path.Combine(Path.GetTempPath(), "devalcopilot-review-correction-tests");
        public HashSet<(Guid RunId, Guid AttemptId, ArtifactPurpose Purpose)> DeletedSealedFiles { get; } = [];
        public string GetPartialPath(Guid runId, Guid attemptId, ArtifactPurpose purpose) => Path.Combine(Root, runId.ToString("N"), attemptId.ToString("N"), $"{purpose}.partial");
        public string GetSealedRelativePath(Guid runId, Guid attemptId, ArtifactPurpose purpose) => $"{runId:N}/{attemptId:N}/{purpose}.sealed";
        public Task<SealedOutputFile?> SealAsync(Guid runId, Guid attemptId, ArtifactPurpose purpose, CancellationToken cancellationToken) => Task.FromResult<SealedOutputFile?>(new SealedOutputFile(GetSealedRelativePath(runId, attemptId, purpose), 1, Convert.ToHexString(SHA256.HashData([1]))));
        public bool HasSealedFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) => false;
        public bool HasPartialFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) => false;
        public Task<SealedOutputFile?> DescribeSealedFileAsync(Guid runId, Guid attemptId, ArtifactPurpose purpose, CancellationToken cancellationToken) => Task.FromResult<SealedOutputFile?>(null);
        public void DeleteOrphanedPartialFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) { }
        public void DeleteOrphanedSealedFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) => DeletedSealedFiles.Add((runId, attemptId, purpose));
        public Task<PartialReadWindow> ReadPartialAsync(Guid runId, Guid attemptId, ArtifactPurpose purpose, long fromOffset, int maxBytes, CancellationToken cancellationToken) => Task.FromResult(new PartialReadWindow(string.Empty, fromOffset, 0));
        public Task<SealedReadWindow> VerifyAndReadSealedAsync(string relativeStoragePath, long expectedByteLength, string expectedContentHash, long fromOffset, int maxBytes, CancellationToken cancellationToken) => Task.FromResult(new SealedReadWindow(SealedReadStatus.Missing, string.Empty, fromOffset, 0));
    }

    private sealed class ThrowOnFirstSaveInterceptor : Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
    {
        private int _thrown;
        public override System.Threading.Tasks.ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(System.Data.Common.DbCommand command, Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData, Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("INSERT INTO", StringComparison.OrdinalIgnoreCase) && Interlocked.Exchange(ref _thrown, 1) == 0)
            {
                throw new DbUpdateException("synthetic unrelated persistence failure");
            }
            return ValueTask.FromResult(result);
        }
    }

    private async Task<(Guid EscalationId, Guid MessageId, long Sequence)> ReadEscalationEventAsync(Guid runId)
    {
        await using var context = _fixture.CreateContext();
        var escalation = await context.ReviewCorrectionEscalations.SingleAsync(item => item.RunId == runId);
        var sequence = await context.Events
            .Where(item => item.RunId == runId
                && item.EventType == RunEventType.CollaborationMessageRecorded
                && item.PayloadJson.Contains(escalation.CollaborationMessageId.ToString()))
            .Select(item => item.Sequence)
            .SingleAsync();
        return (escalation.Id, escalation.CollaborationMessageId, sequence);
    }

    private async Task<(Guid AuthorizationId, Guid MessageId, long Sequence)> ReadAuthorizationEventAsync(Guid runId)
    {
        await using var context = _fixture.CreateContext();
        var authorization = await context.ReviewCorrectionAuthorizations.SingleAsync(item => item.RunId == runId);
        var sequence = await context.Events
            .Where(item => item.RunId == runId
                && item.EventType == RunEventType.CollaborationMessageRecorded
                && item.PayloadJson.Contains(authorization.HumanInstructionMessageId.ToString()))
            .Select(item => item.Sequence)
            .SingleAsync();
        return (authorization.Id, authorization.HumanInstructionMessageId, sequence);
    }

    private sealed class RecordingNotifier(bool throwOnFirstCall = false) : IRunEventNotifier
    {
        private int _callCount;

        public List<(Guid RunId, long Sequence)> Notifications { get; } = [];

        public Task NotifyRunAdvancedAsync(Guid runId, long latestSequence, CancellationToken cancellationToken)
        {
            Notifications.Add((runId, latestSequence));
            if (throwOnFirstCall && Interlocked.Increment(ref _callCount) == 1)
            {
                throw new InvalidOperationException("Synthetic post-commit notification failure.");
            }

            return Task.CompletedTask;
        }
    }
}
