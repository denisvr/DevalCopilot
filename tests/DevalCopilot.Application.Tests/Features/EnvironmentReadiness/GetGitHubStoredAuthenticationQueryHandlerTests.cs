using DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetGitHubStoredAuthentication;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.EnvironmentReadiness;

/// <summary>A fresh database per test: the host capability snapshot is one row per capability across the whole host.</summary>
public sealed class GetGitHubStoredAuthenticationQueryHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);
    private const string GhPath = @"C:\safe\gh.exe";

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private sealed class FakeObserver : IGitHubStoredAuthenticationObserver
    {
        public GitHubStoredAuthenticationObservation Observation { get; set; } = GitHubStoredAuthenticationObservation.Unknown;
        public Exception? Throws { get; set; }
        public List<string> Paths { get; } = [];

        public Task<GitHubStoredAuthenticationObservation> ObserveAsync(string executablePath, CancellationToken cancellationToken)
        {
            Paths.Add(executablePath);
            return Throws is null ? Task.FromResult(Observation) : Task.FromException<GitHubStoredAuthenticationObservation>(Throws);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private async Task SeedSuccessAsync(CapabilityLaunchKind kind = CapabilityLaunchKind.DirectExecutable, string? script = null)
    {
        await using var dbContext = _fixture.CreateContext();
        var snapshot = HostCapabilitySnapshot.Seed(Capability.GitHubCli, Now);
        snapshot.MarkDispatched(Now);
        snapshot.RecordSuccess(kind, GhPath, script, "2.50.0", Now, Now.AddMinutes(5));
        dbContext.HostCapabilitySnapshots.Add(snapshot);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<(GetGitHubStoredAuthenticationQueryResult Result, FakeObserver Observer)> RunAsync(
        DateTimeOffset? at = null, GitHubStoredAuthenticationObservation? observation = null, Exception? throws = null)
    {
        await using var dbContext = _fixture.CreateContext();
        var observer = new FakeObserver { Throws = throws };
        if (observation is not null)
        {
            observer.Observation = observation;
        }

        var handler = new GetGitHubStoredAuthenticationQueryHandler(dbContext, observer, new FixedTimeProvider(at ?? Now));
        return (await handler.HandleAsync(new GetGitHubStoredAuthenticationQuery(), CancellationToken.None), observer);
    }

    [Fact]
    public async Task No_snapshot_is_cli_unavailable_without_invoking_anything()
    {
        var (result, observer) = await RunAsync();

        Assert.Equal(GitHubStoredAuthenticationStatus.CliUnavailable, result.Status);
        Assert.Null(result.ObservedAtUtc);
        Assert.Empty(observer.Paths);
    }

    [Fact]
    public async Task A_never_probed_snapshot_is_cli_unavailable()
    {
        await using (var dbContext = _fixture.CreateContext())
        {
            dbContext.HostCapabilitySnapshots.Add(HostCapabilitySnapshot.Seed(Capability.GitHubCli, Now));
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        var (result, observer) = await RunAsync();

        Assert.Equal(GitHubStoredAuthenticationStatus.CliUnavailable, result.Status);
        Assert.Empty(observer.Paths);
    }

    [Fact]
    public async Task A_failed_last_probe_is_cli_unavailable_even_with_retained_last_known_good_evidence()
    {
        await using (var dbContext = _fixture.CreateContext())
        {
            var snapshot = HostCapabilitySnapshot.Seed(Capability.GitHubCli, Now);
            snapshot.MarkDispatched(Now);
            snapshot.RecordSuccess(CapabilityLaunchKind.DirectExecutable, GhPath, null, "2.50.0", Now, Now.AddMinutes(5));
            snapshot.MarkDispatched(Now);
            snapshot.RecordFailure(CapabilityProbeReason.ExecutableInaccessible, Now.AddMinutes(5));
            dbContext.HostCapabilitySnapshots.Add(snapshot);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        var (result, observer) = await RunAsync();

        Assert.Equal(GitHubStoredAuthenticationStatus.CliUnavailable, result.Status);
        Assert.Empty(observer.Paths);
    }

    [Fact]
    public async Task A_node_script_target_is_never_invoked()
    {
        await SeedSuccessAsync(CapabilityLaunchKind.NodeScript, @"C:\safe\gh.js");

        var (result, observer) = await RunAsync();

        Assert.Equal(GitHubStoredAuthenticationStatus.CliUnavailable, result.Status);
        Assert.Empty(observer.Paths);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(5, true)]
    [InlineData(7, true)]
    [InlineData(7.01, false)]
    [InlineData(60, false)]
    public async Task Freshness_follows_the_readiness_rule_of_the_due_time_plus_two_minutes(double minutesAfterEvidence, bool fresh)
    {
        await SeedSuccessAsync();

        var (result, observer) = await RunAsync(
            Now.AddMinutes(minutesAfterEvidence),
            new GitHubStoredAuthenticationObservation(GitHubStoredAuthenticationStatus.Authenticated, Now));

        Assert.Equal(fresh ? 1 : 0, observer.Paths.Count);
        Assert.Equal(fresh ? GitHubStoredAuthenticationStatus.Authenticated : GitHubStoredAuthenticationStatus.CliUnavailable, result.Status);
    }

    [Theory]
    [InlineData(GitHubStoredAuthenticationStatus.NoActiveAccount)]
    [InlineData(GitHubStoredAuthenticationStatus.Authenticated)]
    [InlineData(GitHubStoredAuthenticationStatus.IssueReported)]
    public async Task Observed_statuses_carry_exactly_the_observation_time_and_the_vetted_path(GitHubStoredAuthenticationStatus status)
    {
        await SeedSuccessAsync();
        var observedAt = Now.AddSeconds(1);

        var (result, observer) = await RunAsync(observation: new GitHubStoredAuthenticationObservation(status, observedAt));

        Assert.Equal(status, result.Status);
        Assert.Equal(observedAt, result.ObservedAtUtc);
        Assert.Equal([GhPath], observer.Paths);
        Assert.Equal("github.com", result.Host);
        Assert.Equal("StoredDefaultConfig", result.Context);
    }

    [Fact]
    public async Task Unknown_carries_no_time()
    {
        await SeedSuccessAsync();

        var (result, _) = await RunAsync(observation: new GitHubStoredAuthenticationObservation(GitHubStoredAuthenticationStatus.Unknown, Now));

        Assert.Equal(GitHubStoredAuthenticationStatus.Unknown, result.Status);
        Assert.Null(result.ObservedAtUtc);
    }

    [Theory]
    [InlineData(GitHubStoredAuthenticationStatus.Authenticated)]
    [InlineData(GitHubStoredAuthenticationStatus.IssueReported)]
    [InlineData(GitHubStoredAuthenticationStatus.NoActiveAccount)]
    public async Task An_observed_status_without_a_time_is_unknown(GitHubStoredAuthenticationStatus status)
    {
        await SeedSuccessAsync();

        var (result, _) = await RunAsync(observation: new GitHubStoredAuthenticationObservation(status, null));

        Assert.Equal(GitHubStoredAuthenticationStatus.Unknown, result.Status);
        Assert.Null(result.ObservedAtUtc);
    }

    [Theory]
    [InlineData(GitHubStoredAuthenticationStatus.CliUnavailable)]
    [InlineData((GitHubStoredAuthenticationStatus)99)]
    public async Task An_adapter_cannot_claim_cli_unavailable_or_an_undefined_status(GitHubStoredAuthenticationStatus status)
    {
        await SeedSuccessAsync();

        var (result, _) = await RunAsync(observation: new GitHubStoredAuthenticationObservation(status, Now));

        Assert.Equal(GitHubStoredAuthenticationStatus.Unknown, result.Status);
        Assert.Null(result.ObservedAtUtc);
    }

    [Fact]
    public async Task An_unexpected_adapter_fault_is_not_swallowed()
    {
        await SeedSuccessAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(throws: new InvalidOperationException("boom")));
    }

    [Fact]
    public async Task The_query_writes_nothing()
    {
        await SeedSuccessAsync();
        await using var dbContext = _fixture.CreateContext();
        var before = await dbContext.HostCapabilitySnapshots.AsNoTracking().SingleAsync();

        await RunAsync(observation: new GitHubStoredAuthenticationObservation(GitHubStoredAuthenticationStatus.Authenticated, Now));

        var after = await dbContext.HostCapabilitySnapshots.AsNoTracking().SingleAsync();
        Assert.Equal(before.NextProbeDueAtUtc, after.NextProbeDueAtUtc);
        Assert.Equal(before.EvidenceObservedAtUtc, after.EvidenceObservedAtUtc);
        Assert.Equal(before.ReasonCode, after.ReasonCode);
    }
}
