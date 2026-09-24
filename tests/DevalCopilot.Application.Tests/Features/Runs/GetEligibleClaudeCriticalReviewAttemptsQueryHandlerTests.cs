using DevalCopilot.Application.Features.Runs.Queries.GetEligibleClaudeCriticalReviewAttempts;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// Mirrors <c>GetEligibleAgentAttemptsQueryHandlerTests</c> exactly, adapted for the
/// ClaudeCode + CriticalReviewer filter and the additional <c>InputCollaborationMessageId</c>
/// projection field this eligibility feed carries. Owns a fresh database per test method for the
/// same reason as its template: the query scans every attempt with no per-run scoping.
/// </summary>
public sealed class GetEligibleClaudeCriticalReviewAttemptsQueryHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 15, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<(Project Project, Run Run, GitWorkspace Workspace, Attempt Attempt, Artifact Manifest, Guid InputCollaborationMessageId)> SeedEligibleAttemptAsync(
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
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Review the next increment", Now);
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
        var inputCollaborationMessageId = Guid.NewGuid();
        var attempt = Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), run.Id, 1,
            seedWorkspace ? workspace.Id : Guid.NewGuid(),
            checkpointId,
            Fingerprint,
            manifestArtifactId,
            TimeSpan.FromMinutes(10), 262144, 524288, Now, 1);
        if (markDispatched)
        {
            attempt.MarkAgentDispatched(Now);
        }

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, inputCollaborationMessageId, sequence: 0));
        if (seedWorkspace)
        {
            dbContext.GitWorkspaces.Add(workspace);

            dbContext.GitCheckpoints.Add(
                GitCheckpoint.Capture(checkpointId, workspace.Id, 1, Now, new string('a', 40), Fingerprint, []));
            if (!checkpointIsCurrent)
            {
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

        return (project, run, workspace, attempt, manifest!, inputCollaborationMessageId);
    }

    [Fact]
    public async Task HandleAsync_returns_an_eligible_attempt_with_its_full_bounded_projection()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, workspace, attempt, manifest, inputCollaborationMessageId) = await SeedEligibleAttemptAsync(dbContext);

        var handler = new GetEligibleClaudeCriticalReviewAttemptsQueryHandler(dbContext);
        var eligible = await handler.HandleAsync(new GetEligibleClaudeCriticalReviewAttemptsQuery(), CancellationToken.None);

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
        Assert.Equal(inputCollaborationMessageId, result.InputCollaborationMessageId);
    }

    [Fact]
    public async Task HandleAsync_excludes_a_running_critical_review_attempt_whose_run_is_no_longer_running()
    {
        await using var dbContext = _fixture.CreateContext();
        await SeedEligibleAttemptAsync(dbContext, runIsRunning: false);

        var handler = new GetEligibleClaudeCriticalReviewAttemptsQueryHandler(dbContext);
        var eligible = await handler.HandleAsync(new GetEligibleClaudeCriticalReviewAttemptsQuery(), CancellationToken.None);

        Assert.Empty(eligible);
    }

    [Fact]
    public async Task HandleAsync_excludes_an_attempt_already_marked_dispatched()
    {
        await using var dbContext = _fixture.CreateContext();
        await SeedEligibleAttemptAsync(dbContext, markDispatched: true);

        var handler = new GetEligibleClaudeCriticalReviewAttemptsQueryHandler(dbContext);
        var eligible = await handler.HandleAsync(new GetEligibleClaudeCriticalReviewAttemptsQuery(), CancellationToken.None);

        Assert.Empty(eligible);
    }

    [Fact]
    public async Task HandleAsync_excludes_an_attempt_whose_git_workspace_no_longer_exists()
    {
        await using var dbContext = _fixture.CreateContext();
        await SeedEligibleAttemptAsync(dbContext, seedWorkspace: false);

        var handler = new GetEligibleClaudeCriticalReviewAttemptsQueryHandler(dbContext);
        var eligible = await handler.HandleAsync(new GetEligibleClaudeCriticalReviewAttemptsQuery(), CancellationToken.None);

        Assert.Empty(eligible);
    }

    [Fact]
    public async Task HandleAsync_excludes_an_attempt_whose_context_manifest_artifact_no_longer_exists()
    {
        await using var dbContext = _fixture.CreateContext();
        await SeedEligibleAttemptAsync(dbContext, seedManifestArtifact: false);

        var handler = new GetEligibleClaudeCriticalReviewAttemptsQueryHandler(dbContext);
        var eligible = await handler.HandleAsync(new GetEligibleClaudeCriticalReviewAttemptsQuery(), CancellationToken.None);

        Assert.Empty(eligible);
    }

    [Fact]
    public async Task HandleAsync_excludes_an_attempt_whose_workspace_is_not_ready()
    {
        await using var dbContext = _fixture.CreateContext();
        await SeedEligibleAttemptAsync(dbContext, workspaceReady: false);

        var handler = new GetEligibleClaudeCriticalReviewAttemptsQueryHandler(dbContext);
        var eligible = await handler.HandleAsync(new GetEligibleClaudeCriticalReviewAttemptsQuery(), CancellationToken.None);

        Assert.Empty(eligible);
    }

    [Fact]
    public async Task HandleAsync_excludes_an_attempt_whose_workspace_has_no_active_mutation_lease()
    {
        await using var dbContext = _fixture.CreateContext();
        await SeedEligibleAttemptAsync(dbContext, leaseActive: false);

        var handler = new GetEligibleClaudeCriticalReviewAttemptsQueryHandler(dbContext);
        var eligible = await handler.HandleAsync(new GetEligibleClaudeCriticalReviewAttemptsQuery(), CancellationToken.None);

        Assert.Empty(eligible);
    }

    [Fact]
    public async Task HandleAsync_excludes_an_attempt_whose_claimed_checkpoint_is_no_longer_the_workspaces_current_one()
    {
        await using var dbContext = _fixture.CreateContext();
        await SeedEligibleAttemptAsync(dbContext, checkpointIsCurrent: false);

        var handler = new GetEligibleClaudeCriticalReviewAttemptsQueryHandler(dbContext);
        var eligible = await handler.HandleAsync(new GetEligibleClaudeCriticalReviewAttemptsQuery(), CancellationToken.None);

        Assert.Empty(eligible);
    }

    [Fact]
    public async Task HandleAsync_excludes_a_codex_planning_attempt_even_though_it_is_otherwise_eligible()
    {
        await using var dbContext = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Plan the next increment", Now);
        run.Claim(Now);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", Now);
        workspace.MarkReady();
        var manifestArtifactId = Guid.NewGuid();
        var attempt = Attempt.ClaimAgent(
            Guid.NewGuid(), run.Id, 1, workspace.Id, Guid.NewGuid(), Fingerprint, manifestArtifactId,
            TimeSpan.FromMinutes(10), 262144, 524288, Now, 1);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.GitWorkspaces.Add(workspace);
        dbContext.GitCheckpoints.Add(GitCheckpoint.Capture(attempt.AgentGitCheckpointId!.Value, workspace.Id, 1, Now, new string('a', 40), Fingerprint, []));
        dbContext.RepositoryMutationLeases.Add(RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, new byte[16], Now));
        dbContext.Attempts.Add(attempt);
        dbContext.Artifacts.Add(Artifact.Record(
            manifestArtifactId, run.Id, attempt.Id, ArtifactPurpose.AgentContextManifest, "application/json",
            @"runs\r\attempts\a\manifest.sealed", "sha256:manifest", 256, false,
            ArtifactCaptureOutcome.Captured, ArtifactSensitivity.HostConstructedContent, ArtifactRetentionPolicy.RetainUntilRunDeleted, Now));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetEligibleClaudeCriticalReviewAttemptsQueryHandler(dbContext);
        var eligible = await handler.HandleAsync(new GetEligibleClaudeCriticalReviewAttemptsQuery(), CancellationToken.None);

        Assert.Empty(eligible);
    }

    [Fact]
    public async Task HandleAsync_returns_nothing_when_no_critical_review_attempt_is_eligible()
    {
        await using var dbContext = _fixture.CreateContext();

        var handler = new GetEligibleClaudeCriticalReviewAttemptsQueryHandler(dbContext);
        var eligible = await handler.HandleAsync(new GetEligibleClaudeCriticalReviewAttemptsQuery(), CancellationToken.None);

        Assert.Empty(eligible);
    }
}
