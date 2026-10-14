using System.Net;
using System.Text.Json;
using DevalCopilot.Application.Features.Runs.Queries.GetGitHubCheckContextsObservation;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// ADR-0045: the answer of the external call is only meaningful for the scope and the binding it was requested for. Each case changes or
/// removes exactly one recorded fact while the scripted contexts observer is still running, returning the most tempting remote answer (the
/// bound repository, the recorded commit and a one-entry collection), and proves the answer is Unavailable with no scope, binding, observation or
/// remote datum. The binding cases fail unless the binding and its event are read again after the call.
/// </summary>
public sealed class GitHubCheckContextsObservationDriftTests : GitHubCheckContextsObservationTestBase
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
            // Both binding facts gone together is a coherent "not bound" reading with the same source and target, so only the post-call
            // binding reading can notice it.
            new("binding row and event both deleted", [Events, Bindings], $"DELETE FROM {Bindings} WHERE \"Id\" = '{binding}'; DELETE FROM {Events} WHERE {EventOfBinding(runId)}"),
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
                bound.Contexts.Answer = async () =>
                {
                    await ExecuteSqlAsync("PRAGMA foreign_keys = OFF; " + statement);
                    return await MatchingSuccess(branch, bound.Sha);
                };

                var reading = await ObserveContextsAsync(bound.Host, runId);

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
        Assert.Equal(cases.Length, bound.Contexts.Calls.Count);

        // Everything is restored byte for byte, so the same request now observes and reports again.
        bound.Contexts.Answer = () => MatchingSuccess(branch, bound.Sha);
        var restored = await ObserveContextsAsync(bound.Host, runId);
        Assert.Equal("ContextsReported", StatusOf(restored));
        Assert.Equal(JsonValueKind.Object, restored.Root.GetProperty("binding").ValueKind);
    }

    /// <summary>
    /// The one drift that keeps the scope kind (Bound), the source and the target and changes only the binding's own values: the binding
    /// row and its canonical event are replaced together by a different but fully coherent binding (new identity, new node identifier).
    /// Only a comparison of the whole binding value can notice it, so it is the regression for "binding values ignored". The replacement is
    /// first proved admissible through the unchanged ADR-0043 read; the same statements then run while the observation waits.
    /// </summary>
    [Fact]
    public async Task A_coherent_replacement_of_the_binding_and_its_event_during_the_observation_is_unavailable_with_every_fact_stripped()
    {
        var bound = await BoundAsync();
        var branch = Scene.BranchName;
        var runId = bound.Ids.RunId;
        var original = await BindingRowAsync(runId);
        var targetRow = await TargetRowAsync(runId);
        var replacement = GitHubRepositoryIdentityBinding.Record(
            Guid.NewGuid(), original.RunId, original.ProjectId, original.LocalCommitOperationId, original.GitWorkspaceId,
            original.ObservationTargetId, original.CommitSha, original.BranchName, "R_kgDOReplaced01", original.Owner, original.Repository,
            targetRow.RecordedAtUtc, original.ObservedAtUtc, original.RecordedAtUtc, original.EventId);
        var statement =
            $"UPDATE {Bindings} SET \"Id\" = '{Upper(replacement.Id)}', \"RepositoryNodeId\" = '{replacement.RepositoryNodeId}' "
            + $"WHERE \"Id\" = '{Upper(original.Id)}'; "
            + $"UPDATE {Events} SET \"PayloadJson\" = '{GitHubRepositoryIdentityBindingEventPayload.Build(replacement)}' WHERE {EventOfBinding(runId)}";
        bound.Contexts.Answer = () => MatchingSuccess(branch, bound.Sha);
        var originalRead = (await GetBindingAsync(bound.Host, runId)).Body;

        // The replacement is a genuine, coherent binding: the unchanged reader admits it (state Available, the new identity) and the
        // observation sees the same Bound scope with the same source and target.
        await CorruptAsync([Events, Bindings], [statement], async () =>
        {
            var read = await GetBindingAsync(bound.Host, runId);
            Assert.True(read.Root.GetProperty("state").GetString() == "Available", read.Body);
            var shown = read.Root.GetProperty("binding");
            Assert.Equal(replacement.Id, shown.GetProperty("id").GetGuid());
            Assert.Equal("R_kgDOReplaced01", shown.GetProperty("repositoryNodeId").GetString());
            Assert.Equal(original.EventId, shown.GetProperty("eventId").GetGuid());
            var scope = await ObserveContextsAsync(bound.Host, runId);
            Assert.True(StatusOf(scope) == "IdentityChanged", scope.Body);
            Assert.Equal(bound.Sha, scope.Root.GetProperty("source").GetProperty("commitSha").GetString());
            Assert.Equal(replacement.Id, scope.Root.GetProperty("binding").GetProperty("id").GetGuid());
        });
        Assert.Equal(originalRead, (await GetBindingAsync(bound.Host, runId)).Body);
        var callsBeforeDrift = bound.Contexts.Calls.Count;

        await CorruptAsync([Events, Bindings], [], async () =>
        {
            bound.Contexts.Answer = async () =>
            {
                await ExecuteSqlAsync("PRAGMA foreign_keys = OFF; " + statement);
                return await MatchingSuccess(branch, bound.Sha);
            };

            var reading = await ObserveContextsAsync(bound.Host, runId);

            Assert.Equal(HttpStatusCode.OK, reading.Status);
            Assert.True(StatusOf(reading) == "Unavailable", reading.Body);
            AssertFacts(reading, source: false, target: false, binding: false, observation: false);
            Assert.DoesNotContain(NodeId, reading.Body, StringComparison.Ordinal);
            Assert.DoesNotContain("R_kgDOReplaced01", reading.Body, StringComparison.Ordinal);
            Assert.DoesNotContain("SUCCESS", reading.Body, StringComparison.Ordinal);
        });

        Assert.Equal(callsBeforeDrift + 1, bound.Contexts.Calls.Count);
        Assert.Equal(originalRead, (await GetBindingAsync(bound.Host, runId)).Body);
        bound.Contexts.Answer = () => MatchingSuccess(branch, bound.Sha);
        var restored = await ObserveContextsAsync(bound.Host, runId);
        Assert.Equal("ContextsReported", StatusOf(restored));
        Assert.Equal(original.Id, restored.Root.GetProperty("binding").GetProperty("id").GetGuid());
    }

    private static Task<GitHubCheckContextsProbe?> MatchingSuccess(string branch, string sha) =>
        ContextsProbe("OCTO-CAT", "Hello.World", reference: new GitHubCheckContextsRef(branch, sha), page: PageOf(1, RunEntry(name: "SUCCESS")));
}
