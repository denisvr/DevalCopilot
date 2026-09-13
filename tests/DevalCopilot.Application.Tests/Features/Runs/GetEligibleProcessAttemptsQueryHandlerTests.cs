using DevalCopilot.Application.Features.Runs.Queries.GetEligibleProcessAttempts;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed class GetEligibleProcessAttemptsQueryHandlerTests(SqliteDatabaseFixture fixture)
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

    [Fact]
    public async Task HandleAsync_returns_an_attempt_only_when_it_is_running_process_and_its_run_is_running()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}");
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Eligible", Now);
        run.Claim(Now);
        var attempt = Attempt.ClaimProcess(Guid.NewGuid(), run.Id, 1, CreateIntent(), Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetEligibleProcessAttemptsQueryHandler(dbContext);
        var eligible = await handler.HandleAsync(new GetEligibleProcessAttemptsQuery(), CancellationToken.None);

        var result = Assert.Single(eligible);
        Assert.Equal(attempt.Id, result.AttemptId);
        Assert.Equal(run.Id, result.RunId);
    }

    [Fact]
    public async Task HandleAsync_excludes_a_running_process_attempt_whose_run_is_no_longer_running()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}");
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Inconsistent state", Now);
        run.Claim(Now);
        var attempt = Attempt.ClaimProcess(Guid.NewGuid(), run.Id, 1, CreateIntent(), Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        // The run moved to a terminal state directly, leaving its attempt Running — the
        // supervisor must never be handed this attempt to execute.
        run.Complete(Now);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetEligibleProcessAttemptsQueryHandler(dbContext);
        var eligible = await handler.HandleAsync(new GetEligibleProcessAttemptsQuery(), CancellationToken.None);

        Assert.Empty(eligible);
    }

    [Fact]
    public async Task HandleAsync_excludes_an_attempt_already_marked_dispatched()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}");
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Already dispatched", Now);
        run.Claim(Now);
        var attempt = Attempt.ClaimProcess(Guid.NewGuid(), run.Id, 1, CreateIntent(), Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        // Durably marked dispatched but still Running (no terminal result recorded yet) —
        // exactly the state a stalled or failed recording leaves behind. The external
        // command must never be invoked for it again.
        attempt.MarkProcessDispatched(Now);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetEligibleProcessAttemptsQueryHandler(dbContext);
        var eligible = await handler.HandleAsync(new GetEligibleProcessAttemptsQuery(), CancellationToken.None);

        Assert.Empty(eligible);
    }
}
