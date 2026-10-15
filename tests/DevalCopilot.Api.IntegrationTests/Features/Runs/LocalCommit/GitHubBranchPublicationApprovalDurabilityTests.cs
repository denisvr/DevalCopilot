using System.Net;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Commands.RecordGitHubBranchPublicationApproval;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// ADR-0048 durability: separate hosts (separate contexts and connections over one database file) racing for one run serialize to one
/// approval and one event; a real SQLite fault, a cancellation before the lock or just before the commit never leaves half of the pair;
/// and a retry after any of them records exactly one pair.
/// </summary>
public sealed class GitHubBranchPublicationApprovalDurabilityTests : GitHubBranchPublicationApprovalTestBase
{
    [Fact]
    public async Task Concurrent_identical_requests_from_separate_hosts_record_one_approval_and_one_event_and_all_return_it()
    {
        var bound = await BoundAsync();
        var hosts = Enumerable.Range(0, 4).Select(_ => StartHost(runSupervisor: false)).ToArray();
        var body = ApprovalBodyFor(bound);

        var responses = await Task.WhenAll(Enumerable.Range(0, 16)
            .Select(index => Task.Run(() => PostApprovalAsync(hosts[index % hosts.Length], bound.RunId, body))));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.Status));
        Assert.Single(responses.Select(response => response.Body).Distinct());
        Assert.Equal((1, 1), await ApprovalCountsAsync(bound.RunId));
        Assert.Equal(
            responses[0].Root.GetProperty("approval").GetProperty("id").GetGuid(), (await ApprovalRowAsync(bound.RunId)).Id);
    }

    [Fact]
    public async Task Concurrent_requests_with_different_identifiers_record_exactly_one_and_the_rest_conflict()
    {
        var bound = await BoundAsync();
        var hosts = Enumerable.Range(0, 4).Select(_ => StartHost(runSupervisor: false)).ToArray();
        var ids = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();

        var responses = await Task.WhenAll(Enumerable.Range(0, 16).Select(index => Task.Run(async () =>
            (Id: ids[index % ids.Length],
             Reading: await PostApprovalAsync(hosts[index % hosts.Length], bound.RunId, ApprovalBodyFor(bound, ids[index % ids.Length]))))));

        Assert.Equal((1, 1), await ApprovalCountsAsync(bound.RunId));
        var winner = (await ApprovalRowAsync(bound.RunId)).Id;
        Assert.Contains(winner, ids);
        Assert.All(responses.Where(response => response.Id == winner), response => Assert.Equal(HttpStatusCode.OK, response.Reading.Status));
        Assert.All(
            responses.Where(response => response.Id != winner),
            response => Assert.True(
                response.Reading.Status == HttpStatusCode.Conflict
                && ErrorCode(response.Reading) == "github_branch_publication_approval.conflict",
                response.Reading.Body));
        Assert.Single(responses.Where(response => response.Reading.Status == HttpStatusCode.OK).Select(response => response.Reading.Body).Distinct());
    }

    [Theory]
    [InlineData("github_branch_publication_approvals", "")]
    [InlineData("events", " WHEN NEW.\"EventType\" = 'run.github_branch_publication_approved'")]
    public async Task A_real_fault_while_saving_either_row_rolls_both_back_propagates_and_a_retry_records_one_pair(string table, string when)
    {
        var bound = await BoundAsync();
        var fingerprint = await DatabaseFingerprintAsync();
        await ExecuteSqlAsync(
            $"CREATE TRIGGER fault_approval BEFORE INSERT ON {table}{when} BEGIN SELECT RAISE(ABORT, 'injected_approval_fault'); END;");
        var body = ApprovalBodyFor(bound);

        var failed = await PostApprovalAsync(bound.Host, bound.RunId, body);

        Assert.Equal(HttpStatusCode.InternalServerError, failed.Status);
        Assert.Equal((0, 0), await ApprovalCountsAsync(bound.RunId));
        Assert.Equal(fingerprint, await DatabaseFingerprintAsync());
        Assert.Equal("ApprovalNotRecorded", StatusOf(await GetApprovalAsync(bound.Host, bound.RunId)));

        await ExecuteSqlAsync("DROP TRIGGER fault_approval");
        Assert.Equal(HttpStatusCode.OK, (await PostApprovalAsync(bound.Host, bound.RunId, body)).Status);
        Assert.Equal((1, 1), await ApprovalCountsAsync(bound.RunId));
    }

    [Fact]
    public async Task A_request_cancelled_before_the_write_lock_leaves_neither_row_and_a_retry_records_one_pair()
    {
        var lockPoint = new LockPointInterceptor();
        var bound = await BoundAsync(configure: UseInterceptors(lockPoint));
        var fingerprint = await DatabaseFingerprintAsync();
        using var cancelled = new CancellationTokenSource();
        lockPoint.AtLockPoint = () => cancelled.Cancel();
        lockPoint.Arm();

        await using (var scope = bound.Host.Services.CreateAsyncScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await mediator.SendAsync(Command(bound), cancelled.Token));
        }

        Assert.Equal(1, lockPoint.Fired);
        Assert.Equal((0, 0), await ApprovalCountsAsync(bound.RunId));
        Assert.Equal(fingerprint, await DatabaseFingerprintAsync());
        Assert.Equal(HttpStatusCode.OK, (await PostApprovalAsync(bound.Host, bound.RunId, ApprovalBodyFor(bound))).Status);
        Assert.Equal((1, 1), await ApprovalCountsAsync(bound.RunId));
    }

    [Fact]
    public async Task A_cancellation_after_both_rows_were_saved_and_before_the_commit_leaves_neither_row()
    {
        var commitPoint = new CommitPointInterceptor();
        var bound = await BoundAsync(configure: UseInterceptors(commitPoint));
        var fingerprint = await DatabaseFingerprintAsync();
        commitPoint.Arm();

        await using (var scope = bound.Host.Services.CreateAsyncScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await mediator.SendAsync(Command(bound), CancellationToken.None));
        }

        Assert.Equal((0, 0), await ApprovalCountsAsync(bound.RunId));
        Assert.Equal(fingerprint, await DatabaseFingerprintAsync());
        Assert.Equal(HttpStatusCode.OK, (await PostApprovalAsync(bound.Host, bound.RunId, ApprovalBodyFor(bound))).Status);
        Assert.Equal((1, 1), await ApprovalCountsAsync(bound.RunId));
    }

    [Fact]
    public async Task A_recorded_approval_cannot_be_updated_or_deleted_and_nothing_it_names_can_be_deleted()
    {
        var bound = await BoundAsync();
        await PostApprovalAsync(bound.Host, bound.RunId, ApprovalBodyFor(bound));
        var fingerprint = await DatabaseFingerprintAsync();
        var row = await ApprovalRowAsync(bound.RunId);
        var id = Upper(row.Id);

        var statements = new[]
        {
            $"UPDATE {Approvals} SET \"RepositoryNodeId\" = 'R_other' WHERE \"Id\" = '{id}'",
            $"UPDATE {Approvals} SET \"ApprovedAtUtc\" = '2999-01-01 00:00:00+00:00'",
            $"DELETE FROM {Approvals} WHERE \"Id\" = '{id}'",
            $"DELETE FROM {Approvals}",
            $"DELETE FROM github_repository_identity_bindings WHERE \"Id\" = '{Upper(row.BindingId)}'",
            $"DELETE FROM github_observation_targets WHERE \"Id\" = '{Upper(row.TargetId)}'",
            $"DELETE FROM runs WHERE \"Id\" = '{Upper(row.RunId)}'",
            $"DELETE FROM local_commit_operations WHERE \"Id\" = '{Upper(row.LocalCommitOperationId)}'",
            $"DELETE FROM git_workspaces WHERE \"Id\" = '{Upper(row.GitWorkspaceId)}'",
            $"DELETE FROM projects WHERE \"Id\" = '{Upper(row.ProjectId)}'",
        };

        foreach (var statement in statements)
        {
            var refused = await Assert.ThrowsAnyAsync<Exception>(() => ExecuteSqlAsync("PRAGMA foreign_keys = ON; " + statement));
            Assert.True(
                refused.Message.Contains("github_branch_publication_approval.immutable", StringComparison.Ordinal)
                || refused.Message.Contains("FOREIGN KEY", StringComparison.OrdinalIgnoreCase)
                || refused.Message.Contains("constraint", StringComparison.OrdinalIgnoreCase),
                $"{statement}: {refused.Message}");
        }

        Assert.Equal(fingerprint, await DatabaseFingerprintAsync());
        Assert.Equal((1, 1), await ApprovalCountsAsync(bound.RunId));
    }

    private static RecordGitHubBranchPublicationApprovalCommand Command(Bound bound) => new(
        bound.RunId, Guid.NewGuid(), bound.Delivered.OperationId, bound.Delivered.Sha, bound.BranchName, bound.Delivered.TargetId,
        bound.BindingId, NodeId);
}
