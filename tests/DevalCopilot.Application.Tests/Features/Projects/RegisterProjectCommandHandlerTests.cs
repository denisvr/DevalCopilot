using DevalCopilot.Application.Features.Projects.Commands.RegisterProject;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Projects;

/// <summary>
/// Drives <see cref="RegisterProjectCommandHandler"/> against fake, in-memory implementations
/// of both ports — proving the handler's own orchestration/business-rule ordering in isolation
/// from real filesystem or Git behavior, which are proven separately at the Infrastructure
/// layer. This is exactly what "Application orchestrates typed results only" makes possible.
/// </summary>
public sealed class RegisterProjectCommandHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);
    private const string CanonicalPath = @"C:\repos\devalcopilot-test";

    // A dedicated instance per test method (a new SqliteDatabaseFixture, not the shared
    // IClassFixture<T> pattern) — HostCapabilitySnapshot.Capability is a fixed, non-random key,
    // so several tests seeding "Git is ready" would otherwise collide against one shared file.
    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private sealed class FakeRootPathInspector(RepositoryRootInspectionResult result) : IRepositoryRootPathInspector
    {
        public RepositoryRootInspectionResult Inspect(string requestedPath) => result;
    }

    private sealed class FakeGitRepositoryInspector(GitRepositoryInspectionResult result) : IGitRepositoryInspector
    {
        public RepositoryRootCandidate? ReceivedCandidate { get; private set; }

        public Task<GitRepositoryInspectionResult> InspectAsync(RepositoryRootCandidate candidate, CancellationToken cancellationToken)
        {
            ReceivedCandidate = candidate;
            return Task.FromResult(result);
        }
    }

    private sealed class NeverCalledGitRepositoryInspector : IGitRepositoryInspector
    {
        public Task<GitRepositoryInspectionResult> InspectAsync(RepositoryRootCandidate candidate, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Git inspection must never be reached for this scenario.");
    }

    private static readonly GitRepositoryInspectionResult SuccessfulCleanOnBranch = new(
        GitRepositoryInspectionOutcome.Success, RepositoryHeadState.OnBranch, "main", new string('a', 40), IsDirty: false);

    private static async Task SeedGitReadyAsync(DevalCopilotDbContext dbContext)
    {
        var snapshot = HostCapabilitySnapshot.Seed(Capability.Git, Now);
        snapshot.MarkDispatched(Now);
        snapshot.RecordSuccess("C:\\Program Files\\Git\\cmd\\git.exe", "2.45.0", Now, Now.AddMinutes(5));
        dbContext.HostCapabilitySnapshots.Add(snapshot);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    [Fact]
    public async Task HandleAsync_registers_a_project_and_its_first_baseline_on_success()
    {
        await using var dbContext = _fixture.CreateContext();
        await SeedGitReadyAsync(dbContext);

        var handler = new RegisterProjectCommandHandler(
            dbContext,
            new FakeRootPathInspector(new RepositoryRootInspectionResult(
                RepositoryRootInspectionOutcome.Success, new RepositoryRootCandidate(CanonicalPath))),
            new FakeGitRepositoryInspector(SuccessfulCleanOnBranch),
            new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new RegisterProjectCommand("DevalCopilot", CanonicalPath), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        var project = await dbContext.Projects.FindAsync(result.Value.ProjectId);
        Assert.NotNull(project);
        Assert.Equal(CanonicalPath, project!.CanonicalPath);
        Assert.Equal(CanonicalPath.ToUpperInvariant(), project.RegistrationIdentityKey);

        var baseline = Assert.Single(dbContext.RepositoryBaselines.Where(b => b.ProjectId == project.Id));
        Assert.Equal(1, baseline.BaselineNumber);
        Assert.Equal(RepositoryHeadState.OnBranch, baseline.HeadState);
        Assert.Equal("main", baseline.BranchName);
        Assert.False(baseline.IsDirty);
    }

    [Fact]
    public async Task HandleAsync_rejects_a_path_already_registered_and_never_invokes_git_inspection()
    {
        await using var dbContext = _fixture.CreateContext();
        await SeedGitReadyAsync(dbContext);
        dbContext.Projects.Add(Project.Register(Guid.NewGuid(), "Existing", CanonicalPath, Now));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RegisterProjectCommandHandler(
            dbContext,
            new FakeRootPathInspector(new RepositoryRootInspectionResult(
                RepositoryRootInspectionOutcome.Success, new RepositoryRootCandidate(CanonicalPath))),
            new NeverCalledGitRepositoryInspector(),
            new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new RegisterProjectCommand("Duplicate", CanonicalPath), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("projects.already_registered", Assert.Single(result.Errors).Code);
        Assert.Equal(1, await dbContext.Projects.CountAsync());
    }

    [Fact]
    public async Task HandleAsync_treats_a_case_variant_of_an_existing_path_as_a_duplicate()
    {
        await using var dbContext = _fixture.CreateContext();
        await SeedGitReadyAsync(dbContext);
        dbContext.Projects.Add(Project.Register(Guid.NewGuid(), "Existing", @"C:\repos\devalcopilot-test", Now));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        // A differently-cased, trailing-separator spelling of the same path — normalization is
        // Infrastructure's job in production; this test drives the handler's own duplicate-key
        // comparison directly with the already-normalized candidate a real inspector would
        // have produced for that spelling.
        var handler = new RegisterProjectCommandHandler(
            dbContext,
            new FakeRootPathInspector(new RepositoryRootInspectionResult(
                RepositoryRootInspectionOutcome.Success, new RepositoryRootCandidate(@"C:\repos\DEVALCOPILOT-TEST"))),
            new NeverCalledGitRepositoryInspector(),
            new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new RegisterProjectCommand("Duplicate", "ignored"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("projects.already_registered", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_fast_when_git_is_not_ready_and_never_invokes_git_inspection()
    {
        await using var dbContext = _fixture.CreateContext();
        // No HostCapabilitySnapshot seeded at all — the closed-set "not ready" case.

        var handler = new RegisterProjectCommandHandler(
            dbContext,
            new FakeRootPathInspector(new RepositoryRootInspectionResult(
                RepositoryRootInspectionOutcome.Success, new RepositoryRootCandidate(CanonicalPath))),
            new NeverCalledGitRepositoryInspector(),
            new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new RegisterProjectCommand("DevalCopilot", CanonicalPath), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("projects.git_unavailable", Assert.Single(result.Errors).Code);
        Assert.Empty(dbContext.Projects);
    }

    [Theory]
    [InlineData(RepositoryRootInspectionOutcome.NotAbsolute, "projects.path_not_absolute")]
    [InlineData(RepositoryRootInspectionOutcome.RemoteRootNotSupported, "projects.remote_root_not_supported")]
    [InlineData(RepositoryRootInspectionOutcome.FilesystemRootNotSupported, "projects.root_not_supported")]
    [InlineData(RepositoryRootInspectionOutcome.PathNotFound, "projects.path_not_found")]
    [InlineData(RepositoryRootInspectionOutcome.PathInaccessible, "projects.path_inaccessible")]
    [InlineData(RepositoryRootInspectionOutcome.ReparsePointNotSupported, "projects.reparse_point_not_supported")]
    public async Task HandleAsync_maps_every_root_inspection_rejection_without_mutation(
        RepositoryRootInspectionOutcome outcome, string expectedCode)
    {
        await using var dbContext = _fixture.CreateContext();

        var handler = new RegisterProjectCommandHandler(
            dbContext,
            new FakeRootPathInspector(new RepositoryRootInspectionResult(outcome, null)),
            new NeverCalledGitRepositoryInspector(),
            new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new RegisterProjectCommand("Name", "ignored"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(expectedCode, Assert.Single(result.Errors).Code);
        Assert.Empty(dbContext.Projects);
    }

    [Theory]
    [InlineData(GitRepositoryInspectionOutcome.GitUnavailable, "projects.git_unavailable")]
    [InlineData(GitRepositoryInspectionOutcome.NotAGitRepository, "projects.not_a_git_repository")]
    [InlineData(GitRepositoryInspectionOutcome.BareRepositoryNotSupported, "projects.bare_repository_not_supported")]
    [InlineData(GitRepositoryInspectionOutcome.LinkedWorktreeNotSupported, "projects.linked_worktree_not_supported")]
    [InlineData(GitRepositoryInspectionOutcome.NotTopLevelRoot, "projects.not_top_level_root")]
    [InlineData(GitRepositoryInspectionOutcome.RepositoryChangedDuringInspection, "projects.repository_changed_during_inspection")]
    [InlineData(GitRepositoryInspectionOutcome.InvalidHeadState, "projects.invalid_head_state")]
    [InlineData(GitRepositoryInspectionOutcome.GitInvocationTimedOut, "projects.git_invocation_timed_out")]
    public async Task HandleAsync_maps_every_git_inspection_rejection_without_mutation(
        GitRepositoryInspectionOutcome outcome, string expectedCode)
    {
        await using var dbContext = _fixture.CreateContext();
        await SeedGitReadyAsync(dbContext);

        var handler = new RegisterProjectCommandHandler(
            dbContext,
            new FakeRootPathInspector(new RepositoryRootInspectionResult(
                RepositoryRootInspectionOutcome.Success, new RepositoryRootCandidate(CanonicalPath))),
            new FakeGitRepositoryInspector(new GitRepositoryInspectionResult(outcome, null, null, null, false)),
            new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new RegisterProjectCommand("Name", CanonicalPath), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(expectedCode, Assert.Single(result.Errors).Code);
        Assert.Empty(dbContext.Projects);
        Assert.Empty(dbContext.RepositoryBaselines);
    }
}
