using System.Data.Common;
using System.Net;
using System.Text;
using System.Text.Json;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// Shared plumbing for the explicit GitHub branch publication approval (ADR-0048): a real delivered run with its recorded ADR-0040 target
/// and ADR-0043 binding through the production host and the unchanged scripted observer (the binding is the only operation that ever
/// observes), raw reads and writes of the protected route so any body can be sent exactly as written, counts of the only two rows the
/// operation may add, and a database interceptor that lets a test change stored facts at the one moment between the operation's initial
/// reading and its write lock.
/// </summary>
public abstract class GitHubBranchPublicationApprovalTestBase : GitHubRepositoryIdentityBindingTestBase
{
    protected GitHubBranchPublicationApprovalTestBase()
    {
    }

    internal GitHubBranchPublicationApprovalTestBase(LocalCommitBorrowedResources? borrowed)
        : base(borrowed)
    {
    }

    internal const string ApprovalRoute = "github-branch-publication-approval";

    internal const string ApprovalEventType = RunEventType.GitHubBranchPublicationApproved;

    internal const string Approvals = "github_branch_publication_approvals";

    internal static readonly string[] ApprovalExcludedTables = [Approvals];

    internal static readonly string[] PostKeys = ["approval", "binding", "runId", "source", "target"];

    internal static readonly string[] GetKeys = ["approval", "binding", "runId", "source", "status", "target"];

    internal static readonly string[] ApprovalKeys =
    [
        "action", "approvedAtUtc", "bindingId", "branchName", "commitSha", "eventId", "host", "id", "localCommitOperationId", "owner",
        "policyVersion", "repository", "repositoryNodeId", "targetId", "workspaceId",
    ];

    internal sealed record Bound(Delivered Delivered, Guid BindingId, string BranchName)
    {
        public LocalCommitHost Host => Delivered.Host;

        public Guid RunId => Delivered.Ids.RunId;
    }

    /// <summary>A host clock that answers a pinned instant while one is set and the real time otherwise.</summary>
    internal sealed class PinnedClock : TimeProvider
    {
        public DateTimeOffset? Pinned { get; set; }

        public override DateTimeOffset GetUtcNow() => Pinned ?? base.GetUtcNow();
    }

    /// <summary>Runs a callback synchronously the first time a transaction is about to start after <see cref="Arm"/>, which for the
    /// operation under test is after its initial reading and before the transaction takes the database write lock (SQLite's driver
    /// begins its serializable transactions immediately), so another connection can still write.</summary>
    internal sealed class LockPointInterceptor : DbTransactionInterceptor
    {
        private int _armed;

        public Action? AtLockPoint { get; set; }

        public int Fired { get; private set; }

