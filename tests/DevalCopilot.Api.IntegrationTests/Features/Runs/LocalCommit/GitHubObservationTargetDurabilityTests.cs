using System.Net;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Commands.RecordGitHubObservationTarget;
using DevalCopilot.Application.Features.Runs.Ports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// ADR-0040 durability: separate hosts (separate contexts and connections over one database file) racing for one delivery serialize to
/// one target and one event; a real SQLite fault, a cancellation or a restart never leaves half of the pair; a notifier that fails after
/// the commit cannot turn a recorded selection into a failure.
/// </summary>
public sealed class GitHubObservationTargetDurabilityTests : GitHubObservationTargetTestBase
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

    private async Task<(LocalCommitLineageIds Ids, Guid OperationId, string Sha)> DeliverOnceAsync()
    {
        using var delivering = StartHost(runSupervisor: true);
        var (ids, operationId) = await DeliverAsync(delivering);
        return (ids, operationId, await CommitShaOfAsync(operationId));
    }

    [Fact]
    public async Task Concurrent_identical_requests_from_separate_hosts_create_one_target_and_one_event_and_all_return_it()
    {
        var (ids, operationId, sha) = await DeliverOnceAsync();
        var hosts = Enumerable.Range(0, 4).Select(_ => StartHost(runSupervisor: false)).ToArray();

        var responses = await Task.WhenAll(Enumerable.Range(0, 32)
            .Select(index => Task.Run(() => PostTargetAsync(hosts[index % hosts.Length], ids.RunId, BodyOf(operationId, sha)))));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.Status));
        Assert.Single(responses.Select(response => response.Body).Distinct());
        Assert.Equal((1, 1), await CountsAsync(ids.RunId));
        Assert.Equal(responses[0].Root.GetProperty("target").GetProperty("id").GetGuid(), (await TargetRowAsync(ids.RunId)).Id);
    }

    [Fact]
    public async Task Concurrent_different_selections_record_exactly_one_and_conflict_with_the_rest()
    {
        var (ids, operationId, sha) = await DeliverOnceAsync();
        var hosts = Enumerable.Range(0, 4).Select(_ => StartHost(runSupervisor: false)).ToArray();
        var owners = new[] { "first-owner", "second-owner", "third-owner", "fourth-owner" };

        var responses = await Task.WhenAll(Enumerable.Range(0, 32).Select(index => Task.Run(async () =>
            (Owner: owners[index % owners.Length],
             Reading: await PostTargetAsync(hosts[index % hosts.Length], ids.RunId, BodyOf(operationId, sha, owners[index % owners.Length]))))));

        var recorded = await TargetRowAsync(ids.RunId);
        Assert.Contains(recorded.Owner, owners);
        Assert.Equal((1, 1), await CountsAsync(ids.RunId));
        foreach (var (owner, reading) in responses)
        {
            if (owner == recorded.Owner)
            {
                Assert.Equal(HttpStatusCode.OK, reading.Status);
                Assert.Equal(recorded.Id, reading.Root.GetProperty("target").GetProperty("id").GetGuid());
            }
            else
            {
                Assert.Equal(HttpStatusCode.Conflict, reading.Status);
                Assert.Equal("github_observation_target.conflict", reading.Root.GetProperty("errors")[0].GetProperty("code").GetString());
            }
        }

        Assert.Equal("Available", (await GetTargetAsync(hosts[0], ids.RunId)).State);
    }

    [Theory]
    [InlineData("github_observation_targets", "")]
    [InlineData("events", " WHEN NEW.\"EventType\" = 'run.github_observation_target_recorded'")]
    public async Task A_real_fault_while_saving_either_row_rolls_both_back_propagates_and_a_retry_records_one_pair(string table, string when)
    {
        var (ids, operationId, sha) = await DeliverOnceAsync();
        using var host = StartHost(runSupervisor: false);
        var fingerprint = await DatabaseFingerprintAsync();
        await ExecuteSqlAsync(
            $"CREATE TRIGGER fault_target BEFORE INSERT ON {table}{when} BEGIN SELECT RAISE(ABORT, 'injected_target_fault'); END;");

        var failed = await PostTargetAsync(host, ids.RunId, BodyOf(operationId, sha));

        Assert.Equal(HttpStatusCode.InternalServerError, failed.Status);
        Assert.Equal((0, 0), await CountsAsync(ids.RunId));
        Assert.Equal(fingerprint, await DatabaseFingerprintAsync());
        Assert.Equal("NotRecorded", (await GetTargetAsync(host, ids.RunId)).State);

        await ExecuteSqlAsync("DROP TRIGGER fault_target");
        Assert.Equal(HttpStatusCode.OK, (await PostTargetAsync(host, ids.RunId, BodyOf(operationId, sha))).Status);
        Assert.Equal((1, 1), await CountsAsync(ids.RunId));
    }

    [Fact]
    public async Task A_cancelled_request_leaves_neither_row()
    {
        var (ids, operationId, sha) = await DeliverOnceAsync();
        using var host = StartHost(runSupervisor: false);
        var fingerprint = await DatabaseFingerprintAsync();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await mediator.SendAsync(
                new RecordGitHubObservationTargetCommand(ids.RunId, operationId, sha, DefaultOwner, DefaultRepository), cancelled.Token));
        }

        Assert.Equal((0, 0), await CountsAsync(ids.RunId));
        Assert.Equal(fingerprint, await DatabaseFingerprintAsync());
    }

    [Fact]
    public async Task A_notifier_that_fails_after_the_commit_cannot_undo_or_fail_the_recorded_selection()
    {
        var (ids, operationId, sha) = await DeliverOnceAsync();
        var notifier = new ThrowingNotifier();
        using var host = StartHost(
            runSupervisor: false,
            configure: services =>
            {
                services.RemoveAll<IRunEventNotifier>();
                services.AddSingleton<IRunEventNotifier>(notifier);
            });

        var response = await PostTargetAsync(host, ids.RunId, BodyOf(operationId, sha));

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal(1, notifier.Calls);
        Assert.Equal((1, 1), await CountsAsync(ids.RunId));
    }

    [Fact]
    public async Task The_selection_survives_a_restart_and_a_replay_after_it_adds_nothing()
    {
        var (ids, operationId, sha) = await DeliverOnceAsync();
        string recorded;
        using (var first = StartHost(runSupervisor: false))
        {
            recorded = await RecordAsync(first, ids.RunId, operationId, sha);
        }

        using var restarted = StartHost(runSupervisor: true);
        var reread = await GetTargetAsync(restarted, ids.RunId);
        var fingerprint = await DatabaseFingerprintAsync();
        var replay = await RecordAsync(restarted, ids.RunId, operationId, sha);

        Assert.Equal("Available", reread.State);
        Assert.Equal(recorded, replay);
        Assert.Equal(reread.Root.GetProperty("target").ToString(), System.Text.Json.JsonDocument.Parse(recorded).RootElement.GetProperty("target").ToString());
        Assert.Equal((1, 1), await CountsAsync(ids.RunId));
        Assert.Equal(fingerprint, await DatabaseFingerprintAsync());
    }
}
