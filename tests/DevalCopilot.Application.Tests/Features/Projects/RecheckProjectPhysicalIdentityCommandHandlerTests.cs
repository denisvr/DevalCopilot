using DevalCopilot.Application.Features.Projects.Commands.RecheckProjectPhysicalIdentity;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Infrastructure.Persistence;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Projects;

public sealed class RecheckProjectPhysicalIdentityCommandHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);
    private const string CanonicalPath = @"C:\repos\devalcopilot-test";

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private sealed class FakeRootPathInspector(RepositoryRootInspectionResult result) : IRepositoryRootPathInspector
    {
        public RepositoryRootInspectionResult Inspect(string requestedPath) => result;
    }

    private sealed class FakePhysicalIdentityInspector(RepositoryPhysicalIdentityInspectionResult result)
        : IRepositoryPhysicalIdentityInspector
    {
        public RepositoryPhysicalIdentityInspectionResult Resolve(RepositoryRootCandidate candidate) => result;
    }

    private static RepositoryRootInspectionResult SuccessfulRoot => new(
        RepositoryRootInspectionOutcome.Success, new RepositoryRootCandidate(CanonicalPath));

    private RecheckProjectPhysicalIdentityCommandHandler CreateHandler(
        DevalCopilotDbContext dbContext, RepositoryRootInspectionResult rootResult, RepositoryPhysicalIdentityInspectionResult identityResult) =>
        new(dbContext, new FakeRootPathInspector(rootResult), new FakePhysicalIdentityInspector(identityResult));

    [Fact]
    public async Task HandleAsync_resolves_an_unresolved_project_and_persists_the_tuple()
    {
        await using var dbContext = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", CanonicalPath, Now);
        dbContext.Projects.Add(project);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var fileId = new byte[16];
        fileId[0] = 1;
        var handler = CreateHandler(
            dbContext, SuccessfulRoot,
            new RepositoryPhysicalIdentityInspectionResult(RepositoryPhysicalIdentityInspectionOutcome.Resolved, 7UL, fileId));

        var result = await handler.HandleAsync(new RecheckProjectPhysicalIdentityCommand(project.Id), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PhysicalIdentityStatus.Resolved, result.Value.Status);
        var reloaded = await dbContext.Projects.FindAsync(project.Id);
        Assert.Equal(PhysicalIdentityStatus.Resolved, reloaded!.PhysicalIdentityStatus);
        Assert.Equal(7UL, reloaded.PhysicalVolumeSerialNumber);
        Assert.Equal(fileId, reloaded.PhysicalFileId);
    }

    [Theory]
    [InlineData(RepositoryPhysicalIdentityInspectionOutcome.UnsupportedFilesystem, PhysicalIdentityFailureReason.UnsupportedFilesystem)]
    [InlineData(RepositoryPhysicalIdentityInspectionOutcome.PathInaccessible, PhysicalIdentityFailureReason.PathInaccessible)]
    public async Task HandleAsync_records_unavailable_with_the_mapped_reason_for_an_unresolved_project(
        RepositoryPhysicalIdentityInspectionOutcome outcome, PhysicalIdentityFailureReason expectedReason)
    {
        await using var dbContext = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", CanonicalPath, Now);
        dbContext.Projects.Add(project);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = CreateHandler(dbContext, SuccessfulRoot, new RepositoryPhysicalIdentityInspectionResult(outcome, null, null));

        var result = await handler.HandleAsync(new RecheckProjectPhysicalIdentityCommand(project.Id), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PhysicalIdentityStatus.Unavailable, result.Value.Status);
        var reloaded = await dbContext.Projects.FindAsync(project.Id);
        Assert.Equal(PhysicalIdentityStatus.Unavailable, reloaded!.PhysicalIdentityStatus);
        Assert.Equal(expectedReason, reloaded.PhysicalIdentityFailureReason);
    }

    [Fact]
    public async Task HandleAsync_maps_a_root_inspection_failure_to_path_inaccessible()
    {
        await using var dbContext = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", CanonicalPath, Now);
        dbContext.Projects.Add(project);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = CreateHandler(
            dbContext,
            new RepositoryRootInspectionResult(RepositoryRootInspectionOutcome.PathNotFound, null),
            new RepositoryPhysicalIdentityInspectionResult(RepositoryPhysicalIdentityInspectionOutcome.Resolved, 1UL, new byte[16]));

        var result = await handler.HandleAsync(new RecheckProjectPhysicalIdentityCommand(project.Id), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PhysicalIdentityStatus.Unavailable, result.Value.Status);
        var reloaded = await dbContext.Projects.FindAsync(project.Id);
        Assert.Equal(PhysicalIdentityFailureReason.PathInaccessible, reloaded!.PhysicalIdentityFailureReason);
    }

    [Fact]
    public async Task HandleAsync_is_idempotent_when_the_resolved_tuple_matches_the_stored_one()
    {
        await using var dbContext = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", CanonicalPath, Now);
        var fileId = new byte[16];
        fileId[1] = 3;
        project.RecordPhysicalIdentityResolved(5UL, fileId);
        dbContext.Projects.Add(project);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = CreateHandler(
            dbContext, SuccessfulRoot,
            new RepositoryPhysicalIdentityInspectionResult(RepositoryPhysicalIdentityInspectionOutcome.Resolved, 5UL, fileId));

        var result = await handler.HandleAsync(new RecheckProjectPhysicalIdentityCommand(project.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PhysicalIdentityStatus.Resolved, result.Value.Status);
    }

    [Fact]
    public async Task HandleAsync_never_overwrites_a_resolved_identity_with_a_different_one()
    {
        await using var dbContext = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", CanonicalPath, Now);
        var originalFileId = new byte[16];
        originalFileId[0] = 1;
        project.RecordPhysicalIdentityResolved(5UL, originalFileId);
        dbContext.Projects.Add(project);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var differentFileId = new byte[16];
        differentFileId[0] = 2;
        var handler = CreateHandler(
            dbContext, SuccessfulRoot,
            new RepositoryPhysicalIdentityInspectionResult(RepositoryPhysicalIdentityInspectionOutcome.Resolved, 6UL, differentFileId));

        var result = await handler.HandleAsync(new RecheckProjectPhysicalIdentityCommand(project.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("projects.physical_identity_changed", Assert.Single(result.Errors).Code);
        var reloaded = await dbContext.Projects.FindAsync(project.Id);
        Assert.Equal(5UL, reloaded!.PhysicalVolumeSerialNumber);
        Assert.Equal(originalFileId, reloaded.PhysicalFileId);
    }

    [Fact]
    public async Task HandleAsync_never_demotes_a_resolved_identity_after_a_transient_check_failure()
    {
        await using var dbContext = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", CanonicalPath, Now);
        var fileId = new byte[16];
        project.RecordPhysicalIdentityResolved(5UL, fileId);
        dbContext.Projects.Add(project);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = CreateHandler(
            dbContext, SuccessfulRoot,
            new RepositoryPhysicalIdentityInspectionResult(RepositoryPhysicalIdentityInspectionOutcome.PathInaccessible, null, null));

        var result = await handler.HandleAsync(new RecheckProjectPhysicalIdentityCommand(project.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("projects.physical_identity_check_failed", Assert.Single(result.Errors).Code);
        var reloaded = await dbContext.Projects.FindAsync(project.Id);
        Assert.Equal(PhysicalIdentityStatus.Resolved, reloaded!.PhysicalIdentityStatus);
        Assert.Equal(5UL, reloaded.PhysicalVolumeSerialNumber);
    }

    [Fact]
    public async Task HandleAsync_fails_not_found_for_an_unknown_project()
    {
        await using var dbContext = _fixture.CreateContext();
        var handler = CreateHandler(
            dbContext, SuccessfulRoot,
            new RepositoryPhysicalIdentityInspectionResult(RepositoryPhysicalIdentityInspectionOutcome.Resolved, 1UL, new byte[16]));

        var result = await handler.HandleAsync(new RecheckProjectPhysicalIdentityCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("projects.not_found", Assert.Single(result.Errors).Code);
    }
}
