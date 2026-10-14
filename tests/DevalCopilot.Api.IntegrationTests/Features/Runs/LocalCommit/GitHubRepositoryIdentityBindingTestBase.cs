using System.Net;
using System.Text;
using System.Text.Json;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// Shared plumbing for the explicit GitHub repository identity binding (ADR-0043): a real delivered run and its recorded ADR-0040
/// target through the production host, a scripted provider-neutral observer, raw reads and writes of the protected route so any body can
/// be sent exactly as written, and counts of the only two rows the operation may add.
/// </summary>
public abstract class GitHubRepositoryIdentityBindingTestBase : GitHubObservationTargetTestBase
{
    internal const string BindingRoute = "github-repository-identity-binding";

    internal const string BindingEventType = RunEventType.GitHubRepositoryIdentityBound;

    internal const string GhPath = @"C:\safe\gh.exe";

    internal const string NodeId = "R_kgDOExample01";

    internal static readonly string[] BindingExcludedTables = ["github_repository_identity_bindings"];

    internal static readonly string[] ResponseKeys = ["binding", "runId", "source", "target"];

    internal static readonly string[] GetResponseKeys = ["binding", "runId", "source", "state", "target"];

    internal static readonly string[] BindingKeys =
        ["eventId", "id", "observedAtUtc", "owner", "recordedAtUtc", "repository", "repositoryNodeId"];

    internal sealed class ScriptedObserver : IGitHubRepositoryIdentityObserver
    {
        public List<(string Path, string Owner, string Repository)> Calls { get; } = [];

        public List<CancellationToken> Tokens { get; } = [];

        public Func<Task<GitHubRepositoryIdentity?>> Answer { get; set; } = () => Task.FromResult<GitHubRepositoryIdentity?>(null);

        public Task<GitHubRepositoryIdentity?> ObserveAsync(
            string executablePath, string owner, string repository, CancellationToken cancellationToken)
        {
            lock (Calls)
            {
                Calls.Add((executablePath, owner, repository));
                Tokens.Add(cancellationToken);
            }

            return Answer();
        }
    }

    /// <summary>A host clock that answers the queued instants first (so a test can script consecutive readings) and the real time after.</summary>
    internal sealed class ScriptedClock : TimeProvider
    {
        public Queue<DateTimeOffset> Queued { get; } = new();

        public override DateTimeOffset GetUtcNow()
        {
            lock (Queued)
            {
                return Queued.Count > 0 ? Queued.Dequeue() : base.GetUtcNow();
            }
        }
    }

    internal static Action<IServiceCollection> UseClock(ScriptedClock clock) => services =>
    {
        services.RemoveAll<TimeProvider>();
        services.AddSingleton<TimeProvider>(clock);
    };

    internal static Task<GitHubRepositoryIdentity?> Identity(
        string owner = DefaultOwner, string repository = DefaultRepository, string nodeId = NodeId) =>
        Task.FromResult<GitHubRepositoryIdentity?>(new GitHubRepositoryIdentity(nodeId, owner, repository));

    internal async Task SeedSnapshotAsync(
        CapabilityLaunchKind kind = CapabilityLaunchKind.DirectExecutable, string? script = null, bool failedLast = false,
        TimeSpan? evidenceAge = null)
    {
        await using var db = OpenDb();
        var now = DateTimeOffset.UtcNow - (evidenceAge ?? TimeSpan.Zero);
        var snapshot = await db.HostCapabilitySnapshots.SingleOrDefaultAsync(candidate => candidate.Capability == Capability.GitHubCli);
        if (snapshot is null)
        {
            snapshot = HostCapabilitySnapshot.Seed(Capability.GitHubCli, now);
            db.HostCapabilitySnapshots.Add(snapshot);
        }

        snapshot.MarkDispatched(now);
        snapshot.RecordSuccess(kind, GhPath, script, "2.50.0", now, now.AddMinutes(5));
        if (failedLast)
        {
            snapshot.MarkDispatched(now);
            snapshot.RecordFailure(CapabilityProbeReason.ExecutableInaccessible, now.AddMinutes(5));
        }

        await db.SaveChangesAsync();
    }

    internal sealed record Delivered(
        LocalCommitHost Host, ScriptedObserver Observer, LocalCommitLineageIds Ids, Guid OperationId, string Sha, Guid TargetId);

