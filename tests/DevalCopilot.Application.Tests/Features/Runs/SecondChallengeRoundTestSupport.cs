using System.Security.Cryptography;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>Shared arrangement for the second challenge-round tests: one claimed, Running run with a
/// Ready workspace, current checkpoint, active lease, and observed Claude and Codex runtimes, plus
/// deterministic Git and artifact fakes that count external work and can inject a concurrent commit
/// exactly between a claim's validation and its durable commit.</summary>
internal static class SecondChallengeRoundTestSupport
{
    public static readonly DateTimeOffset Now = new(2026, 9, 29, 9, 0, 0, TimeSpan.Zero);
    public static readonly string Fingerprint = new('a', 64);

    internal sealed record Scene(Run Run, GitWorkspace Workspace, GitCheckpoint Checkpoint);

    public static async Task<Scene> SeedSceneAsync(
        DevalCopilotDbContext dbContext, bool claudeObserved = true, int maximumAgentAttempts = 16, bool seedCapabilities = true)
    {
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(
            Guid.NewGuid(), project.Id, 1, "Plan and implement the next increment", Now, maximumAgentAttempts: maximumAgentAttempts);
        run.Claim(Now);

        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", Now);
        workspace.MarkReady();

        // Checkpoint number 1 is reserved so a later result checkpoint recorded by a real handler gets number 2.
        Assert.Equal(1, workspace.ReserveCheckpointNumber());
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), Fingerprint, []);
        var lease = RepositoryMutationLease.Acquire(
            Guid.NewGuid(), project.Id, workspace.Id, BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0),
            Guid.NewGuid().ToByteArray(), Now);

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.GitWorkspaces.Add(workspace);
        dbContext.GitCheckpoints.Add(checkpoint);
        dbContext.RepositoryMutationLeases.Add(lease);

        var claude = HostCapabilitySnapshot.Seed(Capability.ClaudeCli, Now);
        if (claudeObserved)
        {
            claude.MarkDispatched(Now);
            claude.RecordSuccess(CapabilityLaunchKind.DirectExecutable, @"C:\safe\claude.exe", null, "2.1.276", Now, Now.AddMinutes(5));
        }

        var codex = HostCapabilitySnapshot.Seed(Capability.CodexCli, Now);
        codex.MarkDispatched(Now);
        codex.RecordSuccess(CapabilityLaunchKind.DirectExecutable, @"C:\safe\codex.exe", null, "1.2.3", Now, Now.AddMinutes(5));
        if (seedCapabilities)
        {
            dbContext.HostCapabilitySnapshots.Add(claude);
            dbContext.HostCapabilitySnapshots.Add(codex);
        }

        await dbContext.SaveChangesAsync(CancellationToken.None);

        return new Scene(run, workspace, checkpoint);
    }

    public static PlanningLineageSeeder SeederFor(DevalCopilotDbContext dbContext, Scene scene, int nextAttemptNumber = 1) =>
        new(dbContext, scene.Run.Id, scene.Workspace.Id, scene.Checkpoint.Id, Fingerprint, Now, nextAttemptNumber);

    /// <summary>Counts Git captures and can run an action on the first one.</summary>
    internal sealed class CountingEvidenceReader(Func<Task>? onCapture = null) : IGitWorkspaceEvidenceReader
    {
        public int Captures { get; private set; }

        public async Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken)
        {
            Captures++;
            if (onCapture is not null)
            {
                await onCapture();
            }

            return new GitWorkspaceEvidenceResult(
                GitWorkspaceEvidenceOutcome.Success, new string('a', 40), Fingerprint, [], null);
        }
    }

    /// <summary>An in-memory artifact store that counts seals and orphan deletions and can run an
    /// action while a manifest is being sealed — after the claim's validation, before its commit.</summary>
    internal sealed class RecordingArtifactStore(Func<Task>? onSeal = null) : IArtifactStore
    {
        private static readonly string PartialRoot = Path.Combine(Path.GetTempPath(), "devalcopilot-second-round-partials");

        public int Seals { get; private set; }

        public HashSet<(Guid RunId, Guid AttemptId, ArtifactPurpose Purpose)> DeletedSealedFiles { get; } = [];

        public string GetPartialPath(Guid runId, Guid attemptId, ArtifactPurpose purpose) =>
            Path.Combine(PartialRoot, $"{runId:N}", $"{attemptId:N}", $"{purpose}.partial");

        public string GetSealedRelativePath(Guid runId, Guid attemptId, ArtifactPurpose purpose) =>
            $"{runId:N}/{attemptId:N}/{purpose}.sealed";

        public async Task<SealedOutputFile?> SealAsync(Guid runId, Guid attemptId, ArtifactPurpose purpose, CancellationToken cancellationToken)
        {
            Seals++;
            if (onSeal is not null)
            {
                await onSeal();
            }

            var partialPath = GetPartialPath(runId, attemptId, purpose);
            var bytes = File.Exists(partialPath) ? await File.ReadAllBytesAsync(partialPath, cancellationToken) : [];
            return new SealedOutputFile(GetSealedRelativePath(runId, attemptId, purpose), bytes.LongLength, Convert.ToHexString(SHA256.HashData(bytes)));
        }

        public string ReadManifest(Guid runId, Guid attemptId) =>
            File.ReadAllText(GetPartialPath(runId, attemptId, ArtifactPurpose.AgentContextManifest));

        public bool HasSealedFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) => false;

        public bool HasPartialFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) => false;

        public Task<SealedOutputFile?> DescribeSealedFileAsync(Guid runId, Guid attemptId, ArtifactPurpose purpose, CancellationToken cancellationToken) =>
            Task.FromResult<SealedOutputFile?>(null);

        public void DeleteOrphanedPartialFile(Guid runId, Guid attemptId, ArtifactPurpose purpose)
        {
        }

        public void DeleteOrphanedSealedFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) =>
            DeletedSealedFiles.Add((runId, attemptId, purpose));

        public Task<PartialReadWindow> ReadPartialAsync(
            Guid runId, Guid attemptId, ArtifactPurpose purpose, long fromOffset, int maxBytes, CancellationToken cancellationToken) =>
            Task.FromResult(new PartialReadWindow(string.Empty, fromOffset, 0));

        public Task<SealedReadWindow> VerifyAndReadSealedAsync(
            string relativeStoragePath, long expectedByteLength, string expectedContentHash, long fromOffset, int maxBytes,
            CancellationToken cancellationToken) =>
            Task.FromResult(new SealedReadWindow(SealedReadStatus.Missing, string.Empty, fromOffset, 0));
    }
}
