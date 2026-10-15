using System.Net;
using System.Text.Json;
using DevalCopilot.Application.Features.Runs.Queries.GetGitHubRepositoryPermissionObservation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// Shared plumbing for the explicit repository permission observation (ADR-0049): a real delivered run, its recorded ADR-0040 target and
/// its ADR-0043 binding through the production host, a scripted provider-neutral permission observer, a host clock whose reading can be
/// moved from inside the scripted external call, and raw reads of the protected route so any body or query can be sent exactly as written.
/// </summary>
public abstract class GitHubRepositoryPermissionObservationTestBase : GitHubRepositoryIdentityBindingTestBase
{
    internal const string PermissionRoute = "github-repository-permission-observation";

    internal static readonly string[] PermissionResponseKeys = ["binding", "observation", "runId", "source", "status", "target"];

    internal static readonly string[] ObservationKeys = ["observedAtUtc", "owner", "repository", "repositoryNodeId", "viewerPermission"];

    internal static readonly string[] Literals = ["READ", "TRIAGE", "TRIAGE_PLUS", "WRITE", "MAINTAIN", "ADMIN"];

    internal sealed class ScriptedPermissionObserver : IGitHubRepositoryPermissionObserver
    {
        public List<(string Path, string Owner, string Repository)> Calls { get; } = [];

        public List<CancellationToken> Tokens { get; } = [];

        public DateTimeOffset? LastStartedAt { get; private set; }

        public Func<Task<GitHubRepositoryPermissionProbe?>> Answer { get; set; } = () => Task.FromResult<GitHubRepositoryPermissionProbe?>(null);

        public Task<GitHubRepositoryPermissionProbe?> ObserveAsync(
            string executablePath, string owner, string repository, CancellationToken cancellationToken)
        {
            lock (Calls)
            {
                Calls.Add((executablePath, owner, repository));
                Tokens.Add(cancellationToken);
            }

            LastStartedAt = DateTimeOffset.UtcNow;
            return Answer();
        }
    }

    /// <summary>A host clock that follows real time until a test fixes it, so the reading taken after the external call can be moved
    /// from inside the scripted call without disturbing any other reading.</summary>
    internal sealed class MovableClock : TimeProvider
    {
        private DateTimeOffset? _fixed;

        public void Fix(DateTimeOffset instant) => _fixed = instant;

        public void Release() => _fixed = null;

        public override DateTimeOffset GetUtcNow() => _fixed ?? base.GetUtcNow();
    }

    internal static Task<GitHubRepositoryPermissionProbe?> PermissionProbe(
        string owner = DefaultOwner, string repository = DefaultRepository, string nodeId = NodeId, string? permission = null) =>
        Task.FromResult<GitHubRepositoryPermissionProbe?>(new GitHubRepositoryPermissionProbe(nodeId, owner, repository, permission));

    /// <summary>The most tempting valid answer: the bound repository (names differing only in case) and a role.</summary>
    internal static Task<GitHubRepositoryPermissionProbe?> Matching(string? permission = "WRITE") =>
        PermissionProbe("OCTO-CAT", "Hello.World", permission: permission);

    internal sealed record Bound(
        LocalCommitHost Host,
        ScriptedObserver IdentityObserver,
        ScriptedPermissionObserver Permission,
        MovableClock Clock,
        LocalCommitLineageIds Ids,
        Guid OperationId,
        string Sha,
        Guid TargetId);

    /// <summary>A delivered run with its target recorded and, by default, its identity binding recorded through the production POST, on a
    /// host whose identity and permission observers are scripted. The scripted observers are cleared after binding so a test counts only
    /// what it causes.</summary>
    internal async Task<Bound> BoundAsync(bool recordTarget = true, bool recordBinding = true, bool seedSnapshot = true)
    {
        var permission = new ScriptedPermissionObserver();
        var clock = new MovableClock();
        var delivered = await DeliveredAsync(
            recordTarget,
            seedSnapshot,
            services =>
            {
                services.RemoveAll<IGitHubRepositoryPermissionObserver>();
                services.AddSingleton<IGitHubRepositoryPermissionObserver>(permission);
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(clock);
            });
        if (recordBinding)
        {
            var posted = await PostBindingAsync(delivered.Host, delivered.Ids.RunId, BodyFor(delivered));
            Assert.Equal(HttpStatusCode.OK, posted.Status);
            delivered.Observer.Calls.Clear();
        }

        return new Bound(
            delivered.Host, delivered.Observer, permission, clock, delivered.Ids, delivered.OperationId, delivered.Sha, delivered.TargetId);
    }

    internal static async Task<ReceiptReading> ObservePermissionAsync(
        LocalCommitHost host, Guid runId, HttpContent? body = null, bool authenticated = true, string query = "")
    {
        using var client = authenticated ? LocalCommitLineage.AuthenticatedClient(host) : host.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/runs/{runId}/{PermissionRoute}{query}") { Content = body };
        var response = await client.SendAsync(request);
        return new ReceiptReading(response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    internal static string StatusOf(ReceiptReading reading) => reading.Root.GetProperty("status").GetString()!;

    internal static void AssertFacts(ReceiptReading reading, bool source, bool target, bool binding, bool observation, string context = "")
    {
        foreach (var (name, expected) in new[]
                 {
                     ("source", source), ("target", target), ("binding", binding), ("observation", observation),
                 })
        {
            Assert.True(
                (reading.Root.GetProperty(name).ValueKind == JsonValueKind.Object) == expected,
                $"{context} {name} expected {(expected ? "present" : "absent")}: {reading.Body}");
        }
    }

    /// <summary>The recorded binding row's UTC recording time, which no admitted observation may precede.</summary>
    internal async Task<DateTimeOffset> BindingRecordedAtAsync(Guid runId) => (await BindingRowAsync(runId)).RecordedAtUtc;

    internal static string EventOfBinding(Guid runId) => $"\"EventType\" = '{BindingEventType}' AND \"RunId\" = '{Upper(runId)}'";

    internal static string EventOfTarget(Guid runId) => $"\"EventType\" = '{TargetEventType}' AND \"RunId\" = '{Upper(runId)}'";
}
