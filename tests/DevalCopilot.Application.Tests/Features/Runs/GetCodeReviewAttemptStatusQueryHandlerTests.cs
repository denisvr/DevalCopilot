using DevalCopilot.Application.Features.Runs.Queries.GetCodeReviewAttemptStatus;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed class GetCodeReviewAttemptStatusQueryHandlerTests(SqliteDatabaseFixture fixture)
    : IClassFixture<SqliteDatabaseFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 19, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);

    private static (Attempt Attempt, AttemptInputMessage ExecutionReport) ClaimCodeReviewAttempt(
        Guid runId, int attemptNumber, DateTimeOffset claimedAtUtc, Guid executionReportMessageId)
    {
        var attempt = Attempt.ClaimAgentCodeReview(
            Guid.NewGuid(), runId, attemptNumber, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, claimedAtUtc, attemptNumber);
        var executionReport = AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, executionReportMessageId, sequence: 0);
        return (attempt, executionReport);
    }

    [Fact]
    public async Task HandleAsync_returns_an_explicit_no_attempt_result_for_a_run_with_no_code_review_attempt_yet()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "No attempt yet", Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetCodeReviewAttemptStatusQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetCodeReviewAttemptStatusQuery(run.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.HasAttempt);
        Assert.Null(result.Value.AttemptId);
        Assert.Empty(result.Value.Artifacts);
        Assert.Null(result.Value.ConfiguredCommandSandbox);
        Assert.Null(result.Value.ConfiguredRolloutPersistence);
    }

    [Fact]
    public async Task HandleAsync_fails_with_a_safe_not_found_when_the_run_does_not_exist()
    {
        await using var dbContext = fixture.CreateContext();

        var handler = new GetCodeReviewAttemptStatusQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetCodeReviewAttemptStatusQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("runs.not_found", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_reports_the_configured_command_sandbox_for_a_coherent_default_assignment()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Review the implementation", Now);
        run.Claim(Now);
        var (attempt, executionReport) = ClaimCodeReviewAttempt(run.Id, 1, Now, Guid.NewGuid());
        attempt.MarkAgentDispatched(Now.AddSeconds(1));
        attempt.CompleteAgent(AgentOutcome.ReviewApproved, Fingerprint, Now.AddSeconds(2), processEvidence: TestProcessEvidence.CleanExit);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        dbContext.AttemptInputMessages.Add(executionReport);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetCodeReviewAttemptStatusQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetCodeReviewAttemptStatusQuery(run.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.HasAttempt);
        Assert.Equal("read-only", result.Value.ConfiguredCommandSandbox);
        Assert.Equal("Disabled", result.Value.ConfiguredRolloutPersistence);
    }

    [Fact]
    public async Task HandleAsync_reports_no_configured_command_sandbox_for_a_valid_historical_nullable_assignment()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Historical code review assignment", Now);
        run.Claim(Now);
        var (attempt, executionReport) = ClaimCodeReviewAttempt(run.Id, 1, Now, Guid.NewGuid());
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        dbContext.AttemptInputMessages.Add(executionReport);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE attempts SET AgentPermissionProfile = NULL, AgentAdapterContractVersion = NULL
            WHERE Id = {attempt.Id}
            """);

        await using var readContext = fixture.CreateContext();
        var result = await new GetCodeReviewAttemptStatusQueryHandler(readContext)
            .HandleAsync(new GetCodeReviewAttemptStatusQuery(run.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.ConfiguredCommandSandbox);
        Assert.Null(result.Value.ConfiguredRolloutPersistence);
    }

    [Fact]
    public async Task HandleAsync_reports_no_configured_command_sandbox_for_a_mismatched_adapter_contract_version()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Mismatched code review adapter contract", Now);
        run.Claim(Now);
        var (attempt, executionReport) = ClaimCodeReviewAttempt(run.Id, 1, Now, Guid.NewGuid());
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        dbContext.AttemptInputMessages.Add(executionReport);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE attempts SET AgentAdapterContractVersion = 'codex-implementation-review-v2' WHERE Id = {attempt.Id}
            """);

        await using var readContext = fixture.CreateContext();
        var result = await new GetCodeReviewAttemptStatusQueryHandler(readContext)
            .HandleAsync(new GetCodeReviewAttemptStatusQuery(run.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.ConfiguredCommandSandbox);
        Assert.Null(result.Value.ConfiguredRolloutPersistence);
    }

    [Fact]
    public async Task HandleAsync_fails_closed_for_an_unparseable_persisted_permission_profile_without_mutating_it()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Corrupt code review permission profile", Now);
        run.Claim(Now);
        var (attempt, executionReport) = ClaimCodeReviewAttempt(run.Id, 1, Now, Guid.NewGuid());
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        dbContext.AttemptInputMessages.Add(executionReport);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var sentinel = "InvalidPermissionSentinel";
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE attempts SET AgentPermissionProfile = {sentinel} WHERE Id = {attempt.Id}");

        await using var readContext = fixture.CreateContext();
        var result = await new GetCodeReviewAttemptStatusQueryHandler(readContext)
            .HandleAsync(new GetCodeReviewAttemptStatusQuery(run.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("agent_attempts.invalid_assignment", error.Code);
        Assert.DoesNotContain(sentinel, error.Description, StringComparison.Ordinal);
        var persistedValue = await readContext.Database
            .SqlQueryRaw<string>("SELECT AgentPermissionProfile AS Value FROM attempts WHERE Id = {0}", attempt.Id)
            .SingleAsync();
        Assert.Equal(sentinel, persistedValue);
    }

    [Fact]
    public async Task HandleAsync_returns_the_most_recently_numbered_attempt_when_more_than_one_exists()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Retried code review", Now);
        run.Claim(Now);
        var (firstAttempt, firstExecutionReport) = ClaimCodeReviewAttempt(run.Id, 1, Now, Guid.NewGuid());
        firstAttempt.CompleteAgent(AgentOutcome.ProviderInvocationFailed, null, Now.AddSeconds(1));
        var (secondAttempt, secondExecutionReport) = ClaimCodeReviewAttempt(run.Id, 2, Now.AddSeconds(2), Guid.NewGuid());
        secondAttempt.MarkAgentDispatched(Now.AddSeconds(3));
        secondAttempt.CompleteAgent(AgentOutcome.ReviewApproved, Fingerprint, Now.AddSeconds(4), processEvidence: TestProcessEvidence.CleanExit);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.AddRange(firstAttempt, secondAttempt);
        dbContext.AttemptInputMessages.Add(firstExecutionReport);
        dbContext.AttemptInputMessages.Add(secondExecutionReport);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetCodeReviewAttemptStatusQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetCodeReviewAttemptStatusQuery(run.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(secondAttempt.Id, result.Value.AttemptId);
        Assert.Equal(2, result.Value.AttemptNumber);
        Assert.Equal(AgentOutcome.ReviewApproved, result.Value.Outcome);
    }
}
