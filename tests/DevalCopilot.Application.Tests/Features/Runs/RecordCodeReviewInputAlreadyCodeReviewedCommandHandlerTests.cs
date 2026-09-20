using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Application.Features.Runs.Commands.RecordCodeReviewInputAlreadyCodeReviewed;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// Mirrors <c>RecordChallengeResolutionInputAlreadyResolvedCommandHandlerTests</c>'s shape exactly,
/// adapted for the two-part input identity (the reviewed ExecutionReport plus the exact ordered
/// claimed verification-execution set) a code-review attempt carries: this handler never trusts the
/// caller's own claim that a competing review exists — it independently re-verifies both parts of
/// the exact identity itself, and every test proving rejection also proves zero mutation.
/// </summary>
public sealed class RecordCodeReviewInputAlreadyCodeReviewedCommandHandlerTests(SqliteDatabaseFixture fixture)
    : IClassFixture<SqliteDatabaseFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);

    private static (Project Project, Run Run, GitWorkspace Workspace, GitCheckpoint Checkpoint) CreateOwningResources()
    {
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Review the implementation", Now);
        run.Claim(Now);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", Now);
        workspace.MarkReady();
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, new string('b', 40), Fingerprint, []);
        return (project, run, workspace, checkpoint);
    }

    private static Attempt CreateClaimedCodeReviewAttempt(Guid runId, Guid workspaceId, Guid checkpointId, int attemptNumber) =>
        Attempt.ClaimAgentCodeReview(
            Guid.NewGuid(), runId, attemptNumber, workspaceId, checkpointId, Fingerprint,
            Guid.NewGuid(), TimeSpan.FromMinutes(10), 262144, 524288, Now);

    private static (List<VerificationCommand> Commands, List<VerificationExecution> Executions) SeedVerificationExecutions(
        Guid projectId, GitWorkspace workspace, GitCheckpoint checkpoint, int count)
    {
        var commands = new List<VerificationCommand>(count);
        var executions = new List<VerificationExecution>(count);
        for (var index = 0; index < count; index++)
        {
            var command = VerificationCommand.Configure(
                Guid.NewGuid(), projectId, index + 1, $"Command {index + 1}", @"C:\dotnet.exe", ["test"], 300, true, Now);
            var execution = VerificationExecution.Claim(Guid.NewGuid(), projectId, index + 1, workspace, checkpoint, command, Now);
            execution.MarkDispatched(Now);
            execution.Complete(VerificationExecutionOutcome.Exited, 0, checkpoint.FingerprintSha256, Now);
            commands.Add(command);
            executions.Add(execution);
        }

        return (commands, executions);
    }

    private static void SeedInputIdentity(
        List<AttemptInputMessage> inputMessages,
        List<AttemptVerificationEvidence> evidenceRows,
        Guid attemptId,
        Guid executionReportMessageId,
        IReadOnlyList<VerificationExecution> orderedExecutions)
    {
        inputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), attemptId, executionReportMessageId, sequence: 0));
        for (var index = 0; index < orderedExecutions.Count; index++)
        {
            evidenceRows.Add(AttemptVerificationEvidence.Record(
                Guid.NewGuid(), attemptId, orderedExecutions[index].VerificationCommandId, orderedExecutions[index].Id, sequence: index));
        }
    }

    [Fact]
    public async Task HandleAsync_records_input_already_code_reviewed_when_a_completed_review_of_the_exact_same_input_identity_exists()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, workspace, checkpoint) = CreateOwningResources();
        var attempt = CreateClaimedCodeReviewAttempt(run.Id, workspace.Id, checkpoint.Id, 1);
        var executionReportMessageId = Guid.NewGuid();
        var (commands, executions) = SeedVerificationExecutions(project.Id, workspace, checkpoint, 2);

        var competingReview = CreateClaimedCodeReviewAttempt(run.Id, workspace.Id, checkpoint.Id, 2);
        competingReview.MarkAgentDispatched(Now);
        competingReview.CompleteAgent(AgentOutcome.ReviewApproved, Fingerprint, Now);

        var inputMessages = new List<AttemptInputMessage>();
        var evidenceRows = new List<AttemptVerificationEvidence>();
        SeedInputIdentity(inputMessages, evidenceRows, attempt.Id, executionReportMessageId, executions);
        SeedInputIdentity(inputMessages, evidenceRows, competingReview.Id, executionReportMessageId, executions);

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.GitWorkspaces.Add(workspace);
        dbContext.GitCheckpoints.Add(checkpoint);
        dbContext.VerificationCommands.AddRange(commands);
        dbContext.VerificationExecutions.AddRange(executions);
        dbContext.Attempts.AddRange(attempt, competingReview);
        dbContext.AttemptInputMessages.AddRange(inputMessages);
        dbContext.AttemptVerificationEvidence.AddRange(evidenceRows);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordCodeReviewInputAlreadyCodeReviewedCommandHandler(dbContext, new FixedTimeProvider(Now.AddSeconds(1)));
        var result = await handler.HandleAsync(new RecordCodeReviewInputAlreadyCodeReviewedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AttemptStatus.Failed, attempt.Status);
        Assert.Equal(AgentOutcome.InputAlreadyCodeReviewed, attempt.AgentOutcome);
        Assert.Null(attempt.AgentDispatchedAtUtc);

        var journalEvent = Assert.Single(dbContext.Events.Where(e => e.AttemptId == attempt.Id));
        Assert.Equal(RunEventType.AgentAttemptCompleted, journalEvent.EventType);
        Assert.Contains("InputAlreadyCodeReviewed", journalEvent.PayloadJson);
    }

    [Fact]
    public async Task HandleAsync_fails_and_does_not_mutate_when_no_competing_review_exists_at_all()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, workspace, checkpoint) = CreateOwningResources();
        var attempt = CreateClaimedCodeReviewAttempt(run.Id, workspace.Id, checkpoint.Id, 1);
        var (commands, executions) = SeedVerificationExecutions(project.Id, workspace, checkpoint, 1);

        var inputMessages = new List<AttemptInputMessage>();
        var evidenceRows = new List<AttemptVerificationEvidence>();
        SeedInputIdentity(inputMessages, evidenceRows, attempt.Id, Guid.NewGuid(), executions);

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.GitWorkspaces.Add(workspace);
        dbContext.GitCheckpoints.Add(checkpoint);
        dbContext.VerificationCommands.AddRange(commands);
        dbContext.VerificationExecutions.AddRange(executions);
        dbContext.Attempts.Add(attempt);
        dbContext.AttemptInputMessages.AddRange(inputMessages);
        dbContext.AttemptVerificationEvidence.AddRange(evidenceRows);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordCodeReviewInputAlreadyCodeReviewedCommandHandler(dbContext, new FixedTimeProvider(Now.AddSeconds(1)));
        var result = await handler.HandleAsync(new RecordCodeReviewInputAlreadyCodeReviewedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.no_competing_review_found", Assert.Single(result.Errors).Code);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Null(attempt.AgentOutcome);
    }

    /// <summary>A prior successful review that matches the ExecutionReport but only partially
    /// covers this attempt's own claimed verification-execution set (missing the second execution
    /// entirely) is never evidence this attempt was superseded.</summary>
    [Fact]
    public async Task HandleAsync_fails_and_does_not_mutate_when_a_completed_review_only_partially_covers_the_verification_set()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, workspace, checkpoint) = CreateOwningResources();
        var attempt = CreateClaimedCodeReviewAttempt(run.Id, workspace.Id, checkpoint.Id, 1);
        var executionReportMessageId = Guid.NewGuid();
        var (commands, executions) = SeedVerificationExecutions(project.Id, workspace, checkpoint, 2);

        var partialCompetingReview = CreateClaimedCodeReviewAttempt(run.Id, workspace.Id, checkpoint.Id, 2);
        partialCompetingReview.MarkAgentDispatched(Now);
        partialCompetingReview.CompleteAgent(AgentOutcome.ReviewApproved, Fingerprint, Now);

        var inputMessages = new List<AttemptInputMessage>();
        var evidenceRows = new List<AttemptVerificationEvidence>();
        SeedInputIdentity(inputMessages, evidenceRows, attempt.Id, executionReportMessageId, executions);
        SeedInputIdentity(inputMessages, evidenceRows, partialCompetingReview.Id, executionReportMessageId, [executions[0]]);

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.GitWorkspaces.Add(workspace);
        dbContext.GitCheckpoints.Add(checkpoint);
        dbContext.VerificationCommands.AddRange(commands);
        dbContext.VerificationExecutions.AddRange(executions);
        dbContext.Attempts.AddRange(attempt, partialCompetingReview);
        dbContext.AttemptInputMessages.AddRange(inputMessages);
        dbContext.AttemptVerificationEvidence.AddRange(evidenceRows);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordCodeReviewInputAlreadyCodeReviewedCommandHandler(dbContext, new FixedTimeProvider(Now.AddSeconds(1)));
        var result = await handler.HandleAsync(new RecordCodeReviewInputAlreadyCodeReviewedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.no_competing_review_found", Assert.Single(result.Errors).Code);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_attempt_does_not_exist()
    {
        await using var dbContext = fixture.CreateContext();

        var handler = new RecordCodeReviewInputAlreadyCodeReviewedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new RecordCodeReviewInputAlreadyCodeReviewedCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.not_found", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_and_does_not_mutate_an_already_dispatched_attempt()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, workspace, checkpoint) = CreateOwningResources();
        var attempt = CreateClaimedCodeReviewAttempt(run.Id, workspace.Id, checkpoint.Id, 1);
        attempt.MarkAgentDispatched(Now);
        var (commands, executions) = SeedVerificationExecutions(project.Id, workspace, checkpoint, 1);

        var inputMessages = new List<AttemptInputMessage>();
        var evidenceRows = new List<AttemptVerificationEvidence>();
        SeedInputIdentity(inputMessages, evidenceRows, attempt.Id, Guid.NewGuid(), executions);

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.GitWorkspaces.Add(workspace);
        dbContext.GitCheckpoints.Add(checkpoint);
        dbContext.VerificationCommands.AddRange(commands);
        dbContext.VerificationExecutions.AddRange(executions);
        dbContext.Attempts.Add(attempt);
        dbContext.AttemptInputMessages.AddRange(inputMessages);
        dbContext.AttemptVerificationEvidence.AddRange(evidenceRows);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordCodeReviewInputAlreadyCodeReviewedCommandHandler(dbContext, new FixedTimeProvider(Now.AddSeconds(1)));
        var result = await handler.HandleAsync(new RecordCodeReviewInputAlreadyCodeReviewedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.not_eligible", Assert.Single(result.Errors).Code);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Null(attempt.AgentOutcome);
    }

    // Fail-closed regression: a genuinely malformed persisted attempt — the correct AgentRole but
    // a response contract that does not cohere with AgentAttemptContract.For(role) — must be
    // rejected by the same safe "not this attempt shape" failure, never treated as a valid
    // code-review attempt just because its role happens to match.
    [Fact]
    public async Task HandleAsync_fails_and_does_not_mutate_an_attempt_with_a_mismatched_response_contract()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, workspace, checkpoint) = CreateOwningResources();
        var attempt = CreateClaimedCodeReviewAttempt(run.Id, workspace.Id, checkpoint.Id, 1);
        var responseContractProperty = typeof(Attempt).GetProperty(nameof(Attempt.AgentResponseContract))!;
        responseContractProperty.GetSetMethod(nonPublic: true)!.Invoke(attempt, [AgentResponseContract.Proposal]);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.GitWorkspaces.Add(workspace);
        dbContext.GitCheckpoints.Add(checkpoint);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordCodeReviewInputAlreadyCodeReviewedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(new RecordCodeReviewInputAlreadyCodeReviewedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.not_code_review", Assert.Single(result.Errors).Code);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Empty(dbContext.Events.Where(e => e.AttemptId == attempt.Id));
    }
}
