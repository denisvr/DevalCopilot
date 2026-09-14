using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs.Queries.GetProcessAttemptOutput;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed class GetProcessAttemptOutputQueryHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private static ProcessExecutionIntent CreateIntent() => new(
        ExecutablePath: @"C:\tools\build.exe", Arguments: ["--verify"], WorkingDirectory: @"C:\repos\devalcopilot",
        ApprovedRoot: @"C:\repos", Timeout: TimeSpan.FromMinutes(5), MaxBytesPerStream: 65536, MaxTotalCapturedBytes: 131072);

    private sealed class FakeArtifactStore : IArtifactStore
    {
        public PartialReadWindow? PartialResponse { get; set; }
        public SealedReadWindow? SealedResponse { get; set; }

        public string GetPartialPath(Guid runId, Guid attemptId, ArtifactPurpose purpose) => "unused";
        public string GetSealedRelativePath(Guid runId, Guid attemptId, ArtifactPurpose purpose) => "unused";
        public Task<SealedOutputFile?> SealAsync(Guid runId, Guid attemptId, ArtifactPurpose purpose, CancellationToken ct) =>
            Task.FromResult<SealedOutputFile?>(null);
        public bool HasSealedFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) => false;
        public bool HasPartialFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) => false;
        public Task<SealedOutputFile?> DescribeSealedFileAsync(Guid runId, Guid attemptId, ArtifactPurpose purpose, CancellationToken ct) =>
            Task.FromResult<SealedOutputFile?>(null);
        public void DeleteOrphanedPartialFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) { }

        public Task<PartialReadWindow> ReadPartialAsync(
            Guid runId, Guid attemptId, ArtifactPurpose purpose, long fromOffset, int maxBytes, CancellationToken ct) =>
            Task.FromResult(PartialResponse ?? new PartialReadWindow(string.Empty, fromOffset, 0));

        public Task<SealedReadWindow> VerifyAndReadSealedAsync(
            string relativeStoragePath, long expectedByteLength, string expectedContentHash, long fromOffset, int maxBytes, CancellationToken ct) =>
            Task.FromResult(SealedResponse ?? new SealedReadWindow(SealedReadStatus.Missing, string.Empty, fromOffset, 0));
    }

    [Fact]
    public async Task HandleAsync_returns_attempt_not_found_for_an_unknown_attempt()
    {
        await using var dbContext = _fixture.CreateContext();
        var store = new FakeArtifactStore();
        var handler = new GetProcessAttemptOutputQueryHandler(dbContext, store);

        var result = await handler.HandleAsync(
            new GetProcessAttemptOutputQuery(Guid.NewGuid(), Guid.NewGuid(), ArtifactPurpose.ProcessStandardOutput, 0, 1024),
            CancellationToken.None);

        Assert.Equal(ProcessAttemptOutputStatus.AttemptNotFound, result.Status);
        Assert.True(result.IsFinal);
    }

    [Fact]
    public async Task HandleAsync_reads_from_the_partial_file_while_the_attempt_is_running()
    {
        await using var dbContext = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}");
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "running", Now);
        run.Claim(Now);
        var attempt = Attempt.ClaimProcess(Guid.NewGuid(), run.Id, 1, CreateIntent(), Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var store = new FakeArtifactStore { PartialResponse = new PartialReadWindow("partial output", 42, 100) };
        var handler = new GetProcessAttemptOutputQueryHandler(dbContext, store);

        var result = await handler.HandleAsync(
            new GetProcessAttemptOutputQuery(run.Id, attempt.Id, ArtifactPurpose.ProcessStandardOutput, 0, 1024),
            CancellationToken.None);

        Assert.Equal(ProcessAttemptOutputStatus.Ok, result.Status);
        Assert.Equal("partial output", result.Text);
        Assert.Equal(42, result.NextOffset);
        Assert.Equal(100, result.TotalLengthSoFar);
        Assert.False(result.IsFinal);
    }

    [Fact]
    public async Task HandleAsync_reads_the_verified_sealed_artifact_once_one_is_recorded()
    {
        await using var dbContext = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}");
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "done", Now);
        run.Claim(Now);
        var attempt = Attempt.ClaimProcess(Guid.NewGuid(), run.Id, 1, CreateIntent(), Now);
        attempt.CompleteProcess(ProcessOutcome.Exited, 0, Now);
        run.Complete(Now);
        var artifact = Artifact.Record(
            Guid.NewGuid(), run.Id, attempt.Id, ArtifactPurpose.ProcessStandardOutput, "text/plain; charset=utf-8",
            @"runs\r\attempts\a\stdout.sealed", "sha256:abc", 20, true, ArtifactCaptureOutcome.Captured, ArtifactSensitivity.RedactedBestEffort, ArtifactRetentionPolicy.RetainUntilRunDeleted, Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        dbContext.Artifacts.Add(artifact);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var store = new FakeArtifactStore { SealedResponse = new SealedReadWindow(SealedReadStatus.Ok, "sealed output", 20, 20) };
        var handler = new GetProcessAttemptOutputQueryHandler(dbContext, store);

        var result = await handler.HandleAsync(
            new GetProcessAttemptOutputQuery(run.Id, attempt.Id, ArtifactPurpose.ProcessStandardOutput, 0, 1024),
            CancellationToken.None);

        Assert.Equal(ProcessAttemptOutputStatus.Ok, result.Status);
        Assert.Equal("sealed output", result.Text);
        Assert.True(result.IsFinal);
        Assert.True(result.Truncated);
    }

    [Fact]
    public async Task HandleAsync_surfaces_an_integrity_mismatch_rather_than_serving_unverified_bytes()
    {
        await using var dbContext = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}");
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "done", Now);
        run.Claim(Now);
        var attempt = Attempt.ClaimProcess(Guid.NewGuid(), run.Id, 1, CreateIntent(), Now);
        attempt.CompleteProcess(ProcessOutcome.Exited, 0, Now);
        run.Complete(Now);
        var artifact = Artifact.Record(
            Guid.NewGuid(), run.Id, attempt.Id, ArtifactPurpose.ProcessStandardOutput, "text/plain; charset=utf-8",
            @"runs\r\attempts\a\stdout.sealed", "sha256:abc", 20, false, ArtifactCaptureOutcome.Captured, ArtifactSensitivity.RedactedBestEffort, ArtifactRetentionPolicy.RetainUntilRunDeleted, Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        dbContext.Artifacts.Add(artifact);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var store = new FakeArtifactStore
        {
            SealedResponse = new SealedReadWindow(SealedReadStatus.IntegrityMismatch, string.Empty, 0, 999),
        };
        var handler = new GetProcessAttemptOutputQueryHandler(dbContext, store);

        var result = await handler.HandleAsync(
            new GetProcessAttemptOutputQuery(run.Id, attempt.Id, ArtifactPurpose.ProcessStandardOutput, 0, 1024),
            CancellationToken.None);

        Assert.Equal(ProcessAttemptOutputStatus.IntegrityMismatch, result.Status);
        Assert.Equal(string.Empty, result.Text);
    }

    [Fact]
    public async Task HandleAsync_reports_no_output_available_for_a_terminal_attempt_with_no_recorded_artifact()
    {
        await using var dbContext = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}");
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "simulated", Now);
        run.Claim(Now);
        var attempt = Attempt.Claim(Guid.NewGuid(), run.Id, 1, Now);
        attempt.Complete(Now);
        run.Complete(Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetProcessAttemptOutputQueryHandler(dbContext, new FakeArtifactStore());

        var result = await handler.HandleAsync(
            new GetProcessAttemptOutputQuery(run.Id, attempt.Id, ArtifactPurpose.ProcessStandardOutput, 0, 1024),
            CancellationToken.None);

        Assert.Equal(ProcessAttemptOutputStatus.NoOutputAvailable, result.Status);
        Assert.True(result.IsFinal);
    }
}
