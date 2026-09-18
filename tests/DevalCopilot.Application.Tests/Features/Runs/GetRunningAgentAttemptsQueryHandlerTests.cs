using DevalCopilot.Application.Features.Runs.Queries.GetRunningAgentAttempts;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed class GetRunningAgentAttemptsQueryHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 16, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private static Attempt ClaimAgentAttempt(Guid runId) => Attempt.ClaimAgent(
        Guid.NewGuid(), runId, 1, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
        TimeSpan.FromMinutes(10), 262144, 524288, Now);

    [Fact]
    public async Task HandleAsync_returns_only_running_agent_attempts()
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
        completedAttempt.CompleteAgent(AgentOutcome.Proposed, Fingerprint, Now);
        completedRun.Complete(Now);

        var processRun = Run.RecordIntent(Guid.NewGuid(), project.Id, 3, "process", Now);
        processRun.Claim(Now);
        var processAttempt = Attempt.ClaimProcess(
            Guid.NewGuid(), processRun.Id, 1,
            new ProcessExecutionIntent(@"C:\tools\build.exe", ["--verify"], @"C:\repos\devalcopilot", @"C:\repos", TimeSpan.FromMinutes(5), 65536, 131072),
            Now);

        var simulatedRun = Run.RecordIntent(Guid.NewGuid(), project.Id, 4, "simulated", Now);
        simulatedRun.Claim(Now);
        var simulatedAttempt = Attempt.Claim(Guid.NewGuid(), simulatedRun.Id, 1, Now);

        dbContext.Runs.AddRange(runningRun, completedRun, processRun, simulatedRun);
        dbContext.Attempts.AddRange(runningAttempt, completedAttempt, processAttempt, simulatedAttempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetRunningAgentAttemptsQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetRunningAgentAttemptsQuery(), CancellationToken.None);

        var only = Assert.Single(result);
        Assert.Equal(runningAttempt.Id, only.AttemptId);
        Assert.Equal(runningRun.Id, only.RunId);
    }

    [Fact]
    public async Task HandleAsync_returns_nothing_when_no_agent_attempt_is_running()
    {
        await using var dbContext = _fixture.CreateContext();

        var handler = new GetRunningAgentAttemptsQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetRunningAgentAttemptsQuery(), CancellationToken.None);

        Assert.Empty(result);
    }
}
