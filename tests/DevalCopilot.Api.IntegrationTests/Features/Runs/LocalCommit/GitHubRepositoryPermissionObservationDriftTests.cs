using System.Net;
using System.Text.Json;
using DevalCopilot.Application.Features.Runs.Queries.GetGitHubRepositoryPermissionObservation;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// ADR-0049: the answer of the external call is only meaningful for the scope and the binding it was requested for. Each case changes or
/// removes exactly one recorded fact while the scripted permission observer is still running, returning the most tempting remote answer
/// (the bound repository and an ADMIN role), and proves the answer is Unavailable with no scope, binding, observation or remote datum.
/// The binding cases fail unless the whole scope, binding included, is read again and compared by complete value after the call, which
/// also covers a complete, coherent replacement of the binding and its event (new identifiers, even with the very same node identifier).
/// </summary>
public sealed class GitHubRepositoryPermissionObservationDriftTests : GitHubRepositoryPermissionObservationTestBase
{
    private sealed record Case(string Name, string[] Tables, string Statement);

    private const string Bindings = "github_repository_identity_bindings";
    private const string Targets = "github_observation_targets";
    private const string Events = "events";
    private const string Operations = "local_commit_operations";

    private static bool IsStripped(ReceiptReading reading) =>
        reading.Status == HttpStatusCode.OK && StatusOf(reading) == "Unavailable"
        && reading.Root.GetProperty("source").ValueKind == JsonValueKind.Null
        && reading.Root.GetProperty("target").ValueKind == JsonValueKind.Null
        && reading.Root.GetProperty("binding").ValueKind == JsonValueKind.Null
        && reading.Root.GetProperty("observation").ValueKind == JsonValueKind.Null
        && !reading.Body.Contains(NodeId, StringComparison.Ordinal)
        && !reading.Body.Contains("ADMIN", StringComparison.Ordinal);

    [Fact]
    public async Task A_scope_or_binding_lost_or_changed_during_the_observation_refuses_the_answer_and_returns_no_remote_data()
    {
        var bound = await BoundAsync();
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
                bound.Permission.Answer = async () =>
                {
                    await ExecuteSqlAsync("PRAGMA foreign_keys = OFF; " + statement);
                    return await Matching("ADMIN");
                };

                var reading = await ObservePermissionAsync(bound.Host, runId);

                if (!IsStripped(reading))
                {
                    failures.Add($"{name}: {reading.Status} {reading.Body}");
                }
            });
        }

        Assert.Empty(failures);
        Assert.Equal(cases.Length, bound.Permission.Calls.Count);

        // Everything is restored byte for byte, so the same request now observes and reports again.
        bound.Permission.Answer = () => Matching("ADMIN");
        var restored = await ObservePermissionAsync(bound.Host, runId);
        Assert.Equal("PermissionReported", StatusOf(restored));
        Assert.Equal(JsonValueKind.Object, restored.Root.GetProperty("binding").ValueKind);
    }

    [Theory]
    [InlineData(NodeId)]
    [InlineData("R_kgDOReplaced1")]
    public async Task A_complete_coherent_replacement_of_the_binding_and_its_event_during_the_call_is_unavailable_with_no_remote_data(string replacementNodeId)
    {
        var bound = await BoundAsync();
        var runId = bound.Ids.RunId;
        var old = await BindingRowAsync(runId);
        var oldEvent = await BindingEventRowAsync(runId);
        var targetRow = await TargetRowAsync(runId);
        var before = await DatabaseFingerprintAsync();

        await CorruptAsync([Bindings, Events], [], async () =>
        {
            bound.Permission.Answer = async () =>
            {
                await ExecuteSqlAsync($"PRAGMA foreign_keys = OFF; DELETE FROM {Events} WHERE \"Id\" = '{Upper(oldEvent.Id)}'");
                await ExecuteSqlAsync($"PRAGMA foreign_keys = OFF; DELETE FROM {Bindings} WHERE \"Id\" = '{Upper(old.Id)}'");
                var replacement = GitHubRepositoryIdentityBinding.Record(
                    Guid.NewGuid(), runId, old.ProjectId, old.LocalCommitOperationId, old.GitWorkspaceId, old.ObservationTargetId,
                    old.CommitSha, old.BranchName, replacementNodeId, old.Owner, old.Repository, targetRow.RecordedAtUtc,
                    old.ObservedAtUtc, old.RecordedAtUtc, Guid.NewGuid());
                await using (var db = OpenDb())
                {
                    db.GitHubRepositoryIdentityBindings.Add(replacement);
                    db.Events.Add(RunEvent.Record(
                        replacement.EventId, runId, null, RunEventType.GitHubRepositoryIdentityBound, ParticipantIdentity.ForHuman(),
                        GitHubRepositoryIdentityBindingEventPayload.Build(replacement), replacement.RecordedAtUtc));
                    await db.SaveChangesAsync();
                }

                return await PermissionProbe("OCTO-CAT", "Hello.World", replacementNodeId, "ADMIN");
            };

            var reading = await ObservePermissionAsync(bound.Host, runId);

            Assert.True(IsStripped(reading), $"{reading.Status} {reading.Body}");
            Assert.DoesNotContain(replacementNodeId, reading.Body, StringComparison.Ordinal);
            var replaced = await BindingRowAsync(runId);
            Assert.NotEqual(old.Id, replaced.Id);
        });

        Assert.Equal(before, await DatabaseFingerprintAsync());
        bound.Permission.Answer = () => Matching("ADMIN");
        Assert.Equal("PermissionReported", StatusOf(await ObservePermissionAsync(bound.Host, runId)));
    }
}
