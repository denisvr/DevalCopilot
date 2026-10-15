using System.Net;
using System.Text;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// ADR-0048 through the production host: a real delivered run with its recorded ADR-0040 target and ADR-0043 binding, the real MVC
/// authentication, mediator, readers and file-backed SQLite. The write is proved to add exactly one approval and one Human event and to
/// touch nothing else (no target, binding, run, lifecycle, local approval or other row), to observe and execute nothing, to replay byte
/// for byte, and to refuse every stale or changed acknowledgement without writing.
/// </summary>
public sealed class GitHubBranchPublicationApprovalEndpointTests : GitHubBranchPublicationApprovalTestBase
{
    [Fact]
    public async Task Approval_adds_exactly_one_approval_and_one_human_event_touches_nothing_else_and_runs_nothing_external()
    {
        var bound = await BoundAsync();
        var observed = bound.Delivered.Observer.Calls.Count;
        var before = await FingerprintWithoutApprovalWritesAsync();
        var approvalId = Guid.NewGuid();
        var started = DateTimeOffset.UtcNow;

        var posted = await PostApprovalAsync(bound.Host, bound.RunId, ApprovalBodyFor(bound, approvalId));

        Assert.Equal(HttpStatusCode.OK, posted.Status);
        Assert.Equal(PostKeys, KeysOf(posted.Root));
        Assert.Equal(bound.RunId, posted.Root.GetProperty("runId").GetGuid());
        Assert.Equal(["branchName", "commitSha", "operationId"], KeysOf(posted.Root.GetProperty("source")));
        Assert.Equal(bound.Delivered.Sha, posted.Root.GetProperty("source").GetProperty("commitSha").GetString());
        Assert.Equal(bound.BranchName, posted.Root.GetProperty("source").GetProperty("branchName").GetString());
        Assert.Equal(["eventId", "host", "id", "owner", "recordedAtUtc", "repository"], KeysOf(posted.Root.GetProperty("target")));
        Assert.Equal(BindingKeys, KeysOf(posted.Root.GetProperty("binding")));
        var approval = posted.Root.GetProperty("approval");
        Assert.Equal(ApprovalKeys, KeysOf(approval));
        Assert.Equal(approvalId, approval.GetProperty("id").GetGuid());
        Assert.Equal("github.com", approval.GetProperty("host").GetString());
        Assert.Equal("PushExactCommit", approval.GetProperty("action").GetString());
        Assert.Equal(1, approval.GetProperty("policyVersion").GetInt32());
        Assert.Equal(bound.Delivered.OperationId, approval.GetProperty("localCommitOperationId").GetGuid());
        Assert.Equal(bound.Delivered.TargetId, approval.GetProperty("targetId").GetGuid());
        Assert.Equal(bound.BindingId, approval.GetProperty("bindingId").GetGuid());
        Assert.Equal(NodeId, approval.GetProperty("repositoryNodeId").GetString());
        Assert.Equal(DefaultOwner, approval.GetProperty("owner").GetString());
        Assert.Equal(DefaultRepository, approval.GetProperty("repository").GetString());
        Assert.Equal(bound.BranchName, approval.GetProperty("branchName").GetString());
        Assert.Equal(bound.Delivered.Sha, approval.GetProperty("commitSha").GetString());

        Assert.Equal((1, 1), await ApprovalCountsAsync(bound.RunId));
        Assert.Equal(before, await FingerprintWithoutApprovalWritesAsync());
        Assert.Equal(observed, bound.Delivered.Observer.Calls.Count);

        var row = await ApprovalRowAsync(bound.RunId);
        var binding = await BindingRowAsync(bound.RunId);
        var target = await TargetRowAsync(bound.RunId);
        var stored = await ApprovalEventRowAsync(bound.RunId);
        Assert.Equal(row.Id, approval.GetProperty("id").GetGuid());
        Assert.Equal(row.EventId, approval.GetProperty("eventId").GetGuid());
        Assert.Equal(stored.Id, row.EventId);
        Assert.Equal(target.EventId, row.TargetEventId);
        Assert.Equal(binding.EventId, row.BindingEventId);
        Assert.Equal(binding.RecordedAtUtc, row.BindingRecordedAtUtc);
        Assert.Equal(target.Owner, row.TargetOwner);
        Assert.Equal(binding.Owner, row.BindingOwner);
        Assert.True(row.BindingRecordedAtUtc <= row.ApprovedAtUtc);
        Assert.InRange(row.ApprovedAtUtc, started.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Equal(row.ApprovedAtUtc, approval.GetProperty("approvedAtUtc").GetDateTimeOffset());
        Assert.Equal(row.ApprovedAtUtc, stored.OccurredAtUtc);
        Assert.Equal(ParticipantKind.Human, stored.ActorKind);
        Assert.Null(stored.AttemptId);
        Assert.Equal(GitHubBranchPublicationApprovalEventPayload.Build(row), stored.PayloadJson);
        Assert.True(Encoding.UTF8.GetByteCount(stored.PayloadJson) <= GitHubBranchPublicationApprovalEventPayload.MaximumLength);
        Assert.DoesNotContain(GhPath, posted.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_recorded_target_without_a_binding_reads_as_not_recorded_with_every_fact_null()
    {
        var unbound = await BoundAsync(recordBinding: false);

        var notBound = await GetApprovalAsync(unbound.Host, unbound.RunId);

        Assert.Equal(HttpStatusCode.OK, notBound.Status);
        Assert.Equal(GetKeys, KeysOf(notBound.Root));
        Assert.Equal("NotRecorded", StatusOf(notBound));
        foreach (var member in new[] { "source", "target", "binding", "approval" })
        {
            Assert.Equal(System.Text.Json.JsonValueKind.Null, notBound.Root.GetProperty(member).ValueKind);
        }
    }

    [Fact]
    public async Task The_read_reports_each_status_with_exactly_its_facts_and_never_writes_or_observes()
    {
        var bound = await BoundAsync();
        var fingerprint = await DatabaseFingerprintAsync();
        var observed = bound.Delivered.Observer.Calls.Count;
        var beforeApproval = await GetApprovalAsync(bound.Host, bound.RunId);
        Assert.Equal("ApprovalNotRecorded", StatusOf(beforeApproval));
        Assert.Equal(bound.Delivered.Sha, beforeApproval.Root.GetProperty("source").GetProperty("commitSha").GetString());
        Assert.Equal(bound.Delivered.TargetId, beforeApproval.Root.GetProperty("target").GetProperty("id").GetGuid());
        Assert.Equal(bound.BindingId, beforeApproval.Root.GetProperty("binding").GetProperty("id").GetGuid());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, beforeApproval.Root.GetProperty("approval").ValueKind);
        Assert.Equal(fingerprint, await DatabaseFingerprintAsync());

        var posted = await PostApprovalAsync(bound.Host, bound.RunId, ApprovalBodyFor(bound));
        var read = await GetApprovalAsync(bound.Host, bound.RunId);

        Assert.Equal("Available", StatusOf(read));
        Assert.Equal(GetKeys, KeysOf(read.Root));
        Assert.Equal(posted.Root.GetProperty("source").ToString(), read.Root.GetProperty("source").ToString());
        Assert.Equal(posted.Root.GetProperty("target").ToString(), read.Root.GetProperty("target").ToString());
        Assert.Equal(posted.Root.GetProperty("binding").ToString(), read.Root.GetProperty("binding").ToString());
        Assert.Equal(posted.Root.GetProperty("approval").ToString(), read.Root.GetProperty("approval").ToString());
        Assert.Equal(observed, bound.Delivered.Observer.Calls.Count);
        Assert.Equal((1, 1), await ApprovalCountsAsync(bound.RunId));
    }

    [Fact]
    public async Task An_exact_replay_returns_the_original_row_event_and_time_and_adds_nothing()
    {
        var bound = await BoundAsync();
        var body = ApprovalBodyFor(bound);
        var first = await PostApprovalAsync(bound.Host, bound.RunId, body);
        var row = await ApprovalRowAsync(bound.RunId);
        var fingerprint = await DatabaseFingerprintAsync();
        await Task.Delay(30);

        var replay = await PostApprovalAsync(bound.Host, bound.RunId, body);

        Assert.Equal(HttpStatusCode.OK, replay.Status);
        Assert.Equal(first.Body, replay.Body);
        Assert.Equal(row.ApprovedAtUtc, (await ApprovalRowAsync(bound.RunId)).ApprovedAtUtc);
        Assert.Equal(fingerprint, await DatabaseFingerprintAsync());
        Assert.Equal((1, 1), await ApprovalCountsAsync(bound.RunId));
    }

    [Fact]
    public async Task A_changed_request_or_another_identifier_for_a_run_that_holds_an_approval_conflicts_and_changes_nothing()
    {
        var bound = await BoundAsync();
        var approvalId = Guid.NewGuid();
        var first = await PostApprovalAsync(bound.Host, bound.RunId, ApprovalBodyFor(bound, approvalId));
        var fingerprint = await DatabaseFingerprintAsync();
        var d = bound.Delivered;
        var bodies = new Dictionary<string, string>
        {
            ["another approval identifier"] = ApprovalBodyOf(Guid.NewGuid(), d.OperationId, d.Sha, bound.BranchName, d.TargetId, bound.BindingId),
            ["another operation"] = ApprovalBodyOf(approvalId, Guid.NewGuid(), d.Sha, bound.BranchName, d.TargetId, bound.BindingId),
            ["another commit"] = ApprovalBodyOf(approvalId, d.OperationId, Nines40, bound.BranchName, d.TargetId, bound.BindingId),
            ["another branch"] = ApprovalBodyOf(approvalId, d.OperationId, d.Sha, "elsewhere", d.TargetId, bound.BindingId),
            ["another target"] = ApprovalBodyOf(approvalId, d.OperationId, d.Sha, bound.BranchName, Guid.NewGuid(), bound.BindingId),
            ["another binding"] = ApprovalBodyOf(approvalId, d.OperationId, d.Sha, bound.BranchName, d.TargetId, Guid.NewGuid()),
            ["another node identifier"] = ApprovalBodyOf(approvalId, d.OperationId, d.Sha, bound.BranchName, d.TargetId, bound.BindingId, "R_other"),
        };

        foreach (var (name, body) in bodies)
        {
            var refused = await PostApprovalAsync(bound.Host, bound.RunId, body);

            Assert.True(refused.Status == HttpStatusCode.Conflict, $"{name}: {refused.Status} {refused.Body}");
            Assert.True(ErrorCode(refused) == "github_branch_publication_approval.conflict", $"{name}: {refused.Body}");
            Assert.True(fingerprint == await DatabaseFingerprintAsync(), name);
        }

        Assert.Equal(first.Body, (await PostApprovalAsync(bound.Host, bound.RunId, ApprovalBodyFor(bound, approvalId))).Body);
    }

    [Fact]
    public async Task A_stale_or_foreign_acknowledgement_is_unavailable_without_writes_and_the_exact_one_then_records()
    {
        var bound = await BoundAsync();
        var d = bound.Delivered;
        var before = await DatabaseFingerprintAsync();
        var approvalId = Guid.NewGuid();
        var bodies = new Dictionary<string, string>
        {
            ["operation"] = ApprovalBodyOf(approvalId, Guid.NewGuid(), d.Sha, bound.BranchName, d.TargetId, bound.BindingId),
            ["commit"] = ApprovalBodyOf(approvalId, d.OperationId, Nines40, bound.BranchName, d.TargetId, bound.BindingId),
            ["branch"] = ApprovalBodyOf(approvalId, d.OperationId, d.Sha, "elsewhere", d.TargetId, bound.BindingId),
            ["branch in another case"] = ApprovalBodyOf(approvalId, d.OperationId, d.Sha, bound.BranchName.ToUpperInvariant(), d.TargetId, bound.BindingId),
            ["branch with a trailing space"] = ApprovalBodyOf(approvalId, d.OperationId, d.Sha, bound.BranchName + " ", d.TargetId, bound.BindingId),
            ["target"] = ApprovalBodyOf(approvalId, d.OperationId, d.Sha, bound.BranchName, Guid.NewGuid(), bound.BindingId),
            ["binding"] = ApprovalBodyOf(approvalId, d.OperationId, d.Sha, bound.BranchName, d.TargetId, Guid.NewGuid()),
            ["node identifier"] = ApprovalBodyOf(approvalId, d.OperationId, d.Sha, bound.BranchName, d.TargetId, bound.BindingId, "R_other"),
            ["node identifier in another case"] = ApprovalBodyOf(approvalId, d.OperationId, d.Sha, bound.BranchName, d.TargetId, bound.BindingId, NodeId.ToUpperInvariant()),
        };

        foreach (var (name, body) in bodies)
        {
            var refused = await PostApprovalAsync(bound.Host, bound.RunId, body);

            Assert.True(refused.Status == HttpStatusCode.Conflict, $"{name}: {refused.Status} {refused.Body}");
            Assert.True(ErrorCode(refused) == "github_branch_publication_approval.unavailable", $"{name}: {refused.Body}");
            Assert.DoesNotContain(NodeId, refused.Body, StringComparison.Ordinal);
            Assert.DoesNotContain(bound.BranchName, refused.Body, StringComparison.Ordinal);
            Assert.True(before == await DatabaseFingerprintAsync(), name);
        }

        Assert.Equal(HttpStatusCode.OK, (await PostApprovalAsync(bound.Host, bound.RunId, ApprovalBodyFor(bound, approvalId))).Status);
        Assert.Equal((1, 1), await ApprovalCountsAsync(bound.RunId));
    }

    [Fact]
    public async Task Without_a_recorded_binding_the_write_is_unavailable_and_records_nothing()
    {
        var unbound = await BoundAsync(recordBinding: false);
        var before = await DatabaseFingerprintAsync();
        var body = ApprovalBodyOf(
            Guid.NewGuid(), unbound.Delivered.OperationId, unbound.Delivered.Sha, unbound.BranchName, unbound.Delivered.TargetId,
            Guid.NewGuid());

        var refused = await PostApprovalAsync(unbound.Host, unbound.RunId, body);

        Assert.Equal(HttpStatusCode.Conflict, refused.Status);
        Assert.Equal("github_branch_publication_approval.unavailable", ErrorCode(refused));
        Assert.Equal(before, await DatabaseFingerprintAsync());
    }

    [Fact]
    public async Task Consent_does_not_require_a_matching_remote_commit_green_checks_a_cli_or_a_stored_authentication()
    {
        var bound = await BoundAsync();
        await ExecuteSqlAsync("DELETE FROM host_capability_snapshots");

        var posted = await PostApprovalAsync(bound.Host, bound.RunId, ApprovalBodyFor(bound));

        Assert.Equal(HttpStatusCode.OK, posted.Status);
        Assert.Equal((1, 1), await ApprovalCountsAsync(bound.RunId));
    }

    [Fact]
    public async Task A_host_clock_earlier_than_the_binding_recording_is_unavailable_never_clamped_and_equal_is_recorded_exactly()
    {
        var clock = new PinnedClock();
        var bound = await BoundAsync(configure: services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        });
        var recordedAt = (await BindingRowAsync(bound.RunId)).RecordedAtUtc;
        var before = await DatabaseFingerprintAsync();

        clock.Pinned = recordedAt.AddTicks(-1);
        var refused = await PostApprovalAsync(bound.Host, bound.RunId, ApprovalBodyFor(bound));

        Assert.Equal(HttpStatusCode.Conflict, refused.Status);
        Assert.Equal("github_branch_publication_approval.unavailable", ErrorCode(refused));
        Assert.Equal(before, await DatabaseFingerprintAsync());

        clock.Pinned = recordedAt;
        var posted = await PostApprovalAsync(bound.Host, bound.RunId, ApprovalBodyFor(bound));

        Assert.Equal(HttpStatusCode.OK, posted.Status);
        Assert.Equal(recordedAt, (await ApprovalRowAsync(bound.RunId)).ApprovedAtUtc);
    }

    [Fact]
    public async Task The_approval_survives_a_restart_and_a_replay_after_it_adds_nothing()
    {
        var bound = await BoundAsync();
        var body = ApprovalBodyFor(bound);
        var recorded = await PostApprovalAsync(bound.Host, bound.RunId, body);

        var restarted = StartHost(runSupervisor: false);
        var reread = await GetApprovalAsync(restarted, bound.RunId);
        var fingerprint = await DatabaseFingerprintAsync();
        var replay = await PostApprovalAsync(restarted, bound.RunId, body);

        Assert.Equal("Available", StatusOf(reread));
        Assert.Equal(recorded.Body, replay.Body);
        Assert.Equal(reread.Root.GetProperty("approval").ToString(), recorded.Root.GetProperty("approval").ToString());
        Assert.Equal(fingerprint, await DatabaseFingerprintAsync());
        Assert.Equal((1, 1), await ApprovalCountsAsync(bound.RunId));
    }

    [Fact]
    public async Task A_notifier_that_fails_after_the_commit_cannot_undo_or_fail_the_recorded_approval()
    {
        var notifier = new ThrowingNotifier();
        var bound = await BoundAsync();
        var host = StartHost(runSupervisor: false, configure: services =>
        {
            services.RemoveAll<IRunEventNotifier>();
            services.AddSingleton<IRunEventNotifier>(notifier);
        });

        var response = await PostApprovalAsync(host, bound.RunId, ApprovalBodyFor(bound));

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal(1, notifier.Calls);
        Assert.Equal((1, 1), await ApprovalCountsAsync(bound.RunId));
    }

    [Fact]
    public async Task Another_runs_approval_that_already_owns_the_identifier_conflicts_and_nothing_is_recorded()
    {
        var bound = await BoundAsync();
        var foreignRun = Guid.NewGuid();
        var approvalId = Guid.NewGuid();
        var foreignRow =
            "INSERT INTO github_branch_publication_approvals (Id, EventId, RunId, ProjectId, LocalCommitOperationId, GitWorkspaceId, "
            + "CommitSha, BranchName, TargetId, TargetEventId, TargetOwner, TargetRepository, TargetRecordedAtUtc, BindingId, "
            + "BindingEventId, RepositoryNodeId, BindingOwner, BindingRepository, BindingObservedAtUtc, BindingRecordedAtUtc, "
            + $"ApprovedAtUtc) VALUES ('{Upper(approvalId)}', '{Upper(Guid.NewGuid())}', '{Upper(foreignRun)}', '{Upper(Guid.NewGuid())}', "
            + $"'{Upper(Guid.NewGuid())}', '{Upper(Guid.NewGuid())}', '{Nines40}', 'other', '{Upper(Guid.NewGuid())}', "
            + $"'{Upper(Guid.NewGuid())}', 'o', 'r', '2001-01-01 00:00:00+00:00', '{Upper(Guid.NewGuid())}', '{Upper(Guid.NewGuid())}', "
            + "'R_x', 'o', 'r', '2001-01-01 00:00:00+00:00', '2001-01-01 00:00:00+00:00', '2001-01-01 00:00:00+00:00')";

        await CorruptAsync([Approvals], [foreignRow], async () =>
        {
            var before = await DatabaseFingerprintAsync();

            var refused = await PostApprovalAsync(bound.Host, bound.RunId, ApprovalBodyFor(bound, approvalId));

            Assert.Equal(HttpStatusCode.Conflict, refused.Status);
            Assert.Equal("github_branch_publication_approval.conflict", ErrorCode(refused));
            Assert.Equal(before, await DatabaseFingerprintAsync());
            Assert.Equal((0, 0), await ApprovalCountsAsync(bound.RunId));
        });
    }

    private sealed class ThrowingNotifier : IRunEventNotifier
    {
        public int Calls;

        public Task NotifyRunAdvancedAsync(Guid runId, long latestSequence, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            throw new InvalidOperationException("The notification transport failed after the facts were durable.");
        }
    }
}
