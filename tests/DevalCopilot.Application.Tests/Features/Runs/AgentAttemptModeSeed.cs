using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>Seeds one claimed, otherwise fully eligible Agent attempt (Ready workspace, active lease, current
/// checkpoint, sealed manifest) on a Running run of a chosen stored execution mode.</summary>
internal static class AgentAttemptModeSeed
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);

    public static Task<(Guid RunId, Guid AttemptId)> SeedClaimedAsync(SqliteDatabaseFixture fixture, string path, int storedMode) =>
        SeedClaimedRawAsync(fixture, path, storedMode.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>The same seed with an exact SQLite literal stored as the execution mode.</summary>
    public static async Task<(Guid RunId, Guid AttemptId)> SeedClaimedRawAsync(
        SqliteDatabaseFixture fixture, string path, string storedLiteral)
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Objective", Now);
        run.Claim(Now);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", Now);
        workspace.MarkReady();
        var identity = project.Id.ToByteArray();
        var lease = RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, BitConverter.ToUInt64(identity), identity, Now);
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), Fingerprint, []);
        var manifestId = Guid.NewGuid();
        var attempt = Claim(path, run.Id, workspace.Id, checkpoint.Id, manifestId);

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.GitWorkspaces.Add(workspace);
        dbContext.RepositoryMutationLeases.Add(lease);
        dbContext.GitCheckpoints.Add(checkpoint);
        dbContext.Attempts.Add(attempt);
        dbContext.AttemptInputMessages.AddRange(
            AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, Guid.NewGuid(), 0),
            AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, Guid.NewGuid(), 1),
            AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, Guid.NewGuid(), 2));
        dbContext.Artifacts.Add(Artifact.Record(
            manifestId, run.Id, attempt.Id, ArtifactPurpose.AgentContextManifest, "application/json",
            $"runs/{run.Id}/attempts/{attempt.Id}/manifest.sealed", "sha256:manifest", 256, false,
            ArtifactCaptureOutcome.Captured, ArtifactSensitivity.HostConstructedContent,
            ArtifactRetentionPolicy.RetainUntilRunDeleted, Now));
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await RunExecutionModeTestSupport.SetStoredRawAsync(dbContext, run.Id, storedLiteral);
        return (run.Id, attempt.Id);
    }

    private static Attempt Claim(string path, Guid runId, Guid workspaceId, Guid checkpointId, Guid manifestId)
    {
        var timeout = TimeSpan.FromMinutes(10);
        return path switch
        {
            "planning" => Attempt.ClaimAgent(Guid.NewGuid(), runId, 1, workspaceId, checkpointId, Fingerprint, manifestId, timeout, 262144, 524288, Now, 1),
            "critical-review" => Attempt.ClaimAgentCriticalReview(Guid.NewGuid(), runId, 1, workspaceId, checkpointId, Fingerprint, manifestId, timeout, 262144, 524288, Now, 1),
            "challenge-resolution" => Attempt.ClaimAgentChallengeResolution(Guid.NewGuid(), runId, 1, workspaceId, checkpointId, Fingerprint, manifestId, timeout, 262144, 524288, Now, 1),
            "implementation" => Attempt.ClaimAgentImplementation(Guid.NewGuid(), runId, 1, workspaceId, checkpointId, Fingerprint, manifestId, timeout, 262144, 524288, Now, 1),
            "code-review" => Attempt.ClaimAgentCodeReview(Guid.NewGuid(), runId, 1, workspaceId, checkpointId, Fingerprint, manifestId, timeout, 262144, 524288, Now, 1),
            "review-correction" => Attempt.ClaimAgentReviewCorrection(Guid.NewGuid(), runId, 1, workspaceId, checkpointId, Fingerprint, manifestId, timeout, 262144, 524288, Now, 1),
            _ => throw new ArgumentOutOfRangeException(nameof(path)),
        };
    }
}
