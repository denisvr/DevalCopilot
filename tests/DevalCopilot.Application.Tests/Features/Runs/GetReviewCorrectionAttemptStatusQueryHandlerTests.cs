using System.Data.Common;
using DevalCopilot.Application.Features.Runs.Queries.GetReviewCorrectionAttemptStatus;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed class GetReviewCorrectionAttemptStatusQueryHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 10, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task Initial_report_query_count_is_fixed_when_unrelated_completed_candidates_grow()
    {
        Guid runId;
        Guid expectedReportId;
        await using (var seedContext = _fixture.CreateContext())
        {
            var seed = SeedInitialReport(seedContext);
            runId = seed.RunId;
            expectedReportId = seed.ExecutionReportId;
            await seedContext.SaveChangesAsync(CancellationToken.None);
        }

        var few = await MeasureAsync(runId, expectedReportId);

        await using (var addCandidatesContext = _fixture.CreateContext())
        {
            var workspace = await addCandidatesContext.GitWorkspaces.SingleAsync();
            var startingCheckpoint = await addCandidatesContext.GitCheckpoints
                .OrderBy(checkpoint => checkpoint.CheckpointNumber)
                .FirstAsync();
            var resultCheckpoint = await addCandidatesContext.GitCheckpoints
                .OrderByDescending(checkpoint => checkpoint.CheckpointNumber)
                .FirstAsync();
            for (var index = 0; index < 40; index++)
            {
                var candidate = Attempt.ClaimAgentImplementation(
                    Guid.NewGuid(), runId, 100 + index, workspace.Id, startingCheckpoint.Id, Fingerprint, Guid.NewGuid(),
                    TimeSpan.FromMinutes(10), 262144, 524288, Now);
                candidate.MarkAgentDispatched(Now);
                candidate.CompleteImplementation(AgentOutcome.Implemented, resultCheckpoint.Id, Now, processEvidence: TestProcessEvidence.CleanExit);
                addCandidatesContext.Attempts.Add(candidate);
            }

            await addCandidatesContext.SaveChangesAsync(CancellationToken.None);
        }

        var many = await MeasureAsync(runId, expectedReportId);

        Assert.Equal(few.Count, many.Count);
        Assert.Equal(expectedReportId, few.ReportId);
        Assert.Equal(expectedReportId, many.ReportId);
        Assert.Equal(6, few.Count);
    }

    [Fact]
    public async Task Projects_the_current_changes_requested_review_before_the_first_correction_attempt()
    {
        Guid runId;
        Guid reviewId;
        Guid reportId;
        await using (var seedContext = _fixture.CreateContext())
        {
            var seed = SeedReviewChangesRequested(seedContext);
            runId = seed.RunId;
            reviewId = seed.ReviewAttemptId;
            reportId = seed.ExecutionReportId;
            await seedContext.SaveChangesAsync(CancellationToken.None);
        }

        await using var context = _fixture.CreateContext();
        var result = await new GetReviewCorrectionAttemptStatusQueryHandler(context)
            .HandleAsync(new GetReviewCorrectionAttemptStatusQuery(runId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.HasAttempt);
        Assert.Null(result.Value.AttemptId);
        Assert.Null(result.Value.AttemptNumber);
        Assert.Equal(reviewId, result.Value.ImplementationReviewAttemptId);
        Assert.Equal(reportId, result.Value.ReviewableExecutionReportMessageId);
        Assert.Equal(0, result.Value.ReviewCorrectionAttemptsUsed);
        Assert.False(result.Value.BudgetExhausted);
        Assert.Null(result.Value.EscalationId);
        Assert.Null(result.Value.EscalationMessageId);
        Assert.False(result.Value.HasAvailableHumanAuthorization);
    }

    private async Task<(int Count, Guid? ReportId)> MeasureAsync(Guid runId, Guid expectedReportId)
    {
        var interceptor = new SelectCountingInterceptor();
        await using var context = _fixture.CreateContext(interceptor);
        var handler = new GetReviewCorrectionAttemptStatusQueryHandler(context);
        var result = await handler.HandleAsync(new GetReviewCorrectionAttemptStatusQuery(runId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedReportId, result.Value.ReviewableExecutionReportMessageId);
        return (interceptor.SelectCount, result.Value.ReviewableExecutionReportMessageId);
    }

    private static Seed SeedInitialReport(DevalCopilotDbContext context)
    {
        var project = Project.Register(Guid.NewGuid(), "Status query project", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Review the implementation", Now);
        run.Claim(Now);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "main", new string('a', 40), "main", Now);
        workspace.MarkReady();
        var startingCheckpoint = GitCheckpoint.Capture(
            Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), Fingerprint, []);
        var resultCheckpoint = GitCheckpoint.Capture(
            Guid.NewGuid(), workspace.Id, 2, Now.AddMinutes(1), new string('b', 40), new string('b', 64), []);

        var planner = Attempt.ClaimAgent(
            Guid.NewGuid(), run.Id, 1, workspace.Id, startingCheckpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now);
        planner.MarkAgentDispatched(Now);
        planner.CompleteAgent(AgentOutcome.Proposed, Fingerprint, Now, processEvidence: TestProcessEvidence.CleanExit);
        var proposal = CollaborationMessage.RecordAgent(
            planner, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode),
            CollaborationMessageType.Proposal, null, "Implement the requested change.",
            "{\"scope\":\"Status\",\"implementationSteps\":\"Apply the change\",\"risks\":\"None\",\"verificationPlan\":\"Tests\",\"escalationPoints\":\"None\"}", Now);

        var acceptanceAttempt = Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), run.Id, 2, workspace.Id, startingCheckpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now);
        acceptanceAttempt.MarkAgentDispatched(Now);
        acceptanceAttempt.CompleteAgent(AgentOutcome.Accepted, Fingerprint, Now, processEvidence: TestProcessEvidence.CleanExit);
        var acceptance = CollaborationMessage.RecordAgent(
            acceptanceAttempt, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex),
            CollaborationMessageType.Acceptance, proposal.Id, "Accepted the proposal.",
            "{\"rationale\":\"The plan is complete.\"}", Now);

        var implementation = Attempt.ClaimAgentImplementation(
            Guid.NewGuid(), run.Id, 3, workspace.Id, startingCheckpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now);
        implementation.MarkAgentDispatched(Now);
        implementation.CompleteImplementation(AgentOutcome.Implemented, resultCheckpoint.Id, Now, processEvidence: TestProcessEvidence.CleanExit);
        var executionReport = CollaborationMessage.RecordAgent(
            implementation, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex),
            CollaborationMessageType.ExecutionReport, proposal.Id, "Implemented the proposal.",
            "{\"completedWork\":\"Applied the change.\",\"verification\":\"Tests passed.\"}", Now);

        context.Projects.Add(project);
        context.Runs.Add(run);
        context.GitWorkspaces.Add(workspace);
        context.GitCheckpoints.AddRange(startingCheckpoint, resultCheckpoint);
        context.Attempts.AddRange(planner, acceptanceAttempt, implementation);
        context.CollaborationMessages.AddRange(proposal, acceptance, executionReport);
        context.AttemptInputMessages.AddRange(
            AttemptInputMessage.Record(Guid.NewGuid(), acceptanceAttempt.Id, proposal.Id, 0),
            AttemptInputMessage.Record(Guid.NewGuid(), implementation.Id, proposal.Id, 0),
            AttemptInputMessage.Record(Guid.NewGuid(), implementation.Id, acceptance.Id, 1));

        return new Seed(run.Id, executionReport.Id);
    }

    private static ReviewSeed SeedReviewChangesRequested(DevalCopilotDbContext context)
    {
        var project = Project.Register(Guid.NewGuid(), "Review status project", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Review the implementation", Now);
        run.Claim(Now);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "main", new string('a', 40), "main", Now);
        workspace.MarkReady();
        var startingCheckpoint = GitCheckpoint.Capture(
            Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), Fingerprint, []);
        var resultCheckpoint = GitCheckpoint.Capture(
            Guid.NewGuid(), workspace.Id, 2, Now.AddMinutes(1), new string('b', 40), new string('b', 64), []);

        var planner = Attempt.ClaimAgent(
            Guid.NewGuid(), run.Id, 1, workspace.Id, startingCheckpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now);
        planner.MarkAgentDispatched(Now);
        planner.CompleteAgent(AgentOutcome.Proposed, Fingerprint, Now, processEvidence: TestProcessEvidence.CleanExit);
        var proposal = CollaborationMessage.RecordAgent(
            planner, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode),
            CollaborationMessageType.Proposal, null, "Implement the requested change.",
            "{\"scope\":\"Status\",\"implementationSteps\":\"Apply the change\",\"risks\":\"None\",\"verificationPlan\":\"Tests\",\"escalationPoints\":\"None\"}", Now);

        var acceptanceAttempt = Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), run.Id, 2, workspace.Id, startingCheckpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now);
        acceptanceAttempt.MarkAgentDispatched(Now);
        acceptanceAttempt.CompleteAgent(AgentOutcome.Accepted, Fingerprint, Now, processEvidence: TestProcessEvidence.CleanExit);
        var acceptance = CollaborationMessage.RecordAgent(
            acceptanceAttempt, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex),
            CollaborationMessageType.Acceptance, proposal.Id, "Accepted the proposal.",
            "{\"rationale\":\"The plan is complete.\"}", Now);

        var implementation = Attempt.ClaimAgentImplementation(
            Guid.NewGuid(), run.Id, 3, workspace.Id, startingCheckpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now);
        implementation.MarkAgentDispatched(Now);
        implementation.CompleteImplementation(AgentOutcome.Implemented, resultCheckpoint.Id, Now, processEvidence: TestProcessEvidence.CleanExit);
        var executionReport = CollaborationMessage.RecordAgent(
            implementation, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode),
            CollaborationMessageType.ExecutionReport, proposal.Id, "Implemented the proposal.",
            "{\"completedWork\":\"Applied the change.\",\"verification\":\"Tests passed.\"}", Now);

        var review = Attempt.ClaimAgentCodeReview(
            Guid.NewGuid(), run.Id, 4, workspace.Id, resultCheckpoint.Id, resultCheckpoint.FingerprintSha256, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now);
        review.MarkAgentDispatched(Now);
        review.CompleteAgent(AgentOutcome.ReviewChangesRequested, resultCheckpoint.FingerprintSha256, Now, processEvidence: TestProcessEvidence.CleanExit);
        var finding = CollaborationMessage.RecordAgent(
            review, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode),
            CollaborationMessageType.ReviewFinding, executionReport.Id, "Add the missing guard.",
            "{\"severity\":\"high\",\"category\":\"correctness\",\"evidence\":\"The input is unchecked.\",\"requiredChange\":\"Add the guard.\"}", Now);

        context.Projects.Add(project);
        context.Runs.Add(run);
        context.GitWorkspaces.Add(workspace);
        context.GitCheckpoints.AddRange(startingCheckpoint, resultCheckpoint);
        context.Attempts.AddRange(planner, acceptanceAttempt, implementation, review);
        context.CollaborationMessages.AddRange(proposal, acceptance, executionReport, finding);
        context.AttemptInputMessages.AddRange(
            AttemptInputMessage.Record(Guid.NewGuid(), acceptanceAttempt.Id, proposal.Id, 0),
            AttemptInputMessage.Record(Guid.NewGuid(), implementation.Id, proposal.Id, 0),
            AttemptInputMessage.Record(Guid.NewGuid(), implementation.Id, acceptance.Id, 1),
            AttemptInputMessage.Record(Guid.NewGuid(), review.Id, executionReport.Id, 0));

        return new ReviewSeed(run.Id, review.Id, executionReport.Id);
    }

    private sealed record Seed(Guid RunId, Guid ExecutionReportId);

    private sealed record ReviewSeed(Guid RunId, Guid ReviewAttemptId, Guid ExecutionReportId);

    private sealed class SelectCountingInterceptor : DbCommandInterceptor
    {
        public int SelectCount { get; private set; }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            CountSelect(command);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            CountSelect(command);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<object> ScalarExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
        {
            CountSelect(command);
            return result;
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            CountSelect(command);
            return ValueTask.FromResult(result);
        }

        private void CountSelect(DbCommand command)
        {
            if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                SelectCount++;
            }
        }
    }
}
