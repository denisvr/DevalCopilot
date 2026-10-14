using System.Net;
using System.Text.Json;
using DevalCopilot.Application.Features.Runs.Queries.GetGitHubCheckRunOutputObservation;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// ADR-0047: the answer of an external call is only meaningful for the scope and the binding it was requested for, and the output phase
/// repeats the proof. Each case changes or removes exactly one recorded fact while the scripted observer of ONE phase is running (the
/// other phase would have answered with the most tempting remote data: the bound repository, the recorded commit and a sentinel
/// check), and proves the answer is Unavailable with no scope, binding, observation or remote datum. The binding cases fail unless the
/// binding and its event are read again, by complete value, after each call.
/// </summary>
public sealed partial class GitHubCheckRunOutputObservationEndpointTests
{
    private sealed record DriftCase(string Name, string[] Tables, string Statement);

    private const string Bindings = "github_repository_identity_bindings";
    private const string Targets = "github_observation_targets";
    private const string Events = "events";
    private const string Operations = "local_commit_operations";

    private DriftCase[] DriftCases(Bound bound, string binding)
    {
        var runId = bound.Ids.RunId;
        var target = Upper(bound.TargetId);
        var operation = Upper(bound.OperationId);
        return
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
    }

    [Theory]
    [InlineData("metadata")]
    [InlineData("output")]
    public async Task A_scope_or_binding_lost_or_changed_during_either_phase_refuses_the_answer_and_returns_no_remote_data(string phase)
    {
        var bound = await SnapshotBoundAsync();
        var runId = bound.Ids.RunId;
        var binding = Upper((await BindingRowAsync(runId)).Id);
        var cases = DriftCases(bound, binding);
        var failures = new List<string>();
        foreach (var drift in cases)
        {
            await CorruptAsync(drift.Tables, [], async () =>
            {
                async Task<GitHubCheckRunProbe?> Drifting(Func<Task<GitHubCheckRunProbe?>> answer)
                {
                    await ExecuteSqlAsync("PRAGMA foreign_keys = OFF; " + drift.Statement);
                    return await answer();
                }

                bound.Output.Metadata = phase == "metadata"
                    ? () => Drifting(() => MetadataProbe(bound))
                    : () => MetadataProbe(bound);
                bound.Output.Output = phase == "output"
                    ? () => Drifting(() => OutputProbe(bound, OutputNode(bound.Sha, Sentinel())))
                    : () => OutputProbe(bound, OutputNode(bound.Sha, Sentinel()));

                var reading = await ObserveOutputAsync(bound.Host, runId);

                if (reading.Status != HttpStatusCode.OK || StatusOf(reading) != "Unavailable"
                    || reading.Root.GetProperty("source").ValueKind != JsonValueKind.Null
                    || reading.Root.GetProperty("target").ValueKind != JsonValueKind.Null
                    || reading.Root.GetProperty("binding").ValueKind != JsonValueKind.Null
                    || reading.Root.GetProperty("observation").ValueKind != JsonValueKind.Null
                    || reading.Body.Contains(NodeId, StringComparison.Ordinal)
                    || reading.Body.Contains("SENTINEL", StringComparison.Ordinal)
                    || reading.Body.Contains(bound.Sha, StringComparison.Ordinal))
                {
                    failures.Add($"{drift.Name} ({phase}): {reading.Status} {reading.Body}");
                }
            });
        }

        Assert.Empty(failures);
        Assert.Equal(cases.Length, bound.Output.MetadataCalls);
        Assert.Equal(phase == "output" ? cases.Length : 0, bound.Output.OutputCalls);

        // Everything is restored byte for byte, so the same request now observes and reports again.
        Admit(bound, Detail("restored"));
        var restored = await ObserveOutputAsync(bound.Host, runId);
        Assert.Equal("OutputReported", StatusOf(restored));
        Assert.Equal(JsonValueKind.Object, restored.Root.GetProperty("binding").ValueKind);
    }

