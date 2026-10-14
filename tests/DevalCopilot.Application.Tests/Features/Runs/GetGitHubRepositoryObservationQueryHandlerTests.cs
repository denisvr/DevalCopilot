using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Application.Features.Runs.Queries.GetGitHubRepositoryObservation;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// The scope-independent behavior of the repository identity observation (ADR-0041) on a real SQLite database: an unknown run, a run
/// whose recorded scope is absent or not admissible, and the guarantee that nothing is invoked and nothing is written in those cases.
/// The coherent paths (observed, unknown, CLI unavailable, a scope lost during the observation) need a real completed delivery and
/// are proved through the production host in the Api integration tests.
/// </summary>
public sealed class GetGitHubRepositoryObservationQueryHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);
    private const string GhPath = @"C:\safe\gh.exe";

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private sealed class FakeObserver : IGitHubRepositoryIdentityObserver
    {
        public List<string> Calls { get; } = [];

        public Task<GitHubRepositoryIdentity?> ObserveAsync(
            string executablePath, string owner, string repository, CancellationToken cancellationToken)
        {
            Calls.Add($"{executablePath}|{owner}|{repository}");
            return Task.FromResult<GitHubRepositoryIdentity?>(new GitHubRepositoryIdentity("R_x", owner, repository));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private async Task SeedFreshCliAsync()
    {
        await using var dbContext = _fixture.CreateContext();
        var snapshot = HostCapabilitySnapshot.Seed(Capability.GitHubCli, Now);
        snapshot.MarkDispatched(Now);
        snapshot.RecordSuccess(CapabilityLaunchKind.DirectExecutable, GhPath, null, "2.50.0", Now, Now.AddMinutes(5));
        dbContext.HostCapabilitySnapshots.Add(snapshot);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<(Devalente.Shared.Results.Result<GetGitHubRepositoryObservationQueryResult> Result, FakeObserver Observer)> RunAsync(Guid runId)
    {
        await using var dbContext = _fixture.CreateContext();
        var observer = new FakeObserver();
        var handler = new GetGitHubRepositoryObservationQueryHandler(dbContext, observer, new FixedTimeProvider(Now));
        return (await handler.HandleAsync(new GetGitHubRepositoryObservationQuery(runId), CancellationToken.None), observer);
    }

    private async Task<string> FingerprintAsync()
    {
        await using var dbContext = _fixture.CreateContext();
        return $"{await dbContext.Runs.CountAsync()}|{await dbContext.Events.CountAsync()}|{await dbContext.GitHubObservationTargets.CountAsync()}"
            + $"|{await dbContext.HostCapabilitySnapshots.CountAsync()}";
    }

    [Fact]
    public async Task An_unknown_run_is_the_fixed_not_found_and_nothing_is_invoked()
    {
        await SeedFreshCliAsync();

        var (result, observer) = await RunAsync(Guid.NewGuid());

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal(GitHubRepositoryObservationErrors.NotFoundCode, error.Code);
        Assert.Equal("github_repository_observation.not_found", error.Code);
        Assert.Empty(observer.Calls);
    }

    [Fact]
    public async Task A_run_with_no_admissible_source_and_no_target_is_not_recorded_and_nothing_is_invoked_or_written()
    {
        await SeedFreshCliAsync();
        await using (var dbContext = _fixture.CreateContext())
        {
            var project = Project.Register(Guid.NewGuid(), "Observation", $@"C:\repos\{Guid.NewGuid():N}", Now);
            dbContext.Projects.Add(project);
            dbContext.Runs.Add(Run.RecordIntent(Guid.Parse("11111111-1111-1111-1111-111111111111"), project.Id, project.ReserveExecutionNumber(), "Deliver", Now));
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        var before = await FingerprintAsync();

        var (result, observer) = await RunAsync(Guid.Parse("11111111-1111-1111-1111-111111111111"));

        Assert.True(result.IsSuccess);
        Assert.Equal(GitHubRepositoryObservationStatus.NotRecorded, result.Value.Status);
        Assert.Null(result.Value.Source);
        Assert.Null(result.Value.Target);
        Assert.Null(result.Value.Observation);
        Assert.Empty(observer.Calls);
        Assert.Equal(before, await FingerprintAsync());
    }
}
