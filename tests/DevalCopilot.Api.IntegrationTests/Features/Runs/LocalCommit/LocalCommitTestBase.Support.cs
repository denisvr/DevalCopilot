using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

public abstract partial class LocalCommitTestBase
{
    internal (LocalCommitHost Host, ScriptedLocalCommitRepository Script) StartScripted(
        bool supervisor, Action<IServiceCollection>? configure = null)
    {
        ScriptedLocalCommitRepository? script = null;
        var host = StartHost(supervisor, inner => script = new ScriptedLocalCommitRepository(inner), configure: configure);
        _scripts.Add(script!);
        return (host, script!);
    }

    internal static async Task WaitUntilAsync(Func<Task<bool>> condition, int seconds = 30)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new Xunit.Sdk.XunitException("The awaited condition was not reached within the bound.");
    }

    internal string BranchTip() => Scene.RunMainGit("rev-parse", "refs/heads/" + Scene.BranchName).Trim();

    internal async Task<int> EventCountAsync(Guid runId, string eventType) =>
        (await EventTypeRowsAsync(runId)).Count(type => type == eventType);

    internal async Task AssertReservedAsync(LocalCommitLineageIds ids, WorkspaceStatus workspaceStatus, LocalCommitStatus operationStatus)
    {
        var operation = await OperationRowAsync(ids.RunId);
        Assert.Equal(operationStatus, operation.Status);
        Assert.Equal(workspaceStatus, (await WorkspaceRowAsync(ids.WorkspaceId)).Status);
        Assert.Equal(RunLifecycle.Running, (await RunRowAsync(ids.RunId)).Lifecycle);
    }
}
