using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Infrastructure.Persistence;

namespace DevalCopilot.Application.Tests.Features.Projects.ProjectVerificationInspection;

/// <summary>Feature-owned builder for the ADR-0069 tests. It reuses the production Domain factories and transitions over a migrated
/// file-backed SQLite database and adds no shared fixture state: every call writes only to the database of the fixture it was created
/// with. No Agent attempt, run or local execution sequence is ever created for an execution it seeds.</summary>
internal sealed class ProjectVerificationInspectionSeed(SqliteDatabaseFixture fixture)
{
    private int _workspaceNumber;

    public static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 26, 9, 0, 0, TimeSpan.Zero).AddTicks(1234567);

    public static readonly string Fingerprint = new('a', 64);

    public static readonly string OtherFingerprint = new('b', 64);

    public const string Sha = "0123456789abcdef0123456789abcdef01234567";

    public SqliteDatabaseFixture Fixture => fixture;

    public async Task<Guid> SeedProjectAsync()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "Verification", $@"C:\repos\{Guid.NewGuid():N}", Now);
        dbContext.Projects.Add(project);
        await dbContext.SaveChangesAsync();
        return project.Id;
    }

    /// <summary>One workspace with one checkpoint of the given number and fingerprint.</summary>
    public async Task<(GitWorkspace Workspace, GitCheckpoint Checkpoint)> SeedWorkspaceAsync(
        Guid projectId, int checkpointNumber = 1, string? fingerprint = null)
    {
        await using var dbContext = fixture.CreateContext();
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), projectId, Interlocked.Increment(ref _workspaceNumber), $@"C:\workspaces\{Guid.NewGuid():N}", "devalcopilot/run", Sha, "main", Now);
        dbContext.GitWorkspaces.Add(workspace);
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, checkpointNumber, Now, Sha, fingerprint ?? Fingerprint, []);
        dbContext.GitCheckpoints.Add(checkpoint);
        await dbContext.SaveChangesAsync();
        return (workspace, checkpoint);
    }

    public async Task<GitCheckpoint> AddCheckpointAsync(GitWorkspace workspace, int number, string fingerprint)
    {
        await using var dbContext = fixture.CreateContext();
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, number, Now.AddMinutes(number), Sha, fingerprint, []);
        dbContext.GitCheckpoints.Add(checkpoint);
        await dbContext.SaveChangesAsync();
        return checkpoint;
    }

    public async Task<VerificationCommand> SeedCommandAsync(Guid projectId, int number = 1, string name = "Unit tests")
    {
        await using var dbContext = fixture.CreateContext();
        var command = VerificationCommand.Configure(
            Guid.NewGuid(), projectId, number, name, @"C:\tools\verify.exe", ["--secret-argument"], 60, true, Now);
        dbContext.Add(command);
        await dbContext.SaveChangesAsync();
        return command;
    }

    public async Task<SeededExecution> SeedExecutionAsync(
        Guid projectId,
        GitWorkspace workspace,
        GitCheckpoint checkpoint,
        VerificationCommand command,
        int number,
        ExecutionShape shape = ExecutionShape.Passed,
        int exitCode = 3)
    {
        await using var dbContext = fixture.CreateContext();
        var execution = Build(projectId, workspace, checkpoint, command, number, shape, exitCode);
        dbContext.Add(execution);
        await dbContext.SaveChangesAsync();
        return new SeededExecution(execution.Id, projectId, number, command, workspace, checkpoint, shape);
    }

    /// <summary>Executions numbered 1 to <paramref name="count"/> (newer numbers claimed later), all Passed, written in one batch.</summary>
    public async Task<IReadOnlyList<SeededExecution>> SeedManyAsync(
        Guid projectId, GitWorkspace workspace, GitCheckpoint checkpoint, VerificationCommand command, int count)
    {
        await using var dbContext = fixture.CreateContext();
        var seeded = new List<SeededExecution>();
        for (var number = 1; number <= count; number++)
        {
            var execution = Build(projectId, workspace, checkpoint, command, number, ExecutionShape.Passed, 0);
            dbContext.Add(execution);
            seeded.Add(new SeededExecution(execution.Id, projectId, number, command, workspace, checkpoint, ExecutionShape.Passed));
        }

        await dbContext.SaveChangesAsync();
        return seeded;
    }

    public async Task<VerificationOutputArtifact> AddOutputAsync(
        Guid executionId,
        VerificationOutputPurpose purpose,
        string relativePath,
        string contentHash,
        long byteLength,
        bool? truncated = false,
        VerificationOutputCaptureOutcome capture = VerificationOutputCaptureOutcome.CapturedWithKnownTruncation)
    {
        await using var dbContext = fixture.CreateContext();
        var output = VerificationOutputArtifact.Record(
            Guid.NewGuid(), executionId, purpose, relativePath, contentHash, byteLength, truncated, capture, Now);
        dbContext.VerificationOutputArtifacts.Add(output);
        await dbContext.SaveChangesAsync();
        return output;
    }

    public static string ContentHash(char digit = 'b') => "sha256:" + new string(digit, 64);

    private static VerificationExecution Build(
        Guid projectId, GitWorkspace workspace, GitCheckpoint checkpoint, VerificationCommand command, int number, ExecutionShape shape, int exitCode)
    {
        var claimed = Now.AddSeconds(number);
        var execution = VerificationExecution.Claim(Guid.NewGuid(), projectId, number, workspace, checkpoint, command, claimed);
        var dispatched = claimed.AddTicks(10_000_001);
        var completed = dispatched.AddTicks(20_000_003);
        var fingerprint = checkpoint.FingerprintSha256;
        switch (shape)
        {
            case ExecutionShape.Running:
                break;
            case ExecutionShape.RunningDispatched:
                execution.MarkDispatched(dispatched);
                break;
            case ExecutionShape.Passed:
                execution.MarkDispatched(dispatched);
                execution.Complete(VerificationExecutionOutcome.Exited, 0, fingerprint, completed);
                break;
            case ExecutionShape.FailedExited:
                execution.MarkDispatched(dispatched);
                execution.Complete(VerificationExecutionOutcome.Exited, exitCode, fingerprint, completed);
                break;
            case ExecutionShape.FailedOutcome:
                execution.MarkDispatched(dispatched);
                execution.Complete(VerificationExecutionOutcome.Failed, null, fingerprint, completed);
                break;
            case ExecutionShape.TimedOut:
                execution.MarkDispatched(dispatched);
                execution.Complete(VerificationExecutionOutcome.TimedOut, null, fingerprint, completed);
                break;
            case ExecutionShape.Cancelled:
                execution.MarkDispatched(dispatched);
                execution.Complete(VerificationExecutionOutcome.Cancelled, null, fingerprint, completed);
                break;
            case ExecutionShape.Interrupted:
                execution.Interrupt(completed);
                break;
            case ExecutionShape.InterruptedDispatched:
                execution.MarkDispatched(dispatched);
                execution.Interrupt(completed);
                break;
            case ExecutionShape.SourceChangedBeforeDispatch:
                execution.MarkSourceChangedBeforeDispatch(OtherFingerprint, dispatched);
                break;
            case ExecutionShape.SourceChangedExited:
                execution.MarkDispatched(dispatched);
                execution.Complete(VerificationExecutionOutcome.Exited, exitCode, OtherFingerprint, completed);
                break;
            case ExecutionShape.SourceChangedTimedOut:
                execution.MarkDispatched(dispatched);
                execution.Complete(VerificationExecutionOutcome.TimedOut, null, OtherFingerprint, completed);
                break;
            case ExecutionShape.DispatchRefused:
                execution.MarkLocalExecutionSequenceDispatchRefused(dispatched);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(shape), shape, null);
        }

        return execution;
    }
}
