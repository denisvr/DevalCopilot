using DevalCopilot.Application.Features.Runs.Queries.GetTerminalProcessAttempts;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed class GetTerminalProcessAttemptsQueryHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private static ProcessExecutionIntent CreateIntent() => new(
        ExecutablePath: @"C:\tools\build.exe", Arguments: ["--verify"], WorkingDirectory: @"C:\repos\devalcopilot",
        ApprovedRoot: @"C:\repos", Timeout: TimeSpan.FromMinutes(5), MaxBytesPerStream: 65536, MaxTotalCapturedBytes: 131072);

    [Fact]
    public async Task HandleAsync_returns_only_non_running_process_attempts()
    {
        await using var dbContext = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}");
        dbContext.Projects.Add(project);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var runningRun = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "running", Now);
        runningRun.Claim(Now);
        var runningAttempt = Attempt.ClaimProcess(Guid.NewGuid(), runningRun.Id, 1, CreateIntent(), Now);

        var completedRun = Run.RecordIntent(Guid.NewGuid(), project.Id, 2, "completed", Now);
        completedRun.Claim(Now);
        var completedAttempt = Attempt.ClaimProcess(Guid.NewGuid(), completedRun.Id, 1, CreateIntent(), Now);
        completedAttempt.CompleteProcess(ProcessOutcome.Exited, 0, Now);
        completedRun.Complete(Now);

        dbContext.Runs.AddRange(runningRun, completedRun);
        dbContext.Attempts.AddRange(runningAttempt, completedAttempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetTerminalProcessAttemptsQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetTerminalProcessAttemptsQuery(), CancellationToken.None);

        var only = Assert.Single(result);
        Assert.Equal(completedAttempt.Id, only.AttemptId);
    }
}
