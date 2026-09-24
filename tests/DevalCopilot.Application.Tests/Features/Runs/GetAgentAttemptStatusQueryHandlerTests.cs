using DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptStatus;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed class GetAgentAttemptStatusQueryHandlerTests(SqliteDatabaseFixture fixture)
    : IClassFixture<SqliteDatabaseFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 19, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);

    private static Attempt ClaimAgentAttempt(Guid runId, int attemptNumber, DateTimeOffset claimedAtUtc) => Attempt.ClaimAgent(
        Guid.NewGuid(), runId, attemptNumber, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
        TimeSpan.FromMinutes(10), 262144, 524288, claimedAtUtc);

    [Fact]
    public async Task HandleAsync_returns_an_explicit_no_attempt_result_for_a_run_with_no_agent_attempt_yet()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "No attempt yet", Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetAgentAttemptStatusQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetAgentAttemptStatusQuery(run.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.HasAttempt);
        Assert.Null(result.Value.AttemptId);
        Assert.Empty(result.Value.Artifacts);
    }

    [Fact]
    public async Task HandleAsync_fails_with_a_safe_not_found_when_the_run_does_not_exist()
    {
        await using var dbContext = fixture.CreateContext();

        var handler = new GetAgentAttemptStatusQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetAgentAttemptStatusQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("runs.not_found", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_returns_the_attempts_bounded_status_and_artifact_metadata()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Plan the next increment", Now);
        run.Claim(Now);
        var attempt = ClaimAgentAttempt(run.Id, 1, Now);
        attempt.MarkAgentDispatched(Now.AddSeconds(1));
        attempt.CompleteAgent(AgentOutcome.Proposed, Fingerprint, Now.AddSeconds(2), processEvidence: TestProcessEvidence.CleanExit);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        dbContext.Artifacts.Add(Artifact.Record(
            Guid.NewGuid(), run.Id, attempt.Id, ArtifactPurpose.AgentFinalResponse, "application/json",
            @"runs\r\attempts\a\final.sealed", "sha256:final", 128, false,
            ArtifactCaptureOutcome.Captured, ArtifactSensitivity.RedactedBestEffort, ArtifactRetentionPolicy.RetainUntilRunDeleted, Now));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetAgentAttemptStatusQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetAgentAttemptStatusQuery(run.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.HasAttempt);
        Assert.Equal(attempt.Id, result.Value.AttemptId);
        Assert.Equal(1, result.Value.AttemptNumber);
        Assert.Equal(AttemptStatus.Completed, result.Value.Status);
        Assert.Equal(AgentOutcome.Proposed, result.Value.Outcome);
        Assert.Equal(Now, result.Value.ClaimedAtUtc);
        Assert.Equal(Now.AddSeconds(1), result.Value.DispatchedAtUtc);
        Assert.Equal(Now.AddSeconds(2), result.Value.CompletedAtUtc);

        var artifact = Assert.Single(result.Value.Artifacts);
        Assert.Equal(ArtifactPurpose.AgentFinalResponse, artifact.Purpose);
        Assert.Equal(128, artifact.ByteLength);
        Assert.False(artifact.Truncated);
        Assert.Equal(ArtifactCaptureOutcome.Captured, artifact.CaptureOutcome);
    }

    [Fact]
    public async Task HandleAsync_returns_the_highest_numbered_agent_attempt_when_more_than_one_exists()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Retried planning", Now);
        run.Claim(Now);
        var firstAttempt = ClaimAgentAttempt(run.Id, 1, Now);
        firstAttempt.CompleteAgent(AgentOutcome.ProviderInvocationFailed, null, Now.AddSeconds(1));
        var secondAttempt = ClaimAgentAttempt(run.Id, 2, Now.AddSeconds(2));
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.AddRange(firstAttempt, secondAttempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetAgentAttemptStatusQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetAgentAttemptStatusQuery(run.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.HasAttempt);
        Assert.Equal(secondAttempt.Id, result.Value.AttemptId);
        Assert.Equal(2, result.Value.AttemptNumber);
        Assert.Equal(AttemptStatus.Running, result.Value.Status);
        Assert.Null(result.Value.Outcome);
    }

    [Fact]
    public async Task HandleAsync_ignores_non_agent_attempts_on_the_run()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Mixed attempt kinds", Now);
        run.Claim(Now);
        var agentAttempt = ClaimAgentAttempt(run.Id, 1, Now);
        // A later, higher-numbered Process attempt on the same run must never be mistaken for
        // the Codex planning attempt this query reports on.
        var processAttempt = Attempt.ClaimProcess(
            Guid.NewGuid(), run.Id, 2,
            new ProcessExecutionIntent(@"C:\tools\build.exe", ["--verify"], @"C:\repos\devalcopilot", @"C:\repos", TimeSpan.FromMinutes(5), 65536, 131072),
            Now.AddSeconds(5));
        // The run-wide "one Running attempt" invariant forbids two attempts Running at once for
        // the same run regardless of kind — complete the Process attempt so this seed remains
        // legal, while still proving the query never mistakes it for the Agent attempt.
        processAttempt.Complete(Now.AddSeconds(6));
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.AddRange(agentAttempt, processAttempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetAgentAttemptStatusQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetAgentAttemptStatusQuery(run.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.HasAttempt);
        Assert.Equal(agentAttempt.Id, result.Value.AttemptId);
    }

    /// <summary>
    /// Regression for a real bug this handler now fixes: it used to pick "the most recent Agent
    /// attempt of any role", so once a run also had ClaudeCode critical-review attempts, this
    /// query could silently start returning a critical-review attempt's status under what is
    /// documented as the Codex planning status endpoint. With both present on the same run, this
    /// query must return the Codex Planner attempt and exclude the Claude CriticalReviewer one
    /// even though the CriticalReviewer attempt has the higher, more recent attempt number.
    /// </summary>
    [Fact]
    public async Task HandleAsync_returns_the_planner_attempt_and_excludes_a_more_recent_critical_reviewer_attempt_on_the_same_run()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Plan then review", Now);
        run.Claim(Now);
        var planningAttempt = ClaimAgentAttempt(run.Id, 1, Now);
        planningAttempt.MarkAgentDispatched(Now.AddSeconds(1));
        planningAttempt.CompleteAgent(AgentOutcome.Proposed, Fingerprint, Now.AddSeconds(2), processEvidence: TestProcessEvidence.CleanExit);

        // More recent (higher AttemptNumber) than the planning attempt, and Completed rather than
        // Running, so both attempts can legally coexist on the same run under the run-wide
        // "at most one Running attempt" invariant — never mistaken for the Planner attempt this
        // query reports on despite being the more recent of the two.
        var reviewAttempt = Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), run.Id, 2, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now.AddSeconds(3));
        reviewAttempt.MarkAgentDispatched(Now.AddSeconds(4));
        reviewAttempt.CompleteAgent(AgentOutcome.Accepted, Fingerprint, Now.AddSeconds(5), processEvidence: TestProcessEvidence.CleanExit);

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.AddRange(planningAttempt, reviewAttempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetAgentAttemptStatusQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetAgentAttemptStatusQuery(run.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.HasAttempt);
        Assert.Equal(planningAttempt.Id, result.Value.AttemptId);
        Assert.Equal(1, result.Value.AttemptNumber);
        Assert.Equal(AgentOutcome.Proposed, result.Value.Outcome);
    }

    [Fact]
    public async Task HandleAsync_projects_host_measured_evidence_as_a_sibling_of_the_semantic_outcome()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Evidence projection", Now);
        run.Claim(Now);
        var attempt = ClaimAgentAttempt(run.Id, 1, Now);
        attempt.MarkAgentDispatched(Now.AddSeconds(1));
        var timedOut = AgentProcessExecutionEvidence.Create(ProcessOutcome.TimedOut, null, TimeSpan.FromMinutes(10));
        attempt.CompleteAgent(AgentOutcome.ProviderInvocationFailed, null, Now.AddMinutes(10), timedOut);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await using var readContext = fixture.CreateContext();
        var result = await new GetAgentAttemptStatusQueryHandler(readContext).HandleAsync(new GetAgentAttemptStatusQuery(run.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AgentOutcome.ProviderInvocationFailed, result.Value.Outcome);
        Assert.Equal(timedOut, result.Value.ProcessExecution);
        Assert.Equal(TimeSpan.FromMinutes(10), result.Value.Timeout);
    }

    [Fact]
    public async Task HandleAsync_projects_absent_evidence_as_unknown_while_the_attempt_is_running()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Evidence still unknown", Now);
        run.Claim(Now);
        var attempt = ClaimAgentAttempt(run.Id, 1, Now);
        attempt.MarkAgentDispatched(Now.AddSeconds(1));
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await new GetAgentAttemptStatusQueryHandler(dbContext).HandleAsync(new GetAgentAttemptStatusQuery(run.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Outcome);
        Assert.Null(result.Value.ProcessExecution);
        Assert.Equal(TimeSpan.FromMinutes(10), result.Value.Timeout);
    }
}
