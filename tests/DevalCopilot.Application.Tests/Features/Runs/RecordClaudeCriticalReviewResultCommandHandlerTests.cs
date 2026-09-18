using System.Text.Json;
using DevalCopilot.Application.Features.Runs.Commands.RecordClaudeCriticalReviewResult;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed class RecordClaudeCriticalReviewResultCommandHandlerTests(SqliteDatabaseFixture fixture)
    : IClassFixture<SqliteDatabaseFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);
    private static readonly IReadOnlyList<SealedCriticalReviewArtifact> NoArtifacts = [];

    private static ValidatedCriticalReview AcceptanceReview(string summary = "The proposal is sound.") =>
        ValidatedCriticalReview.ForAcceptance(new ValidatedAcceptance(summary, JsonSerializer.Serialize(new { rationale = "Matches the stated risks." })));

    private static ValidatedCriticalReview ChallengesReview(int count, string challengeSetSummary = "The proposal has material gaps.")
    {
        var challenges = Enumerable.Range(1, count)
            .Select(index => new ValidatedChallenge(
                $"Challenge {index} summary",
                JsonSerializer.Serialize(new
                {
                    disputedItem = $"Disputed item {index}",
                    materialImpact = $"Material impact {index}",
                    reasoning = $"Reasoning {index}",
                    alternativeOrQuestion = $"Alternative or question {index}",
                })))
            .ToList();
        return ValidatedCriticalReview.ForChallenges(challenges, challengeSetSummary);
    }

    private static (Project Project, Run Run, Attempt Attempt) CreateClaimedCriticalReviewAttempt(string? checkpointFingerprint = null)
    {
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Review the next increment", Now);
        run.Claim(Now);
        var attempt = Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), run.Id, 1, Guid.NewGuid(), Guid.NewGuid(), checkpointFingerprint ?? Fingerprint,
            Guid.NewGuid(), Guid.NewGuid(), TimeSpan.FromMinutes(10), 262144, 524288, Now);
        return (project, run, attempt);
    }

    [Fact]
    public async Task HandleAsync_records_exactly_one_acceptance_message_replying_to_the_reviewed_proposal()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt) = CreateClaimedCriticalReviewAttempt();
        attempt.MarkAgentDispatched(Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var review = AcceptanceReview("The proposal correctly scopes the ledger change.");
        var handler = new RecordClaudeCriticalReviewResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1)));
        var result = await handler.HandleAsync(
            new RecordClaudeCriticalReviewResultCommand(run.Id, attempt.Id, AgentOutcome.Accepted, Fingerprint, NoArtifacts, review, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AttemptStatus.Completed, result.Value.Status);
        Assert.Equal(AttemptStatus.Completed, attempt.Status);
        Assert.Equal(AgentOutcome.Accepted, attempt.AgentOutcome);

        var message = Assert.Single(dbContext.CollaborationMessages.Where(m => m.RunId == run.Id));
        Assert.Equal(attempt.Id, message.AttemptId);
        Assert.Equal(ParticipantKind.Claude, message.Actor);
        Assert.Equal(ParticipantKind.Codex, message.Recipient);
        Assert.Equal(CollaborationMessageType.Acceptance, message.Type);
        Assert.Equal(CollaborationMessage.ProtocolVersionOne, message.ProtocolVersion);
        Assert.Equal(CollaborationMessageProvenance.ProviderObserved, message.Provenance);
        Assert.Equal("The proposal correctly scopes the ledger change.", message.Summary);
        Assert.Equal(review.Acceptance!.StructuredContentJson, message.StructuredContentJson);
        Assert.Equal(attempt.AgentInputCollaborationMessageId, message.InReplyToMessageId);
        Assert.NotEqual(Guid.Empty, message.Id);

        var journalEvent = Assert.Single(dbContext.Events.Where(e => e.AttemptId == attempt.Id));
        Assert.Equal(RunEventType.CollaborationMessageRecorded, journalEvent.EventType);
        Assert.Equal(result.Value.LatestEventSequence, journalEvent.Sequence);
        Assert.Contains(message.Id.ToString(), journalEvent.PayloadJson);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public async Task HandleAsync_records_one_challenge_message_per_challenge_all_replying_to_the_same_reviewed_proposal(int challengeCount)
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt) = CreateClaimedCriticalReviewAttempt();
        attempt.MarkAgentDispatched(Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var review = ChallengesReview(challengeCount);
        var handler = new RecordClaudeCriticalReviewResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1)));
        var result = await handler.HandleAsync(
            new RecordClaudeCriticalReviewResultCommand(run.Id, attempt.Id, AgentOutcome.Challenged, Fingerprint, NoArtifacts, review, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AttemptStatus.Completed, attempt.Status);
        Assert.Equal(AgentOutcome.Challenged, attempt.AgentOutcome);

        var messages = dbContext.CollaborationMessages.Where(m => m.RunId == run.Id).OrderBy(m => m.Sequence).ToList();
        Assert.Equal(challengeCount, messages.Count);
        Assert.All(messages, message =>
        {
            Assert.Equal(attempt.Id, message.AttemptId);
            Assert.Equal(ParticipantKind.Claude, message.Actor);
            Assert.Equal(ParticipantKind.Codex, message.Recipient);
            Assert.Equal(CollaborationMessageType.Challenge, message.Type);
            Assert.Equal(attempt.AgentInputCollaborationMessageId, message.InReplyToMessageId);
            Assert.Equal(CollaborationMessageProvenance.ProviderObserved, message.Provenance);
        });

        var journalEvents = dbContext.Events.Where(e => e.AttemptId == attempt.Id).OrderBy(e => e.Sequence).ToList();
        Assert.Equal(challengeCount, journalEvents.Count);
        Assert.All(journalEvents, journalEvent => Assert.Equal(RunEventType.CollaborationMessageRecorded, journalEvent.EventType));
        Assert.Equal(journalEvents[^1].Sequence, result.Value.LatestEventSequence);
    }

    [Fact]
    public async Task HandleAsync_records_all_challenge_messages_and_events_atomically_in_a_single_save()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt) = CreateClaimedCriticalReviewAttempt();
        attempt.MarkAgentDispatched(Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var review = ChallengesReview(3);
        var handler = new RecordClaudeCriticalReviewResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1)));
        var result = await handler.HandleAsync(
            new RecordClaudeCriticalReviewResultCommand(run.Id, attempt.Id, AgentOutcome.Challenged, Fingerprint, NoArtifacts, review, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        // Re-read from a fresh context to confirm everything actually reached durable storage
        // together — never a partial write followed by a later failed save.
        await using var freshContext = fixture.CreateContext();
        Assert.Equal(3, freshContext.CollaborationMessages.Count(m => m.RunId == run.Id));
        Assert.Equal(3, freshContext.Events.Count(e => e.AttemptId == attempt.Id));
        Assert.Equal(AttemptStatus.Completed, freshContext.Attempts.Single(a => a.Id == attempt.Id).Status);
    }

    /// <summary>
    /// The closed caller-selectable policy for a Claude critical-review attempt:
    /// <see cref="AgentOutcome.SourceChanged"/> and <see cref="AgentOutcome.WorkspaceNoLongerEligible"/>
    /// belong exclusively to their own dedicated pre-dispatch commands;
    /// <see cref="AgentOutcome.InputAlreadyReviewed"/> belongs exclusively to its own dedicated
    /// command, which independently re-verifies the competing review before ever recording it;
    /// and <see cref="AgentOutcome.Proposed"/> belongs exclusively to the Codex planning contract.
    /// Every one of them must be rejected with a safe, stable error and zero mutation — never
    /// allowed to reach <c>Attempt.CompleteAgent</c>, whose own contract check would otherwise be
    /// the only thing standing between a cross-contract outcome and an unhandled exception
    /// escaping this handler (this test previously documented exactly that gap for
    /// <see cref="AgentOutcome.Proposed"/>, before the closed allowlist below was added).
    /// </summary>
    [Theory]
    [InlineData(AgentOutcome.SourceChanged)]
    [InlineData(AgentOutcome.WorkspaceNoLongerEligible)]
    [InlineData(AgentOutcome.InputAlreadyReviewed)]
    [InlineData(AgentOutcome.Proposed)]
    public async Task HandleAsync_rejects_every_non_caller_selectable_outcome_without_mutating_the_attempt(AgentOutcome outcome)
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt) = CreateClaimedCriticalReviewAttempt();
        attempt.MarkAgentDispatched(Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordClaudeCriticalReviewResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1)));
        var result = await handler.HandleAsync(
            new RecordClaudeCriticalReviewResultCommand(run.Id, attempt.Id, outcome, Fingerprint, NoArtifacts, null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.outcome_not_caller_selectable", Assert.Single(result.Errors).Code);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Null(attempt.AgentOutcome);
        Assert.Empty(dbContext.CollaborationMessages.Where(m => m.RunId == run.Id));
        Assert.Empty(dbContext.Events.Where(e => e.AttemptId == attempt.Id));
        Assert.Empty(dbContext.Artifacts.Where(a => a.AttemptId == attempt.Id));
    }

    [Fact]
    public async Task HandleAsync_rejects_an_undefined_outcome_without_mutating_the_attempt()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt) = CreateClaimedCriticalReviewAttempt();
        attempt.MarkAgentDispatched(Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordClaudeCriticalReviewResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1)));
        var result = await handler.HandleAsync(
            new RecordClaudeCriticalReviewResultCommand(run.Id, attempt.Id, (AgentOutcome)999, Fingerprint, NoArtifacts, null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.invalid_outcome", Assert.Single(result.Errors).Code);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Null(attempt.AgentOutcome);
        Assert.Empty(dbContext.CollaborationMessages.Where(m => m.RunId == run.Id));
        Assert.Empty(dbContext.Events.Where(e => e.AttemptId == attempt.Id));
        Assert.Empty(dbContext.Artifacts.Where(a => a.AttemptId == attempt.Id));
    }

    [Fact]
    public async Task HandleAsync_rejects_a_proposed_outcome_that_carries_a_review()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt) = CreateClaimedCriticalReviewAttempt();
        attempt.MarkAgentDispatched(Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var review = AcceptanceReview();
        var handler = new RecordClaudeCriticalReviewResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1)));
        var result = await handler.HandleAsync(
            new RecordClaudeCriticalReviewResultCommand(run.Id, attempt.Id, AgentOutcome.ProviderInvocationFailed, null, NoArtifacts, review, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.review_requires_success_outcome", Assert.Single(result.Errors).Code);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Null(attempt.AgentOutcome);
        Assert.Empty(dbContext.CollaborationMessages.Where(m => m.RunId == run.Id));
        Assert.Empty(dbContext.Events.Where(e => e.AttemptId == attempt.Id));
    }

    [Fact]
    public async Task HandleAsync_records_a_provider_invocation_failure_without_appending_a_collaboration_message()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt) = CreateClaimedCriticalReviewAttempt();
        attempt.MarkAgentDispatched(Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordClaudeCriticalReviewResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1)));
        var result = await handler.HandleAsync(
            new RecordClaudeCriticalReviewResultCommand(run.Id, attempt.Id, AgentOutcome.ProviderInvocationFailed, null, NoArtifacts, null, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AttemptStatus.Failed, attempt.Status);
        Assert.Equal(AgentOutcome.ProviderInvocationFailed, attempt.AgentOutcome);
        Assert.Empty(dbContext.CollaborationMessages.Where(m => m.RunId == run.Id));

        var journalEvent = Assert.Single(dbContext.Events.Where(e => e.AttemptId == attempt.Id));
        Assert.Equal(RunEventType.AgentAttemptCompleted, journalEvent.EventType);
        Assert.Contains("ProviderInvocationFailed", journalEvent.PayloadJson);
    }

    [Fact]
    public async Task HandleAsync_overrides_to_source_changed_and_appends_no_review_when_fresh_evidence_no_longer_matches_the_claimed_checkpoint()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt) = CreateClaimedCriticalReviewAttempt(checkpointFingerprint: Fingerprint);
        attempt.MarkAgentDispatched(Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var review = AcceptanceReview("Should never be recorded.");
        var handler = new RecordClaudeCriticalReviewResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1)));
        var result = await handler.HandleAsync(
            new RecordClaudeCriticalReviewResultCommand(
                run.Id, attempt.Id, AgentOutcome.Accepted, new string('b', 64), NoArtifacts, review, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AttemptStatus.Failed, attempt.Status);
        Assert.Equal(AgentOutcome.SourceChanged, attempt.AgentOutcome);
        Assert.Empty(dbContext.CollaborationMessages.Where(m => m.RunId == run.Id));

        var journalEvent = Assert.Single(dbContext.Events.Where(e => e.AttemptId == attempt.Id));
        Assert.Equal(RunEventType.AgentAttemptCompleted, journalEvent.EventType);
        Assert.Contains("SourceChanged", journalEvent.PayloadJson);
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_run_does_not_exist()
    {
        await using var dbContext = fixture.CreateContext();

        var handler = new RecordClaudeCriticalReviewResultCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new RecordClaudeCriticalReviewResultCommand(
                Guid.NewGuid(), Guid.NewGuid(), AgentOutcome.ProviderInvocationFailed, null, NoArtifacts, null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("runs.not_found", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_and_does_not_mutate_a_codex_planning_attempt()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Plan the next increment", Now);
        run.Claim(Now);
        var attempt = Attempt.ClaimAgent(
            Guid.NewGuid(), run.Id, 1, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now);
        attempt.MarkAgentDispatched(Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordClaudeCriticalReviewResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1)));
        var result = await handler.HandleAsync(
            new RecordClaudeCriticalReviewResultCommand(
                run.Id, attempt.Id, AgentOutcome.Accepted, Fingerprint, NoArtifacts, AcceptanceReview(), null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.not_critical_review", Assert.Single(result.Errors).Code);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
    }

    [Fact]
    public async Task HandleAsync_fails_and_does_not_mutate_an_already_terminal_attempt()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt) = CreateClaimedCriticalReviewAttempt();
        attempt.CompleteAgent(AgentOutcome.ProviderInvocationFailed, null, Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordClaudeCriticalReviewResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1)));
        var result = await handler.HandleAsync(
            new RecordClaudeCriticalReviewResultCommand(run.Id, attempt.Id, AgentOutcome.Accepted, Fingerprint, NoArtifacts, AcceptanceReview(), null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.not_active", Assert.Single(result.Errors).Code);
        Assert.Equal(AgentOutcome.ProviderInvocationFailed, attempt.AgentOutcome);
    }

    [Fact]
    public async Task HandleAsync_rejects_a_result_for_an_attempt_that_was_never_dispatched()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt) = CreateClaimedCriticalReviewAttempt();
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordClaudeCriticalReviewResultCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new RecordClaudeCriticalReviewResultCommand(run.Id, attempt.Id, AgentOutcome.ProviderInvocationFailed, null, NoArtifacts, null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.not_dispatched", Assert.Single(result.Errors).Code);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
    }

    [Fact]
    public async Task HandleAsync_rejects_an_accepted_outcome_without_a_completion_fingerprint()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt) = CreateClaimedCriticalReviewAttempt();
        attempt.MarkAgentDispatched(Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordClaudeCriticalReviewResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1)));
        var result = await handler.HandleAsync(
            new RecordClaudeCriticalReviewResultCommand(run.Id, attempt.Id, AgentOutcome.Accepted, null, NoArtifacts, AcceptanceReview(), null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.review_requires_completion_fingerprint", Assert.Single(result.Errors).Code);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Null(attempt.AgentOutcome);
    }

    [Fact]
    public async Task HandleAsync_rejects_an_accepted_outcome_without_a_review()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt) = CreateClaimedCriticalReviewAttempt();
        attempt.MarkAgentDispatched(Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordClaudeCriticalReviewResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1)));
        var result = await handler.HandleAsync(
            new RecordClaudeCriticalReviewResultCommand(run.Id, attempt.Id, AgentOutcome.Accepted, Fingerprint, NoArtifacts, null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.review_requires_validated_review", Assert.Single(result.Errors).Code);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Null(attempt.AgentOutcome);
    }

    [Fact]
    public async Task HandleAsync_rejects_an_accepted_outcome_that_carries_a_challenge_set()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt) = CreateClaimedCriticalReviewAttempt();
        attempt.MarkAgentDispatched(Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var review = ChallengesReview(1);
        var handler = new RecordClaudeCriticalReviewResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1)));
        var result = await handler.HandleAsync(
            new RecordClaudeCriticalReviewResultCommand(run.Id, attempt.Id, AgentOutcome.Accepted, Fingerprint, NoArtifacts, review, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.invalid_review_shape", Assert.Single(result.Errors).Code);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Null(attempt.AgentOutcome);
        Assert.Empty(dbContext.CollaborationMessages.Where(m => m.RunId == run.Id));
    }

    [Fact]
    public async Task HandleAsync_rejects_a_challenged_outcome_that_carries_an_acceptance()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt) = CreateClaimedCriticalReviewAttempt();
        attempt.MarkAgentDispatched(Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var review = AcceptanceReview();
        var handler = new RecordClaudeCriticalReviewResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1)));
        var result = await handler.HandleAsync(
            new RecordClaudeCriticalReviewResultCommand(run.Id, attempt.Id, AgentOutcome.Challenged, Fingerprint, NoArtifacts, review, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.invalid_review_shape", Assert.Single(result.Errors).Code);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Null(attempt.AgentOutcome);
        Assert.Empty(dbContext.CollaborationMessages.Where(m => m.RunId == run.Id));
    }

    [Fact]
    public async Task HandleAsync_rejects_an_accepted_outcome_with_an_unsafe_summary_without_mutating_the_attempt()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt) = CreateClaimedCriticalReviewAttempt();
        attempt.MarkAgentDispatched(Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var review = AcceptanceReview("Uses a hardcoded password for the ledger.");
        var handler = new RecordClaudeCriticalReviewResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1)));
        var result = await handler.HandleAsync(
            new RecordClaudeCriticalReviewResultCommand(run.Id, attempt.Id, AgentOutcome.Accepted, Fingerprint, NoArtifacts, review, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.invalid_review_content", Assert.Single(result.Errors).Code);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Null(attempt.AgentOutcome);
        Assert.Empty(dbContext.CollaborationMessages.Where(m => m.RunId == run.Id));
        Assert.Empty(dbContext.Events.Where(e => e.AttemptId == attempt.Id));
    }

    [Fact]
    public async Task HandleAsync_records_sealed_artifacts_with_the_correct_media_type_per_purpose()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt) = CreateClaimedCriticalReviewAttempt();
        attempt.MarkAgentDispatched(Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var sealedArtifacts = new SealedCriticalReviewArtifact[]
        {
            new(ArtifactPurpose.AgentStandardOutput, @"runs\r\attempts\a\stdout.sealed", 12, "sha256:abc", false),
            new(ArtifactPurpose.AgentStandardError, @"runs\r\attempts\a\stderr.sealed", 0, "sha256:def", true),
            new(ArtifactPurpose.AgentFinalResponse, @"runs\r\attempts\a\final.sealed", 34, "sha256:ghi", false),
        };

        var handler = new RecordClaudeCriticalReviewResultCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new RecordClaudeCriticalReviewResultCommand(
                run.Id, attempt.Id, AgentOutcome.ProviderInvocationFailed, null, sealedArtifacts, null, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var persisted = dbContext.Artifacts.Where(a => a.AttemptId == attempt.Id).ToList();
        Assert.Equal(3, persisted.Count);
        Assert.Equal("text/plain; charset=utf-8", persisted.Single(a => a.Purpose == ArtifactPurpose.AgentStandardOutput).MediaType);
        Assert.Equal("text/plain; charset=utf-8", persisted.Single(a => a.Purpose == ArtifactPurpose.AgentStandardError).MediaType);
        Assert.Equal("application/json", persisted.Single(a => a.Purpose == ArtifactPurpose.AgentFinalResponse).MediaType);
    }

    [Fact]
    public async Task HandleAsync_records_the_provider_session_id_when_present()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt) = CreateClaimedCriticalReviewAttempt();
        attempt.MarkAgentDispatched(Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordClaudeCriticalReviewResultCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new RecordClaudeCriticalReviewResultCommand(
                run.Id, attempt.Id, AgentOutcome.Accepted, Fingerprint, NoArtifacts, AcceptanceReview(), "session-abc"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("session-abc", attempt.AgentProviderSessionId);
    }

    [Fact]
    public async Task HandleAsync_rejects_an_overlong_provider_session_id()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt) = CreateClaimedCriticalReviewAttempt();
        attempt.MarkAgentDispatched(Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var overlongSessionId = new string('s', 257);
        var handler = new RecordClaudeCriticalReviewResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1)));
        var result = await handler.HandleAsync(
            new RecordClaudeCriticalReviewResultCommand(
                run.Id, attempt.Id, AgentOutcome.ProviderInvocationFailed, null, NoArtifacts, null, overlongSessionId),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.provider_session_id_too_long", Assert.Single(result.Errors).Code);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Null(attempt.AgentProviderSessionId);
    }
}
