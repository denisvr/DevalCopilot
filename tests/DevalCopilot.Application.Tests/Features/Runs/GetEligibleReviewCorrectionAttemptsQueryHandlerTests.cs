using DevalCopilot.Application.Features.Runs.Queries.GetEligibleReviewCorrectionAttempts;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed class GetEligibleReviewCorrectionAttemptsQueryHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 14, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);
    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();
    public Task DisposeAsync() => fixture.DisposeAsync();

    [Fact]
    public async Task HandleAsync_returns_only_an_undispatched_current_correction_with_ordered_findings()
    {
        await using var dbContext = fixture.CreateContext();
        var eligible = await SeedAsync(dbContext, 1, dispatched: false);
        await SeedAsync(dbContext, 2, dispatched: true);

        var result = await new GetEligibleReviewCorrectionAttemptsQueryHandler(dbContext)
            .HandleAsync(new GetEligibleReviewCorrectionAttemptsQuery(), CancellationToken.None);

        var candidate = Assert.Single(result);
        Assert.Equal(eligible.Attempt.Id, candidate.AttemptId);
        Assert.Equal(eligible.FirstFindingId, candidate.OrderedInputMessageIds[0]);
        Assert.Equal(eligible.SecondFindingId, candidate.OrderedInputMessageIds[1]);
        Assert.DoesNotContain(eligible.ReportId, candidate.OrderedInputMessageIds);
    }

    private async Task<Seed> SeedAsync(DevalCopilotDbContext dbContext, int number, bool dispatched)
    {
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Correct the implementation", Now);
        run.Claim(Now);
        var workspace = GitWorkspace.Prepare(Guid.NewGuid(), project.Id, number, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", Now);
        workspace.MarkReady();
        var physicalIdentity = project.Id.ToByteArray();
        var lease = RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, BitConverter.ToUInt64(physicalIdentity), physicalIdentity, Now);
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), Fingerprint, []);
        var manifestId = Guid.NewGuid();
        var attempt = Attempt.ClaimAgentReviewCorrection(Guid.NewGuid(), run.Id, 1, workspace.Id, checkpoint.Id, Fingerprint, manifestId, TimeSpan.FromMinutes(20), 262144, 524288, Now);
        if (dispatched) attempt.MarkAgentDispatched(Now);

        var reportId = Guid.NewGuid();
        var firstFindingId = Guid.NewGuid();
        var secondFindingId = Guid.NewGuid();
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.GitWorkspaces.Add(workspace);
        dbContext.RepositoryMutationLeases.Add(lease);
        dbContext.GitCheckpoints.Add(checkpoint);
        dbContext.Attempts.Add(attempt);
        dbContext.AttemptInputMessages.AddRange(
            AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, reportId, 0),
            AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, firstFindingId, 1),
            AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, secondFindingId, 2));
        dbContext.Artifacts.Add(Artifact.Record(
            manifestId, run.Id, attempt.Id, ArtifactPurpose.AgentContextManifest, "application/json",
            $"runs/{run.Id}/attempts/{attempt.Id}/manifest.sealed", "sha256:manifest", 256, false,
            ArtifactCaptureOutcome.Captured, ArtifactSensitivity.HostConstructedContent,
            ArtifactRetentionPolicy.RetainUntilRunDeleted, Now));
        await dbContext.SaveChangesAsync(CancellationToken.None);
        return new Seed(attempt, reportId, firstFindingId, secondFindingId);
    }

    private sealed record Seed(Attempt Attempt, Guid ReportId, Guid FirstFindingId, Guid SecondFindingId);
}
