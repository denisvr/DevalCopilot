using DevalCopilot.Application.Features.Runs.Queries.GetEligibleAgentAttempts;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// Owns a fresh database per test method rather than a shared <see cref="IClassFixture{T}"/>:
/// the eligibility query scans every Agent attempt with no per-run scoping, so a shared database
/// would let one test's eligible row leak into another test's "nothing eligible" assertion —
/// mirrors <c>ReconcileInterruptedProcessAttemptsCommandHandlerTests</c>.
/// </summary>
public sealed class GetEligibleAgentAttemptsQueryHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 15, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<(Project Project, Run Run, GitWorkspace Workspace, Attempt Attempt, Artifact Manifest)> SeedEligibleAttemptAsync(
        DevalCopilotDbContext dbContext,
        bool runIsRunning = true,
        bool seedWorkspace = true,
        bool workspaceReady = true,
        bool seedManifestArtifact = true,
        bool markDispatched = false,
        bool leaseActive = true,
        bool checkpointIsCurrent = true)
    {
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Plan the next increment", Now);
        run.Claim(Now);
        if (!runIsRunning)
        {
            run.Complete(Now);
        }

        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", Now);
        if (workspaceReady)
        {
            workspace.MarkReady();
        }

        var manifestArtifactId = Guid.NewGuid();
        var checkpointId = Guid.NewGuid();
        var attempt = Attempt.ClaimAgent(
            Guid.NewGuid(), run.Id, 1,
            seedWorkspace ? workspace.Id : Guid.NewGuid(),
            checkpointId,
            Fingerprint,
            manifestArtifactId,
            TimeSpan.FromMinutes(10), 262144, 524288, Now);
        if (markDispatched)
        {
            attempt.MarkAgentDispatched(Now);
        }

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        if (seedWorkspace)
        {
            dbContext.GitWorkspaces.Add(workspace);

            dbContext.GitCheckpoints.Add(
                GitCheckpoint.Capture(checkpointId, workspace.Id, 1, Now, new string('a', 40), Fingerprint, []));
            if (!checkpointIsCurrent)
            {
                // A newer checkpoint superseded the one this attempt claimed — the workspace
                // moved on before this attempt was ever dispatched.
                dbContext.GitCheckpoints.Add(
                    GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 2, Now, new string('b', 40), Fingerprint, []));
            }

            var lease = RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, new byte[16], Now);
            if (!leaseActive)
            {
                lease.Release(Now);
            }

            dbContext.RepositoryMutationLeases.Add(lease);
        }

        dbContext.Attempts.Add(attempt);

        Artifact? manifest = null;
        if (seedManifestArtifact)
        {
            manifest = Artifact.Record(
                manifestArtifactId, run.Id, attempt.Id, ArtifactPurpose.AgentContextManifest, "application/json",
                @"runs\r\attempts\a\manifest.sealed", "sha256:manifest", 256, false,
                ArtifactCaptureOutcome.Captured, ArtifactSensitivity.HostConstructedContent, ArtifactRetentionPolicy.RetainUntilRunDeleted, Now);
            dbContext.Artifacts.Add(manifest);
        }

        await dbContext.SaveChangesAsync(CancellationToken.None);

        return (project, run, workspace, attempt, manifest!);
    }

    [Fact]
    public async Task HandleAsync_returns_an_eligible_attempt_with_its_full_bounded_projection()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, workspace, attempt, manifest) = await SeedEligibleAttemptAsync(dbContext);

        var handler = new GetEligibleAgentAttemptsQueryHandler(dbContext);
        var eligible = await handler.HandleAsync(new GetEligibleAgentAttemptsQuery(), CancellationToken.None);

        var result = Assert.Single(eligible);
        Assert.Equal(attempt.Id, result.AttemptId);
        Assert.Equal(run.Id, result.RunId);
        Assert.Equal(workspace.Id, result.GitWorkspaceId);
        Assert.Equal(workspace.WorkspacePath, result.WorkspacePath);
        Assert.Equal(attempt.AgentGitCheckpointId, result.GitCheckpointId);
        Assert.Equal(Fingerprint, result.CheckpointFingerprintSha256);
        Assert.Equal(manifest.RelativeStoragePath, result.ContextManifestRelativeStoragePath);
        Assert.Equal(manifest.ByteLength, result.ContextManifestByteLength);
        Assert.Equal(manifest.ContentHash, result.ContextManifestContentHash);
        Assert.Equal(TimeSpan.FromMinutes(10), result.Timeout);
        Assert.Equal(262144, result.MaxBytesPerStream);
        Assert.Equal(524288, result.MaxTotalCapturedBytes);
    }

    [Fact]
    public async Task HandleAsync_excludes_a_running_agent_attempt_whose_run_is_no_longer_running()
    {
        await using var dbContext = _fixture.CreateContext();
        await SeedEligibleAttemptAsync(dbContext, runIsRunning: false);

        var handler = new GetEligibleAgentAttemptsQueryHandler(dbContext);
        var eligible = await handler.HandleAsync(new GetEligibleAgentAttemptsQuery(), CancellationToken.None);

        Assert.Empty(eligible);
    }

    [Fact]
    public async Task HandleAsync_excludes_an_attempt_already_marked_dispatched()
    {
        await using var dbContext = _fixture.CreateContext();
        await SeedEligibleAttemptAsync(dbContext, markDispatched: true);

        var handler = new GetEligibleAgentAttemptsQueryHandler(dbContext);
        var eligible = await handler.HandleAsync(new GetEligibleAgentAttemptsQuery(), CancellationToken.None);

        Assert.Empty(eligible);
    }

    [Fact]
    public async Task HandleAsync_excludes_an_attempt_whose_git_workspace_no_longer_exists()
    {
        await using var dbContext = _fixture.CreateContext();
        // Ownership/consistency gate: the attempt's claimed workspace id has no matching
        // GitWorkspace row — the inner join must exclude it rather than hand the supervisor a
        // workspace path that cannot be resolved.
        await SeedEligibleAttemptAsync(dbContext, seedWorkspace: false);

        var handler = new GetEligibleAgentAttemptsQueryHandler(dbContext);
        var eligible = await handler.HandleAsync(new GetEligibleAgentAttemptsQuery(), CancellationToken.None);

        Assert.Empty(eligible);
    }

    [Fact]
    public async Task HandleAsync_excludes_an_attempt_whose_context_manifest_artifact_no_longer_exists()
    {
        await using var dbContext = _fixture.CreateContext();
        // Same ownership/consistency gate, for the sealed context-manifest artifact.
        await SeedEligibleAttemptAsync(dbContext, seedManifestArtifact: false);

        var handler = new GetEligibleAgentAttemptsQueryHandler(dbContext);
        var eligible = await handler.HandleAsync(new GetEligibleAgentAttemptsQuery(), CancellationToken.None);

        Assert.Empty(eligible);
    }

    [Fact]
    public async Task HandleAsync_excludes_an_attempt_whose_workspace_is_not_ready()
    {
        await using var dbContext = _fixture.CreateContext();
        await SeedEligibleAttemptAsync(dbContext, workspaceReady: false);

        var handler = new GetEligibleAgentAttemptsQueryHandler(dbContext);
        var eligible = await handler.HandleAsync(new GetEligibleAgentAttemptsQuery(), CancellationToken.None);

        Assert.Empty(eligible);
    }

    [Fact]
    public async Task HandleAsync_excludes_an_attempt_whose_workspace_has_no_active_mutation_lease()
    {
        await using var dbContext = _fixture.CreateContext();
        await SeedEligibleAttemptAsync(dbContext, leaseActive: false);

        var handler = new GetEligibleAgentAttemptsQueryHandler(dbContext);
        var eligible = await handler.HandleAsync(new GetEligibleAgentAttemptsQuery(), CancellationToken.None);

        Assert.Empty(eligible);
    }

    [Fact]
    public async Task HandleAsync_excludes_an_attempt_whose_claimed_checkpoint_is_no_longer_the_workspaces_current_one()
    {
        await using var dbContext = _fixture.CreateContext();
        await SeedEligibleAttemptAsync(dbContext, checkpointIsCurrent: false);

        var handler = new GetEligibleAgentAttemptsQueryHandler(dbContext);
        var eligible = await handler.HandleAsync(new GetEligibleAgentAttemptsQuery(), CancellationToken.None);

        Assert.Empty(eligible);
    }

    [Fact]
    public async Task HandleAsync_returns_nothing_when_no_agent_attempt_is_eligible()
    {
        await using var dbContext = _fixture.CreateContext();

        var handler = new GetEligibleAgentAttemptsQueryHandler(dbContext);
        var eligible = await handler.HandleAsync(new GetEligibleAgentAttemptsQuery(), CancellationToken.None);

        Assert.Empty(eligible);
    }

    /// <summary>
    /// Regression for a real bug this handler now fixes: it used to match any Agent attempt
    /// regardless of provider, so a claimed Claude critical-review attempt would have been handed
    /// to this Codex-only supervisor, which only knows how to invoke the Codex CLI. Seeded as two
    /// separate runs rather than one — the run-wide "at most one Running attempt per run"
    /// invariant (the filtered unique index on Attempts) makes a Codex attempt and a Claude
    /// attempt simultaneously Running on the very same run impossible in the first place — but
    /// this still proves the fix: with both an otherwise-eligible Codex planning attempt and an
    /// otherwise-eligible Claude critical-review attempt present in the database at once, this
    /// query returns only the Codex one.
    /// </summary>
    [Fact]
    public async Task HandleAsync_excludes_an_eligible_claude_critical_review_attempt_and_returns_only_the_codex_one()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, _, _, codexAttempt, _) = await SeedEligibleAttemptAsync(dbContext);

        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var claudeRun = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Review the next increment", Now);
        claudeRun.Claim(Now);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", Now);
        workspace.MarkReady();
        var checkpointId = Guid.NewGuid();
        var claudeManifestArtifactId = Guid.NewGuid();
        var claudeAttempt = Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), claudeRun.Id, 1, workspace.Id, checkpointId, Fingerprint, claudeManifestArtifactId,
            TimeSpan.FromMinutes(10), 262144, 524288, Now);

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(claudeRun);
        dbContext.GitWorkspaces.Add(workspace);
        dbContext.GitCheckpoints.Add(GitCheckpoint.Capture(checkpointId, workspace.Id, 1, Now, new string('a', 40), Fingerprint, []));
        dbContext.RepositoryMutationLeases.Add(RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, Guid.NewGuid().ToByteArray(), Now));
        dbContext.Attempts.Add(claudeAttempt);
        dbContext.Artifacts.Add(Artifact.Record(
            claudeManifestArtifactId, claudeRun.Id, claudeAttempt.Id, ArtifactPurpose.AgentContextManifest, "application/json",
            @"runs\r\attempts\b\manifest.sealed", "sha256:manifest-b", 256, false,
            ArtifactCaptureOutcome.Captured, ArtifactSensitivity.HostConstructedContent, ArtifactRetentionPolicy.RetainUntilRunDeleted, Now));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetEligibleAgentAttemptsQueryHandler(dbContext);
        var eligible = await handler.HandleAsync(new GetEligibleAgentAttemptsQuery(), CancellationToken.None);

        var result = Assert.Single(eligible);
        Assert.Equal(codexAttempt.Id, result.AttemptId);
    }
}
