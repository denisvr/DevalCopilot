using DevalCopilot.Application.Features.Projects.Queries.GetProjectWorkspace;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Infrastructure.Persistence;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Projects;

public sealed class GetProjectWorkspaceQueryHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task HandleAsync_returns_not_requested_when_no_workspace_ever_existed()
    {
        await using var dbContext = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", @"C:\repos\Foo", Now);
        project.RecordPhysicalIdentityResolved(1UL, new byte[16]);
        dbContext.Projects.Add(project);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetProjectWorkspaceQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetProjectWorkspaceQuery(project.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(WorkspacePreparationState.NotRequested, result.Value.State);
        Assert.Null(result.Value.CandidatePath);
    }

    [Fact]
    public async Task HandleAsync_fails_not_found_for_an_unknown_project()
    {
        await using var dbContext = _fixture.CreateContext();

        var handler = new GetProjectWorkspaceQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetProjectWorkspaceQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("projects.not_found", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_surfaces_the_candidate_path_and_branch_only_once_ready()
    {
        await using var dbContext = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", @"C:\repos\Foo", Now);
        project.RecordPhysicalIdentityResolved(1UL, new byte[16]);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, project.ReserveWorkspaceNumber(), @"C:\workspaces\p\1",
            "devalcopilot/workspace/p/1", new string('a', 40), "main", Now);
        workspace.MarkReady();
        var lease = RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1UL, new byte[16], Now);

        dbContext.Projects.Add(project);
        dbContext.GitWorkspaces.Add(workspace);
        dbContext.RepositoryMutationLeases.Add(lease);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetProjectWorkspaceQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetProjectWorkspaceQuery(project.Id), CancellationToken.None);

        Assert.Equal(WorkspacePreparationState.Ready, result.Value.State);
        Assert.Equal(@"C:\workspaces\p\1", result.Value.CandidatePath);
        Assert.Equal("devalcopilot/workspace/p/1", result.Value.BranchName);
        Assert.Equal("Active", result.Value.LeaseStatus);
    }

    [Fact]
    public async Task HandleAsync_never_surfaces_a_candidate_path_for_a_blocked_workspace()
    {
        await using var dbContext = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", @"C:\repos\Foo", Now);
        project.RecordPhysicalIdentityResolved(1UL, new byte[16]);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, project.ReserveWorkspaceNumber(), @"C:\workspaces\p\1",
            "devalcopilot/workspace/p/1", new string('a', 40), "main", Now);
        workspace.MarkFailedToPrepare("workspaces.git_invocation_failed");
        var lease = RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1UL, new byte[16], Now);
        lease.Release(Now);

        dbContext.Projects.Add(project);
        dbContext.GitWorkspaces.Add(workspace);
        dbContext.RepositoryMutationLeases.Add(lease);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetProjectWorkspaceQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetProjectWorkspaceQuery(project.Id), CancellationToken.None);

        Assert.Equal(WorkspacePreparationState.Blocked, result.Value.State);
        Assert.Null(result.Value.CandidatePath);
        Assert.Equal("workspaces.git_invocation_failed", result.Value.BlockedReasonCode);
        Assert.NotNull(result.Value.BlockedReasonMessage);
    }

    [Fact]
    public async Task HandleAsync_reports_the_physical_identity_blocked_message_when_unavailable()
    {
        await using var dbContext = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", @"C:\repos\Foo", Now);
        project.RecordPhysicalIdentityUnavailable(PhysicalIdentityFailureReason.UnsupportedFilesystem);
        dbContext.Projects.Add(project);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetProjectWorkspaceQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetProjectWorkspaceQuery(project.Id), CancellationToken.None);

        Assert.Equal("Unavailable", result.Value.PhysicalIdentityStatus);
        Assert.NotNull(result.Value.PhysicalIdentityBlockedMessage);
    }

    [Fact]
    public async Task HandleAsync_selects_the_greatest_workspace_number_as_current()
    {
        await using var dbContext = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", @"C:\repos\Foo", Now);
        project.RecordPhysicalIdentityResolved(1UL, new byte[16]);

        var first = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, project.ReserveWorkspaceNumber(), @"C:\workspaces\p\1",
            "devalcopilot/workspace/p/1", new string('a', 40), "main", Now);
        first.MarkFailedToPrepare("workspaces.git_invocation_failed");
        var second = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, project.ReserveWorkspaceNumber(), @"C:\workspaces\p\2",
            "devalcopilot/workspace/p/2", new string('b', 40), "main", Now);
        second.MarkReady();

        dbContext.Projects.Add(project);
        dbContext.GitWorkspaces.AddRange(first, second);
        dbContext.RepositoryMutationLeases.Add(RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, second.Id, 1UL, new byte[16], Now));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetProjectWorkspaceQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetProjectWorkspaceQuery(project.Id), CancellationToken.None);

        Assert.Equal(WorkspacePreparationState.Ready, result.Value.State);
        Assert.Equal(@"C:\workspaces\p\2", result.Value.CandidatePath);
    }
}