        public void Arm() => Interlocked.Exchange(ref _armed, 1);

        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection,
            TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _armed, 0) == 1)
            {
                Fired++;
                AtLockPoint?.Invoke();
            }

            return base.TransactionStartingAsync(connection, eventData, result, cancellationToken);
        }
    }

    /// <summary>Throws exactly once when the next transaction is about to commit, which is after both inserts were saved and before they
    /// are durable.</summary>
    internal sealed class CommitPointInterceptor : DbTransactionInterceptor
    {
        private int _armed;

        public void Arm() => Interlocked.Exchange(ref _armed, 1);

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _armed, 0) == 1)
            {
                throw new OperationCanceledException("The request was cancelled just before the commit.");
            }

            return base.TransactionCommittingAsync(transaction, eventData, result, cancellationToken);
        }
    }

    internal enum EvidenceCommand
    {
        RowsOnly,
        EventsOnly,
        RowsAndEvents,
    }

    /// <summary>Records every database command of the host that observes approval evidence (an approval-table or approval-event read)
    /// as a fixed phase marker, never as SQL text, and after <see cref="Arm"/> runs a callback once, before the first such command that reads approval events
    /// executes: the controlled boundary at which a competing request on another host may commit.</summary>
    internal sealed class EvidenceBoundaryInterceptor : DbCommandInterceptor
    {
        private int _armed;

        public Func<Task>? AtBoundary { get; set; }

        public int Fired { get; private set; }

        public List<EvidenceCommand> Observed { get; } = [];

        public void Arm() => Interlocked.Exchange(ref _armed, 1);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            var rows = command.CommandText.Contains(Approvals, StringComparison.Ordinal);
            var events = command.CommandText.Contains(ApprovalEventType, StringComparison.Ordinal)
                || command.Parameters.Cast<DbParameter>().Any(parameter => Equals(parameter.Value, ApprovalEventType));
            if (!rows && !events)
            {
                return result;
            }

            Observed.Add(rows && events ? EvidenceCommand.RowsAndEvents : rows ? EvidenceCommand.RowsOnly : EvidenceCommand.EventsOnly);
            if (events && Interlocked.Exchange(ref _armed, 0) == 1 && AtBoundary is { } boundary)
            {
                Fired++;
                await boundary();
            }

            return result;
        }
    }

    internal static Action<IServiceCollection> UseInterceptors(params IInterceptor[] interceptors) => services =>
        services.ConfigureDbContext<DevalCopilot.Infrastructure.Persistence.DevalCopilotDbContext>(
            options => options.AddInterceptors(interceptors));

    /// <summary>A delivered run whose target and binding are recorded through the production endpoints (one scripted observation).</summary>
    internal async Task<Bound> BoundAsync(bool recordBinding = true, Action<IServiceCollection>? configure = null)
    {
        var delivered = await DeliveredAsync(configure: configure);
        var bindingId = Guid.Empty;
        if (recordBinding)
        {
            var posted = await PostBindingAsync(delivered.Host, delivered.Ids.RunId, BodyFor(delivered));
            Assert.Equal(HttpStatusCode.OK, posted.Status);
            bindingId = (await BindingRowAsync(delivered.Ids.RunId)).Id;
        }

        return new Bound(delivered, bindingId, (await OperationByIdAsync(delivered.OperationId)).BranchName);
    }

    internal static string ApprovalBodyOf(
        Guid approvalId, Guid operationId, string commitSha, string branchName, Guid targetId, Guid bindingId, string repositoryNodeId = NodeId) =>
        JsonSerializer.Serialize(new { approvalId, operationId, commitSha, branchName, targetId, bindingId, repositoryNodeId });

    internal static string ApprovalBodyFor(Bound bound, Guid? approvalId = null) => ApprovalBodyOf(
        approvalId ?? Guid.NewGuid(), bound.Delivered.OperationId, bound.Delivered.Sha, bound.BranchName, bound.Delivered.TargetId,
        bound.BindingId);

    internal static async Task<ReceiptReading> PostApprovalAsync(
        LocalCommitHost host, Guid runId, string json, string query = "", string contentType = "application/json")
    {
        using var client = LocalCommitLineage.AuthenticatedClient(host);
        using var content = new StringContent(json, Encoding.UTF8, contentType);
        var response = await client.PostAsync($"/api/runs/{runId}/{ApprovalRoute}{query}", content);
        return new ReceiptReading(response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    internal static async Task<ReceiptReading> PostApprovalContentAsync(LocalCommitHost host, Guid runId, HttpContent content)
    {
        using var client = LocalCommitLineage.AuthenticatedClient(host);
        var response = await client.PostAsync($"/api/runs/{runId}/{ApprovalRoute}", content);
        return new ReceiptReading(response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    internal static async Task<ReceiptReading> GetApprovalAsync(
        LocalCommitHost host, Guid runId, HttpContent? body = null, string query = "")
    {
        using var client = LocalCommitLineage.AuthenticatedClient(host);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/runs/{runId}/{ApprovalRoute}{query}") { Content = body };
        var response = await client.SendAsync(request);
        return new ReceiptReading(response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    internal static string StatusOf(ReceiptReading reading) => reading.Root.GetProperty("status").GetString()!;

    internal async Task<(int Approvals, int Events)> ApprovalCountsAsync(Guid runId)
    {
        await using var db = OpenDb();
        return (
            await db.GitHubBranchPublicationApprovals.AsNoTracking().CountAsync(approval => approval.RunId == runId),
            await db.Events.AsNoTracking().CountAsync(runEvent => runEvent.RunId == runId && runEvent.EventType == ApprovalEventType));
    }

    internal async Task<GitHubBranchPublicationApproval> ApprovalRowAsync(Guid runId)
    {
        await using var db = OpenDb();
        return await db.GitHubBranchPublicationApprovals.AsNoTracking().SingleAsync(approval => approval.RunId == runId);
    }

    internal async Task<RunEvent> ApprovalEventRowAsync(Guid runId)
    {
        await using var db = OpenDb();
        return await db.Events.AsNoTracking().SingleAsync(runEvent => runEvent.RunId == runId && runEvent.EventType == ApprovalEventType);
    }

    /// <summary>A fingerprint of everything except the approval table and its event type, so "only these two rows were added" is provable
    /// while the target, the binding, their events, the run and every approval of the local commit stay inside it.</summary>
    internal Task<string> FingerprintWithoutApprovalWritesAsync() => DatabaseFingerprintAsync(ApprovalExcludedTables, ApprovalEventType);
}
