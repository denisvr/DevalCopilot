using DevalCopilot.Application.Features.Runs.Commands.RecordInterruptedProcessOutputArtifact;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed class RecordInterruptedProcessOutputArtifactCommandHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private static ProcessExecutionIntent CreateIntent() => new(
        ExecutablePath: @"C:\tools\build.exe",
        Arguments: ["--verify"],
        WorkingDirectory: @"C:\repos\devalcopilot",
        ApprovedRoot: @"C:\repos",
        Timeout: TimeSpan.FromMinutes(5),
        MaxBytesPerStream: 65536,
        MaxTotalCapturedBytes: 131072);

    /// <summary>
    /// The real state recovery finds an attempt in: still <c>Running</c>, dispatched, about to
    /// be reconciled to <c>Interrupted</c> — recovery always runs before that reconciliation.
    /// </summary>
    private static (Project Project, Run Run, Attempt Attempt) CreateDispatchedRunningProcessAttempt()
    {
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}");
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Orphaned by a crash", Now);
        run.Claim(Now);
        var attempt = Attempt.ClaimProcess(Guid.NewGuid(), run.Id, 1, CreateIntent(), Now);
        attempt.MarkProcessDispatched(Now);
        return (project, run, attempt);
    }

    private static RecordInterruptedProcessOutputArtifactCommand CreateCommand(Guid runId, Guid attemptId) => new(
        runId, attemptId, ArtifactPurpose.ProcessStandardOutput, @"runs\r\attempts\a\stdout.sealed", 123, "sha256:abc");

    [Fact]
    public async Task HandleAsync_imports_a_recovered_artifact_as_partial_host_interrupted()
    {
        await using var dbContext = _fixture.CreateContext();
        var (project, run, attempt) = CreateDispatchedRunningProcessAttempt();
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordInterruptedProcessOutputArtifactCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(10)));
        var result = await handler.HandleAsync(CreateCommand(run.Id, attempt.Id), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);

        var persisted = dbContext.Artifacts.Single(a => a.AttemptId == attempt.Id);
        Assert.Equal(ArtifactCaptureOutcome.PartialHostInterrupted, persisted.CaptureOutcome);
        Assert.Equal(123, persisted.ByteLength);
        Assert.False(persisted.Truncated);
        Assert.Equal(ArtifactSensitivity.RedactedBestEffort, persisted.Sensitivity);
        Assert.Equal(ArtifactRetentionPolicy.RetainUntilRunDeleted, persisted.RetentionPolicy);

        var events = dbContext.Events.Where(e => e.AttemptId == attempt.Id && e.EventType == RunEventType.ProcessOutputCaptured).ToList();
        Assert.Single(events);
    }

    [Fact]
    public async Task HandleAsync_is_idempotent_and_never_creates_a_second_artifact_for_the_same_attempt_and_purpose()
    {
        await using var dbContext = _fixture.CreateContext();
        var (project, run, attempt) = CreateDispatchedRunningProcessAttempt();
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordInterruptedProcessOutputArtifactCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(10)));
        var command = CreateCommand(run.Id, attempt.Id);

        var first = await handler.HandleAsync(command, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        // A later restart recovering the same evidence again — the exact idempotency scenario.
        var second = await handler.HandleAsync(command, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(first.Value);
        Assert.True(second.IsSuccess);
        Assert.False(second.Value);

        Assert.Single(dbContext.Artifacts.Where(a => a.AttemptId == attempt.Id));
        Assert.Single(dbContext.Events.Where(e => e.AttemptId == attempt.Id && e.EventType == RunEventType.ProcessOutputCaptured));
    }

    [Fact]
    public async Task HandleAsync_does_not_import_a_second_artifact_when_a_normal_capture_already_recorded_one()
    {
        await using var dbContext = _fixture.CreateContext();
        var (project, run, attempt) = CreateDispatchedRunningProcessAttempt();
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        dbContext.Artifacts.Add(Artifact.Record(
            Guid.NewGuid(), run.Id, attempt.Id, ArtifactPurpose.ProcessStandardOutput, "text/plain; charset=utf-8",
            @"runs\r\attempts\a\stdout.sealed", "sha256:already-there", 10, false, ArtifactCaptureOutcome.Captured,
            ArtifactSensitivity.RedactedBestEffort, ArtifactRetentionPolicy.RetainUntilRunDeleted, Now));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordInterruptedProcessOutputArtifactCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(10)));
        var result = await handler.HandleAsync(
            new RecordInterruptedProcessOutputArtifactCommand(
                run.Id, attempt.Id, ArtifactPurpose.ProcessStandardOutput, @"runs\r\attempts\a\stdout.sealed", 999, "sha256:different"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
        Assert.Single(dbContext.Artifacts.Where(a => a.AttemptId == attempt.Id));
        Assert.Equal("sha256:already-there", dbContext.Artifacts.Single(a => a.AttemptId == attempt.Id).ContentHash);
    }

    [Fact]
    public async Task HandleAsync_fails_without_mutation_when_the_attempt_does_not_exist()
    {
        await using var dbContext = _fixture.CreateContext();

        var handler = new RecordInterruptedProcessOutputArtifactCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(CreateCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.not_found", Assert.Single(result.Errors).Code);
        Assert.Empty(dbContext.Artifacts);
        Assert.Empty(dbContext.Events);
    }

    [Fact]
    public async Task HandleAsync_fails_without_mutation_when_the_attempt_belongs_to_a_different_run()
    {
        await using var dbContext = _fixture.CreateContext();
        var (project, run, attempt) = CreateDispatchedRunningProcessAttempt();
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordInterruptedProcessOutputArtifactCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(CreateCommand(Guid.NewGuid(), attempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.not_found", Assert.Single(result.Errors).Code);
        Assert.Empty(dbContext.Artifacts);
        Assert.Empty(dbContext.Events);
    }

    [Fact]
    public async Task HandleAsync_fails_without_mutation_for_a_simulated_attempt()
    {
        await using var dbContext = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}");
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Simulated run", Now);
        run.Claim(Now);
        var attempt = Attempt.Claim(Guid.NewGuid(), run.Id, 1, Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordInterruptedProcessOutputArtifactCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(CreateCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.not_process", Assert.Single(result.Errors).Code);
        Assert.Empty(dbContext.Artifacts);
        Assert.Empty(dbContext.Events);
    }

    [Theory]
    [MemberData(nameof(NonRunningStates))]
    public async Task HandleAsync_fails_without_mutation_when_the_attempt_is_not_running(
        Action<Attempt, DateTimeOffset> moveToNonRunningState)
    {
        await using var dbContext = _fixture.CreateContext();
        var (project, run, attempt) = CreateDispatchedRunningProcessAttempt();
        moveToNonRunningState(attempt, Now.AddMinutes(1));
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordInterruptedProcessOutputArtifactCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(10)));
        var result = await handler.HandleAsync(CreateCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.not_active", Assert.Single(result.Errors).Code);
        Assert.Empty(dbContext.Artifacts);
        Assert.Empty(dbContext.Events);
    }

    public static TheoryData<Action<Attempt, DateTimeOffset>> NonRunningStates() => new()
    {
        (attempt, now) => attempt.CompleteProcess(ProcessOutcome.Exited, 0, now),
        (attempt, now) => attempt.Fail(now),
        (attempt, now) => attempt.Interrupt(now),
    };

    [Fact]
    public async Task HandleAsync_fails_without_mutation_for_an_unsupported_purpose()
    {
        await using var dbContext = _fixture.CreateContext();
        var (project, run, attempt) = CreateDispatchedRunningProcessAttempt();
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordInterruptedProcessOutputArtifactCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(10)));
        var result = await handler.HandleAsync(
            new RecordInterruptedProcessOutputArtifactCommand(
                run.Id, attempt.Id, (ArtifactPurpose)99, @"runs\r\attempts\a\stdout.sealed", 123, "sha256:abc"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("artifacts.unsupported_purpose", Assert.Single(result.Errors).Code);
        Assert.Empty(dbContext.Artifacts);
        Assert.Empty(dbContext.Events);
    }
}
