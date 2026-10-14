using System.Net;
using System.Text.Json;
using DevalCopilot.Application.Features.Runs.Queries.GetGitHubCiObservation;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// ADR-0044: the answer of the external call is only meaningful for the scope and the binding it was requested for. Each case changes or
/// removes exactly one recorded fact while the scripted check observer is still running, returning the most tempting remote answer (the
/// bound repository, the recorded commit and a SUCCESS state), and proves the answer is Unavailable with no scope, binding, observation or
/// remote datum. The binding cases fail unless the binding and its event are read again after the call.
/// </summary>
public sealed class GitHubCiObservationDriftTests : GitHubCiObservationTestBase
{
    private sealed record Case(string Name, string[] Tables, string Statement);

    private const string Bindings = "github_repository_identity_bindings";
    private const string Targets = "github_observation_targets";
    private const string Events = "events";
    private const string Operations = "local_commit_operations";

    [Fact]
    public async Task A_scope_or_binding_lost_or_changed_during_the_observation_refuses_the_answer_and_returns_no_remote_data()
    {
        var bound = await BoundAsync();
        var branch = Scene.BranchName;
        var runId = bound.Ids.RunId;
        var binding = Upper((await BindingRowAsync(runId)).Id);
        var target = Upper(bound.TargetId);
        var operation = Upper(bound.OperationId);

        Case[] cases =
        [
            new("binding event deleted", [Events], $"DELETE FROM {Events} WHERE {EventOfBinding(runId)}"),
            new("binding row deleted", [Bindings], $"DELETE FROM {Bindings} WHERE \"Id\" = '{binding}'"),
            new("binding node identifier replaced by another valid one", [Bindings], $"UPDATE {Bindings} SET \"RepositoryNodeId\" = 'R_kgDODrifted01' WHERE \"Id\" = '{binding}'"),
            new("binding owner changed in case", [Bindings], $"UPDATE {Bindings} SET \"Owner\" = 'OCTO-CAT' WHERE \"Id\" = '{binding}'"),
            new("binding event payload emptied", [Events], $"UPDATE {Events} SET \"PayloadJson\" = '' WHERE {EventOfBinding(runId)}"),
            new("target event deleted", [Events], $"DELETE FROM {Events} WHERE {EventOfTarget(runId)}"),
            new("target row deleted", [Targets], $"DELETE FROM {Targets} WHERE \"Id\" = '{target}'"),
            new("target owner changed", [Targets], $"UPDATE {Targets} SET \"Owner\" = 'someone-else' WHERE \"Id\" = '{target}'"),
            new("source branch changed", [Operations], $"UPDATE {Operations} SET \"BranchName\" = 'elsewhere' WHERE \"Id\" = '{operation}'"),
            new("source commit changed", [Operations], $"UPDATE {Operations} SET \"CommitSha\" = '{new string('0', 40)}' WHERE \"Id\" = '{operation}'"),
        ];

        var failures = new List<string>();
        foreach (var (name, tables, statement) in cases)
        {
            await CorruptAsync(tables, [], async () =>
            {
                bound.Ci.Answer = async () =>
                {
                    await ExecuteSqlAsync("PRAGMA foreign_keys = OFF; " + statement);
                    return await MatchingSuccess(branch, bound.Sha);
                };

                var reading = await ObserveCiAsync(bound.Host, runId);

                if (reading.Status != HttpStatusCode.OK || StatusOf(reading) != "Unavailable"
                    || reading.Root.GetProperty("source").ValueKind != JsonValueKind.Null
                    || reading.Root.GetProperty("target").ValueKind != JsonValueKind.Null
                    || reading.Root.GetProperty("binding").ValueKind != JsonValueKind.Null
                    || reading.Root.GetProperty("observation").ValueKind != JsonValueKind.Null
                    || reading.Body.Contains(NodeId, StringComparison.Ordinal)
                    || reading.Body.Contains("SUCCESS", StringComparison.Ordinal)
                    || reading.Body.Contains(bound.Sha, StringComparison.Ordinal))
                {
                    failures.Add($"{name}: {reading.Status} {reading.Body}");
                }
            });
        }

        Assert.Empty(failures);
        Assert.Equal(cases.Length, bound.Ci.Calls.Count);

        // Everything is restored byte for byte, so the same request now observes and reports again.
        bound.Ci.Answer = () => MatchingSuccess(branch, bound.Sha);
        var restored = await ObserveCiAsync(bound.Host, runId);
        Assert.Equal("StatusReported", StatusOf(restored));
        Assert.Equal(JsonValueKind.Object, restored.Root.GetProperty("binding").ValueKind);
    }

    private static Task<GitHubCiProbe?> MatchingSuccess(string branch, string sha) =>
        CiProbe("OCTO-CAT", "Hello.World", reference: new GitHubCiRef(branch, sha), rollup: "SUCCESS");
}