    /// <summary>A delivered run on a host whose identity observer is scripted, with a current CLI snapshot and the ADR-0040 target recorded.</summary>
    internal async Task<Delivered> DeliveredAsync(
        bool recordTarget = true, bool seedSnapshot = true, Action<IServiceCollection>? configure = null)
    {
        LocalCommitLineageIds ids;
        Guid operationId;
        using (var delivering = StartHost(runSupervisor: true))
        {
            (ids, operationId) = await DeliverAsync(delivering);
        }

        var observer = new ScriptedObserver { Answer = () => Identity() };
        var host = StartHost(
            runSupervisor: false,
            configure: services =>
            {
                services.RemoveAll<IGitHubRepositoryIdentityObserver>();
                services.AddSingleton<IGitHubRepositoryIdentityObserver>(observer);
                configure?.Invoke(services);
            });
        var sha = await CommitShaOfAsync(operationId);
        var targetId = Guid.Empty;
        if (recordTarget)
        {
            await RecordAsync(host, ids.RunId, operationId, sha);
            targetId = (await TargetRowAsync(ids.RunId)).Id;
        }

        if (seedSnapshot)
        {
            await SeedSnapshotAsync();
        }

        return new Delivered(host, observer, ids, operationId, sha, targetId);
    }

    internal static string BindingBodyOf(Guid operationId, string commitSha, Guid targetId, string repositoryNodeId = NodeId) =>
        JsonSerializer.Serialize(new { operationId, commitSha, targetId, repositoryNodeId });

    internal static string BodyFor(Delivered delivered, string repositoryNodeId = NodeId) =>
        BindingBodyOf(delivered.OperationId, delivered.Sha, delivered.TargetId, repositoryNodeId);

    internal static async Task<ReceiptReading> PostBindingAsync(LocalCommitHost host, Guid runId, string json)
    {
        using var client = LocalCommitLineage.AuthenticatedClient(host);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await client.PostAsync($"/api/runs/{runId}/{BindingRoute}", content);
        return new ReceiptReading(response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    internal static async Task<ReceiptReading> GetBindingAsync(LocalCommitHost host, Guid runId, HttpContent? body = null)
    {
        using var client = LocalCommitLineage.AuthenticatedClient(host);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/runs/{runId}/{BindingRoute}") { Content = body };
        var response = await client.SendAsync(request);
        return new ReceiptReading(response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    internal static string ErrorCode(ReceiptReading reading) => reading.Root.GetProperty("errors")[0].GetProperty("code").GetString()!;

    internal async Task<(int Bindings, int Events)> BindingCountsAsync(Guid runId)
    {
        await using var db = OpenDb();
        return (
            await db.GitHubRepositoryIdentityBindings.AsNoTracking().CountAsync(binding => binding.RunId == runId),
            await db.Events.AsNoTracking().CountAsync(runEvent => runEvent.RunId == runId && runEvent.EventType == BindingEventType));
    }

    internal async Task<GitHubRepositoryIdentityBinding> BindingRowAsync(Guid runId)
    {
        await using var db = OpenDb();
        return await db.GitHubRepositoryIdentityBindings.AsNoTracking().SingleAsync(binding => binding.RunId == runId);
    }

    internal async Task<RunEvent> BindingEventRowAsync(Guid runId)
    {
        await using var db = OpenDb();
        return await db.Events.AsNoTracking().SingleAsync(runEvent => runEvent.RunId == runId && runEvent.EventType == BindingEventType);
    }

    /// <summary>A fingerprint of everything except the binding table and its event type, so "only these two rows were added" is provable.</summary>
    internal Task<string> FingerprintWithoutBindingWritesAsync() =>
        DatabaseFingerprintAsync([.. ExcludedTables, .. BindingExcludedTables], BindingEventType);

    /// <summary>Executes one SQL statement on an independent connection (used from inside the scripted observer and to corrupt rows).</summary>
    internal async Task ExecuteSqlAsync(string statement, int busyTimeoutSeconds = 5)
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(
            $"Data Source={DatabasePath};Default Timeout={busyTimeoutSeconds};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
#pragma warning disable CA2100 // Test-only SQL assembled from constants and identifiers this test generated.
        command.CommandText = statement;
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>A readable stream with no known length, so the client sends it chunked rather than with a declared length.</summary>
    internal sealed class UnsizedStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream _inner = new(bytes);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
