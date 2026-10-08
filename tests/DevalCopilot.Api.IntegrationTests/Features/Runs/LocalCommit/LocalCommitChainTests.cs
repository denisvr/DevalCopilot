using System.Net;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>ADR-0029's unique completed head chain end to end: a later run in the same workspace starts from the recorded commit of
/// the earlier one, restart reconciliation expects exactly that tip, and a broken chain refuses further delivery.</summary>
public sealed class LocalCommitChainTests : LocalCommitTestBase
{
    [Fact]
    public async Task A_later_run_in_the_same_workspace_extends_the_completed_chain_and_restart_keeps_it_ready()
    {
        using var host = StartHost(runSupervisor: true);
        var first = await LocalCommitLineage.SeedAsync(host, Scene);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(host, first.RunId, first)).StatusCode);
        await WaitForAsync(host, first.RunId, status => status.Operation?.Status == "Completed");
        var firstCommit = BranchTip();
        Assert.Equal(WorkspaceStatus.Ready, (await WorkspaceRowAsync(first.WorkspaceId)).Status);

        var second = await LocalCommitLineage.SeedAsync(host, Scene, continueFrom: first);
        Assert.Equal(first.WorkspaceId, second.WorkspaceId);
        var eligible = await StatusAsync(host, second.RunId);
        Assert.True(eligible.Eligible, eligible.RefusalCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(host, second.RunId, second)).StatusCode);
        var done = await WaitForAsync(host, second.RunId, status => status.Operation?.Status == "Completed");

        var secondOperation = done.Operation!;
        Assert.Equal(firstCommit, secondOperation.ParentCommitSha);
        Assert.Equal(BranchTip(), secondOperation.CommitSha);
        Assert.NotEqual(firstCommit, secondOperation.CommitSha);
        Assert.Equal("3", Scene.RunMainGit("rev-list", "--count", "refs/heads/" + Scene.BranchName).Trim());
        Assert.Equal(
            "second approved change\n", Scene.RunMainGit("cat-file", "-p", secondOperation.CommitSha + ":a.txt").Replace("\r", string.Empty));
        Assert.Equal(RunLifecycle.Completed, (await RunRowAsync(second.RunId)).Lifecycle);
        Assert.Equal(WorkspaceStatus.Ready, (await WorkspaceRowAsync(second.WorkspaceId)).Status);
        var events = (await EventTypeRowsAsync(second.RunId)).Where(type => type.StartsWith("local_commit.", StringComparison.Ordinal));
        Assert.Equal(["local_commit.admitted", "local_commit.executing", "local_commit.completed"], events);
        host.Dispose();

        using var restarted = StartHost(runSupervisor: false);
        Assert.Equal(WorkspaceStatus.Ready, (await WorkspaceRowAsync(first.WorkspaceId)).Status);
        Assert.Equal(secondOperation.CommitSha, BranchTip());
    }

    [Fact]
    public async Task A_broken_earlier_edge_refuses_every_later_delivery_with_a_parent_mismatch()
    {
        using var host = StartHost(runSupervisor: true);
        var first = await LocalCommitLineage.SeedAsync(host, Scene);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(host, first.RunId, first)).StatusCode);
        await WaitForAsync(host, first.RunId, status => status.Operation?.Status == "Completed");
        var operation = await OperationRowAsync(first.RunId);
        await ExecuteSqlAsync(
            $"UPDATE local_commit_operations SET \"ParentCommitSha\" = '{new string('9', 40)}' WHERE \"Id\" = '{operation.Id.ToString().ToUpperInvariant()}'");

        var second = await LocalCommitLineage.SeedAsync(host, Scene, continueFrom: first);

        var status = await StatusAsync(host, second.RunId);
        Assert.False(status.Eligible);
        Assert.Equal("local_commit.parent_mismatch", status.RefusalCode);
        var response = await PostAsync(host, second.RunId, second);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("local_commit.parent_mismatch", await ErrorCodeAsync(response));
        Assert.Equal(1, await OperationCountAsync(host));
    }
}
