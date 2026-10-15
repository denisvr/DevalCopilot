using System.Collections.Concurrent;
using System.Net;
using DevalCopilot.Application.Features.Runs.Queries.GetGitHubRepositoryPermissionObservation;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit.GitHubObservationTargetTestBase;
using static DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit.GitHubRepositoryIdentityBindingTestBase;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// ADR-0049: the repository permission port joins the shared sealed-scene guard's refusal inventory (and nothing else changes). Both
/// existing sealed clone compositions (the check-context clone and the check-run output clone) must resolve a named refusing permission
/// observer from the effective host, record the refusal in their own queue only, start no non-Git process (a fail-on-use process executor
/// is composed into each host) and leave the sealed scene and every sealed image byte for byte unchanged. The production endpoint reached
/// through a guarded host fails at the refusal and is never observed or written.
/// </summary>
public sealed class GitHubRepositoryPermissionObservationSealedGuardTests(GitHubCheckContextsSealedEvidence evidence)
    : IClassFixture<GitHubCheckContextsSealedEvidence>, IDisposable
{
    private const string Refusal = "IGitHubRepositoryPermissionObserver.ObserveAsync";
    private const string Message = Refusal + " is refused on a sealed scene.";

    private readonly List<IDisposable> _clones = [];

    public void Dispose()
    {
        foreach (var clone in _clones)
        {
            clone.Dispose();
        }
    }

    private async Task<(ConcurrentQueue<string> Refusals, LocalCommitHost Host, Guid RunId, FailOnUseProcessExecutionAdapter Process)> ContextsCloneAsync()
    {
        var clone = evidence.ReserveClone();
        _clones.Add(clone);
        var process = new FailOnUseProcessExecutionAdapter();
        var bound = await clone.ProvisionAsync(GitHubCheckContextsSealedStage.BindingRecorded, processExecution: process);
        return (clone.Refusals, bound.Host, bound.Ids.RunId, process);
    }

    private async Task<(ConcurrentQueue<string> Refusals, LocalCommitHost Host, Guid RunId, FailOnUseProcessExecutionAdapter Process)> OutputCloneAsync()
    {
        var clone = new GitHubCheckRunOutputSealedClone(evidence);
        _clones.Add(clone);
        var process = new FailOnUseProcessExecutionAdapter();
        var bound = await clone.ProvisionAsync(GitHubCheckContextsSealedStage.BindingRecorded, processExecution: process);
        return (clone.Refusals, bound.Host, bound.Ids.RunId, process);
    }

    private static async Task<InvalidOperationException> RefuseAsync(LocalCommitHost host)
    {
        var permission = host.Services.GetRequiredService<IGitHubRepositoryPermissionObserver>();
        Assert.IsAssignableFrom<SealedSceneGuard.RefusingProxy>(permission);

        var refused = await Record.ExceptionAsync(() => permission.ObserveAsync(GhPath, DefaultOwner, DefaultRepository, CancellationToken.None));

        return Assert.IsType<InvalidOperationException>(refused);
    }

    [Fact]
    public async Task The_check_context_clone_host_refuses_permission_observation_with_its_named_refusal_and_starts_no_non_git_process()
    {
        var sceneBefore = evidence.SceneFingerprint();
        var (refusals, host, _, process) = await ContextsCloneAsync();

        var refused = await RefuseAsync(host);

        Assert.Equal(Message, refused.Message);
        Assert.Equal([Refusal], refusals);
        Assert.Empty(process.Attempts);
        Assert.Equal(sceneBefore, evidence.SceneFingerprint());
        evidence.AssertSealedUnchanged();
    }

    [Fact]
    public async Task The_check_run_output_clone_host_refuses_permission_observation_with_its_named_refusal_and_starts_no_non_git_process()
    {
        var sceneBefore = evidence.SceneFingerprint();
        var (refusals, host, _, process) = await OutputCloneAsync();

        var refused = await RefuseAsync(host);

        Assert.Equal(Message, refused.Message);
        Assert.Equal([Refusal], refusals);
        Assert.Empty(process.Attempts);
        Assert.Equal(sceneBefore, evidence.SceneFingerprint());
        evidence.AssertSealedUnchanged();
    }

    [Fact]
    public async Task Refusal_queues_are_isolated_between_clone_compositions_that_are_alive_at_once()
    {
        var (contexts, contextsHost, _, contextsProcess) = await ContextsCloneAsync();
        var (output, outputHost, _, outputProcess) = await OutputCloneAsync();

        await RefuseAsync(contextsHost);
        Assert.Equal([Refusal], contexts);
        Assert.Empty(output);

        await RefuseAsync(outputHost);
        await RefuseAsync(outputHost);
        Assert.Equal([Refusal], contexts);
        Assert.Equal([Refusal, Refusal], output);
        Assert.Empty(contextsProcess.Attempts);
        Assert.Empty(outputProcess.Attempts);
        evidence.AssertSealedUnchanged();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_production_endpoint_on_a_guarded_clone_host_fails_at_the_named_refusal_and_is_never_observed_or_written(bool contextsClone)
    {
        var (refusals, host, runId, process) = contextsClone ? await ContextsCloneAsync() : await OutputCloneAsync();
        using var client = LocalCommitLineage.AuthenticatedClient(host);

        var response = await client.GetAsync($"/api/runs/{runId}/{GitHubRepositoryPermissionObservationTestBase.PermissionRoute}");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal([Refusal], refusals);
        Assert.Empty(process.Attempts);
        evidence.AssertSealedUnchanged();
    }

    [Fact]
    public async Task The_shared_guard_alone_refuses_the_permission_port_with_its_named_refusal_and_lists_it_exactly_once()
    {
        var refusals = new ConcurrentQueue<string>();
        var services = new ServiceCollection();
        SealedSceneGuard.Apply(services, refusals);
        await using var provider = services.BuildServiceProvider();
        var permission = provider.GetRequiredService<IGitHubRepositoryPermissionObserver>();

        var refused = await Record.ExceptionAsync(() => permission.ObserveAsync(GhPath, DefaultOwner, DefaultRepository, CancellationToken.None));

        Assert.Equal(Message, Assert.IsType<InvalidOperationException>(refused).Message);
        Assert.Equal([Refusal], refusals);
        Assert.Single(SealedSceneGuard.GuardedPorts, port => port == typeof(IGitHubRepositoryPermissionObserver));
    }
}
