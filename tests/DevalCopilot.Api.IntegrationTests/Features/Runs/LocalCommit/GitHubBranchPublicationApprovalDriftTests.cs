using System.Net;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// ADR-0048 admission under the write lock. Each case changes one stored fact (always leaving the stored scope coherent and every
/// identifier in the request unchanged) at the one moment between the operation's initial reading and its write lock, and proves the
/// operation notices through the complete value comparison of source, target and binding: it refuses with the fixed unavailable, records
/// nothing, and a fresh request then records the new scope exactly. The race of an identical request and the vanishing or replacement of
/// an existing approval are proved never to be recreated.
/// </summary>
public sealed class GitHubBranchPublicationApprovalDriftTests : GitHubBranchPublicationApprovalTestBase
{
    private async Task<(Bound Bound, LockPointInterceptor Interceptor)> BoundWithInterceptorAsync()
    {
        var interceptor = new LockPointInterceptor();
        var bound = await BoundAsync(configure: UseInterceptors(interceptor));
        return (bound, interceptor);
    }

    private async Task AssertDriftIsRefusedAndTheFreshRequestRecordsTheNewScopeAsync(
        Bound bound, LockPointInterceptor interceptor, Action drift, Func<Task> verifyRecordedScope)
    {
        var body = ApprovalBodyFor(bound);
        interceptor.AtLockPoint = drift;
        interceptor.Arm();

        var refused = await PostApprovalAsync(bound.Host, bound.RunId, body);

        Assert.Equal(1, interceptor.Fired);
        Assert.True(refused.Status == HttpStatusCode.Conflict, refused.Body);
        Assert.Equal("github_branch_publication_approval.unavailable", ErrorCode(refused));
        Assert.Equal((0, 0), await ApprovalCountsAsync(bound.RunId));

        var recorded = await PostApprovalAsync(bound.Host, bound.RunId, body);
        Assert.Equal(HttpStatusCode.OK, recorded.Status);
        Assert.Equal((1, 1), await ApprovalCountsAsync(bound.RunId));
        await verifyRecordedScope();
    }

    private void Sql(string statement) => ExecuteSqlAsync(statement).GetAwaiter().GetResult();

    [Fact]
    public async Task A_coherent_change_of_the_binding_owner_case_during_the_request_is_unavailable_with_every_request_id_unchanged()
    {
        var (bound, interceptor) = await BoundWithInterceptorAsync();
        var bindingEvent = $"\"EventType\" = '{BindingEventType}' AND \"RunId\" = '{Upper(bound.RunId)}'";

        await AssertDriftIsRefusedAndTheFreshRequestRecordsTheNewScopeAsync(
            bound,
            interceptor,
            () =>
            {
                Sql($"UPDATE github_repository_identity_bindings SET \"Owner\" = 'OCTO-CAT' WHERE \"Id\" = '{Upper(bound.BindingId)}'");
                Sql($"UPDATE events SET \"PayloadJson\" = replace(\"PayloadJson\", '\"owner\":\"octo-cat\"', '\"owner\":\"OCTO-CAT\"') WHERE {bindingEvent}");
            },
            async () =>
            {
                Assert.Equal("OCTO-CAT", (await ApprovalRowAsync(bound.RunId)).BindingOwner);
                Assert.Equal("Available", StatusOf(await GetApprovalAsync(bound.Host, bound.RunId)));
            });
    }

    [Fact]
    public async Task A_coherent_change_of_the_binding_observation_time_during_the_request_is_unavailable()
    {
        var (bound, interceptor) = await BoundWithInterceptorAsync();
        var original = await BindingRowAsync(bound.RunId);
        var drifted = original.ObservedAtUtc.AddTicks(-1) < (await TargetRowAsync(bound.RunId)).RecordedAtUtc
            ? original.ObservedAtUtc.AddTicks(1)
            : original.ObservedAtUtc.AddTicks(-1);

        await AssertDriftIsRefusedAndTheFreshRequestRecordsTheNewScopeAsync(
            bound,
            interceptor,
            () =>
            {
                var replacement = GitHubRepositoryIdentityBinding.Record(
                    original.Id, original.RunId, original.ProjectId, original.LocalCommitOperationId, original.GitWorkspaceId,
                    original.ObservationTargetId, original.CommitSha, original.BranchName, original.RepositoryNodeId, original.Owner,
                    original.Repository, (TargetRowAsync(bound.RunId).GetAwaiter().GetResult()).RecordedAtUtc, drifted,
                    original.RecordedAtUtc, original.EventId);
                var payload = GitHubRepositoryIdentityBindingEventPayload.Build(replacement);
                Sql(
                    $"UPDATE github_repository_identity_bindings SET \"ObservedAtUtc\" = '{drifted:yyyy-MM-dd HH:mm:ss.fffffffzzz}' "
                    + $"WHERE \"Id\" = '{Upper(bound.BindingId)}'");
                Sql(
                    $"UPDATE events SET \"PayloadJson\" = '{payload.Replace("'", "''", StringComparison.Ordinal)}' "
                    + $"WHERE \"Id\" = '{Upper(original.EventId)}'");
            },
            async () => Assert.Equal(drifted, (await ApprovalRowAsync(bound.RunId)).BindingObservedAtUtc));
    }

