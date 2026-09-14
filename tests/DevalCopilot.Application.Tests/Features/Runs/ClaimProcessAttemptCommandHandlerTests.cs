using DevalCopilot.Application.Features.Runs.Commands.ClaimProcessAttempt;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed class ClaimProcessAttemptCommandHandlerTests(SqliteDatabaseFixture fixture)
    : IClassFixture<SqliteDatabaseFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);

    private static ProcessExecutionIntent CreateIntent() => new(
        ExecutablePath: @"C:\tools\build.exe",
        Arguments: ["--verify"],
        WorkingDirectory: @"C:\repos\devalcopilot",
        ApprovedRoot: @"C:\repos",
        Timeout: TimeSpan.FromMinutes(5),
        MaxBytesPerStream: 65536,
        MaxTotalCapturedBytes: 131072);

    private static (Project Project, Run Run) CreateRun()
    {
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Run a real command", Now);
        return (project, run);
    }

    [Fact]
    public async Task HandleAsync_claims_the_run_and_creates_a_process_attempt_with_its_intent_persisted()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run) = CreateRun();
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var intent = CreateIntent();
        var handler = new ClaimProcessAttemptCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(new ClaimProcessAttemptCommand(run.Id, intent), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.AttemptNumber);
        Assert.Equal(RunLifecycle.Running, run.Lifecycle);

        // The handler itself never calls SaveChanges — that is the transaction pipeline's
        // job in production — so the test performs it explicitly to assert against what was
        // actually persisted, not just what this context's change tracker holds in memory.
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var attempt = Assert.Single(dbContext.Attempts, candidate => candidate.RunId == run.Id);
        Assert.Equal(AttemptKind.Process, attempt.Kind);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Equal(intent.ExecutablePath, attempt.ProcessExecutablePath);
        Assert.Equal(intent.Arguments, attempt.ProcessArguments);
        Assert.Equal(intent.WorkingDirectory, attempt.ProcessWorkingDirectory);
        Assert.Equal(intent.ApprovedRoot, attempt.ProcessApprovedRoot);
        Assert.Equal(intent.Timeout, attempt.ProcessTimeout);
        Assert.Equal(intent.MaxBytesPerStream, attempt.ProcessMaxBytesPerStream);
        Assert.Equal(intent.MaxTotalCapturedBytes, attempt.ProcessMaxTotalCapturedBytes);
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_run_does_not_exist()
    {
        await using var dbContext = fixture.CreateContext();

        var handler = new ClaimProcessAttemptCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(new ClaimProcessAttemptCommand(Guid.NewGuid(), CreateIntent()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("runs.not_found", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_and_does_not_mutate_the_run_when_it_was_already_claimed()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run) = CreateRun();
        run.Claim(Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new ClaimProcessAttemptCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(new ClaimProcessAttemptCommand(run.Id, CreateIntent()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("runs.already_claimed", Assert.Single(result.Errors).Code);
        Assert.Empty(dbContext.Attempts.Where(candidate => candidate.RunId == run.Id));
    }
}
