using System.Text.Json;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.RecordChallengeResolutionResult;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed class RecordChallengeResolutionResultCommandHandlerTests(SqliteDatabaseFixture fixture)
    : IClassFixture<SqliteDatabaseFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);
    private static readonly IReadOnlyList<SealedChallengeResolutionArtifact> NoArtifacts = [];

    private static ValidatedRevisedProposal RevisedProposal(string summary = "Revised proposal summary") => new(
        summary,
        JsonSerializer.Serialize(new
        {
            scope = "Revised scope",
            implementationSteps = "Revised steps",
            risks = "Revised risks",
            verificationPlan = "Revised verification",
            escalationPoints = "Revised escalation",
        }));

    private static ValidatedChallengeResolution Resolution(IReadOnlyList<Guid> challengeMessageIds, string summary = "Overall resolution summary")
    {
        var decisions = challengeMessageIds
            .Select((id, index) => new ValidatedDecision(
                id,
                $"Decision {index + 1} summary",
                JsonSerializer.Serialize(new
                {
                    resolution = ChallengeResolutionOutputSchema.AcceptedResolution,
                    rationale = $"Rationale {index + 1}",
                    resultingPlanChanges = $"Plan changes {index + 1}",
                    nextAction = $"Next action {index + 1}",
                })))
            .ToList();
        return ValidatedChallengeResolution.Create(summary, decisions, RevisedProposal());
    }

    private static (Project Project, Run Run, Attempt Attempt, AttemptInputMessage OriginalProposal, List<AttemptInputMessage> Challenges)
        CreateClaimedChallengeResolutionAttempt(int challengeCount = 2, string? checkpointFingerprint = null)
    {
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Review the next increment", Now);
        run.Claim(Now);
        var attempt = Attempt.ClaimAgentChallengeResolution(
            Guid.NewGuid(), run.Id, 1, Guid.NewGuid(), Guid.NewGuid(), checkpointFingerprint ?? Fingerprint,
            Guid.NewGuid(), TimeSpan.FromMinutes(10), 262144, 524288, Now, 1);
        var originalProposal = AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, Guid.NewGuid(), sequence: 0);
        var challenges = Enumerable.Range(1, challengeCount)
            .Select(index => AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, Guid.NewGuid(), sequence: index))
            .ToList();
        return (project, run, attempt, originalProposal, challenges);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public async Task HandleAsync_records_one_decision_per_challenge_and_exactly_one_revised_proposal(int challengeCount)
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt, originalProposal, challenges) = CreateClaimedChallengeResolutionAttempt(challengeCount);
        attempt.MarkAgentDispatched(Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        dbContext.AttemptInputMessages.Add(originalProposal);
        dbContext.AttemptInputMessages.AddRange(challenges);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var challengeMessageIds = challenges.Select(c => c.CollaborationMessageId).ToList();
        var resolution = Resolution(challengeMessageIds);
        var handler = new RecordChallengeResolutionResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1)));
        var result = await handler.HandleAsync(
            new RecordChallengeResolutionResultCommand(run.Id, attempt.Id, AgentOutcome.Resolved, Fingerprint, NoArtifacts, resolution, null, ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AttemptStatus.Completed, attempt.Status);
        Assert.Equal(AgentOutcome.Resolved, attempt.AgentOutcome);

        var messages = dbContext.CollaborationMessages.Where(m => m.RunId == run.Id).OrderBy(m => m.Sequence).ToList();
        Assert.Equal(challengeCount + 1, messages.Count);

        var decisionMessages = messages.Where(m => m.Type == CollaborationMessageType.Decision).ToList();
        Assert.Equal(challengeCount, decisionMessages.Count);
        Assert.Equal(challengeMessageIds.ToHashSet(), decisionMessages.Select(m => m.InReplyToMessageId!.Value).ToHashSet());
        Assert.All(decisionMessages, message =>
        {
            Assert.Equal(attempt.Id, message.AttemptId);
            Assert.Equal(ParticipantIdentity.ForAgent(AgentRole.Resolver, AgentProvider.Codex), message.Actor);
            Assert.Equal(ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), message.Recipient);
            Assert.Equal(CollaborationMessageProvenance.ProviderObserved, message.Provenance);
        });

        var revisedProposalMessage = Assert.Single(messages, m => m.Type == CollaborationMessageType.Proposal);
        Assert.Equal(originalProposal.CollaborationMessageId, revisedProposalMessage.InReplyToMessageId);
        Assert.Equal(ParticipantIdentity.ForAgent(AgentRole.Resolver, AgentProvider.Codex), revisedProposalMessage.Actor);
        Assert.Equal(ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), revisedProposalMessage.Recipient);
        Assert.Equal("Revised proposal summary", revisedProposalMessage.Summary);

        var events = dbContext.Events.Where(e => e.AttemptId == attempt.Id).OrderBy(e => e.Sequence).ToList();
        Assert.Equal(challengeCount + 1, events.Count);
        Assert.All(events, e => Assert.Equal(RunEventType.CollaborationMessageRecorded, e.EventType));
        Assert.Equal(result.Value.LatestEventSequence, events.Last().Sequence);
        Assert.All(events, e => Assert.Equal(ParticipantIdentity.ForAgent(AgentRole.Resolver, AgentProvider.Codex), e.Actor));
        Assert.Equal(ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex), ParticipantIdentity.ForAgentWithUnknownRole(attempt.AgentProvider!.Value));
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_outcome_is_not_caller_selectable()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt, originalProposal, challenges) = CreateClaimedChallengeResolutionAttempt();
        attempt.MarkAgentDispatched(Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        dbContext.AttemptInputMessages.Add(originalProposal);
        dbContext.AttemptInputMessages.AddRange(challenges);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordChallengeResolutionResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1)));
        var result = await handler.HandleAsync(
            new RecordChallengeResolutionResultCommand(run.Id, attempt.Id, AgentOutcome.Accepted, Fingerprint, NoArtifacts, null, null, ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.outcome_not_caller_selectable", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_for_an_undefined_outcome_value()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt, originalProposal, challenges) = CreateClaimedChallengeResolutionAttempt();
        attempt.MarkAgentDispatched(Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        dbContext.AttemptInputMessages.Add(originalProposal);
        dbContext.AttemptInputMessages.AddRange(challenges);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordChallengeResolutionResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1)));
        var result = await handler.HandleAsync(
            new RecordChallengeResolutionResultCommand(run.Id, attempt.Id, (AgentOutcome)9999, Fingerprint, NoArtifacts, null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.invalid_outcome", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_when_resolved_is_reported_without_a_completion_fingerprint()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt, originalProposal, challenges) = CreateClaimedChallengeResolutionAttempt();
        attempt.MarkAgentDispatched(Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        dbContext.AttemptInputMessages.Add(originalProposal);
        dbContext.AttemptInputMessages.AddRange(challenges);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var resolution = Resolution(challenges.Select(c => c.CollaborationMessageId).ToList());
        var handler = new RecordChallengeResolutionResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1)));
        var result = await handler.HandleAsync(
            new RecordChallengeResolutionResultCommand(run.Id, attempt.Id, AgentOutcome.Resolved, null, NoArtifacts, resolution, null, ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.resolution_requires_completion_fingerprint", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_when_resolved_is_reported_without_a_validated_resolution()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt, originalProposal, challenges) = CreateClaimedChallengeResolutionAttempt();
        attempt.MarkAgentDispatched(Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        dbContext.AttemptInputMessages.Add(originalProposal);
        dbContext.AttemptInputMessages.AddRange(challenges);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordChallengeResolutionResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1)));
        var result = await handler.HandleAsync(
            new RecordChallengeResolutionResultCommand(run.Id, attempt.Id, AgentOutcome.Resolved, Fingerprint, NoArtifacts, null, null, ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.resolution_requires_validated_resolution", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_resolution_does_not_resolve_the_exact_claimed_challenge_set()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt, originalProposal, challenges) = CreateClaimedChallengeResolutionAttempt(challengeCount: 2);
        attempt.MarkAgentDispatched(Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        dbContext.AttemptInputMessages.Add(originalProposal);
        dbContext.AttemptInputMessages.AddRange(challenges);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        // Only resolves the first challenge, omitting the second — never allowed to succeed.
        var incompleteResolution = Resolution([challenges[0].CollaborationMessageId]);
        var handler = new RecordChallengeResolutionResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1)));
        var result = await handler.HandleAsync(
            new RecordChallengeResolutionResultCommand(run.Id, attempt.Id, AgentOutcome.Resolved, Fingerprint, NoArtifacts, incompleteResolution, null, ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.invalid_resolution_shape", Assert.Single(result.Errors).Code);
    }

    // Regression test: a naive `Decisions.Select(...).ToHashSet().SetEquals(expected)` check
    // collapses a duplicated Challenge id before comparing, so a resolution with one Challenge
    // resolved twice and the other omitted can still pass set-equality by pure coincidence of
    // cardinality (3 decisions -> a 2-element set that happens to equal the 2-element expected
    // set). Constructs the ValidatedChallengeResolution directly (bypassing the parser
    // entirely) to prove the handler itself — not just ChallengeResolutionResponseParser — is
    // the boundary that rejects this.
    [Fact]
    public async Task HandleAsync_fails_and_mutates_nothing_when_a_challenge_is_resolved_twice_while_the_rest_of_the_set_is_complete()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt, originalProposal, challenges) = CreateClaimedChallengeResolutionAttempt(challengeCount: 2);
        attempt.MarkAgentDispatched(Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        dbContext.AttemptInputMessages.Add(originalProposal);
        dbContext.AttemptInputMessages.AddRange(challenges);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        // Resolves challenge[0] twice and challenge[1] once — the resulting id *set* is
        // {challenge[0], challenge[1]}, identical to the expected set, even though there are
        // three Decisions for two Challenges.
        var duplicateResolution = Resolution(
            [challenges[0].CollaborationMessageId, challenges[0].CollaborationMessageId, challenges[1].CollaborationMessageId]);
        var handler = new RecordChallengeResolutionResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1)));
        var result = await handler.HandleAsync(
            new RecordChallengeResolutionResultCommand(run.Id, attempt.Id, AgentOutcome.Resolved, Fingerprint, NoArtifacts, duplicateResolution, null, ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.invalid_resolution_shape", Assert.Single(result.Errors).Code);

        var reloadedAttempt = await dbContext.Attempts.FindAsync(attempt.Id);
        Assert.Equal(AttemptStatus.Running, reloadedAttempt!.Status);
        Assert.Null(reloadedAttempt.AgentOutcome);
        Assert.Empty(dbContext.CollaborationMessages.Where(m => m.RunId == run.Id));
        Assert.Empty(dbContext.Events.Where(e => e.AttemptId == attempt.Id));
        Assert.Empty(dbContext.Artifacts.Where(a => a.AttemptId == attempt.Id));
    }

    [Fact]
    public async Task HandleAsync_fails_when_a_resolution_is_supplied_for_a_non_resolved_outcome()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt, originalProposal, challenges) = CreateClaimedChallengeResolutionAttempt();
        attempt.MarkAgentDispatched(Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        dbContext.AttemptInputMessages.Add(originalProposal);
        dbContext.AttemptInputMessages.AddRange(challenges);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var resolution = Resolution(challenges.Select(c => c.CollaborationMessageId).ToList());
        var handler = new RecordChallengeResolutionResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1)));
        var result = await handler.HandleAsync(
            new RecordChallengeResolutionResultCommand(
                run.Id, attempt.Id, AgentOutcome.InvalidStructuredOutput, null, NoArtifacts, resolution, null, ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.resolution_requires_success_outcome", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_attempt_was_never_dispatched()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt, originalProposal, challenges) = CreateClaimedChallengeResolutionAttempt();
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        dbContext.AttemptInputMessages.Add(originalProposal);
        dbContext.AttemptInputMessages.AddRange(challenges);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordChallengeResolutionResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1)));
        var result = await handler.HandleAsync(
            new RecordChallengeResolutionResultCommand(
                run.Id, attempt.Id, AgentOutcome.ProviderInvocationFailed, null, NoArtifacts, null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.not_dispatched", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_attempt_is_not_a_challenge_resolution_attempt()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Review the next increment", Now);
        run.Claim(Now);
        var planningAttempt = Attempt.ClaimAgent(
            Guid.NewGuid(), run.Id, 1, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now, 1);
        planningAttempt.MarkAgentDispatched(Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(planningAttempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordChallengeResolutionResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1)));
        var result = await handler.HandleAsync(
            new RecordChallengeResolutionResultCommand(
                run.Id, planningAttempt.Id, AgentOutcome.ProviderInvocationFailed, null, NoArtifacts, null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.not_challenge_resolution", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_overrides_to_source_changed_and_records_no_collaboration_messages_when_the_fingerprint_no_longer_matches()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt, originalProposal, challenges) = CreateClaimedChallengeResolutionAttempt();
        attempt.MarkAgentDispatched(Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        dbContext.AttemptInputMessages.Add(originalProposal);
        dbContext.AttemptInputMessages.AddRange(challenges);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var resolution = Resolution(challenges.Select(c => c.CollaborationMessageId).ToList());
        var handler = new RecordChallengeResolutionResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1)));
        var result = await handler.HandleAsync(
            new RecordChallengeResolutionResultCommand(
                run.Id, attempt.Id, AgentOutcome.Resolved, new string('b', 64), NoArtifacts, resolution, null, ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AgentOutcome.SourceChanged, attempt.AgentOutcome);
        Assert.Empty(dbContext.CollaborationMessages.Where(m => m.RunId == run.Id));
        var journalEvent = Assert.Single(dbContext.Events.Where(e => e.AttemptId == attempt.Id));
        Assert.Equal(RunEventType.AgentAttemptCompleted, journalEvent.EventType);
    }

    [Fact]
    public async Task HandleAsync_persists_cancellation_evidence_atomically_with_the_provider_failure()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt, originalProposal, challenges) = CreateClaimedChallengeResolutionAttempt();
        attempt.MarkAgentDispatched(Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        dbContext.AttemptInputMessages.Add(originalProposal);
        dbContext.AttemptInputMessages.AddRange(challenges);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordChallengeResolutionResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1)));
        var result = await handler.HandleAsync(
            new RecordChallengeResolutionResultCommand(
                run.Id, attempt.Id, AgentOutcome.ProviderInvocationFailed, null, NoArtifacts, null, null,
                new AgentProcessEvidence(ProcessExecutionOutcome.Cancelled, null, TimeSpan.FromSeconds(12))),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        await using var verification = fixture.CreateContext();
        var persisted = await verification.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == attempt.Id);
        Assert.Equal(AgentOutcome.ProviderInvocationFailed, persisted.AgentOutcome);
        Assert.Equal(ProcessOutcome.Cancelled, persisted.AgentProcessOutcome);
        Assert.Null(persisted.AgentProcessExitCode);
        Assert.Equal(TimeSpan.FromSeconds(12), persisted.AgentProcessDuration);
    }

    [Fact]
    public async Task HandleAsync_rejects_a_resolution_without_process_evidence()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt, originalProposal, challenges) = CreateClaimedChallengeResolutionAttempt();
        attempt.MarkAgentDispatched(Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        dbContext.AttemptInputMessages.Add(originalProposal);
        dbContext.AttemptInputMessages.AddRange(challenges);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordChallengeResolutionResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1)));
        var result = await handler.HandleAsync(
            new RecordChallengeResolutionResultCommand(
                run.Id, attempt.Id, AgentOutcome.InvalidStructuredOutput, Fingerprint, NoArtifacts, null, null),
            CancellationToken.None);

        Assert.Equal(AgentProcessEvidenceRecording.CleanExitRequiredCode, Assert.Single(result.Errors).Code);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Null(attempt.AgentProcessOutcome);
    }
}
