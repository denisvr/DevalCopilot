using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs.Commands.ClaimProcessAttempt;
using DevalCopilot.Application.Features.Runs.Commands.MarkProcessAttemptDispatched;
using DevalCopilot.Application.Features.Runs.Commands.RecordProcessAttemptResult;
using DevalCopilot.Application.Features.Runs.Queries.GetEligibleProcessAttempts;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;
using DomainProcessOutcome = DevalCopilot.Domain.Features.Runs.ProcessOutcome;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Processes;

/// <summary>
/// Drives the Claim → reconstruct → execute → record collaborators directly, one call at a
/// time, against the real fixture executable and the real
/// <see cref="ChildProcessExecutionAdapter"/> — proving they work together end to end. This
/// does not start <c>ProcessAttemptSupervisor</c> itself (see
/// <c>ProcessAttemptSupervisorHostedTests</c> in DevalCopilot.Api.IntegrationTests for that).
/// </summary>
public sealed class ProcessAttemptCollaboratorPipelineTests : IClassFixture<SqliteFileFixture>, IDisposable
{
    private static readonly DateTimeOffset ClaimedAt = new(2026, 9, 13, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RecordedAt = new(2026, 9, 13, 9, 0, 5, TimeSpan.Zero);
    private static readonly string FixtureExecutablePath = Path.Combine(AppContext.BaseDirectory, "ProcessExecutionFixture.exe");

    private readonly SqliteFileFixture _fixture;
    private readonly string _approvedRoot =
        Path.Combine(Path.GetTempPath(), $"devalcopilot-hosted-path-tests-{Guid.NewGuid():N}");

    public ProcessAttemptCollaboratorPipelineTests(SqliteFileFixture fixture)
    {
        _fixture = fixture;
        Directory.CreateDirectory(_approvedRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_approvedRoot))
        {
            Directory.Delete(_approvedRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Driving_claim_reconstruction_execution_and_recording_directly_reaches_a_completed_terminal_state()
    {
        await using var dbContext = _fixture.CreateContext();
        await dbContext.Database.MigrateAsync();

        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", @"C:\repos\hosted-path-test");
        dbContext.Projects.Add(project);
        await dbContext.SaveChangesAsync();

        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), "Run a real fixture command", ClaimedAt);
        dbContext.Runs.Add(run);
        await dbContext.SaveChangesAsync();

        var intent = new ProcessExecutionIntent(
            ExecutablePath: FixtureExecutablePath,
            Arguments: ["exit-code", "0"],
            WorkingDirectory: _approvedRoot,
            ApprovedRoot: _approvedRoot,
            Timeout: TimeSpan.FromSeconds(10),
            MaxBytesPerStream: ProcessExecutionRequest.DefaultMaxBytesPerStream,
            MaxTotalCapturedBytes: ProcessExecutionRequest.DefaultMaxTotalCapturedBytes);

        var claimHandler = new ClaimProcessAttemptCommandHandler(dbContext, new FixedTimeProvider(ClaimedAt));
        var claimResult = await claimHandler.HandleAsync(new ClaimProcessAttemptCommand(run.Id, intent), CancellationToken.None);
        Assert.True(claimResult.IsSuccess);
        await dbContext.SaveChangesAsync();

        // Reconstructs the request only from what was actually persisted — never from the
        // `intent` local variable above — proving the durable intent round-trips correctly.
        var eligibleHandler = new GetEligibleProcessAttemptsQueryHandler(dbContext);
        var eligibleAttempts = await eligibleHandler.HandleAsync(new GetEligibleProcessAttemptsQuery(), CancellationToken.None);
        var eligibleAttempt = Assert.Single(eligibleAttempts);

        Assert.Equal(claimResult.Value.AttemptId, eligibleAttempt.AttemptId);
        Assert.Equal(run.Id, eligibleAttempt.RunId);
        Assert.Equal(intent.ExecutablePath, eligibleAttempt.ExecutablePath);
        Assert.Equal(intent.Arguments, eligibleAttempt.Arguments);
        Assert.Equal(intent.WorkingDirectory, eligibleAttempt.WorkingDirectory);
        Assert.Equal(intent.ApprovedRoot, eligibleAttempt.ApprovedRoot);
        Assert.Equal(intent.Timeout, eligibleAttempt.Timeout);
        Assert.Equal(intent.MaxBytesPerStream, eligibleAttempt.MaxBytesPerStream);
        Assert.Equal(intent.MaxTotalCapturedBytes, eligibleAttempt.MaxTotalCapturedBytes);

        // Durably committed before the adapter is ever invoked, exactly as the real
        // supervisor does — the execution-start claim that guarantees at most one invocation.
        var dispatchHandler = new MarkProcessAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(ClaimedAt));
        var dispatchResult = await dispatchHandler.HandleAsync(
            new MarkProcessAttemptDispatchedCommand(eligibleAttempt.RunId, eligibleAttempt.AttemptId), CancellationToken.None);
        Assert.True(dispatchResult.IsSuccess);
        await dbContext.SaveChangesAsync();

        var request = new ProcessExecutionRequest
        {
            ExecutablePath = eligibleAttempt.ExecutablePath,
            Arguments = eligibleAttempt.Arguments,
            WorkingDirectory = eligibleAttempt.WorkingDirectory,
            ApprovedRoot = eligibleAttempt.ApprovedRoot,
            Timeout = eligibleAttempt.Timeout,
            MaxBytesPerStream = eligibleAttempt.MaxBytesPerStream,
            MaxTotalCapturedBytes = eligibleAttempt.MaxTotalCapturedBytes,
            EnvironmentVariables = new Dictionary<string, string>(),
        };

        var adapter = new ChildProcessExecutionAdapter();
        var executionResult = await adapter.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal(ProcessExecutionOutcome.Exited, executionResult.Outcome);
        Assert.Equal(0, executionResult.ExitCode);

        var recordHandler = new RecordProcessAttemptResultCommandHandler(dbContext, new FixedTimeProvider(RecordedAt));
        var recordResult = await recordHandler.HandleAsync(
            new RecordProcessAttemptResultCommand(
                eligibleAttempt.RunId, eligibleAttempt.AttemptId, DomainProcessOutcome.Exited, executionResult.ExitCode, []),
            CancellationToken.None);
        Assert.True(recordResult.IsSuccess);
        await dbContext.SaveChangesAsync();

        var persistedAttempt = await dbContext.Attempts.FindAsync(eligibleAttempt.AttemptId);
        var persistedRun = await dbContext.Runs.FindAsync(run.Id);

        Assert.NotNull(persistedAttempt);
        Assert.Equal(AttemptStatus.Completed, persistedAttempt.Status);
        Assert.Equal(DomainProcessOutcome.Exited, persistedAttempt.ProcessOutcome);
        Assert.Equal(0, persistedAttempt.ProcessExitCode);
        Assert.NotNull(persistedAttempt.ProcessDispatchedAtUtc);
        Assert.NotNull(persistedRun);
        Assert.Equal(RunLifecycle.Completed, persistedRun.Lifecycle);
    }
}
