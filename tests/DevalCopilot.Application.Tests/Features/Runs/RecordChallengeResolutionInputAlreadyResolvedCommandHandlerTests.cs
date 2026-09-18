using DevalCopilot.Application.Features.Runs.Commands.RecordChallengeResolutionInputAlreadyResolved;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// Mirrors <c>RecordClaudeCriticalReviewInputAlreadyReviewedCommandHandlerTests</c>'s shape
/// exactly, one level further down the collaboration protocol: this handler never trusts the
/// caller's own claim that a competing resolution exists — it independently re-verifies the
/// exact ordered input identity itself, and every test proving rejection also proves zero
/// mutation.
/// </summary>
public sealed class RecordChallengeResolutionInputAlreadyResolvedCommandHandlerTests(SqliteDatabaseFixture fixture)
    : IClassFixture<SqliteDatabaseFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);

    private static (Project Project, Run Run, Attempt Attempt) CreateClaimedChallengeResolutionAttempt(int attemptNumber = 1)
    {
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Resolve the challenged proposal", Now);
        run.Claim(Now);
        var attempt = Attempt.ClaimAgentChallengeResolution(
            Guid.NewGuid(), run.Id, attemptNumber, Guid.NewGuid(), Guid.NewGuid(), Fingerprint,
            Guid.NewGuid(), TimeSpan.FromMinutes(10), 262144, 524288, Now);
        return (project, run, attempt);
    }

    private static List<AttemptInputMessage> OrderedInputMessages(Guid attemptId, IReadOnlyList<Guid> collaborationMessageIds) =>
        collaborationMessageIds
            .Select((id, index) => AttemptInputMessage.Record(Guid.NewGuid(), attemptId, id, sequence: index))
            .ToList();

    [Fact]
    public async Task HandleAsync_records_input_already_resolved_when_a_completed_resolution_of_the_exact_same_ordered_input_set_exists()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt) = CreateClaimedChallengeResolutionAttempt();
        var orderedIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        var competingResolution = Attempt.ClaimAgentChallengeResolution(
            Guid.NewGuid(), run.Id, 2, Guid.NewGuid(), Guid.NewGuid(), Fingerprint,
            Guid.NewGuid(), TimeSpan.FromMinutes(10), 262144, 524288, Now);
        competingResolution.MarkAgentDispatched(Now);
        competingResolution.CompleteAgent(AgentOutcome.Resolved, Fingerprint, Now);

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.AddRange(attempt, competingResolution);
        dbContext.AttemptInputMessages.AddRange(OrderedInputMessages(attempt.Id, orderedIds));
        dbContext.AttemptInputMessages.AddRange(OrderedInputMessages(competingResolution.Id, orderedIds));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordChallengeResolutionInputAlreadyResolvedCommandHandler(dbContext, new FixedTimeProvider(Now.AddSeconds(1)));
        var result = await handler.HandleAsync(
            new RecordChallengeResolutionInputAlreadyResolvedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AttemptStatus.Failed, attempt.Status);
        Assert.Equal(AgentOutcome.InputAlreadyResolved, attempt.AgentOutcome);
        Assert.Null(attempt.AgentDispatchedAtUtc);
        Assert.Equal(Now.AddSeconds(1), attempt.CompletedAtUtc);

        var journalEvent = Assert.Single(dbContext.Events.Where(e => e.AttemptId == attempt.Id));
        Assert.Equal(RunEventType.AgentAttemptCompleted, journalEvent.EventType);
        Assert.Contains("InputAlreadyResolved", journalEvent.PayloadJson);
        Assert.Empty(dbContext.CollaborationMessages.Where(m => m.RunId == run.Id));
    }

    /// <summary>The core guarantee this command exists for: it never trusts a caller's claim on
    /// faith. A speculative or stale dispatch — no completed successful resolution of the exact
    /// ordered input set actually exists — is safely rejected and mutates nothing.</summary>
    [Fact]
    public async Task HandleAsync_fails_and_does_not_mutate_when_no_competing_resolution_exists_at_all()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt) = CreateClaimedChallengeResolutionAttempt();
        var orderedIds = new[] { Guid.NewGuid(), Guid.NewGuid() };

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        dbContext.AttemptInputMessages.AddRange(OrderedInputMessages(attempt.Id, orderedIds));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordChallengeResolutionInputAlreadyResolvedCommandHandler(dbContext, new FixedTimeProvider(Now.AddSeconds(1)));
        var result = await handler.HandleAsync(
            new RecordChallengeResolutionInputAlreadyResolvedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.no_competing_resolution_found", Assert.Single(result.Errors).Code);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Null(attempt.AgentOutcome);
        Assert.Empty(dbContext.Events.Where(e => e.AttemptId == attempt.Id));
    }

    /// <summary>A prior successful resolution that only partially covers this attempt's own
    /// ordered input set (missing the second Challenge entirely) is never evidence this attempt
    /// was superseded — mirrors the dispatch-time gate's own partial-match test.</summary>
    [Fact]
    public async Task HandleAsync_fails_and_does_not_mutate_when_a_completed_resolution_only_partially_covers_the_input_set()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt) = CreateClaimedChallengeResolutionAttempt();
        var proposalId = Guid.NewGuid();
        var challenge1Id = Guid.NewGuid();
        var challenge2Id = Guid.NewGuid();

        var partialCompetingResolution = Attempt.ClaimAgentChallengeResolution(
            Guid.NewGuid(), run.Id, 2, Guid.NewGuid(), Guid.NewGuid(), Fingerprint,
            Guid.NewGuid(), TimeSpan.FromMinutes(10), 262144, 524288, Now);
        partialCompetingResolution.MarkAgentDispatched(Now);
        partialCompetingResolution.CompleteAgent(AgentOutcome.Resolved, Fingerprint, Now);

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.AddRange(attempt, partialCompetingResolution);
        dbContext.AttemptInputMessages.AddRange(OrderedInputMessages(attempt.Id, [proposalId, challenge1Id, challenge2Id]));
        dbContext.AttemptInputMessages.AddRange(OrderedInputMessages(partialCompetingResolution.Id, [proposalId, challenge1Id]));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordChallengeResolutionInputAlreadyResolvedCommandHandler(dbContext, new FixedTimeProvider(Now.AddSeconds(1)));
        var result = await handler.HandleAsync(
            new RecordChallengeResolutionInputAlreadyResolvedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.no_competing_resolution_found", Assert.Single(result.Errors).Code);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_attempt_does_not_exist()
    {
        await using var dbContext = fixture.CreateContext();

        var handler = new RecordChallengeResolutionInputAlreadyResolvedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new RecordChallengeResolutionInputAlreadyResolvedCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.not_found", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_and_does_not_mutate_a_claude_critical_review_attempt()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Review the next increment", Now);
        run.Claim(Now);
        var attempt = Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), run.Id, 1, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordChallengeResolutionInputAlreadyResolvedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new RecordChallengeResolutionInputAlreadyResolvedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.not_challenge_resolution", Assert.Single(result.Errors).Code);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
    }

    [Fact]
    public async Task HandleAsync_fails_and_does_not_mutate_an_already_dispatched_attempt()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt) = CreateClaimedChallengeResolutionAttempt();
        attempt.MarkAgentDispatched(Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordChallengeResolutionInputAlreadyResolvedCommandHandler(dbContext, new FixedTimeProvider(Now.AddSeconds(1)));
        var result = await handler.HandleAsync(
            new RecordChallengeResolutionInputAlreadyResolvedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.not_eligible", Assert.Single(result.Errors).Code);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Null(attempt.AgentOutcome);
    }

    [Fact]
    public async Task HandleAsync_fails_and_does_not_mutate_an_already_terminal_attempt()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt) = CreateClaimedChallengeResolutionAttempt();
        attempt.CompleteAgent(AgentOutcome.ProviderInvocationFailed, null, Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordChallengeResolutionInputAlreadyResolvedCommandHandler(dbContext, new FixedTimeProvider(Now.AddSeconds(1)));
        var result = await handler.HandleAsync(
            new RecordChallengeResolutionInputAlreadyResolvedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.not_eligible", Assert.Single(result.Errors).Code);
        Assert.Equal(AgentOutcome.ProviderInvocationFailed, attempt.AgentOutcome);
    }
}
