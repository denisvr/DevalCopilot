using DevalCopilot.Application.Features.Projects.Commands.ConfigureVerificationCommand;
using DevalCopilot.Application.Features.Projects.Commands.DeleteVerificationCommand;
using DevalCopilot.Application.Features.Projects.Commands.UpdateVerificationCommand;
using DevalCopilot.Application.Features.Projects.Queries.GetProjectVerificationCommands;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Projects;

public sealed class VerificationCommandHandlersTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);
    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<Project> AddProjectAsync(DevalCopilotDbContext dbContext)
    {
        var project = Project.Register(Guid.NewGuid(), "Test", $@"C:\repos\{Guid.NewGuid():N}", Now);
        dbContext.Projects.Add(project);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        return project;
    }

    [Fact]
    public async Task Configure_update_list_and_explicit_delete_keep_commands_project_scoped()
    {
        await using var dbContext = _fixture.CreateContext();
        var project = await AddProjectAsync(dbContext);
        var configure = new ConfigureVerificationCommandCommandHandler(dbContext, new FixedTimeProvider(Now));

        var createResult = await configure.HandleAsync(
            new ConfigureVerificationCommandCommand(
                project.Id,
                "Backend tests",
                @"C:\Program Files\dotnet\dotnet.exe",
                ["test", "DevalCopilot.slnx", "--no-restore"],
                300,
                true),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.True(createResult.IsSuccess);
        Assert.Equal(1, createResult.Value.CommandNumber);

        var update = new UpdateVerificationCommandCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1)));
        var updateResult = await update.HandleAsync(
            new UpdateVerificationCommandCommand(
                project.Id,
                createResult.Value.VerificationCommandId,
                "Backend tests",
                @"C:\Program Files\dotnet\dotnet.exe",
                ["test", "DevalCopilot.slnx", "--no-build"],
                120,
                false),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.True(updateResult.IsSuccess);
        var list = await new GetProjectVerificationCommandsQueryHandler(dbContext).HandleAsync(
            new GetProjectVerificationCommandsQuery(project.Id), CancellationToken.None);
        var command = Assert.Single(list);
        Assert.Equal(["test", "DevalCopilot.slnx", "--no-build"], command.Arguments);
        Assert.False(command.IsEnabled);

        var deleteResult = await new DeleteVerificationCommandCommandHandler(dbContext).HandleAsync(
            new DeleteVerificationCommandCommand(project.Id, command.VerificationCommandId), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.True(deleteResult.IsSuccess);
        Assert.Empty(await dbContext.VerificationCommands.ToListAsync());
    }

    [Fact]
    public async Task Update_and_delete_reject_commands_owned_by_another_project_without_mutation()
    {
        await using var dbContext = _fixture.CreateContext();
        var owner = await AddProjectAsync(dbContext);
        var other = await AddProjectAsync(dbContext);
        var configured = VerificationCommand.Configure(
            Guid.NewGuid(), owner.Id, owner.ReserveVerificationCommandNumber(), "Tests", @"C:\dotnet.exe", [], 60, true, Now);
        dbContext.VerificationCommands.Add(configured);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var updateResult = await new UpdateVerificationCommandCommandHandler(dbContext, new FixedTimeProvider(Now)).HandleAsync(
            new UpdateVerificationCommandCommand(other.Id, configured.Id, "Changed", @"C:\dotnet.exe", [], 60, false),
            CancellationToken.None);
        var deleteResult = await new DeleteVerificationCommandCommandHandler(dbContext).HandleAsync(
            new DeleteVerificationCommandCommand(other.Id, configured.Id), CancellationToken.None);

        Assert.True(updateResult.IsFailure);
        Assert.True(deleteResult.IsFailure);
        Assert.Equal("Tests", (await dbContext.VerificationCommands.SingleAsync()).Name);
    }
}