    [Fact]
    public async Task A_coherent_change_of_the_target_owner_case_during_the_request_is_unavailable()
    {
        var (bound, interceptor) = await BoundWithInterceptorAsync();
        var targetEvent = $"\"EventType\" = '{TargetEventType}' AND \"RunId\" = '{Upper(bound.RunId)}'";

        await AssertDriftIsRefusedAndTheFreshRequestRecordsTheNewScopeAsync(
            bound,
            interceptor,
            () =>
            {
                Sql($"UPDATE github_observation_targets SET \"Owner\" = 'Octo-Cat' WHERE \"Id\" = '{Upper(bound.Delivered.TargetId)}'");
                Sql($"UPDATE events SET \"PayloadJson\" = replace(\"PayloadJson\", '\"owner\":\"octo-cat\"', '\"owner\":\"Octo-Cat\"') WHERE {targetEvent}");
            },
            async () => Assert.Equal("Octo-Cat", (await ApprovalRowAsync(bound.RunId)).TargetOwner));
    }

    [Fact]
    public async Task A_replaced_binding_during_the_request_is_unavailable_and_records_nothing_for_the_stale_identifiers()
    {
        var (bound, interceptor) = await BoundWithInterceptorAsync();
        var body = ApprovalBodyFor(bound);
        var original = await BindingRowAsync(bound.RunId);
        interceptor.AtLockPoint = () =>
        {
            var replacement = GitHubRepositoryIdentityBinding.Record(
                Guid.NewGuid(), original.RunId, original.ProjectId, original.LocalCommitOperationId, original.GitWorkspaceId,
                original.ObservationTargetId, original.CommitSha, original.BranchName, "R_replacement", original.Owner,
                original.Repository, (TargetRowAsync(bound.RunId).GetAwaiter().GetResult()).RecordedAtUtc, original.ObservedAtUtc,
                original.RecordedAtUtc, Guid.NewGuid());
            using var db = OpenDb();
            Sql($"DELETE FROM github_repository_identity_bindings WHERE \"Id\" = '{Upper(original.Id)}'");
            Sql($"DELETE FROM events WHERE \"Id\" = '{Upper(original.EventId)}'");
            db.GitHubRepositoryIdentityBindings.Add(replacement);
            db.Events.Add(RunEvent.Record(
                replacement.EventId, replacement.RunId, null, RunEventType.GitHubRepositoryIdentityBound, ParticipantIdentity.ForHuman(),
                GitHubRepositoryIdentityBindingEventPayload.Build(replacement), replacement.RecordedAtUtc));
            db.SaveChanges();
        };
        interceptor.Arm();

        var refused = await PostApprovalAsync(bound.Host, bound.RunId, body);

        Assert.Equal(HttpStatusCode.Conflict, refused.Status);
        Assert.Equal("github_branch_publication_approval.unavailable", ErrorCode(refused));
        Assert.Equal((0, 0), await ApprovalCountsAsync(bound.RunId));
        var stale = await PostApprovalAsync(bound.Host, bound.RunId, body);
        Assert.Equal(HttpStatusCode.Conflict, stale.Status);
        Assert.Equal((0, 0), await ApprovalCountsAsync(bound.RunId));
    }

    [Fact]
    public async Task An_identical_request_that_records_first_during_the_request_is_replayed_and_recorded_once()
    {
        var (bound, interceptor) = await BoundWithInterceptorAsync();
        var body = ApprovalBodyFor(bound);
        var second = StartHost(runSupervisor: false);
        ReceiptReading? raced = null;
        interceptor.AtLockPoint = () => raced = PostApprovalAsync(second, bound.RunId, body).GetAwaiter().GetResult();
        interceptor.Arm();

        var replayed = await PostApprovalAsync(bound.Host, bound.RunId, body);

        Assert.Equal(HttpStatusCode.OK, raced!.Status);
        Assert.Equal(HttpStatusCode.OK, replayed.Status);
        Assert.Equal(raced.Body, replayed.Body);
        Assert.Equal((1, 1), await ApprovalCountsAsync(bound.RunId));
    }

