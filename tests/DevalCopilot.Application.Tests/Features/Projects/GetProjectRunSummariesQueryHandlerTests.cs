using DevalCopilot.Application.Features.EnvironmentReadiness.Commands.EnsureHostCapabilityCatalogSeeded;
using DevalCopilot.Application.Features.Projects.Queries.GetProjectRunSummaries;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Projects;

/// <summary>
/// Proves the host-scoped design's central property: registering any number of projects never
/// multiplies the underlying capability observation. Owns a fresh database per test method — a
/// shared one would let one test's seeded catalog leak into another's exact-count assertion.
/// </summary>
public sealed class GetProjectRunSummariesQueryHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task Two_registered_projects_project_the_identical_shared_capability_observation()
    {
        await using var dbContext = _fixture.CreateContext();

        var seedHandler = new EnsureHostCapabilityCatalogSeededCommandHandler(dbContext, new FixedTimeProvider(Now));
        await seedHandler.HandleAsync(new EnsureHostCapabilityCatalogSeededCommand(), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var gitSnapshot = await dbContext.HostCapabilitySnapshots.FindAsync(Capability.Git);
        gitSnapshot!.MarkDispatched(Now);
        gitSnapshot.RecordSuccess(CapabilityLaunchKind.DirectExecutable, @"C:\Program Files\Git\cmd\git.exe", null, "2.43.0", Now, Now.AddMinutes(5));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var firstProject = Project.Register(Guid.NewGuid(), "First", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var secondProject = Project.Register(Guid.NewGuid(), "Second", $@"C:\repos\{Guid.NewGuid():N}", Now);
        dbContext.Projects.AddRange(firstProject, secondProject);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetProjectRunSummariesQueryHandler(dbContext, new FixedTimeProvider(Now));
        var results = await handler.HandleAsync(new GetProjectRunSummariesQuery(), CancellationToken.None);

        Assert.Equal(2, results.Count);
        var first = results.Single(r => r.ProjectId == firstProject.Id);
        var second = results.Single(r => r.ProjectId == secondProject.Id);

        // Exactly the fixed catalog size, for both projects — never per-project duplication.
        Assert.Equal(CapabilityCatalog.All.Count, first.Capabilities.Count);
        Assert.Equal(CapabilityCatalog.All.Count, second.Capabilities.Count);

        var firstGit = first.Capabilities.Single(c => c.Capability == Capability.Git);
        var secondGit = second.Capabilities.Single(c => c.Capability == Capability.Git);

        Assert.Equal(firstGit.Version, secondGit.Version);
        Assert.Equal(firstGit.LastCheckedUtc, secondGit.LastCheckedUtc);
        Assert.Equal(firstGit.DisplayStatus, secondGit.DisplayStatus);
        Assert.Equal("2.43.0", firstGit.Version);
    }

    [Fact]
    public async Task Registering_five_projects_still_yields_exactly_the_fixed_catalog_size_per_project()
    {
        await using var dbContext = _fixture.CreateContext();

        var seedHandler = new EnsureHostCapabilityCatalogSeededCommandHandler(dbContext, new FixedTimeProvider(Now));
        await seedHandler.HandleAsync(new EnsureHostCapabilityCatalogSeededCommand(), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        for (var i = 0; i < 5; i++)
        {
            dbContext.Projects.Add(Project.Register(Guid.NewGuid(), $"Project{i}", $@"C:\repos\{Guid.NewGuid():N}", Now));
        }

        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetProjectRunSummariesQueryHandler(dbContext, new FixedTimeProvider(Now));
        var results = await handler.HandleAsync(new GetProjectRunSummariesQuery(), CancellationToken.None);

        Assert.Equal(5, results.Count);
        Assert.All(results, project => Assert.Equal(CapabilityCatalog.All.Count, project.Capabilities.Count));

        var totalSnapshotRows = await dbContext.HostCapabilitySnapshots.CountAsync();
        Assert.Equal(CapabilityCatalog.All.Count, totalSnapshotRows);
    }

    [Fact]
    public async Task A_capability_is_presented_as_never_probed_when_the_catalog_has_not_been_seeded_yet()
    {
        await using var dbContext = _fixture.CreateContext();
        dbContext.Projects.Add(Project.Register(Guid.NewGuid(), "Unseeded", $@"C:\repos\{Guid.NewGuid():N}", Now));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetProjectRunSummariesQueryHandler(dbContext, new FixedTimeProvider(Now));
        var results = await handler.HandleAsync(new GetProjectRunSummariesQuery(), CancellationToken.None);

        var project = Assert.Single(results);
        Assert.Equal(CapabilityCatalog.All.Count, project.Capabilities.Count);
        Assert.All(project.Capabilities, capability => Assert.Null(capability.DisplayStatus));
    }

    [Fact]
    public async Task A_project_with_no_baseline_at_all_projects_as_not_yet_validated()
    {
        await using var dbContext = _fixture.CreateContext();
        dbContext.Projects.Add(Project.Register(Guid.NewGuid(), "Legacy", @"C:\repos\legacy-no-baseline", Now));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetProjectRunSummariesQueryHandler(dbContext, new FixedTimeProvider(Now));
        var results = await handler.HandleAsync(new GetProjectRunSummariesQuery(), CancellationToken.None);

        var project = Assert.Single(results);
        Assert.Null(project.HeadState);
        Assert.Null(project.BranchName);
        Assert.Null(project.HeadCommitSha);
        Assert.False(project.IsDirty);
        Assert.Null(project.BaselineObservedAtUtc);
    }

    [Fact]
    public async Task The_current_baseline_is_selected_by_the_greatest_baseline_number_not_the_latest_timestamp()
    {
        await using var dbContext = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "Reregistered", @"C:\repos\re-baselined", Now);
        dbContext.Projects.Add(project);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        // Baseline 2 is deliberately given an EARLIER timestamp than baseline 1, so a
        // timestamp-based "latest" selection would wrongly pick baseline 1 — only the greatest
        // BaselineNumber may ever be treated as current.
        dbContext.RepositoryBaselines.Add(RepositoryBaseline.Capture(
            Guid.NewGuid(), project.Id, 1, Now, RepositoryHeadState.OnBranch, "main", new string('a', 40), isDirty: true));
        dbContext.RepositoryBaselines.Add(RepositoryBaseline.Capture(
            Guid.NewGuid(), project.Id, 2, Now.AddMinutes(-10), RepositoryHeadState.OnBranch, "main", new string('b', 40), isDirty: false));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetProjectRunSummariesQueryHandler(dbContext, new FixedTimeProvider(Now));
        var results = await handler.HandleAsync(new GetProjectRunSummariesQuery(), CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal(new string('b', 40), result.HeadCommitSha);
        Assert.False(result.IsDirty);
    }
}
