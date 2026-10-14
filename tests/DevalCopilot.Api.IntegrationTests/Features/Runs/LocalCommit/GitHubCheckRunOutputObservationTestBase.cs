using System.Net;
using System.Text.Json;
using DevalCopilot.Application.Features.Runs.Queries.GetGitHubCheckRunOutputObservation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// Shared plumbing for the explicit check-run output inspection (ADR-0047): a real delivered run, its recorded ADR-0040 target and its
/// ADR-0043 binding through the production host, a scripted provider-neutral two-phase observer, a host clock whose reading can be moved
/// from inside a scripted external call, and raw reads of the protected route so any query or body can be sent exactly as written. The
/// fixtures own their clock and observer types: nothing of the ADR-0045 test base is reused or changed.
/// </summary>
public abstract class GitHubCheckRunOutputObservationTestBase : GitHubRepositoryIdentityBindingTestBase
{
    protected GitHubCheckRunOutputObservationTestBase()
    {
    }

    internal GitHubCheckRunOutputObservationTestBase(LocalCommitBorrowedResources? borrowed)
        : base(borrowed)
    {
    }

    internal const string OutputRoute = "github-check-run-output-observation";

    internal const string CheckId = "CR_kwDOCheck001";

    internal const string SuiteId = "CS_kwDOSuite001";

    internal static readonly string[] OutputResponseKeys = ["binding", "checkRunId", "observation", "runId", "source", "status", "target"];

    internal static readonly string[] OutputObservationKeys = ["checkRun", "observedAtUtc", "owner", "ref", "repository", "repositoryNodeId"];

    internal static readonly string[] CheckRunKeys = ["checkSuiteId", "commitSha", "conclusion", "id", "name", "output", "status"];

    internal static readonly string[] OutputTextKeys = ["summary", "text", "title"];

    internal sealed record OutputCall(string Phase, string Path, string Owner, string Repository, string Branch, string CheckRunId);

    internal sealed class ScriptedOutputObserver : IGitHubCheckRunOutputObserver
    {
        public List<OutputCall> Calls { get; } = [];

        public List<CancellationToken> Tokens { get; } = [];

        public DateTimeOffset? LastStartedAt { get; private set; }

        public Func<Task<GitHubCheckRunProbe?>> Metadata { get; set; } = () => Task.FromResult<GitHubCheckRunProbe?>(null);

        public Func<Task<GitHubCheckRunProbe?>> Output { get; set; } = () => Task.FromResult<GitHubCheckRunProbe?>(null);

        /// <summary>When set, answers a metadata call from what the call itself asked for, so interleaved requests can be told apart.</summary>
        public Func<OutputCall, Task<GitHubCheckRunProbe?>>? MetadataByCall { get; set; }

        /// <summary>When set, answers an output call from what the call itself asked for.</summary>
        public Func<OutputCall, Task<GitHubCheckRunProbe?>>? OutputByCall { get; set; }

        public int MetadataCalls => Calls.Count(call => call.Phase == "metadata");

        public int OutputCalls => Calls.Count(call => call.Phase == "output");

        public Task<GitHubCheckRunProbe?> ObserveMetadataAsync(
            string executablePath, string owner, string repository, string branchName, string checkRunId, CancellationToken cancellationToken)
        {
            var call = Record("metadata", executablePath, owner, repository, branchName, checkRunId, cancellationToken);
            return MetadataByCall is null ? Metadata() : MetadataByCall(call);
        }

        public Task<GitHubCheckRunProbe?> ObserveOutputAsync(
            string executablePath, string owner, string repository, string branchName, string checkRunId, CancellationToken cancellationToken)
        {
            var call = Record("output", executablePath, owner, repository, branchName, checkRunId, cancellationToken);
            return OutputByCall is null ? Output() : OutputByCall(call);
        }

        private OutputCall Record(
            string phase, string path, string owner, string repository, string branch, string checkRunId, CancellationToken token)
        {
            var call = new OutputCall(phase, path, owner, repository, branch, checkRunId);
            lock (Calls)
            {
                Calls.Add(call);
                Tokens.Add(token);
            }

            LastStartedAt = DateTimeOffset.UtcNow;
            return call;
        }
    }