    /// <summary>
    /// The one drift that keeps the scope kind (Bound), the source and the target and changes only the binding's own values: the binding
    /// row and its canonical event are replaced together by a different but fully coherent binding (new identity, new node identifier).
    /// Only a comparison of the whole binding value can notice it, so it is the regression for "binding values ignored" after each
    /// call. The replacement is first proved admissible through the unchanged ADR-0043 read; the same statements then run while the
    /// observation waits inside the metadata or the output call.
    /// </summary>
    [Theory]
    [InlineData("metadata")]
    [InlineData("output")]
    public async Task A_coherent_replacement_of_the_binding_and_its_event_during_either_phase_is_unavailable_with_every_fact_stripped(string phase)
    {
        var bound = await SnapshotBoundAsync();
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
        Admit(bound, Sentinel());
        var originalRead = (await GetBindingAsync(bound.Host, runId)).Body;

        // The replacement is a genuine, coherent binding: the unchanged reader admits it (state Available, the new identity) and the
        // inspection sees the same Bound scope with the same source and target.
        await CorruptAsync([Events, Bindings], [statement], async () =>
        {
            var read = await GetBindingAsync(bound.Host, runId);
            Assert.True(read.Root.GetProperty("state").GetString() == "Available", read.Body);
            var shown = read.Root.GetProperty("binding");
            Assert.Equal(replacement.Id, shown.GetProperty("id").GetGuid());
            Assert.Equal("R_kgDOReplaced01", shown.GetProperty("repositoryNodeId").GetString());
            var scope = await ObserveOutputAsync(bound.Host, runId);
            Assert.True(StatusOf(scope) == "IdentityChanged", scope.Body);
            Assert.Equal(bound.Sha, scope.Root.GetProperty("source").GetProperty("commitSha").GetString());
            Assert.Equal(replacement.Id, scope.Root.GetProperty("binding").GetProperty("id").GetGuid());
        });
        Assert.Equal(originalRead, (await GetBindingAsync(bound.Host, runId)).Body);
        var metadataBefore = bound.Output.MetadataCalls;
        var outputBefore = bound.Output.OutputCalls;

        await CorruptAsync([Events, Bindings], [], async () =>
        {
            async Task<GitHubCheckRunProbe?> Drifting(Func<Task<GitHubCheckRunProbe?>> answer)
            {
                await ExecuteSqlAsync("PRAGMA foreign_keys = OFF; " + statement);
                return await answer();
            }

            bound.Output.Metadata = phase == "metadata" ? () => Drifting(() => MetadataProbe(bound)) : () => MetadataProbe(bound);
            bound.Output.Output = phase == "output"
                ? () => Drifting(() => OutputProbe(bound, OutputNode(bound.Sha, Sentinel())))
                : () => OutputProbe(bound, OutputNode(bound.Sha, Sentinel()));

            var reading = await ObserveOutputAsync(bound.Host, runId);

            Assert.Equal(HttpStatusCode.OK, reading.Status);
            Assert.True(StatusOf(reading) == "Unavailable", reading.Body);
            AssertFacts(reading, source: false, target: false, binding: false, observation: false);
            Assert.DoesNotContain(NodeId, reading.Body, StringComparison.Ordinal);
            Assert.DoesNotContain("R_kgDOReplaced01", reading.Body, StringComparison.Ordinal);
            Assert.DoesNotContain("SENTINEL", reading.Body, StringComparison.Ordinal);
        });

        Assert.Equal(metadataBefore + 1, bound.Output.MetadataCalls);
        Assert.Equal(outputBefore + (phase == "output" ? 1 : 0), bound.Output.OutputCalls);
        Assert.Equal(originalRead, (await GetBindingAsync(bound.Host, runId)).Body);
        Admit(bound, Detail("restored"));
        var restored = await ObserveOutputAsync(bound.Host, runId);
        Assert.Equal("OutputReported", StatusOf(restored));
        Assert.Equal(original.Id, restored.Root.GetProperty("binding").GetProperty("id").GetGuid());
    }
}
