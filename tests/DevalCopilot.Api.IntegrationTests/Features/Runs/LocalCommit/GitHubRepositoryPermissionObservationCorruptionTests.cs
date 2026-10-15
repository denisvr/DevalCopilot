using System.Net;
using System.Text.Json;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// ADR-0049: a binding that is not the recorded one is unavailable before anything is invoked. Each case breaks exactly one recorded fact
/// of a real binding, its target, its source or either event, proves the answer is Unavailable with no fact (never NotRecorded or
/// BindingNotRecorded, never a partial scope) and that the permission observer was never invoked, then restores every row and trigger and
/// proves the original answer returns.
/// </summary>
public sealed class GitHubRepositoryPermissionObservationCorruptionTests : GitHubRepositoryPermissionObservationTestBase
{
    private sealed record Case(string Name, string[] Tables, string[] Statements);

    private const string Bindings = "github_repository_identity_bindings";
    private const string Targets = "github_observation_targets";
    private const string Events = "events";
    private const string Operations = "local_commit_operations";

    [Fact]
    public async Task A_binding_target_source_or_event_that_is_not_coherent_is_unavailable_with_no_fact_and_nothing_is_invoked_then_returns_when_restored()
    {
        var bound = await BoundAsync();
        bound.Permission.Answer = () => Matching("ADMIN");
        var runId = bound.Ids.RunId;
        var row = await BindingRowAsync(runId);
        var binding = Upper(row.Id);
        var target = Upper(bound.TargetId);
        var operation = Upper(bound.OperationId);
        var someone = Upper(Guid.NewGuid());

        Case[] cases =
        [
            new("binding node identifier is outside the supported subset", [Bindings], [$"UPDATE {Bindings} SET \"RepositoryNodeId\" = 'has space' WHERE \"Id\" = '{binding}'"]),
            new("binding node identifier differs from its event", [Bindings], [$"UPDATE {Bindings} SET \"RepositoryNodeId\" = 'R_other' WHERE \"Id\" = '{binding}'"]),
            new("binding owner names another owner", [Bindings], [$"UPDATE {Bindings} SET \"Owner\" = 'someone-else' WHERE \"Id\" = '{binding}'"]),
            new("binding commit is another well formed commit", [Bindings], [$"UPDATE {Bindings} SET \"CommitSha\" = '{Nines40}' WHERE \"Id\" = '{binding}'"]),
            new("binding branch is another branch", [Bindings], [$"UPDATE {Bindings} SET \"BranchName\" = 'elsewhere' WHERE \"Id\" = '{binding}'"]),
            new("binding names another target", [Bindings], [$"UPDATE {Bindings} SET \"ObservationTargetId\" = '{someone}' WHERE \"Id\" = '{binding}'"]),
            new("binding time differs from its event time", [Bindings], [$"UPDATE {Bindings} SET \"RecordedAtUtc\" = '2999-01-01 00:00:00+00:00' WHERE \"Id\" = '{binding}'"]),
            new("binding event is missing", [Events], [$"DELETE FROM {Events} WHERE {EventOfBinding(runId)}"]),
            new("binding event payload is empty", [Events], [$"UPDATE {Events} SET \"PayloadJson\" = '' WHERE {EventOfBinding(runId)}"]),
            new("binding row is missing and only its event remains", [Bindings], [$"DELETE FROM {Bindings} WHERE \"Id\" = '{binding}'"]),
            new("target row is missing so the binding is an orphan", [Targets], [$"DELETE FROM {Targets} WHERE \"Id\" = '{target}'"]),
            new("target owner is unsupported", [Targets], [$"UPDATE {Targets} SET \"Owner\" = 'bad owner' WHERE \"Id\" = '{target}'"]),
            new("target owner names another owner", [Targets], [$"UPDATE {Targets} SET \"Owner\" = 'someone-else' WHERE \"Id\" = '{target}'"]),
            new("target event is missing", [Events], [$"DELETE FROM {Events} WHERE {EventOfTarget(runId)}"]),
            new("source branch is another branch", [Operations], [$"UPDATE {Operations} SET \"BranchName\" = 'elsewhere' WHERE \"Id\" = '{operation}'"]),
            new("source commit is another commit", [Operations], [$"UPDATE {Operations} SET \"CommitSha\" = '{new string('0', 40)}' WHERE \"Id\" = '{operation}'"]),
        ];

        var good = await ObservePermissionAsync(bound.Host, runId);
        Assert.Equal("PermissionReported", StatusOf(good));
        var callsAfterGood = bound.Permission.Calls.Count;
        var failures = new List<string>();

        foreach (var (name, tables, statements) in cases)
        {
            await CorruptAsync(tables, statements, async () =>
            {
                var reading = await ObservePermissionAsync(bound.Host, runId);

                if (reading.Status != HttpStatusCode.OK || StatusOf(reading) != "Unavailable"
                    || reading.Root.GetProperty("source").ValueKind != JsonValueKind.Null
                    || reading.Root.GetProperty("target").ValueKind != JsonValueKind.Null
                    || reading.Root.GetProperty("binding").ValueKind != JsonValueKind.Null
                    || reading.Root.GetProperty("observation").ValueKind != JsonValueKind.Null
                    || reading.Body.Contains("ADMIN", StringComparison.Ordinal))
                {
                    failures.Add($"{name}: {reading.Status} {reading.Body}");
                }

                await Task.CompletedTask;
            });
        }

        Assert.Empty(failures);
        Assert.Equal(callsAfterGood, bound.Permission.Calls.Count);
        var restored = await ObservePermissionAsync(bound.Host, runId);
        Assert.Equal("PermissionReported", StatusOf(restored));
        Assert.Equal(callsAfterGood + 1, bound.Permission.Calls.Count);
    }
}
