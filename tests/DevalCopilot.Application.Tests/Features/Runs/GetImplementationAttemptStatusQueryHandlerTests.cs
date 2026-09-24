using DevalCopilot.Application.Features.Runs.Queries.GetImplementationAttemptStatus;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed class GetImplementationAttemptStatusQueryHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 9, 0, 0, TimeSpan.Zero);
    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Theory]
    [InlineData("AgentRequestedModel")]
    [InlineData("AgentAdapterContractVersion")]
    public async Task HandleAsync_fails_closed_for_corrupt_assignment_without_mutating_it(string column)
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var secretSentinel = new string('x', 129);
        await using (var seed = _fixture.CreateContext())
        {
            var project = Project.Register(Guid.NewGuid(), "Status assignment", @"C:\repo", Now);
            seed.Projects.Add(project);
            seed.Runs.Add(Run.RecordIntent(runId, project.Id, 1, "Inspect assignment", Now));
            seed.Attempts.Add(Attempt.ClaimAgentImplementation(
                attemptId, runId, 1, Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), Guid.NewGuid(),
                TimeSpan.FromMinutes(20), 65536, 131072, Now, 1));
            await seed.SaveChangesAsync();
            _ = column switch
            {
                "AgentRequestedModel" => await seed.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE attempts SET AgentRequestedModel = {secretSentinel} WHERE Id = {attemptId}"),
                "AgentAdapterContractVersion" => await seed.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE attempts SET AgentAdapterContractVersion = {secretSentinel} WHERE Id = {attemptId}"),
                _ => throw new InvalidOperationException("Unexpected test column."),
            };
        }

        await using var context = _fixture.CreateContext();
        var result = await new GetImplementationAttemptStatusQueryHandler(context)
            .HandleAsync(new GetImplementationAttemptStatusQuery(runId), CancellationToken.None);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("agent_attempts.invalid_assignment", error.Code);
        Assert.DoesNotContain(secretSentinel, error.Description, StringComparison.Ordinal);
        var persistedValue = column switch
        {
            "AgentRequestedModel" => await context.Attempts.AsNoTracking()
                .Where(attempt => attempt.Id == attemptId).Select(attempt => attempt.AgentRequestedModel).SingleAsync(),
            "AgentAdapterContractVersion" => await context.Attempts.AsNoTracking()
                .Where(attempt => attempt.Id == attemptId).Select(attempt => attempt.AgentAdapterContractVersion).SingleAsync(),
            _ => throw new InvalidOperationException("Unexpected test column."),
        };
        Assert.Equal(secretSentinel, persistedValue);
    }

    [Theory]
    [InlineData("AgentProvider", "InvalidProviderSentinel")]
    [InlineData("AgentPermissionProfile", "InvalidPermissionSentinel")]
    public async Task HandleAsync_fails_closed_for_unparseable_persisted_enum_without_mutating_it(string column, string sentinel)
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        await using (var seed = _fixture.CreateContext())
        {
            var project = Project.Register(Guid.NewGuid(), "Corrupt enum assignment", @"C:\repo", Now);
            seed.Projects.Add(project);
            seed.Runs.Add(Run.RecordIntent(runId, project.Id, 1, "Inspect corrupted assignment", Now));
            seed.Attempts.Add(Attempt.ClaimAgentImplementation(
                attemptId, runId, 1, Guid.NewGuid(), Guid.NewGuid(), new string('d', 64), Guid.NewGuid(),
                TimeSpan.FromMinutes(20), 65536, 131072, Now, 1));
            await seed.SaveChangesAsync();
            _ = column switch
            {
                "AgentProvider" => await seed.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE attempts SET AgentProvider = {sentinel} WHERE Id = {attemptId}"),
                "AgentPermissionProfile" => await seed.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE attempts SET AgentPermissionProfile = {sentinel} WHERE Id = {attemptId}"),
                _ => throw new InvalidOperationException("Unexpected test column."),
            };
        }

        await using var context = _fixture.CreateContext();
        var result = await new GetImplementationAttemptStatusQueryHandler(context)
            .HandleAsync(new GetImplementationAttemptStatusQuery(runId), CancellationToken.None);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("agent_attempts.invalid_assignment", error.Code);
        Assert.DoesNotContain(sentinel, error.Description, StringComparison.Ordinal);
        var persistedValue = column switch
        {
            "AgentProvider" => await context.Database
                .SqlQueryRaw<string>("SELECT AgentProvider AS Value FROM attempts WHERE Id = {0}", attemptId)
                .SingleAsync(),
            "AgentPermissionProfile" => await context.Database
                .SqlQueryRaw<string>("SELECT AgentPermissionProfile AS Value FROM attempts WHERE Id = {0}", attemptId)
                .SingleAsync(),
            _ => throw new InvalidOperationException("Unexpected test column."),
        };
        Assert.Equal(sentinel, persistedValue);
    }

    [Fact]
    public async Task HandleAsync_projects_valid_historical_nullable_assignment_as_unknown_and_null()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        await using (var seed = _fixture.CreateContext())
        {
            var project = Project.Register(Guid.NewGuid(), "Historical assignment", @"C:\repo", Now);
            seed.Projects.Add(project);
            seed.Runs.Add(Run.RecordIntent(runId, project.Id, 1, "Inspect historical assignment", Now));
            seed.Attempts.Add(Attempt.ClaimAgentImplementation(
                attemptId, runId, 1, Guid.NewGuid(), Guid.NewGuid(), new string('b', 64), Guid.NewGuid(),
                TimeSpan.FromMinutes(20), 65536, 131072, Now, 1));
            seed.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), attemptId, Guid.NewGuid(), 0));
            await seed.SaveChangesAsync();
            await seed.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE attempts SET AgentRequestedModel = NULL, AgentObservedModel = NULL,
                    AgentRequestedEffort = NULL, AgentObservedEffort = NULL,
                    AgentPermissionProfile = NULL, AgentAdapterContractVersion = NULL
                WHERE Id = {attemptId}
                """);
        }

        await using var context = _fixture.CreateContext();
        var result = await new GetImplementationAttemptStatusQueryHandler(context)
            .HandleAsync(new GetImplementationAttemptStatusQuery(runId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AgentPermissionProfile.Unknown, result.Value.Assignment!.PermissionProfile);
        Assert.Null(result.Value.Assignment.RequestedModel);
        Assert.Null(result.Value.Assignment.ObservedModel);
        Assert.Null(result.Value.Assignment.RequestedEffort);
        Assert.Null(result.Value.Assignment.ObservedEffort);
        Assert.Null(result.Value.Assignment.AdapterContractVersion);
    }

}
