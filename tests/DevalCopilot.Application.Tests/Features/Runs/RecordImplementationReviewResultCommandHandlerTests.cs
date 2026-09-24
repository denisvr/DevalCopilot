using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs.Commands.RecordImplementationReviewResult;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// Exercises the atomic recording path for a completed Codex code-review attempt: the Attempt
/// completion, the immutable <see cref="CheckpointReview"/> plus its complete
/// <see cref="CheckpointReviewEvidence"/> set (materialized from this attempt's durably claimed
/// <see cref="AttemptVerificationEvidence"/> rows — never re-selected), the ReviewApproval or
/// ReviewFinding collaboration facts, and their <see cref="RunEvent"/>s — all in one transaction.
/// </summary>
public sealed class RecordImplementationReviewResultCommandHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);
    private static readonly IReadOnlyList<SealedImplementationReviewArtifact> NoArtifacts = [];

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<(Run Run, Attempt Attempt, Guid ExecutionReportMessageId, VerificationExecution Execution)> SeedClaimedCodeReviewAttemptAsync(
        DevalCopilotDbContext dbContext)
    {
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Review the next increment", Now);
        run.Claim(Now);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", Now);
        workspace.MarkReady();
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, new string('b', 40), Fingerprint, []);
        var command = VerificationCommand.Configure(Guid.NewGuid(), project.Id, 1, "Backend tests", @"C:\dotnet.exe", ["test"], 300, true, Now);

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.GitWorkspaces.Add(workspace);
        dbContext.GitCheckpoints.Add(checkpoint);
        dbContext.VerificationCommands.Add(command);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var execution = VerificationExecution.Claim(Guid.NewGuid(), project.Id, 1, workspace, checkpoint, command, Now);
        execution.MarkDispatched(Now);
        execution.Complete(VerificationExecutionOutcome.Exited, 0, checkpoint.FingerprintSha256, Now);
        dbContext.VerificationExecutions.Add(execution);

        var attempt = Attempt.ClaimAgentCodeReview(
            Guid.NewGuid(), run.Id, 2, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now, 1);
        attempt.MarkAgentDispatched(Now);
        dbContext.Attempts.Add(attempt);

        var executionReportMessageId = Guid.NewGuid();
        dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, executionReportMessageId, sequence: 0));
        dbContext.AttemptVerificationEvidence.Add(
            AttemptVerificationEvidence.Record(Guid.NewGuid(), attempt.Id, command.Id, execution.Id, sequence: 0));

        await dbContext.SaveChangesAsync(CancellationToken.None);

        return (run, attempt, executionReportMessageId, execution);
    }

    [Fact]
    public async Task HandleAsync_records_an_approval_with_its_complete_evidence_set()
    {
        await using var dbContext = _fixture.CreateContext();
        var (run, attempt, executionReportMessageId, execution) = await SeedClaimedCodeReviewAttemptAsync(dbContext);

        var review = ValidatedImplementationReview.CreateApproved("All good.", "Follows the plan.", "None material.");
        var handler = new RecordImplementationReviewResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1)));

        var result = await handler.HandleAsync(
            new RecordImplementationReviewResultCommand(run.Id, attempt.Id, AgentOutcome.ReviewApproved, Fingerprint, NoArtifacts, review, null, ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AttemptStatus.Completed, attempt.Status);
        Assert.Equal(AgentOutcome.ReviewApproved, attempt.AgentOutcome);

        var checkpointReview = Assert.Single(dbContext.CheckpointReviews.Where(r => r.GitCheckpointId == attempt.AgentGitCheckpointId));
        Assert.Equal(ReviewDecision.Approved, checkpointReview.Decision);
        Assert.Equal(ReviewActorKind.FutureAgent, checkpointReview.ActorKind);
        var evidenceMember = Assert.Single(checkpointReview.Evidence);
        Assert.Equal(execution.Id, evidenceMember.VerificationExecutionId);
        Assert.Equal(VerificationExecutionStatus.Passed, evidenceMember.VerificationExecutionStatus);

        var approvalMessage = Assert.Single(dbContext.CollaborationMessages.Where(m => m.RunId == run.Id));
        Assert.Equal(CollaborationMessageType.ReviewApproval, approvalMessage.Type);
        Assert.Equal(executionReportMessageId, approvalMessage.InReplyToMessageId);
        Assert.Equal(ParticipantIdentity.ForAgent(AgentRole.CodeReviewer, AgentProvider.Codex), approvalMessage.Actor);
        Assert.Equal(ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), approvalMessage.Recipient);

        var events = dbContext.Events.Where(e => e.AttemptId == attempt.Id).ToList();
        Assert.Single(events);
        Assert.Equal(result.Value.LatestEventSequence, events[0].Sequence);
        Assert.Equal(approvalMessage.Actor, events[0].Actor);
        Assert.Equal(ParticipantIdentity.ForAgent(AgentRole.CodeReviewer, attempt.AgentProvider!.Value), approvalMessage.Actor);
    }

    [Fact]
    public async Task HandleAsync_records_one_finding_per_reported_material_finding()
    {
        await using var dbContext = _fixture.CreateContext();
        var (run, attempt, executionReportMessageId, _) = await SeedClaimedCodeReviewAttemptAsync(dbContext);

        var findings = new[]
        {
            new ValidatedReviewFinding("high", "correctness", "Finding 1", "Evidence 1", "Change 1", "src/Foo.cs"),
            new ValidatedReviewFinding("low", "standards", "Finding 2", "Evidence 2", "Change 2", null),
        };
        var review = ValidatedImplementationReview.CreateChangesRequested("Needs work.", findings);
        var handler = new RecordImplementationReviewResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1)));

        var result = await handler.HandleAsync(
            new RecordImplementationReviewResultCommand(run.Id, attempt.Id, AgentOutcome.ReviewChangesRequested, Fingerprint, NoArtifacts, review, null, ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AgentOutcome.ReviewChangesRequested, attempt.AgentOutcome);

        var checkpointReview = Assert.Single(dbContext.CheckpointReviews.Where(r => r.GitCheckpointId == attempt.AgentGitCheckpointId));
        Assert.Equal(ReviewDecision.ChangesRequested, checkpointReview.Decision);
        Assert.Single(checkpointReview.Evidence);

        var findingMessages = dbContext.CollaborationMessages.Where(m => m.RunId == run.Id).OrderBy(m => m.Sequence).ToList();
        Assert.Equal(2, findingMessages.Count);
        Assert.All(findingMessages, message =>
        {
            Assert.Equal(CollaborationMessageType.ReviewFinding, message.Type);
            Assert.Equal(executionReportMessageId, message.InReplyToMessageId);
            // The affected path is never duplicated into the durable ledger — only the four
            // closed structured fields are.
            Assert.DoesNotContain("Foo.cs", message.StructuredContentJson);
        });
    }

    [Fact]
    public async Task HandleAsync_never_records_a_review_when_fresh_evidence_shows_source_drift()
    {
        await using var dbContext = _fixture.CreateContext();
        var (run, attempt, _, _) = await SeedClaimedCodeReviewAttemptAsync(dbContext);

        var review = ValidatedImplementationReview.CreateApproved("All good.", "Follows the plan.", "None material.");
        var handler = new RecordImplementationReviewResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1)));

        var driftedFingerprint = new string('c', 64);
        var result = await handler.HandleAsync(
            new RecordImplementationReviewResultCommand(run.Id, attempt.Id, AgentOutcome.ReviewApproved, driftedFingerprint, NoArtifacts, review, null, ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AgentOutcome.SourceChanged, attempt.AgentOutcome);
        Assert.Empty(dbContext.CheckpointReviews.Where(r => r.GitCheckpointId == attempt.AgentGitCheckpointId));
        Assert.Empty(dbContext.CollaborationMessages.Where(m => m.RunId == run.Id));
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_outcome_is_not_caller_selectable()
    {
        await using var dbContext = _fixture.CreateContext();
        var (run, attempt, _, _) = await SeedClaimedCodeReviewAttemptAsync(dbContext);

        var handler = new RecordImplementationReviewResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1)));

        var result = await handler.HandleAsync(
            new RecordImplementationReviewResultCommand(run.Id, attempt.Id, AgentOutcome.InputAlreadyCodeReviewed, null, NoArtifacts, null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.outcome_not_caller_selectable", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_when_an_approved_outcome_carries_findings()
    {
        await using var dbContext = _fixture.CreateContext();
        var (run, attempt, _, _) = await SeedClaimedCodeReviewAttemptAsync(dbContext);

        var findings = new[] { new ValidatedReviewFinding("low", "standards", "F", "E", "C", null) };
        var review = ValidatedImplementationReview.CreateChangesRequested("Mismatch.", findings);
        var handler = new RecordImplementationReviewResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1)));

        var result = await handler.HandleAsync(
            new RecordImplementationReviewResultCommand(run.Id, attempt.Id, AgentOutcome.ReviewApproved, Fingerprint, NoArtifacts, review, null, ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.invalid_review_shape", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_when_a_review_outcome_has_no_completion_fingerprint()
    {
        await using var dbContext = _fixture.CreateContext();
        var (run, attempt, _, _) = await SeedClaimedCodeReviewAttemptAsync(dbContext);

        var review = ValidatedImplementationReview.CreateApproved("All good.", "Rationale.", "Risks.");
        var handler = new RecordImplementationReviewResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1)));

        var result = await handler.HandleAsync(
            new RecordImplementationReviewResultCommand(run.Id, attempt.Id, AgentOutcome.ReviewApproved, null, NoArtifacts, review, null, ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.review_requires_completion_fingerprint", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_persists_non_zero_exit_evidence_and_rejects_invalid_output_against_it()
    {
        await using var dbContext = _fixture.CreateContext();
        var (run, attempt, _, _) = await SeedClaimedCodeReviewAttemptAsync(dbContext);
        var nonZero = new AgentProcessEvidence(ProcessExecutionOutcome.Exited, 1, TimeSpan.FromSeconds(8));

        var handler = new RecordImplementationReviewResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1)));
        var rejected = await handler.HandleAsync(
            new RecordImplementationReviewResultCommand(
                run.Id, attempt.Id, AgentOutcome.InvalidStructuredOutput, Fingerprint, NoArtifacts, null, null, nonZero),
            CancellationToken.None);
        Assert.Equal(AgentProcessEvidenceRecording.CleanExitRequiredCode, Assert.Single(rejected.Errors).Code);

        var recorded = await handler.HandleAsync(
            new RecordImplementationReviewResultCommand(
                run.Id, attempt.Id, AgentOutcome.ProviderInvocationFailed, null, NoArtifacts, null, null, nonZero),
            CancellationToken.None);

        Assert.True(recorded.IsSuccess);
        await using var verification = _fixture.CreateContext();
        var persisted = await verification.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == attempt.Id);
        Assert.Equal(AgentOutcome.ProviderInvocationFailed, persisted.AgentOutcome);
        Assert.Equal(ProcessOutcome.Exited, persisted.AgentProcessOutcome);
        Assert.Equal(1, persisted.AgentProcessExitCode);
        Assert.Equal(TimeSpan.FromSeconds(8), persisted.AgentProcessDuration);
    }
}
