using DevalCopilot.Application.Features.Runs.Queries.GetEligibleImplementationAttempts;
using DevalCopilot.Application.Features.Runs.Queries.GetEligibleReviewCorrectionAttempts;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>The claimed turn-limit request and adapter contract version flow unchanged through both
/// mutating-path eligibility feeds, so a supervisor invokes exactly what the claim recorded.</summary>
public sealed class GetEligibleMutationAttemptsTurnLimitTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private delegate Attempt AttemptFactory(Guid runId, Guid workspaceId, Guid checkpointId, Guid manifestId);

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(37)]
    [InlineData(100)]
    public async Task The_implementation_feed_projects_the_claimed_limit_and_its_v2_version(int? limit)
    {
        await using var dbContext = _fixture.CreateContext();
        await SeedAsync(dbContext, (runId, workspaceId, checkpointId, manifestId) =>
            Attempt.ClaimAgentImplementationWithAssignment(
                Guid.NewGuid(), runId, 1, workspaceId, checkpointId, Fingerprint, manifestId,
                TimeSpan.FromMinutes(20), 262144, 524288, Now, "opus", "high", AgentPermissionProfile.WorkspaceEditOnly,
                ClaudeMutationAdapterContract.ImplementationV2, 1, limit));

        var result = await new GetEligibleImplementationAttemptsQueryHandler(dbContext)
            .HandleAsync(new GetEligibleImplementationAttemptsQuery(), CancellationToken.None);

        var candidate = Assert.Single(result);
        Assert.Equal(limit, candidate.RequestedMaxTurns);
        Assert.Equal("claude-implementation-v2", candidate.AdapterContractVersion);
        Assert.Equal("opus", candidate.RequestedClaudeModel);
        Assert.Equal("high", candidate.RequestedClaudeEffort);
    }

    [Fact]
    public async Task The_implementation_feed_projects_a_legacy_v1_attempt_with_a_null_limit_and_its_v1_version()
    {
        await using var dbContext = _fixture.CreateContext();
        await SeedAsync(dbContext, (runId, workspaceId, checkpointId, manifestId) => Attempt.ClaimAgentImplementation(
            Guid.NewGuid(), runId, 1, workspaceId, checkpointId, Fingerprint, manifestId,
            TimeSpan.FromMinutes(20), 262144, 524288, Now, 1));

        var result = await new GetEligibleImplementationAttemptsQueryHandler(dbContext)
            .HandleAsync(new GetEligibleImplementationAttemptsQuery(), CancellationToken.None);

        var candidate = Assert.Single(result);
        Assert.Null(candidate.RequestedMaxTurns);
        Assert.Equal("claude-implementation-v1", candidate.AdapterContractVersion);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(37)]
    [InlineData(100)]
    public async Task The_review_correction_feed_projects_the_claimed_limit_and_its_v2_version(int? limit)
    {
        await using var dbContext = _fixture.CreateContext();
        await SeedAsync(dbContext, (runId, workspaceId, checkpointId, manifestId) =>
            Attempt.ClaimAgentReviewCorrectionWithModelRequest(
                Guid.NewGuid(), runId, 1, workspaceId, checkpointId, Fingerprint, manifestId,
                TimeSpan.FromMinutes(20), 262144, 524288, Now, "sonnet", "low", 1, limit));

        var result = await new GetEligibleReviewCorrectionAttemptsQueryHandler(dbContext)
            .HandleAsync(new GetEligibleReviewCorrectionAttemptsQuery(), CancellationToken.None);

        var candidate = Assert.Single(result);
        Assert.Equal(limit, candidate.RequestedMaxTurns);
        Assert.Equal("claude-review-correction-v2", candidate.AdapterContractVersion);
        Assert.Equal("sonnet", candidate.RequestedClaudeModel);
        Assert.Equal("low", candidate.RequestedClaudeEffort);
    }

    [Fact]
    public async Task The_review_correction_feed_projects_a_legacy_v1_attempt_with_a_null_limit_and_its_v1_version()
    {
        await using var dbContext = _fixture.CreateContext();
        await SeedAsync(dbContext, (runId, workspaceId, checkpointId, manifestId) => Attempt.ClaimAgentReviewCorrection(
            Guid.NewGuid(), runId, 1, workspaceId, checkpointId, Fingerprint, manifestId,
            TimeSpan.FromMinutes(20), 262144, 524288, Now, 1));

        var result = await new GetEligibleReviewCorrectionAttemptsQueryHandler(dbContext)
            .HandleAsync(new GetEligibleReviewCorrectionAttemptsQuery(), CancellationToken.None);

        var candidate = Assert.Single(result);
        Assert.Null(candidate.RequestedMaxTurns);
        Assert.Equal("claude-review-correction-v1", candidate.AdapterContractVersion);
    }

    [Fact]
    public async Task A_later_run_level_change_does_not_alter_what_the_feeds_project()
    {
        await using var dbContext = _fixture.CreateContext();
        var runId = await SeedAsync(dbContext, (id, workspaceId, checkpointId, manifestId) =>
            Attempt.ClaimAgentReviewCorrectionWithModelRequest(
                Guid.NewGuid(), id, 1, workspaceId, checkpointId, Fingerprint, manifestId,
                TimeSpan.FromMinutes(20), 262144, 524288, Now, null, null, 1, 7));
        await ClaudeMutationTurnLimitTestSupport.SetLimitAsync(_fixture, runId, 99);

        await using var queryContext = _fixture.CreateContext();
        var result = await new GetEligibleReviewCorrectionAttemptsQueryHandler(queryContext)
            .HandleAsync(new GetEligibleReviewCorrectionAttemptsQuery(), CancellationToken.None);

        Assert.Equal(7, Assert.Single(result).RequestedMaxTurns);
    }

    private async Task<Guid> SeedAsync(DevalCopilotDbContext dbContext, AttemptFactory attemptFactory)
    {
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Objective", Now);
        run.Claim(Now);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", Now);
        workspace.MarkReady();
        var physicalIdentity = project.Id.ToByteArray();
        var lease = RepositoryMutationLease.Acquire(
            Guid.NewGuid(), project.Id, workspace.Id, BitConverter.ToUInt64(physicalIdentity), physicalIdentity, Now);
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), Fingerprint, []);
        var manifestId = Guid.NewGuid();
        var attempt = attemptFactory(run.Id, workspace.Id, checkpoint.Id, manifestId);

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
        return run.Id;
    }
}
