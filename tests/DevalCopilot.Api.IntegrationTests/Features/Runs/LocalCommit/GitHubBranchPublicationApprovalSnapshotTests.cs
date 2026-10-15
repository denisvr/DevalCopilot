using System.Net;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// ADR-0048 correction R2 on the protected production host. A separate, prestarted writer host commits a competing request at the
/// controlled boundary of the first approval-evidence database command of the request under test (a one-shot interception, with no
/// sleeps and no repeated runs): the reading is one whole snapshot, so an identical competing request is replayed (never refused as
/// unavailable), a competing request with another identifier conflicts, and a stable orphan stays unavailable.
/// </summary>
public sealed class GitHubBranchPublicationApprovalSnapshotTests : GitHubBranchPublicationApprovalTestBase
{
    private async Task<(Bound Bound, EvidenceBoundaryInterceptor Interceptor)> BoundWithInterceptorAsync()
    {
        var interceptor = new EvidenceBoundaryInterceptor();
        var bound = await BoundAsync(configure: UseInterceptors(interceptor));
        interceptor.Observed.Clear();
        return (bound, interceptor);
    }

    [Fact]
    public async Task An_identical_request_that_commits_at_the_boundary_of_the_evidence_command_is_replayed_with_one_pair_recorded()
    {
        var (bound, interceptor) = await BoundWithInterceptorAsync();
        var body = ApprovalBodyFor(bound);
        var writer = StartHost(runSupervisor: false);
        ReceiptReading? competing = null;
        interceptor.AtBoundary = async () => competing = await PostApprovalAsync(writer, bound.RunId, body);
        interceptor.Arm();

        var replayed = await PostApprovalAsync(bound.Host, bound.RunId, body);

        Assert.Equal(1, interceptor.Fired);
        Assert.Equal(HttpStatusCode.OK, competing!.Status);
        Assert.True(replayed.Status == HttpStatusCode.OK, replayed.Status.ToString());
        Assert.Equal(competing.Body, replayed.Body);
        Assert.Equal((1, 1), await ApprovalCountsAsync(bound.RunId));
        Assert.Equal(EvidenceCommand.RowsAndEvents, interceptor.Observed[0]);
    }

    [Fact]
    public async Task A_request_with_another_identifier_after_a_competing_commit_at_the_boundary_conflicts_and_the_first_stays()
    {
        var (bound, interceptor) = await BoundWithInterceptorAsync();
        var writer = StartHost(runSupervisor: false);
        var firstBody = ApprovalBodyFor(bound);
        interceptor.AtBoundary = async () => await PostApprovalAsync(writer, bound.RunId, firstBody);
        interceptor.Arm();

        var refused = await PostApprovalAsync(bound.Host, bound.RunId, ApprovalBodyFor(bound));

        Assert.Equal(1, interceptor.Fired);
        Assert.True(refused.Status == HttpStatusCode.Conflict, $"{refused.Status} {ErrorCode(refused)}");
        Assert.Equal("github_branch_publication_approval.conflict", ErrorCode(refused));
        Assert.Equal((1, 1), await ApprovalCountsAsync(bound.RunId));
        Assert.Equal(EvidenceCommand.RowsAndEvents, interceptor.Observed[0]);
    }

    [Fact]
    public async Task A_stable_orphan_event_stays_unavailable_at_the_boundary_and_nothing_is_recorded_or_recreated()
    {
        var (bound, interceptor) = await BoundWithInterceptorAsync();
        var body = ApprovalBodyFor(bound);
        Assert.Equal(HttpStatusCode.OK, (await PostApprovalAsync(bound.Host, bound.RunId, body)).Status);
        interceptor.Observed.Clear();

        await CorruptAsync([Approvals], [$"DELETE FROM {Approvals}"], async () =>
        {
            interceptor.AtBoundary = () => Task.CompletedTask;
            interceptor.Arm();

            var refused = await PostApprovalAsync(bound.Host, bound.RunId, body);

            Assert.Equal(1, interceptor.Fired);
            Assert.Equal(EvidenceCommand.RowsAndEvents, interceptor.Observed[0]);
            Assert.Equal(HttpStatusCode.Conflict, refused.Status);
            Assert.Equal("github_branch_publication_approval.unavailable", ErrorCode(refused));
            Assert.Equal((0, 1), await ApprovalCountsAsync(bound.RunId));
        });
    }
}
