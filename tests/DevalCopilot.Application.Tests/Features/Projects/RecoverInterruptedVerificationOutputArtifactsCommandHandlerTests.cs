using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Commands.RecordVerificationExecutionResult;
using DevalCopilot.Application.Features.Projects.Commands.RecoverInterruptedVerificationOutputArtifacts;
using DevalCopilot.Application.Features.Projects.Queries.GetVerificationExecutionOutput;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Projects;

public sealed class RecoverInterruptedVerificationOutputArtifactsCommandHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 10, 0, 0, TimeSpan.Zero);
    private readonly SqliteDatabaseFixture _fixture = new();
    private readonly string _artifactRoot = Path.Combine(Path.GetTempPath(), $"devalcopilot-verification-recovery-{Guid.NewGuid():N}");

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public async Task DisposeAsync()
    {
        await _fixture.DisposeAsync();
        if (Directory.Exists(_artifactRoot))
        {
            Directory.Delete(_artifactRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Restart_imports_sealed_but_unrecorded_stdout_and_stderr_without_fabricating_outcome()
    {
        var executionId = await AddRunningExecutionAsync();
        var artifactStore = new FilesystemArtifactStore(_artifactRoot);
        await WriteAndSealAsync(artifactStore, executionId, VerificationOutputPurpose.StandardOutput, "stdout after crash");
        await WriteAndSealAsync(artifactStore, executionId, VerificationOutputPurpose.StandardError, "stderr after crash");

        var firstRecovery = await RecoverAsync(artifactStore);

        Assert.Equal(2, firstRecovery);
        await using var dbContext = _fixture.CreateContext();
        var execution = Assert.Single(dbContext.VerificationExecutions);
        Assert.Equal(VerificationExecutionStatus.Running, execution.Status);
        Assert.Null(execution.Outcome);
        Assert.Equal(2, dbContext.VerificationOutputArtifacts.Count());
        Assert.All(dbContext.VerificationOutputArtifacts, artifact =>
        {
            Assert.StartsWith("sha256:", artifact.ContentHash, StringComparison.Ordinal);
            Assert.Null(artifact.Truncated);
            Assert.Equal(VerificationOutputCaptureOutcome.RecoveredAfterHostInterruption, artifact.CaptureOutcome);
        });
    }

    [Fact]
    public async Task Repeated_restart_recovery_does_not_create_duplicate_rows()
    {
        var executionId = await AddRunningExecutionAsync();
        var artifactStore = new FilesystemArtifactStore(_artifactRoot);
        await WriteAndSealAsync(artifactStore, executionId, VerificationOutputPurpose.StandardOutput, "one import");
        await WriteAndSealAsync(artifactStore, executionId, VerificationOutputPurpose.StandardError, "one error import");

        Assert.Equal(2, await RecoverAsync(artifactStore));
        Assert.Equal(0, await RecoverAsync(artifactStore));

        await using var dbContext = _fixture.CreateContext();
        Assert.Equal(2, dbContext.VerificationOutputArtifacts.Count());
    }

    [Fact]
    public async Task Partial_only_files_are_cleaned_up_and_never_exposed_as_sealed_output()
    {
        var (projectId, executionId) = await AddRunningExecutionAsyncWithProject();
        var artifactStore = new FilesystemArtifactStore(_artifactRoot);
        var partialPath = artifactStore.GetPartialPath(executionId, VerificationOutputPurpose.StandardOutput);
        Directory.CreateDirectory(Path.GetDirectoryName(partialPath)!);
        await File.WriteAllTextAsync(partialPath, "partial after crash");

        Assert.Equal(0, await RecoverAsync(artifactStore));

        await using var dbContext = _fixture.CreateContext();
        Assert.Empty(dbContext.VerificationOutputArtifacts);
        Assert.False(File.Exists(partialPath));
        var output = new GetVerificationExecutionOutputQueryHandler(dbContext, artifactStore);
        var result = await output.HandleAsync(
            new GetVerificationExecutionOutputQuery(
                projectId, executionId, VerificationOutputPurpose.StandardOutput, 0, 1024),
            CancellationToken.None);
        Assert.True(result.IsFailure);
        Assert.Equal("verification.output_not_found", result.Errors[0].Code);
    }

    [Fact]
    public async Task Recovery_is_ownership_scoped_to_each_running_execution()
    {
        var first = await AddRunningExecutionAsyncWithProject();
        var second = await AddRunningExecutionAsyncWithProject();
        var artifactStore = new FilesystemArtifactStore(_artifactRoot);
        await WriteAndSealAsync(artifactStore, second.ExecutionId, VerificationOutputPurpose.StandardOutput, "belongs to second");

        Assert.Equal(1, await RecoverAsync(artifactStore));

        await using var dbContext = _fixture.CreateContext();
        Assert.Empty(dbContext.VerificationOutputArtifacts.Where(artifact => artifact.VerificationExecutionId == first.ExecutionId));
        var imported = Assert.Single(dbContext.VerificationOutputArtifacts);
        Assert.Equal(second.ExecutionId, imported.VerificationExecutionId);
        Assert.DoesNotContain(first.ExecutionId.ToString(), imported.RelativeStoragePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Same_host_recording_preserves_known_truncation()
    {
        var executionId = await AddRunningExecutionAsync();
        await using var dbContext = _fixture.CreateContext();
        var execution = await dbContext.VerificationExecutions.SingleAsync();
        var handler = new RecordVerificationExecutionResultCommandHandler(dbContext, new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(
            new RecordVerificationExecutionResultCommand(
                executionId,
                VerificationExecutionOutcome.Exited,
                0,
                execution.CheckpointFingerprintSha256,
                [new SealedVerificationOutputArtifact(
                    VerificationOutputPurpose.StandardOutput,
                    $@"verifications\{executionId}\stdout.sealed",
                    5,
                    "sha256:known",
                    false)]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        var artifact = Assert.Single(dbContext.VerificationOutputArtifacts);
        Assert.False(artifact.Truncated);
        Assert.Equal(VerificationOutputCaptureOutcome.CapturedWithKnownTruncation, artifact.CaptureOutcome);
    }

    private async Task<int> RecoverAsync(FilesystemArtifactStore artifactStore)
    {
        await using var dbContext = _fixture.CreateContext();
        var handler = new RecoverInterruptedVerificationOutputArtifactsCommandHandler(
            dbContext,
            artifactStore,
            new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new RecoverInterruptedVerificationOutputArtifactsCommand(), CancellationToken.None);
        Assert.True(result.IsSuccess);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        return result.Value;
    }

    private async Task<Guid> AddRunningExecutionAsync()
    {
        var result = await AddRunningExecutionAsyncWithProject();
        return result.ExecutionId;
    }

    private async Task<(Guid ProjectId, Guid ExecutionId)> AddRunningExecutionAsyncWithProject()
    {
        await using var dbContext = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "Project", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var workspace = GitWorkspace.Prepare(Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", Now);
        workspace.MarkReady();
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), new string('b', 64), []);
        var recipe = VerificationCommand.Configure(Guid.NewGuid(), project.Id, 1, "Tests", @"C:\dotnet.exe", ["test"], 60, true, Now);
        dbContext.Projects.Add(project);
        dbContext.GitWorkspaces.Add(workspace);
        dbContext.GitCheckpoints.Add(checkpoint);
        dbContext.VerificationCommands.Add(recipe);
        dbContext.RepositoryMutationLeases.Add(RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, Guid.NewGuid().ToByteArray(), Now));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var execution = VerificationExecution.Claim(
            Guid.NewGuid(), project.Id, 1, workspace, checkpoint, recipe, Now);
        execution.MarkDispatched(Now);
        dbContext.VerificationExecutions.Add(execution);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        return (project.Id, execution.Id);
    }

    private static async Task WriteAndSealAsync(
        FilesystemArtifactStore artifactStore,
        Guid executionId,
        VerificationOutputPurpose purpose,
        string contents)
    {
        var partialPath = artifactStore.GetPartialPath(executionId, purpose);
        Directory.CreateDirectory(Path.GetDirectoryName(partialPath)!);
        await File.WriteAllTextAsync(partialPath, contents);
        Assert.NotNull(await artifactStore.SealAsync(executionId, purpose, CancellationToken.None));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
