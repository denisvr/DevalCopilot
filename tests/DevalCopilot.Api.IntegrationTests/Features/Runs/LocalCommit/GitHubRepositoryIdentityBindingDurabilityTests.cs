using System.Net;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Commands.BindGitHubRepositoryIdentity;
using DevalCopilot.Application.Features.Runs.Ports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// ADR-0043 durability: separate hosts (separate contexts and connections over one database file) racing for one target serialize to one
/// binding and one event; a real SQLite fault, a cancellation or a restart never leaves half of the pair; a notifier that fails after the
/// commit cannot turn a recorded binding into a failure.
/// </summary>
public sealed class GitHubRepositoryIdentityBindingDurabilityTests : GitHubRepositoryIdentityBindingTestBase
{
    private sealed class ThrowingNotifier : IRunEventNotifier
    {
        public int Calls;

        public Task NotifyRunAdvancedAsync(Guid runId, long latestSequence, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            throw new InvalidOperationException("The notification transport failed after the facts were durable.");
        }
    }

    private LocalCommitHost HostWith(ScriptedObserver observer, Action<IServiceCollection>? more = null) =>
        StartHost(
            runSupervisor: false,
            configure: services =>
            {
                services.RemoveAll<IGitHubRepositoryIdentityObserver>();
                services.AddSingleton<IGitHubRepositoryIdentityObserver>(observer);
                more?.Invoke(services);
            });

    /// <summary>A delivered run with its target recorded and a current snapshot, with the first host closed so others own the database.</summary>
    private async Task<(Delivered Delivered, ScriptedObserver Observer)> PreparedAsync()
    {
        var delivered = await DeliveredAsync();
        return (delivered, delivered.Observer);
    }

