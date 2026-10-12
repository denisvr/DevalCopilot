using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Projects.Queries.GetProjectGitHubOrigin;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Projects;

/// <summary>A fresh database per test: the host capability snapshot is one row per capability across the whole host.</summary>
public sealed class GetProjectGitHubOriginQueryHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);
    private const string GitPath = @"C:\safe\git.exe";
    private const string RootPath = @"C:\repos\origin-fixture";

    private readonly SqliteDatabaseFixture _fixture = new();
    private readonly List<string> _calls = [];

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private static byte[] FileId(byte seed)
    {
        var bytes = new byte[16];
        bytes[0] = seed;
        return bytes;
    }

    private sealed class FakeRootInspector(List<string> calls) : IRepositoryRootPathInspector
    {
        public Queue<RepositoryRootInspectionResult> Results { get; } = new();

        public RepositoryRootInspectionResult Inspect(string requestedPath)
        {
            calls.Add($"root:{requestedPath}");
            return Results.Count > 0
                ? Results.Dequeue()
                : new RepositoryRootInspectionResult(RepositoryRootInspectionOutcome.Success, new RepositoryRootCandidate(requestedPath));
        }
    }

    private sealed class FakeIdentityInspector(List<string> calls) : IRepositoryPhysicalIdentityInspector
    {
        public Queue<RepositoryPhysicalIdentityInspectionResult> Results { get; } = new();

        public RepositoryPhysicalIdentityInspectionResult Resolve(RepositoryRootCandidate candidate)
        {
            calls.Add("identity");
            return Results.Count > 0
                ? Results.Dequeue()
                : new RepositoryPhysicalIdentityInspectionResult(RepositoryPhysicalIdentityInspectionOutcome.Resolved, 7UL, FileId(1));
        }
    }

    private sealed class FakeObserver(List<string> calls) : IGitHubOriginObserver
    {
        public GitHubOriginObservation Observation { get; set; } =
            new(GitHubOriginStatus.Available, new GitHubOriginTarget("acme", "widgets"));

        public Exception? Throws { get; set; }
        public List<(string Git, string Root)> Invocations { get; } = [];

        public Task<GitHubOriginObservation> ObserveAsync(
            string gitExecutablePath, RepositoryRootCandidate candidate, CancellationToken cancellationToken)
        {
            calls.Add("observe");
            Invocations.Add((gitExecutablePath, candidate.CanonicalPath));
            return Throws is null ? Task.FromResult(Observation) : Task.FromException<GitHubOriginObservation>(Throws);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private async Task<Guid> SeedAsync(
        CapabilityLaunchKind kind = CapabilityLaunchKind.DirectExecutable,
        string? script = null,
        bool resolved = true,
        byte[]? fileId = null,
        bool seedGit = true)
    {
        await using var dbContext = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "Origin fixture", RootPath, Now);
        if (resolved)
        {
            project.RecordPhysicalIdentityResolved(7UL, fileId ?? FileId(1));
        }

        dbContext.Projects.Add(project);
        if (seedGit)
        {
            var snapshot = HostCapabilitySnapshot.Seed(Capability.Git, Now);
            snapshot.MarkDispatched(Now);
            snapshot.RecordSuccess(kind, GitPath, script, "2.53.0", Now, Now.AddMinutes(5));
            dbContext.HostCapabilitySnapshots.Add(snapshot);
        }

        await dbContext.SaveChangesAsync(CancellationToken.None);
        return project.Id;
    }

    private sealed record Run(
        Devalente.Shared.Results.Result<GetProjectGitHubOriginQueryResult> Result,
        FakeObserver Observer,
        FakeRootInspector Roots,
        FakeIdentityInspector Identities);

    private async Task<Run> RunAsync(
        Guid projectId,
        DateTimeOffset? at = null,
        Action<FakeObserver, FakeRootInspector, FakeIdentityInspector>? arrange = null)
    {
        await using var dbContext = _fixture.CreateContext();
        var observer = new FakeObserver(_calls);
        var roots = new FakeRootInspector(_calls);
        var identities = new FakeIdentityInspector(_calls);
        arrange?.Invoke(observer, roots, identities);
        var handler = new GetProjectGitHubOriginQueryHandler(dbContext, roots, identities, observer, new FixedTimeProvider(at ?? Now));

        var result = await handler.HandleAsync(new GetProjectGitHubOriginQuery(projectId), CancellationToken.None);

        Assert.False(dbContext.ChangeTracker.HasChanges());
        Assert.Empty(dbContext.ChangeTracker.Entries());
        return new Run(result, observer, roots, identities);
    }

    private static void AssertUnavailable(Run run, Guid projectId)
    {
        Assert.True(run.Result.IsSuccess);
        Assert.Equal(projectId, run.Result.Value.ProjectId);
        Assert.Equal(GitHubOriginStatus.Unavailable, run.Result.Value.Status);
        Assert.Null(run.Result.Value.ObservedAtUtc);
        Assert.Null(run.Result.Value.Target);
    }

    [Fact]
    public async Task An_unknown_project_is_the_fixed_not_found_with_nothing_inspected_or_observed()
    {
        await SeedAsync();

        var run = await RunAsync(Guid.NewGuid());

        Assert.True(run.Result.IsFailure);
        Assert.Equal("projects.not_found", Assert.Single(run.Result.Errors).Code);
        Assert.Empty(_calls);
    }

    [Fact]
    public async Task Available_carries_the_target_and_the_time_after_both_identity_checks()
    {
        var projectId = await SeedAsync();

        var run = await RunAsync(projectId);

        Assert.True(run.Result.IsSuccess);
        Assert.Equal(GitHubOriginStatus.Available, run.Result.Value.Status);
        Assert.Equal(Now, run.Result.Value.ObservedAtUtc);
        Assert.Equal(new GitHubOriginTarget("acme", "widgets"), run.Result.Value.Target);
        Assert.Equal("github.com", run.Result.Value.Target!.Host);
        Assert.Equal("RepositoryLocalConfig", run.Result.Value.Source);
        Assert.Equal([$"root:{RootPath}", "identity", "observe", $"root:{RootPath}", "identity"], _calls);
        Assert.Equal([(GitPath, RootPath)], run.Observer.Invocations);
    }

    [Fact]
    public async Task Not_configured_carries_a_time_and_no_target()
    {
        var projectId = await SeedAsync();

        var run = await RunAsync(projectId, arrange: (observer, _, _) => observer.Observation = GitHubOriginObservation.NotConfigured);

        Assert.True(run.Result.IsSuccess);
        Assert.Equal(GitHubOriginStatus.NotConfigured, run.Result.Value.Status);
        Assert.Equal(Now, run.Result.Value.ObservedAtUtc);
        Assert.Null(run.Result.Value.Target);
    }

    [Fact]
    public async Task An_observer_unavailable_answer_carries_neither_time_nor_target()
    {
        var projectId = await SeedAsync();

        var run = await RunAsync(projectId, arrange: (observer, _, _) => observer.Observation = GitHubOriginObservation.Unavailable);

        AssertUnavailable(run, projectId);
    }

    [Fact]
    public async Task No_git_snapshot_is_unavailable_before_any_inspection_or_observation()
    {
        var projectId = await SeedAsync(seedGit: false);

        var run = await RunAsync(projectId);

        AssertUnavailable(run, projectId);
        Assert.Empty(_calls);
    }

    [Fact]
    public async Task A_node_script_git_target_is_never_inspected_or_invoked()
    {
        var projectId = await SeedAsync(CapabilityLaunchKind.NodeScript, @"C:\safe\git.js");

        var run = await RunAsync(projectId);

        AssertUnavailable(run, projectId);
        Assert.Empty(_calls);
    }

    [Fact]
    public async Task A_failed_last_probe_is_unavailable_even_with_retained_last_known_good_evidence()
    {
        await using (var dbContext = _fixture.CreateContext())
        {
            var snapshot = HostCapabilitySnapshot.Seed(Capability.Git, Now);
            snapshot.MarkDispatched(Now);
            snapshot.RecordSuccess(CapabilityLaunchKind.DirectExecutable, GitPath, null, "2.53.0", Now, Now.AddMinutes(5));
            snapshot.MarkDispatched(Now);
            snapshot.RecordFailure(CapabilityProbeReason.ExecutableInaccessible, Now.AddMinutes(5));
            dbContext.HostCapabilitySnapshots.Add(snapshot);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        var projectId = await SeedAsync(seedGit: false);

        var run = await RunAsync(projectId);

        AssertUnavailable(run, projectId);
        Assert.Empty(_calls);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(7, true)]
    [InlineData(7.01, false)]
    [InlineData(60, false)]
    public async Task Freshness_follows_the_readiness_rule_of_the_due_time_plus_two_minutes(double minutesAfterEvidence, bool fresh)
    {
        var projectId = await SeedAsync();

        var run = await RunAsync(projectId, Now.AddMinutes(minutesAfterEvidence));

        Assert.Equal(fresh ? 1 : 0, run.Observer.Invocations.Count);
        Assert.Equal(fresh ? GitHubOriginStatus.Available : GitHubOriginStatus.Unavailable, run.Result.Value.Status);
        if (!fresh)
        {
            Assert.Empty(_calls);
        }
    }

    [Fact]
    public async Task An_unresolved_stored_identity_is_unavailable_without_inspecting_or_invoking_or_updating_it()
    {
        var projectId = await SeedAsync(resolved: false);

        var run = await RunAsync(projectId);

        AssertUnavailable(run, projectId);
        Assert.Empty(_calls);
        await using var dbContext = _fixture.CreateContext();
        var stored = await dbContext.Projects.FindAsync(projectId);
        Assert.Equal(PhysicalIdentityStatus.Unresolved, stored!.PhysicalIdentityStatus);
        Assert.Null(stored.PhysicalVolumeSerialNumber);
    }

    [Fact]
    public async Task A_malformed_stored_file_identity_is_unavailable_without_any_call()
    {
        var projectId = await SeedAsync(fileId: new byte[15]);

        var run = await RunAsync(projectId);

        AssertUnavailable(run, projectId);
        Assert.Empty(_calls);
    }

    [Fact]
    public async Task A_root_that_no_longer_inspects_is_unavailable_and_nothing_is_invoked()
    {
        var projectId = await SeedAsync();

        var run = await RunAsync(projectId, arrange: (_, roots, _) =>
            roots.Results.Enqueue(new RepositoryRootInspectionResult(RepositoryRootInspectionOutcome.ReparsePointNotSupported, null)));

        AssertUnavailable(run, projectId);
        Assert.Equal([$"root:{RootPath}"], _calls);
    }

    [Theory]
    [InlineData(RepositoryPhysicalIdentityInspectionOutcome.PathInaccessible)]
    [InlineData(RepositoryPhysicalIdentityInspectionOutcome.UnsupportedFilesystem)]
    public async Task An_identity_that_cannot_be_resolved_before_is_unavailable_and_nothing_is_invoked(
        RepositoryPhysicalIdentityInspectionOutcome outcome)
    {
        var projectId = await SeedAsync();

        var run = await RunAsync(projectId, arrange: (_, _, identities) =>
            identities.Results.Enqueue(new RepositoryPhysicalIdentityInspectionResult(outcome, null, null)));

        AssertUnavailable(run, projectId);
        Assert.Equal([$"root:{RootPath}", "identity"], _calls);
    }

    [Theory]
    [InlineData(8UL, 1)]
    [InlineData(7UL, 2)]
    public async Task A_changed_identity_before_is_unavailable_and_nothing_is_invoked_or_updated(ulong volume, byte seed)
    {
        var projectId = await SeedAsync();

        var run = await RunAsync(projectId, arrange: (_, _, identities) => identities.Results.Enqueue(
            new RepositoryPhysicalIdentityInspectionResult(RepositoryPhysicalIdentityInspectionOutcome.Resolved, volume, FileId(seed))));

        AssertUnavailable(run, projectId);
        Assert.Empty(run.Observer.Invocations);
        await using var dbContext = _fixture.CreateContext();
        var stored = await dbContext.Projects.FindAsync(projectId);
        Assert.Equal(7UL, stored!.PhysicalVolumeSerialNumber);
        Assert.Equal(FileId(1), stored.PhysicalFileId);
    }

    [Fact]
    public async Task A_resolved_identity_with_a_wrong_file_id_length_is_never_a_match()
    {
        var projectId = await SeedAsync();

        var run = await RunAsync(projectId, arrange: (_, _, identities) => identities.Results.Enqueue(
            new RepositoryPhysicalIdentityInspectionResult(RepositoryPhysicalIdentityInspectionOutcome.Resolved, 7UL, new byte[15])));

        AssertUnavailable(run, projectId);
        Assert.Empty(run.Observer.Invocations);
    }

    [Theory]
    [InlineData("root")]
    [InlineData("identity-failed")]
    [InlineData("identity-changed")]
    public async Task An_identity_that_fails_or_changes_after_the_observation_discards_an_available_answer(string after)
    {
        var projectId = await SeedAsync();

        var run = await RunAsync(projectId, arrange: (_, roots, identities) =>
        {
            roots.Results.Enqueue(new RepositoryRootInspectionResult(RepositoryRootInspectionOutcome.Success, new RepositoryRootCandidate(RootPath)));
            identities.Results.Enqueue(new RepositoryPhysicalIdentityInspectionResult(RepositoryPhysicalIdentityInspectionOutcome.Resolved, 7UL, FileId(1)));
            switch (after)
            {
                case "root":
                    roots.Results.Enqueue(new RepositoryRootInspectionResult(RepositoryRootInspectionOutcome.PathNotFound, null));
                    break;
                case "identity-failed":
                    identities.Results.Enqueue(new RepositoryPhysicalIdentityInspectionResult(RepositoryPhysicalIdentityInspectionOutcome.PathInaccessible, null, null));
                    break;
                default:
                    identities.Results.Enqueue(new RepositoryPhysicalIdentityInspectionResult(RepositoryPhysicalIdentityInspectionOutcome.Resolved, 7UL, FileId(9)));
                    break;
            }
        });

        AssertUnavailable(run, projectId);
        Assert.Single(run.Observer.Invocations);
    }

    [Fact]
    public async Task A_root_path_that_changed_between_the_two_checks_is_unavailable()
    {
        var projectId = await SeedAsync();

        var run = await RunAsync(projectId, arrange: (_, roots, _) =>
        {
            roots.Results.Enqueue(new RepositoryRootInspectionResult(RepositoryRootInspectionOutcome.Success, new RepositoryRootCandidate(RootPath)));
            roots.Results.Enqueue(new RepositoryRootInspectionResult(RepositoryRootInspectionOutcome.Success, new RepositoryRootCandidate(@"C:\repos\elsewhere")));
        });

        AssertUnavailable(run, projectId);
    }

    [Theory]
    [InlineData(GitHubOriginStatus.Available, null, null)]
    [InlineData(GitHubOriginStatus.Available, "", "widgets")]
    [InlineData(GitHubOriginStatus.Available, "acme", "")]
    [InlineData(GitHubOriginStatus.NotConfigured, "acme", "widgets")]
    [InlineData(GitHubOriginStatus.Unavailable, "acme", "widgets")]
    [InlineData((GitHubOriginStatus)99, null, null)]
    public async Task An_incoherent_observation_is_never_reported_as_observed(GitHubOriginStatus status, string? owner, string? repository)
    {
        var projectId = await SeedAsync();

        var run = await RunAsync(projectId, arrange: (observer, _, _) => observer.Observation =
            new GitHubOriginObservation(status, owner is null || repository is null ? null : new GitHubOriginTarget(owner, repository)));

        AssertUnavailable(run, projectId);
    }

    [Fact]
    public async Task An_unexpected_observer_fault_propagates_and_is_not_a_status()
    {
        var projectId = await SeedAsync();
        await using var dbContext = _fixture.CreateContext();
        var observer = new FakeObserver(_calls) { Throws = new InvalidOperationException("boom") };
        var handler = new GetProjectGitHubOriginQueryHandler(
            dbContext, new FakeRootInspector(_calls), new FakeIdentityInspector(_calls), observer, new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync(new GetProjectGitHubOriginQuery(projectId), CancellationToken.None));
    }

    [Fact]
    public async Task Cancellation_propagates()
    {
        var projectId = await SeedAsync();
        await using var dbContext = _fixture.CreateContext();
        var observer = new FakeObserver(_calls) { Throws = new OperationCanceledException() };
        var handler = new GetProjectGitHubOriginQueryHandler(
            dbContext, new FakeRootInspector(_calls), new FakeIdentityInspector(_calls), observer, new FixedTimeProvider(Now));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => handler.HandleAsync(new GetProjectGitHubOriginQuery(projectId), CancellationToken.None));
    }
}
