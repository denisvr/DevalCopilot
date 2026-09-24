using DevalCopilot.Application.Features.Runs.Queries.GetEligibleChallengeResolutionAttempts;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// Mirrors <c>GetEligibleClaudeCriticalReviewAttemptsQueryHandlerTests</c> exactly, adapted for
/// the Codex + Resolver filter and the ordered original-Proposal-plus-Challenge-set projection
/// this eligibility feed carries.
/// </summary>
public sealed class GetEligibleChallengeResolutionAttemptsQueryHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 15, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<(Project Project, Run Run, GitWorkspace Workspace, Attempt Attempt, Artifact Manifest, Guid OriginalProposalId, List<Guid> ChallengeIds)>
        SeedEligibleAttemptAsync(
            DevalCopilotDbContext dbContext,
            bool runIsRunning = true,
            bool seedWorkspace = true,
            bool workspaceReady = true,
            bool seedManifestArtifact = true,
            bool markDispatched = false,
            bool leaseActive = true,
            bool checkpointIsCurrent = true,
            int challengeCount = 2)
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
        var originalProposalId = Guid.NewGuid();
        var challengeIds = Enumerable.Range(0, challengeCount).Select(_ => Guid.NewGuid()).ToList();
        var attempt = Attempt.ClaimAgentChallengeResolution(
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
        dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, originalProposalId, sequence: 0));
        for (var index = 0; index < challengeIds.Count; index++)
        {
            dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, challengeIds[index], sequence: index + 1));
        }

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

        return (project, run, workspace, attempt, manifest!, originalProposalId, challengeIds);
    }

    [Fact]
    public async Task HandleAsync_returns_an_eligible_attempt_with_its_ordered_original_proposal_and_challenge_ids()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, workspace, attempt, manifest, originalProposalId, challengeIds) = await SeedEligibleAttemptAsync(dbContext, challengeCount: 3);

        var handler = new GetEligibleChallengeResolutionAttemptsQueryHandler(dbContext);
        var eligible = await handler.HandleAsync(new GetEligibleChallengeResolutionAttemptsQuery(), CancellationToken.None);

        var result = Assert.Single(eligible);
        Assert.Equal(attempt.Id, result.AttemptId);
        Assert.Equal(run.Id, result.RunId);
        Assert.Equal(workspace.Id, result.GitWorkspaceId);
        Assert.Equal(workspace.WorkspacePath, result.WorkspacePath);
        Assert.Equal(manifest.RelativeStoragePath, result.ContextManifestRelativeStoragePath);
        Assert.Equal(originalProposalId, result.OriginalProposalMessageId);
        Assert.Equal(challengeIds, result.ChallengeMessageIds);
    }

    [Fact]
    public async Task HandleAsync_excludes_a_running_resolution_attempt_whose_run_is_no_longer_running()
    {
        await using var dbContext = _fixture.CreateContext();
        await SeedEligibleAttemptAsync(dbContext, runIsRunning: false);

        var handler = new GetEligibleChallengeResolutionAttemptsQueryHandler(dbContext);
        var eligible = await handler.HandleAsync(new GetEligibleChallengeResolutionAttemptsQuery(), CancellationToken.None);

        Assert.Empty(eligible);
    }

    [Fact]
    public async Task HandleAsync_excludes_an_attempt_already_marked_dispatched()
    {
        await using var dbContext = _fixture.CreateContext();
        await SeedEligibleAttemptAsync(dbContext, markDispatched: true);

        var handler = new GetEligibleChallengeResolutionAttemptsQueryHandler(dbContext);
        var eligible = await handler.HandleAsync(new GetEligibleChallengeResolutionAttemptsQuery(), CancellationToken.None);

        Assert.Empty(eligible);
    }

    [Fact]
    public async Task HandleAsync_excludes_an_attempt_whose_workspace_is_not_ready()
    {
        await using var dbContext = _fixture.CreateContext();
        await SeedEligibleAttemptAsync(dbContext, workspaceReady: false);

        var handler = new GetEligibleChallengeResolutionAttemptsQueryHandler(dbContext);
        var eligible = await handler.HandleAsync(new GetEligibleChallengeResolutionAttemptsQuery(), CancellationToken.None);

        Assert.Empty(eligible);
    }

    [Fact]
    public async Task HandleAsync_excludes_an_attempt_whose_workspace_has_no_active_mutation_lease()
    {
        await using var dbContext = _fixture.CreateContext();
        await SeedEligibleAttemptAsync(dbContext, leaseActive: false);

        var handler = new GetEligibleChallengeResolutionAttemptsQueryHandler(dbContext);
        var eligible = await handler.HandleAsync(new GetEligibleChallengeResolutionAttemptsQuery(), CancellationToken.None);

        Assert.Empty(eligible);
    }

    [Fact]
    public async Task HandleAsync_excludes_an_attempt_whose_claimed_checkpoint_is_no_longer_the_workspaces_current_one()
    {
        await using var dbContext = _fixture.CreateContext();
        await SeedEligibleAttemptAsync(dbContext, checkpointIsCurrent: false);

        var handler = new GetEligibleChallengeResolutionAttemptsQueryHandler(dbContext);
        var eligible = await handler.HandleAsync(new GetEligibleChallengeResolutionAttemptsQuery(), CancellationToken.None);

        Assert.Empty(eligible);
    }

    [Fact]
    public async Task HandleAsync_excludes_an_attempt_whose_context_manifest_artifact_no_longer_exists()
    {
        await using var dbContext = _fixture.CreateContext();
        await SeedEligibleAttemptAsync(dbContext, seedManifestArtifact: false);

        var handler = new GetEligibleChallengeResolutionAttemptsQueryHandler(dbContext);
        var eligible = await handler.HandleAsync(new GetEligibleChallengeResolutionAttemptsQuery(), CancellationToken.None);

        Assert.Empty(eligible);
    }

    [Fact]
    public async Task HandleAsync_excludes_a_claude_critical_review_attempt_even_though_it_is_otherwise_eligible()
    {
        await using var dbContext = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Review the next increment", Now);
        run.Claim(Now);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", Now);
        workspace.MarkReady();
        var manifestArtifactId = Guid.NewGuid();
        var attempt = Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), run.Id, 1, workspace.Id, Guid.NewGuid(), Fingerprint, manifestArtifactId,
            TimeSpan.FromMinutes(10), 262144, 524288, Now, 1);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.GitWorkspaces.Add(workspace);
        dbContext.GitCheckpoints.Add(GitCheckpoint.Capture(attempt.AgentGitCheckpointId!.Value, workspace.Id, 1, Now, new string('a', 40), Fingerprint, []));
        dbContext.RepositoryMutationLeases.Add(RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, new byte[16], Now));
        dbContext.Attempts.Add(attempt);
        dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, Guid.NewGuid(), sequence: 0));
        dbContext.Artifacts.Add(Artifact.Record(
            manifestArtifactId, run.Id, attempt.Id, ArtifactPurpose.AgentContextManifest, "application/json",
            @"runs\r\attempts\a\manifest.sealed", "sha256:manifest", 256, false,
            ArtifactCaptureOutcome.Captured, ArtifactSensitivity.HostConstructedContent, ArtifactRetentionPolicy.RetainUntilRunDeleted, Now));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetEligibleChallengeResolutionAttemptsQueryHandler(dbContext);
        var eligible = await handler.HandleAsync(new GetEligibleChallengeResolutionAttemptsQuery(), CancellationToken.None);

        Assert.Empty(eligible);
    }

    [Fact]
    public async Task HandleAsync_returns_nothing_when_no_resolution_attempt_is_eligible()
    {
        await using var dbContext = _fixture.CreateContext();

        var handler = new GetEligibleChallengeResolutionAttemptsQueryHandler(dbContext);
        var eligible = await handler.HandleAsync(new GetEligibleChallengeResolutionAttemptsQuery(), CancellationToken.None);

        Assert.Empty(eligible);
    }
}
