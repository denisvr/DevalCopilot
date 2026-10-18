using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>Feature-owned builder for the ADR-0052 handler tests. It reuses the production factories and adds no shared fixture state:
/// every call writes only to the database of the fixture it was created with.</summary>
internal sealed class RecordedVerificationSeed(SqliteDatabaseFixture fixture)
{
    private int _workspaceNumber;
    private int _memberNumber;

    public static readonly DateTimeOffset Now = new(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);

    public static readonly string Fingerprint = new('a', 64);

    public const string Sha = "0123456789abcdef0123456789abcdef01234567";

    public static string ContentHash(char digit = 'b') => "sha256:" + new string(digit, 64);

    public async Task<(Guid ProjectId, Guid RunId)> SeedRunAsync()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "Verification", $@"C:\repos\{Guid.NewGuid():N}", Now);
        dbContext.Projects.Add(project);
        var run = Run.RecordClassifiedIntent(
            Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), RunExecutionMode.ManualAgent, "Objective", Now);
        dbContext.Runs.Add(run);
        await dbContext.SaveChangesAsync();
        return (project.Id, run.Id);
    }

    public async Task<SeededVerificationScene> SeedAsync(
        bool diagnosis, params bool[] failed) => await SeedAsync(await SeedRunAsync(), diagnosis, 1, failed);

    /// <summary>Seeds one workspace, checkpoint, one recipe plus terminal execution per entry of <paramref name="failed"/> (true: Failed
    /// with exit code 1, false: Passed with exit code 0) and the CodeReviewer attempt that recorded them in order.</summary>
    public async Task<SeededVerificationScene> SeedAsync(
        (Guid ProjectId, Guid RunId) run, bool diagnosis, int attemptNumber, params bool[] failed)
    {
        await using var dbContext = fixture.CreateContext();
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), run.ProjectId, Interlocked.Increment(ref _workspaceNumber), $@"C:\workspaces\{Guid.NewGuid():N}", "devalcopilot/run", Sha, "main", Now);
        dbContext.GitWorkspaces.Add(workspace);
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, Sha, Fingerprint, []);
        dbContext.GitCheckpoints.Add(checkpoint);
        await dbContext.SaveChangesAsync();

        var members = new List<SeededVerificationMember>();
        for (var index = 0; index < failed.Length; index++)
        {
            members.Add(await SeedMemberAsync(dbContext, run.ProjectId, workspace, checkpoint, Interlocked.Increment(ref _memberNumber), failed[index]));
        }

        var attempt = ClaimAttempt(run.RunId, attemptNumber, workspace.Id, checkpoint.Id, diagnosis);
        dbContext.Attempts.Add(attempt);
        for (var index = 0; index < members.Count; index++)
        {
            dbContext.AttemptVerificationEvidence.Add(RecordMembership(attempt.Id, members[index], index, diagnosis));
        }

        await dbContext.SaveChangesAsync();
        return new SeededVerificationScene(run.ProjectId, run.RunId, workspace, checkpoint, attempt, members);
    }

    public static Attempt ClaimAttempt(Guid runId, int number, Guid workspaceId, Guid checkpointId, bool diagnosis)
    {
        var timeout = TimeSpan.FromMinutes(10);
        return diagnosis
            ? Attempt.ClaimAgentVerificationDiagnosis(
                Guid.NewGuid(), runId, number, workspaceId, checkpointId, Fingerprint, Guid.NewGuid(), timeout, 1024, 2048, Now, null, null, number)
            : Attempt.ClaimAgentCodeReview(
                Guid.NewGuid(), runId, number, workspaceId, checkpointId, Fingerprint, Guid.NewGuid(), timeout, 1024, 2048, Now, number);
    }

    public static AttemptVerificationEvidence RecordMembership(Guid attemptId, SeededVerificationMember member, int position, bool diagnosis) =>
        diagnosis
            ? AttemptVerificationEvidence.RecordDiagnosisSnapshot(
                Guid.NewGuid(), attemptId, member.Command.Id, member.Execution.Id, position, new string('c', 64))
            : AttemptVerificationEvidence.Record(Guid.NewGuid(), attemptId, member.Command.Id, member.Execution.Id, position);

    /// <summary>One recipe and one terminal execution bound to the given workspace and checkpoint; execution numbers are project-scoped.</summary>
    public async Task<SeededVerificationMember> SeedMemberAsync(
        DevalCopilotDbContext dbContext, Guid projectId, GitWorkspace workspace, GitCheckpoint checkpoint, int number, bool failed)
    {
        var command = VerificationCommand.Configure(
            Guid.NewGuid(), projectId, number, $"Command {number}", @"C:\tools\verify.exe", ["--secret-argument"], 60, true, Now);
        dbContext.Add(command);
        await dbContext.SaveChangesAsync();
        var execution = VerificationExecution.Claim(Guid.NewGuid(), projectId, number, workspace, checkpoint, command, Now.AddSeconds(number));
        execution.MarkDispatched(Now.AddSeconds(number + 1));
        execution.Complete(VerificationExecutionOutcome.Exited, failed ? 1 : 0, checkpoint.FingerprintSha256, Now.AddSeconds(number + 2));
        dbContext.Add(execution);
        await dbContext.SaveChangesAsync();
        return new SeededVerificationMember(command, execution);
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
}
