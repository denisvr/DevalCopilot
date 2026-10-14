using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Queries.GetGitHubCiObservation;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// The scope-independent behavior of the explicit commit check observation (ADR-0044) on a real file-backed SQLite database: an unknown
/// run, a run with nothing recorded, a binding or binding event that has no coherent scope to belong to (an orphan is never "not recorded"),
/// the closed rollup-state policy, and the guarantee that nothing is invoked and nothing is written in those cases. The coherent paths
/// need a real completed delivery and are proved through the production host in the Api integration tests.
/// </summary>
public sealed class GetGitHubCiObservationQueryHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);
    private const string GhPath = @"C:\safe\gh.exe";

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private sealed class FakeObserver : IGitHubCiObserver
    {
        public List<string> Calls { get; } = [];

        public Task<GitHubCiProbe?> ObserveAsync(
            string executablePath, string owner, string repository, string branchName, CancellationToken cancellationToken)
        {
            Calls.Add($"{executablePath}|{owner}|{repository}|{branchName}");
            return Task.FromResult<GitHubCiProbe?>(new GitHubCiProbe("R_x", owner, repository, null, null));
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

    private async Task<(Devalente.Shared.Results.Result<GetGitHubCiObservationQueryResult> Result, FakeObserver Observer)> RunAsync(Guid runId)
    {
        await using var dbContext = _fixture.CreateContext();
        var observer = new FakeObserver();
        var handler = new GetGitHubCiObservationQueryHandler(dbContext, observer, new FixedTimeProvider(Now));
        return (await handler.HandleAsync(new GetGitHubCiObservationQuery(runId), CancellationToken.None), observer);
    }

    private async Task<string> FingerprintAsync()
    {
        await using var dbContext = _fixture.CreateContext();
        return $"{await dbContext.Runs.CountAsync()}|{await dbContext.Events.CountAsync()}|{await dbContext.GitHubObservationTargets.CountAsync()}"
            + $"|{await dbContext.GitHubRepositoryIdentityBindings.CountAsync()}|{await dbContext.HostCapabilitySnapshots.CountAsync()}";
    }

    private static void AssertNoFacts(GetGitHubCiObservationQueryResult value)
    {
        Assert.Null(value.Source);
        Assert.Null(value.Target);
        Assert.Null(value.Binding);
        Assert.Null(value.Observation);
    }

    [Fact]
    public async Task An_unknown_run_is_the_fixed_not_found_and_nothing_is_invoked()
    {
        await SeedFreshCliAsync();

        var (result, observer) = await RunAsync(Guid.NewGuid());

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal(GitHubCiObservationErrors.NotFoundCode, error.Code);
        Assert.Equal("github_ci_observation.not_found", error.Code);
        Assert.Empty(observer.Calls);
    }

    [Fact]
    public async Task A_run_with_no_target_and_no_binding_is_not_recorded_with_no_fact_and_nothing_is_invoked_or_written()
    {
        await SeedFreshCliAsync();
        var runId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        await using (var dbContext = _fixture.CreateContext())
        {
            var project = Project.Register(Guid.NewGuid(), "Observation", $@"C:\repos\{Guid.NewGuid():N}", Now);
            dbContext.Projects.Add(project);
            dbContext.Runs.Add(Run.RecordIntent(runId, project.Id, project.ReserveExecutionNumber(), "Deliver", Now));
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        var before = await FingerprintAsync();

        var (result, observer) = await RunAsync(runId);

        Assert.True(result.IsSuccess);
        Assert.Equal(GitHubCiObservationStatus.NotRecorded, result.Value.Status);
        Assert.Equal(runId, result.Value.RunId);
        AssertNoFacts(result.Value);
        Assert.Empty(observer.Calls);
        Assert.Equal(before, await FingerprintAsync());
    }

    private async Task<(LocalCommitRows Rows, GitHubObservationTarget Target, GitHubRepositoryIdentityBinding Binding)> SeedOrphanBindingAsync(
        bool recordTargetEvent, bool recordBindingEvent)
    {
        await using var db = _fixture.CreateContext();
        var rows = await LocalCommitRowsSeed.SeedAsync(db, LocalCommitStatus.Completed, WorkspaceStatus.Ready);
        var target = GitHubObservationTarget.Record(
            Guid.NewGuid(), rows.Run.Id, rows.Project.Id, rows.Operation.Id, rows.Workspace.Id, rows.Operation.CommitSha,
            rows.Operation.BranchName, "octo-cat", "hello.world", LocalCommitRowsSeed.Now, Guid.NewGuid());
        db.GitHubObservationTargets.Add(target);
        if (recordTargetEvent)
        {
            db.Events.Add(RunEvent.Record(
                target.EventId, rows.Run.Id, null, RunEventType.GitHubObservationTargetRecorded, ParticipantIdentity.ForHuman(),
                GitHubObservationTargetEventPayload.Build(target), LocalCommitRowsSeed.Now));
        }

        var binding = GitHubRepositoryIdentityBinding.Record(
            Guid.NewGuid(), rows.Run.Id, rows.Project.Id, rows.Operation.Id, rows.Workspace.Id, target.Id, rows.Operation.CommitSha,
            rows.Operation.BranchName, "R_kgDOExample01", "octo-cat", "hello.world", LocalCommitRowsSeed.Now,
            LocalCommitRowsSeed.Now.AddSeconds(1), LocalCommitRowsSeed.Now.AddSeconds(2), Guid.NewGuid());
        db.GitHubRepositoryIdentityBindings.Add(binding);
        if (recordBindingEvent)
        {
            db.Events.Add(RunEvent.Record(
                binding.EventId, rows.Run.Id, null, RunEventType.GitHubRepositoryIdentityBound, ParticipantIdentity.ForHuman(),
                GitHubRepositoryIdentityBindingEventPayload.Build(binding), binding.RecordedAtUtc));
        }

        await db.SaveChangesAsync();
        return (rows, target, binding);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task A_target_and_binding_without_an_admissible_delivery_source_are_unavailable_never_not_recorded_and_nothing_is_invoked(
        bool recordTargetEvent, bool recordBindingEvent)
    {
        await SeedFreshCliAsync();
        var (rows, _, _) = await SeedOrphanBindingAsync(recordTargetEvent, recordBindingEvent);
        var before = await FingerprintAsync();

        var (result, observer) = await RunAsync(rows.Run.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(GitHubCiObservationStatus.Unavailable, result.Value.Status);
        AssertNoFacts(result.Value);
        Assert.Empty(observer.Calls);
        Assert.Equal(before, await FingerprintAsync());
    }

    [Fact]
    public async Task A_binding_event_alone_is_an_orphan_and_unavailable_never_not_recorded()
    {
        await SeedFreshCliAsync();
        await using (var db = _fixture.CreateContext())
        {
            var rows = await LocalCommitRowsSeed.SeedAsync(db, LocalCommitStatus.Completed, WorkspaceStatus.Ready);
            db.Events.Add(RunEvent.Record(
                Guid.NewGuid(), rows.Run.Id, null, RunEventType.GitHubRepositoryIdentityBound, ParticipantIdentity.ForHuman(),
                "{}", LocalCommitRowsSeed.Now));
            await db.SaveChangesAsync();
        }

        await using var read = _fixture.CreateContext();
        var runId = await read.Runs.AsNoTracking().Select(candidate => candidate.Id).SingleAsync();

        var (result, observer) = await RunAsync(runId);

        Assert.Equal(GitHubCiObservationStatus.Unavailable, result.Value.Status);
        AssertNoFacts(result.Value);
        Assert.Empty(observer.Calls);
    }

    [Theory]
    [InlineData("ERROR", true)]
    [InlineData("EXPECTED", true)]
    [InlineData("FAILURE", true)]
    [InlineData("PENDING", true)]
    [InlineData("SUCCESS", true)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData("success", false)]
    [InlineData("Success", false)]
    [InlineData("SUCCESS ", false)]
    [InlineData(" SUCCESS", false)]
    [InlineData("PASSED", false)]
    [InlineData("GREEN", false)]
    [InlineData("APPROVED", false)]
    [InlineData("SUCCES", false)]
    [InlineData("QUEUED", false)]
    [InlineData("IN_PROGRESS", false)]
    [InlineData("COMPLETED", false)]
    [InlineData("NEUTRAL", false)]
    [InlineData("SUCCESS\n", false)]
    public void The_rollup_state_policy_admits_exactly_the_five_literal_values_and_nothing_else(string value, bool admitted)
    {
        Assert.Equal(admitted, GitHubCiRollupStatePolicy.IsRollupState(value));
    }

    [Fact]
    public void An_absent_rollup_state_is_not_a_rollup_state()
    {
        Assert.False(GitHubCiRollupStatePolicy.IsRollupState(null));
    }
}
