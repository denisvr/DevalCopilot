using System.Net;
using System.Text.Json;
using DevalCopilot.Application.Features.Runs.Queries.GetGitHubCheckContextsObservation;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// Shared plumbing for the explicit check-context observation (ADR-0045): a real delivered run, its recorded ADR-0040 target and its
/// ADR-0043 binding through the production host, a scripted provider-neutral contexts observer, a host clock whose reading can be moved
/// from inside the scripted external call, and raw reads of the protected route so any body can be sent exactly as written.
/// </summary>
public abstract class GitHubCheckContextsObservationTestBase : GitHubRepositoryIdentityBindingTestBase
{
    protected GitHubCheckContextsObservationTestBase()
    {
    }

    internal GitHubCheckContextsObservationTestBase(LocalCommitBorrowedResources? borrowed)
        : base(borrowed)
    {
    }

    internal const string ContextsRoute = "github-check-contexts-observation";

    internal const string Sha40 = "0123456789abcdef0123456789abcdef01234567";

    internal static readonly string[] ContextsResponseKeys = ["binding", "observation", "runId", "source", "status", "target"];

    internal static readonly string[] ObservationKeys =
        ["contexts", "observedAtUtc", "owner", "ref", "repository", "repositoryNodeId", "totalCount"];

    internal static readonly string[] ContextKeys = ["checkSuiteId", "conclusion", "id", "kind", "name", "status"];

    internal sealed class ScriptedContextsObserver : IGitHubCheckContextsObserver
    {
        public List<(string Path, string Owner, string Repository, string Branch)> Calls { get; } = [];

        public List<CancellationToken> Tokens { get; } = [];

        public DateTimeOffset? LastStartedAt { get; private set; }

        public Func<Task<GitHubCheckContextsProbe?>> Answer { get; set; } = () => Task.FromResult<GitHubCheckContextsProbe?>(null);

        public Task<GitHubCheckContextsProbe?> ObserveAsync(
            string executablePath, string owner, string repository, string branchName, CancellationToken cancellationToken)
        {
            lock (Calls)
            {
                Calls.Add((executablePath, owner, repository, branchName));
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

    internal static GitHubCheckContext RunEntry(
        string id = "CR_1", string name = "build", string status = "COMPLETED", string? conclusion = "SUCCESS", string suite = "CS_1") =>
        new(GitHubCheckContextsPolicy.CheckRunKind, id, name, status, conclusion, suite);

    internal static GitHubCheckContext StatusEntry(string id = "SC_1", string name = "ci/legacy", string state = "SUCCESS") =>
        new(GitHubCheckContextsPolicy.StatusContextKind, id, name, state, null, null);

    internal static Task<GitHubCheckContextsProbe?> ContextsProbe(
        string owner = DefaultOwner, string repository = DefaultRepository, string nodeId = NodeId, GitHubCheckContextsRef? reference = null,
        GitHubCheckContextsPage? page = null) =>
        Task.FromResult<GitHubCheckContextsProbe?>(new GitHubCheckContextsProbe(nodeId, owner, repository, reference, page));

    internal static GitHubCheckContextsPage PageOf(int total, params GitHubCheckContext[] contexts) => new(total, contexts);

    internal static GitHubCheckContext[] DistinctRuns(int count, string prefix = "CR") =>
        Enumerable.Range(0, count).Select(index => RunEntry(id: $"{prefix}_{index}", name: $"job {index}")).ToArray();

    internal sealed record Bound(
        LocalCommitHost Host,
        ScriptedObserver IdentityObserver,
        ScriptedContextsObserver Contexts,
        MovableClock Clock,
        LocalCommitLineageIds Ids,
        Guid OperationId,
        string Sha,
        Guid TargetId);

    /// <summary>A delivered run with its target recorded and, by default, its identity binding recorded through the production POST, on a
    /// host whose identity and contexts observers are scripted. The scripted observers are cleared after binding so a test counts only what
    /// it causes.</summary>
    internal async Task<Bound> BoundAsync(
        bool recordTarget = true, bool recordBinding = true, bool seedSnapshot = true, IGitHubCheckContextsObserver? observer = null)
    {
        var contexts = new ScriptedContextsObserver();
        var clock = new MovableClock();
        var delivered = await DeliveredAsync(
            recordTarget,
            seedSnapshot,
            services =>
            {
                services.RemoveAll<IGitHubCheckContextsObserver>();
                services.AddSingleton(observer ?? contexts);
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
            delivered.Host, delivered.Observer, contexts, clock, delivered.Ids, delivered.OperationId, delivered.Sha, delivered.TargetId);
    }

    internal static async Task<ReceiptReading> ObserveContextsAsync(
        LocalCommitHost host, Guid runId, HttpContent? body = null, bool authenticated = true, string query = "")
    {
        using var client = authenticated ? LocalCommitLineage.AuthenticatedClient(host) : host.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/runs/{runId}/{ContextsRoute}{query}") { Content = body };
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
