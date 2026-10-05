using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Projects.Queries.GetGitCheckpointDiff;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Infrastructure.Persistence;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.TrackedFixture;

namespace DevalCopilot.Application.Tests.Features.Projects;

/// <summary>ADR-0027: the protected checkpoint inspection query keeps project/checkpoint membership, a Ready workspace, an active
/// lease and exact fingerprint agreement, asks the reader only for the explicit inspection capture, and derives everything it
/// returns from attested facts. No raw patch a reader or double might hold is ever returned.</summary>
public sealed class GetGitCheckpointDiffQueryHandlerTests : IAsyncLifetime
{
    private const string RawPatchSentinel = "RAW-PATCH-SENTINEL-77c1";
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('c', 64);

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task The_comparison_is_derived_from_attested_facts_through_the_inspection_capture_only()
    {
        var scene = await SeedAsync();
        var reader = new RecordingReader(Capture(
            [Modified("a.txt"), Added("b.txt"), Untracked("u.txt")],
            Edit("a.txt", "old\n", "new\n"), Add("b.txt", "fresh\n")));

        var result = await Handler(reader).HandleAsync(new GetGitCheckpointDiffQuery(scene.ProjectId, scene.CheckpointId), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, reader.InspectionCalls);
        Assert.Equal(0, reader.OtherCalls);
        Assert.Equal(Fingerprint, result.Value.FingerprintSha256);
        Assert.True(result.Value.IsComplete);
        Assert.Equal(2, result.Value.TrackedPathCount);
        Assert.Equal(2, result.Value.ComparedPathCount);
        Assert.Contains("diff --git a/a.txt b/a.txt\n", result.Value.ComparisonText, StringComparison.Ordinal);
        Assert.Contains("--- /dev/null\n+++ b/b.txt\n", result.Value.ComparisonText, StringComparison.Ordinal);
        Assert.DoesNotContain("u.txt", result.Value.ComparisonText, StringComparison.Ordinal);
        Assert.DoesNotContain(RawPatchSentinel, result.Value.ComparisonText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_reader_that_returns_a_raw_patch_in_its_inspection_capture_cannot_make_the_query_return_it()
    {
        var scene = await SeedAsync();
        var attested = Capture([Modified("a.txt")], Omit("a.txt", GitWorkspaceTrackedOmission.ContainmentUnproven));
        var misbehaving = attested with { CompleteDiff = "diff --git a/a.txt b/a.txt\n+" + RawPatchSentinel + "\n" };

        var result = await Handler(new RecordingReader(misbehaving))
            .HandleAsync(new GetGitCheckpointDiffQuery(scene.ProjectId, scene.CheckpointId), default);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsComplete);
        Assert.Equal(string.Empty, result.Value.ComparisonText);
        Assert.Equal(new GetGitCheckpointDiffOmission("a.txt", "containment_unproven"), Assert.Single(result.Value.Omissions));
        Assert.DoesNotContain(RawPatchSentinel, System.Text.Json.JsonSerializer.Serialize(result.Value), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_reader_without_attestation_support_yields_unattested_omissions_never_its_raw_patch()
    {
        var scene = await SeedAsync();
        var legacy = new LegacyReader(Capture([Modified("a.txt"), Modified("b.txt")]) with { CompleteDiff = RawPatchSentinel });

        var result = await Handler(legacy).HandleAsync(new GetGitCheckpointDiffQuery(scene.ProjectId, scene.CheckpointId), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(string.Empty, result.Value.ComparisonText);
        Assert.False(result.Value.IsComplete);
        Assert.Equal(["a.txt:not_attested", "b.txt:not_attested"], result.Value.Omissions.Select(item => $"{item.Path}:{item.Reason}"));
        Assert.DoesNotContain(RawPatchSentinel, System.Text.Json.JsonSerializer.Serialize(result.Value), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_root_instruction_file_is_compared_for_the_human_inspection()
    {
        var scene = await SeedAsync();
        var reader = new RecordingReader(Capture([Modified("AGENTS.md")], Edit("AGENTS.md", "old\n", "new\n")));

        var result = await Handler(reader).HandleAsync(new GetGitCheckpointDiffQuery(scene.ProjectId, scene.CheckpointId), default);

        Assert.True(result.Value.IsComplete);
        Assert.Contains("diff --git a/AGENTS.md b/AGENTS.md\n", result.Value.ComparisonText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_stale_checkpoint_is_refused_without_any_comparison()
    {
        var scene = await SeedAsync();
        var reader = new RecordingReader(Capture([Modified("a.txt")], Edit("a.txt", "o\n", "n\n")) with { FingerprintSha256 = new string('d', 64) });

        var result = await Handler(reader).HandleAsync(new GetGitCheckpointDiffQuery(scene.ProjectId, scene.CheckpointId), default);

        Assert.True(result.IsFailure);
        Assert.Equal("git_evidence.stale_checkpoint", Assert.Single(result.Errors).Code);
    }

    [Theory]
    [InlineData(GitWorkspaceEvidenceOutcome.RepositoryChangedDuringCapture, "git_evidence.changed_during_capture")]
    [InlineData(GitWorkspaceEvidenceOutcome.EvidenceTooLarge, "git_evidence.too_large")]
    [InlineData(GitWorkspaceEvidenceOutcome.GitUnavailable, "git_evidence.git_unavailable")]
    [InlineData(GitWorkspaceEvidenceOutcome.GitInvocationTimedOut, "git_evidence.timed_out")]
    [InlineData(GitWorkspaceEvidenceOutcome.InvalidGitState, "git_evidence.invalid_state")]
    [InlineData(GitWorkspaceEvidenceOutcome.GitInvocationFailed, "git_evidence.capture_failed")]
    public async Task A_failed_capture_is_a_safe_refusal(GitWorkspaceEvidenceOutcome outcome, string code)
    {
        var scene = await SeedAsync();
        var reader = new RecordingReader(new GitWorkspaceEvidenceResult(outcome, null, null, [], null));

        var result = await Handler(reader).HandleAsync(new GetGitCheckpointDiffQuery(scene.ProjectId, scene.CheckpointId), default);

        Assert.Equal(code, Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task Membership_readiness_and_the_active_lease_are_checked_before_the_workspace_is_read()
    {
        var scene = await SeedAsync();
        var reader = new RecordingReader(Capture([]));

        var wrongProject = await Handler(reader).HandleAsync(new GetGitCheckpointDiffQuery(Guid.NewGuid(), scene.CheckpointId), default);
        var unknown = await Handler(reader).HandleAsync(new GetGitCheckpointDiffQuery(scene.ProjectId, Guid.NewGuid()), default);
        await using (var dbContext = _fixture.CreateContext())
        {
            var lease = dbContext.RepositoryMutationLeases.Single(item => item.WorkspaceId == scene.WorkspaceId);
            lease.Release(Now);
            await dbContext.SaveChangesAsync();
        }

        var released = await Handler(reader).HandleAsync(new GetGitCheckpointDiffQuery(scene.ProjectId, scene.CheckpointId), default);

        Assert.Equal("git_checkpoints.not_found", Assert.Single(wrongProject.Errors).Code);
        Assert.Equal("git_checkpoints.not_found", Assert.Single(unknown.Errors).Code);
        Assert.Equal("workspaces.lease_not_active", Assert.Single(released.Errors).Code);
        Assert.Equal(0, reader.InspectionCalls);
    }

    private GetGitCheckpointDiffQueryHandler Handler(IGitWorkspaceEvidenceReader reader) =>
        new(_fixture.CreateContext(), reader);

    private static GitWorkspaceEvidenceResult Capture(
        IReadOnlyList<GitWorkspaceChangedPath> paths, params GitWorkspaceTrackedFile[] facts) =>
        new(GitWorkspaceEvidenceOutcome.Success, new string('a', 40), Fingerprint, paths, null, TrackedFiles: facts);

    private async Task<Scene> SeedAsync()
    {
        await using var dbContext = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "Inspection", $@"C:\repos\{Guid.NewGuid():N}", Now);
        project.RecordPhysicalIdentityResolved(1UL, new byte[16]);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, project.ReserveWorkspaceNumber(), $@"C:\workspaces\{Guid.NewGuid():N}",
            "branch", new string('a', 40), "main", Now);
        workspace.MarkReady();
        var lease = RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1UL, new byte[16], Now);
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), Fingerprint, []);
        dbContext.Projects.Add(project);
        dbContext.GitWorkspaces.Add(workspace);
        dbContext.RepositoryMutationLeases.Add(lease);
        dbContext.GitCheckpoints.Add(checkpoint);
        await dbContext.SaveChangesAsync();
        return new Scene(project.Id, workspace.Id, checkpoint.Id);
    }

    private sealed record Scene(Guid ProjectId, Guid WorkspaceId, Guid CheckpointId);

    /// <summary>A reader that implements the inspection capture and fails any other capture, so the query cannot use a raw one.</summary>
    private sealed class RecordingReader(GitWorkspaceEvidenceResult inspection) : IGitWorkspaceEvidenceReader
    {
        public int InspectionCalls { get; private set; }

        public int OtherCalls { get; private set; }

        public Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken)
        {
            OtherCalls++;
            throw new InvalidOperationException("The inspection query must not use the ordinary raw-patch capture.");
        }

        public Task<GitWorkspaceEvidenceResult> CaptureForCheckpointInspectionAsync(
            string workspacePath, CancellationToken cancellationToken)
        {
            InspectionCalls++;
            return Task.FromResult(inspection);
        }
    }

    /// <summary>A reader written before attestation existed: only the ordinary capture, which carries a raw patch.</summary>
    private sealed class LegacyReader(GitWorkspaceEvidenceResult capture) : IGitWorkspaceEvidenceReader
    {
        public Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken) =>
            Task.FromResult(capture);
    }
}