    /// <summary>A host clock that follows real time until a test fixes it, so a reading taken between or after the external calls can be
    /// moved from inside a scripted call without disturbing any other reading.</summary>
    internal sealed class OutputClock : TimeProvider
    {
        private DateTimeOffset? _fixed;

        public void Fix(DateTimeOffset instant) => _fixed = instant;

        public void Release() => _fixed = null;

        public override DateTimeOffset GetUtcNow() => _fixed ?? base.GetUtcNow();
    }

    internal static GitHubCheckRunAssociation Association(
        string sha, string id = CheckId, string runRepository = NodeId, string suite = SuiteId, string suiteRepository = NodeId) =>
        new(id, runRepository, suite, suiteRepository, sha);

    internal static GitHubCheckRunNode MetadataNode(
        string sha, string id = CheckId, string runRepository = NodeId, string suite = SuiteId, string suiteRepository = NodeId) =>
        new(GitHubCheckRunOutputPolicy.CheckRunTypeName, Association(sha, id, runRepository, suite, suiteRepository), null);

    internal static GitHubCheckRunDetail Detail(
        string name = "build", string status = "COMPLETED", string? conclusion = "FAILURE", string? title = "Title",
        string? summary = "Summary", string? text = "Text") =>
        new(name, status, conclusion, new GitHubCheckRunOutputText(title, summary, text));

    internal static GitHubCheckRunNode OutputNode(
        string sha, GitHubCheckRunDetail? detail = null, string id = CheckId, string runRepository = NodeId, string suite = SuiteId,
        string suiteRepository = NodeId) =>
        new(
            GitHubCheckRunOutputPolicy.CheckRunTypeName,
            Association(sha, id, runRepository, suite, suiteRepository),
            detail ?? Detail());

    /// <summary>The observed names deliberately differ from the recorded ones in ASCII case: the answer must expose what GitHub returned.</summary>
    internal static Task<GitHubCheckRunProbe?> Probe(
        GitHubCheckRunOutputRef? reference, GitHubCheckRunNode? node, string nodeId = NodeId, string owner = "OCTO-CAT",
        string repository = "Hello.World") =>
        Task.FromResult<GitHubCheckRunProbe?>(new GitHubCheckRunProbe(nodeId, owner, repository, reference, node));

    internal static string Query(string checkRunId = CheckId) => "?checkRunId=" + Uri.EscapeDataString(checkRunId);

    /// <summary>A prepared host. <c>IdentityObserver</c> is the scripted identity observer of a per-test production lineage, and null for a
    /// sealed clone, whose identity port refuses every call (the clone's refusal queue is the effective evidence there).</summary>
    internal sealed record Bound(
        LocalCommitHost Host,
        ScriptedObserver? IdentityObserver,
        ScriptedOutputObserver Output,
        OutputClock Clock,
        LocalCommitLineageIds Ids,
        Guid OperationId,
        string Sha,
        Guid TargetId);

    /// <summary>A delivered run with its target recorded and, by default, its identity binding recorded through the production POST, on a
    /// host whose identity and output observers are scripted. The scripted observers are cleared after binding so a test counts only what
    /// it causes. This is the full per-test production lineage; the sealed clone is the cheaper form.</summary>
    internal async Task<Bound> BoundAsync(bool recordTarget = true, bool recordBinding = true, IGitHubCheckRunOutputObserver? observer = null)
    {
        var output = new ScriptedOutputObserver();
        var clock = new OutputClock();
        var delivered = await DeliveredAsync(
            recordTarget,
            seedSnapshot: true,
            services =>
            {
                services.RemoveAll<IGitHubCheckRunOutputObserver>();
                services.AddSingleton(observer ?? output);
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
            delivered.Host, delivered.Observer, output, clock, delivered.Ids, delivered.OperationId, delivered.Sha, delivered.TargetId);
    }

    internal static async Task<ReceiptReading> ObserveOutputAsync(
        LocalCommitHost host, Guid runId, HttpContent? body = null, bool authenticated = true, string? query = null)
    {
        using var client = authenticated ? LocalCommitLineage.AuthenticatedClient(host) : host.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/runs/{runId}/{OutputRoute}{query ?? Query()}") { Content = body };
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