    [Fact]
    public async Task Concurrent_identical_requests_from_separate_hosts_create_one_binding_and_one_event_and_all_return_it()
    {
        var (delivered, observer) = await PreparedAsync();
        var hosts = Enumerable.Range(0, 4).Select(_ => HostWith(observer)).ToArray();

        var responses = await Task.WhenAll(Enumerable.Range(0, 16)
            .Select(index => Task.Run(() => PostBindingAsync(hosts[index % hosts.Length], delivered.Ids.RunId, BodyFor(delivered)))));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.Status));
        Assert.Single(responses.Select(response => response.Body).Distinct());
        Assert.Equal((1, 1), await BindingCountsAsync(delivered.Ids.RunId));
        Assert.Equal(responses[0].Root.GetProperty("binding").GetProperty("id").GetGuid(), (await BindingRowAsync(delivered.Ids.RunId)).Id);
    }

    [Fact]
    public async Task Concurrent_different_node_identifiers_record_exactly_one_and_the_rest_are_refused()
    {
        var (delivered, observer) = await PreparedAsync();
        var hosts = Enumerable.Range(0, 4).Select(_ => HostWith(observer)).ToArray();
        var nodes = new[] { "R_first01", "R_second01", "R_third01", "R_fourth01" };
        observer.Answer = () => Identity(nodeId: NodeId);

        var responses = await Task.WhenAll(Enumerable.Range(0, 16).Select(index => Task.Run(async () =>
            (Node: nodes[index % nodes.Length],
             Reading: await PostBindingAsync(hosts[index % hosts.Length], delivered.Ids.RunId, BodyFor(delivered, nodes[index % nodes.Length]))))));

        Assert.Equal((0, 0), await BindingCountsAsync(delivered.Ids.RunId));
        Assert.All(responses, response =>
        {
            Assert.Equal(HttpStatusCode.Conflict, response.Reading.Status);
            Assert.Equal("github_repository_identity_binding.identity_changed", ErrorCode(response.Reading));
        });

        // The only identity the observer agrees with is the one that is recorded, and everything else then conflicts with it.
        observer.Answer = () => Identity(nodeId: nodes[0]);
        var mixed = await Task.WhenAll(Enumerable.Range(0, 16).Select(index => Task.Run(async () =>
            (Node: nodes[index % nodes.Length],
             Reading: await PostBindingAsync(hosts[index % hosts.Length], delivered.Ids.RunId, BodyFor(delivered, nodes[index % nodes.Length]))))));

        Assert.Equal((1, 1), await BindingCountsAsync(delivered.Ids.RunId));
        Assert.Equal(nodes[0], (await BindingRowAsync(delivered.Ids.RunId)).RepositoryNodeId);
        Assert.All(mixed.Where(response => response.Node == nodes[0]), response => Assert.Equal(HttpStatusCode.OK, response.Reading.Status));
        Assert.All(
            mixed.Where(response => response.Node != nodes[0]),
            response => Assert.True(
                response.Reading.Status == HttpStatusCode.Conflict
                && ErrorCode(response.Reading) is "github_repository_identity_binding.conflict"
                    or "github_repository_identity_binding.identity_changed",
                response.Reading.Body));
    }

    [Theory]
    [InlineData("github_repository_identity_bindings", "")]
    [InlineData("events", " WHEN NEW.\"EventType\" = 'run.github_repository_identity_bound'")]
    public async Task A_real_fault_while_saving_either_row_rolls_both_back_propagates_and_a_retry_records_one_pair(string table, string when)
    {
        var delivered = await DeliveredAsync();
        var fingerprint = await DatabaseFingerprintAsync();
        await ExecuteSqlAsync(
            $"CREATE TRIGGER fault_binding BEFORE INSERT ON {table}{when} BEGIN SELECT RAISE(ABORT, 'injected_binding_fault'); END;");

        var failed = await PostBindingAsync(delivered.Host, delivered.Ids.RunId, BodyFor(delivered));

        Assert.Equal(HttpStatusCode.InternalServerError, failed.Status);
        Assert.Equal((0, 0), await BindingCountsAsync(delivered.Ids.RunId));
        Assert.Equal(fingerprint, await DatabaseFingerprintAsync());
        Assert.Equal("NotRecorded", (await GetBindingAsync(delivered.Host, delivered.Ids.RunId)).Root.GetProperty("state").GetString());

        await ExecuteSqlAsync("DROP TRIGGER fault_binding");
        Assert.Equal(HttpStatusCode.OK, (await PostBindingAsync(delivered.Host, delivered.Ids.RunId, BodyFor(delivered))).Status);
        Assert.Equal((1, 1), await BindingCountsAsync(delivered.Ids.RunId));
    }

    [Fact]
    public async Task A_request_cancelled_after_the_observation_and_before_the_commit_leaves_neither_row()
    {
        var delivered = await DeliveredAsync();
        var fingerprint = await DatabaseFingerprintAsync();
        using var cancelled = new CancellationTokenSource();
        delivered.Observer.Answer = async () =>
        {
            await cancelled.CancelAsync();
            return new GitHubRepositoryIdentity(NodeId, DefaultOwner, DefaultRepository);
        };

        await using (var scope = delivered.Host.Services.CreateAsyncScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await mediator.SendAsync(
                new BindGitHubRepositoryIdentityCommand(
                    delivered.Ids.RunId, delivered.OperationId, delivered.Sha, delivered.TargetId, NodeId),
                cancelled.Token));
        }

        Assert.Equal((0, 0), await BindingCountsAsync(delivered.Ids.RunId));
        Assert.Equal(fingerprint, await DatabaseFingerprintAsync());
        Assert.Single(delivered.Observer.Calls);
    }

    [Fact]
    public async Task A_notifier_that_fails_after_the_commit_cannot_undo_or_fail_the_recorded_binding()
    {
        var (delivered, observer) = await PreparedAsync();
        var notifier = new ThrowingNotifier();
        var host = HostWith(observer, services =>
        {
            services.RemoveAll<IRunEventNotifier>();
            services.AddSingleton<IRunEventNotifier>(notifier);
        });

        var response = await PostBindingAsync(host, delivered.Ids.RunId, BodyFor(delivered));

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal(1, notifier.Calls);
        Assert.Equal((1, 1), await BindingCountsAsync(delivered.Ids.RunId));
    }

    [Fact]
    public async Task The_binding_survives_a_restart_and_a_replay_after_it_adds_nothing_and_observes_nothing()
    {
        var (delivered, observer) = await PreparedAsync();
        var recorded = await PostBindingAsync(delivered.Host, delivered.Ids.RunId, BodyFor(delivered));

        var restarted = HostWith(observer);
        var reread = await GetBindingAsync(restarted, delivered.Ids.RunId);
        var fingerprint = await DatabaseFingerprintAsync();
        var replay = await PostBindingAsync(restarted, delivered.Ids.RunId, BodyFor(delivered));

        Assert.Equal("Available", reread.Root.GetProperty("state").GetString());
        Assert.Equal(recorded.Body, replay.Body);
        Assert.Equal(reread.Root.GetProperty("binding").ToString(), recorded.Root.GetProperty("binding").ToString());
        Assert.Single(observer.Calls);
        Assert.Equal((1, 1), await BindingCountsAsync(delivered.Ids.RunId));
        Assert.Equal(fingerprint, await DatabaseFingerprintAsync());
    }
}
