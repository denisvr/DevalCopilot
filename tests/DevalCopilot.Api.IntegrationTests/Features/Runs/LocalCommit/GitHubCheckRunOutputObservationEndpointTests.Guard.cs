using System.Collections.Concurrent;
using System.Net;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Application.Features.Runs.Queries.GetGitHubCheckContextsObservation;
using DevalCopilot.Application.Features.Runs.Queries.GetGitHubCheckRunOutputObservation;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// ADR-0047 correction: the feature-owned clone host over the sealed scene enables only this operation's controlled output observer. The
/// repository identity and check-context observers, which the shared guard leaves to each class, are resolved from the effective host
/// and must refuse at once with their named refusal recorded in the clone's own queue. A fail-on-use process executor is composed into
/// these hosts, so a port that still reached a production adapter would be counted as a process attempt and never start an executable.
/// </summary>
public sealed partial class GitHubCheckRunOutputObservationEndpointTests
{
    private const string IdentityRefusal = "IGitHubRepositoryIdentityObserver.ObserveAsync";
    private const string ContextsRefusal = "IGitHubCheckContextsObserver.ObserveAsync";

    private async Task<(Bound Bound, FailOnUseProcessExecutionAdapter Process)> GuardedAsync()
    {
        var process = new FailOnUseProcessExecutionAdapter();
        var bound = await _clone.ProvisionAsync(GitHubCheckContextsSealedStage.BindingRecorded, processExecution: process);
        return (bound, process);
    }

    [Fact]
    public async Task The_resolved_clone_host_refuses_identity_observation_with_its_named_refusal_and_starts_no_process()
    {
        var (bound, process) = await GuardedAsync();
        var identity = bound.Host.Services.GetRequiredService<IGitHubRepositoryIdentityObserver>();

        var refused = await Record.ExceptionAsync(
            () => identity.ObserveAsync(GhPath, DefaultOwner, DefaultRepository, CancellationToken.None));

        Assert.Equal($"{IdentityRefusal} is refused on a sealed scene.", Assert.IsType<InvalidOperationException>(refused).Message);
        Assert.Equal([IdentityRefusal], _clone.Refusals);
        Assert.Empty(process.Attempts);
        _evidence.AssertSealedUnchanged();
    }

    [Fact]
    public async Task The_resolved_clone_host_refuses_contexts_observation_with_its_named_refusal_and_starts_no_process()
    {
        var (bound, process) = await GuardedAsync();
        var contexts = bound.Host.Services.GetRequiredService<IGitHubCheckContextsObserver>();

        var refused = await Record.ExceptionAsync(
            () => contexts.ObserveAsync(GhPath, DefaultOwner, DefaultRepository, Scene.BranchName, CancellationToken.None));

        Assert.Equal($"{ContextsRefusal} is refused on a sealed scene.", Assert.IsType<InvalidOperationException>(refused).Message);
        Assert.Equal([ContextsRefusal], _clone.Refusals);
        Assert.Empty(process.Attempts);
        _evidence.AssertSealedUnchanged();
    }

    [Fact]
    public async Task The_controlled_output_observer_still_answers_through_the_guarded_host_with_no_refusal_and_no_process()
    {
        var (bound, process) = await GuardedAsync();
        Admit(bound, Detail("guarded host"));

        var reading = await ObserveOutputAsync(bound.Host, bound.Ids.RunId);

        Assert.Equal(HttpStatusCode.OK, reading.Status);
        Assert.True(StatusOf(reading) == "OutputReported", reading.Body);
        Assert.Equal("guarded host", reading.Root.GetProperty("observation").GetProperty("checkRun").GetProperty("name").GetString());
        Assert.Equal(["metadata", "output"], bound.Output.Calls.Select(call => call.Phase).ToArray());
        Assert.Empty(_clone.Refusals);
        Assert.Empty(process.Attempts);
        _evidence.AssertSealedUnchanged();
    }

    [Fact]
    public async Task The_shared_guard_alone_refuses_the_output_port_with_its_named_refusal_so_only_this_clone_composition_enables_it()
    {
        var refusals = new ConcurrentQueue<string>();
        var services = new ServiceCollection();
        SealedSceneGuard.Apply(services, refusals);
        await using var provider = services.BuildServiceProvider();
        var output = provider.GetRequiredService<IGitHubCheckRunOutputObserver>();

        var refused = await Record.ExceptionAsync(
            () => output.ObserveMetadataAsync(GhPath, DefaultOwner, DefaultRepository, Scene.BranchName, CheckId, CancellationToken.None));

        Assert.Equal(
            "IGitHubCheckRunOutputObserver.ObserveMetadataAsync is refused on a sealed scene.",
            Assert.IsType<InvalidOperationException>(refused).Message);
        Assert.Equal(["IGitHubCheckRunOutputObserver.ObserveMetadataAsync"], refusals);
        Assert.Single(SealedSceneGuard.GuardedPorts, port => port == typeof(IGitHubCheckRunOutputObserver));
    }
}
