using DevalCopilot.Application.Features.Runs.Queries.GetTerminalAgentAttempts;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed class GetTerminalAgentAttemptsQueryHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 17, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private static Attempt ClaimAgentAttempt(Guid runId) => Attempt.ClaimAgent(
        Guid.NewGuid(), runId, 1, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
        TimeSpan.FromMinutes(10), 262144, 524288, Now, 1);

    [Fact]
    public async Task HandleAsync_returns_only_non_running_agent_attempts()
    {
        await using var dbContext = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        dbContext.Projects.Add(project);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var runningRun = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "running", Now);
        runningRun.Claim(Now);
        var runningAttempt = ClaimAgentAttempt(runningRun.Id);

        var completedRun = Run.RecordIntent(Guid.NewGuid(), project.Id, 2, "completed", Now);
        completedRun.Claim(Now);
        var completedAttempt = ClaimAgentAttempt(completedRun.Id);
        completedAttempt.MarkAgentDispatched(Now);
        completedAttempt.CompleteAgent(AgentOutcome.Proposed, Fingerprint, Now, processEvidence: TestProcessEvidence.CleanExit);
        completedRun.Complete(Now);

        var interruptedRun = Run.RecordIntent(Guid.NewGuid(), project.Id, 3, "interrupted", Now);
        interruptedRun.Claim(Now);
        var interruptedAttempt = ClaimAgentAttempt(interruptedRun.Id);
        interruptedAttempt.Interrupt(Now);
        interruptedRun.MarkInterrupted(Now);

        dbContext.Runs.AddRange(runningRun, completedRun, interruptedRun);
        dbContext.Attempts.AddRange(runningAttempt, completedAttempt, interruptedAttempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetTerminalAgentAttemptsQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetTerminalAgentAttemptsQuery(), CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, a => a.AttemptId == completedAttempt.Id);
        Assert.Contains(result, a => a.AttemptId == interruptedAttempt.Id);
        Assert.DoesNotContain(result, a => a.AttemptId == runningAttempt.Id);
    }

    [Fact]
    public async Task HandleAsync_returns_nothing_when_no_agent_attempt_is_terminal()
    {
        await using var dbContext = _fixture.CreateContext();

        var handler = new GetTerminalAgentAttemptsQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetTerminalAgentAttemptsQuery(), CancellationToken.None);

        Assert.Empty(result);
    }
}