    [Fact]
    public async Task A_different_request_that_records_first_during_the_request_conflicts_and_the_first_stays()
    {
        var (bound, interceptor) = await BoundWithInterceptorAsync();
        var second = StartHost(runSupervisor: false);
        var firstBody = ApprovalBodyFor(bound);
        interceptor.AtLockPoint = () => PostApprovalAsync(second, bound.RunId, firstBody).GetAwaiter().GetResult();
        interceptor.Arm();

        var refused = await PostApprovalAsync(bound.Host, bound.RunId, ApprovalBodyFor(bound));

        Assert.Equal(HttpStatusCode.Conflict, refused.Status);
        Assert.Equal("github_branch_publication_approval.conflict", ErrorCode(refused));
        Assert.Equal((1, 1), await ApprovalCountsAsync(bound.RunId));
    }

    [Fact]
    public async Task An_approval_that_vanishes_during_a_replay_is_unavailable_and_never_recreated()
    {
        var (bound, interceptor) = await BoundWithInterceptorAsync();
        var body = ApprovalBodyFor(bound);
        Assert.Equal(HttpStatusCode.OK, (await PostApprovalAsync(bound.Host, bound.RunId, body)).Status);

        await CorruptAsync([Approvals, "events"], [], async () =>
        {
            interceptor.AtLockPoint = () =>
            {
                Sql($"DELETE FROM {Approvals}");
                Sql($"DELETE FROM events WHERE \"EventType\" = '{ApprovalEventType}'");
            };
            interceptor.Arm();

            var refused = await PostApprovalAsync(bound.Host, bound.RunId, body);

            Assert.Equal(HttpStatusCode.Conflict, refused.Status);
            Assert.Equal("github_branch_publication_approval.unavailable", ErrorCode(refused));
            Assert.Equal((0, 0), await ApprovalCountsAsync(bound.RunId));
        });
    }

    [Fact]
    public async Task An_approval_that_is_replaced_by_another_coherent_one_during_a_replay_is_unavailable_and_not_returned()
    {
        var (bound, interceptor) = await BoundWithInterceptorAsync();
        var body = ApprovalBodyFor(bound);
        Assert.Equal(HttpStatusCode.OK, (await PostApprovalAsync(bound.Host, bound.RunId, body)).Status);
        var original = await ApprovalRowAsync(bound.RunId);

        await CorruptAsync([Approvals, "events"], [], async () =>
        {
            interceptor.AtLockPoint = () =>
            {
                var replacement = GitHubBranchPublicationApproval.Record(
                    Guid.NewGuid(), Guid.NewGuid(), original.RunId, original.ProjectId, original.LocalCommitOperationId,
                    original.GitWorkspaceId, original.CommitSha, original.BranchName, original.TargetId, original.TargetEventId,
                    original.TargetOwner, original.TargetRepository, original.TargetRecordedAtUtc, original.BindingId,
                    original.BindingEventId, original.RepositoryNodeId, original.BindingOwner, original.BindingRepository,
                    original.BindingObservedAtUtc, original.BindingRecordedAtUtc, original.ApprovedAtUtc.AddSeconds(1));
                Sql($"DELETE FROM {Approvals}");
                Sql($"DELETE FROM events WHERE \"EventType\" = '{ApprovalEventType}'");
                using var db = OpenDb();
                db.GitHubBranchPublicationApprovals.Add(replacement);
                db.Events.Add(RunEvent.Record(
                    replacement.EventId, replacement.RunId, null, ApprovalEventType, ParticipantIdentity.ForHuman(),
                    GitHubBranchPublicationApprovalEventPayload.Build(replacement), replacement.ApprovedAtUtc));
                db.SaveChanges();
            };
            interceptor.Arm();

            var refused = await PostApprovalAsync(bound.Host, bound.RunId, body);

            Assert.Equal(HttpStatusCode.Conflict, refused.Status);
            Assert.Equal("github_branch_publication_approval.unavailable", ErrorCode(refused));
            Assert.Equal((1, 1), await ApprovalCountsAsync(bound.RunId));
        });
    }
}
