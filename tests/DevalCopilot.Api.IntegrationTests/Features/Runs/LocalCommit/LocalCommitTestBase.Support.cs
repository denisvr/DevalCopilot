using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

public abstract partial class LocalCommitTestBase
{
    internal (LocalCommitHost Host, ScriptedLocalCommitRepository Script) StartScripted(
        bool supervisor, Action<IServiceCollection>? configure = null, Func<ILocalCommitPreparer, ILocalCommitPreparer>? decoratePreparer = null)
    {
        ScriptedLocalCommitRepository? script = null;
        var host = StartHost(supervisor, inner => script = new ScriptedLocalCommitRepository(inner), decoratePreparer, configure);
        _scripts.Add(script!);
        return (host, script!);
    }

    /// <summary>Two identical requests of one operation prepare at the same time, through the real preparer and Git. Each completed
    /// preparation parks in <see cref="LocalCommitCompetition.Gate"/> until the test releases that exact captured result, so which
    /// request is admitted first is chosen by the test from what was captured, never by timing. <paramref name="refuse"/> selects,
    /// by entry ordinal, the calls the real preparer is made to refuse with a non-current checkpoint fingerprint.</summary>
    internal async Task<LocalCommitCompetition> StartCompetitionAsync(bool runSupervisor, Func<int, bool>? refuse = null)
    {
        var gate = new PreparationGate(LocalCommitCompetition.Bound);
        var preparer = new ScriptedLocalCommitPreparer { InduceCheckpointNotCurrentFor = refuse, AfterPrepare = gate.ParkAsync };
        var (host, repository) = StartScripted(runSupervisor, decoratePreparer: inner => preparer.Attach(inner));
        var ids = await LocalCommitLineage.SeedAsync(host, Scene);

        // The owned storage root is deliberately cold: the two requests below are the FIRST use of it, so their preparations
        // contend in the production first-use initialization and neither may be refused for that reason alone.
        Assert.False(Directory.Exists(Scene.Storage.Root), "the owned storage root must not exist before the first preparation");
        var operationId = Guid.NewGuid();
        Task<HttpResponseMessage>[] requests =
            [PostAsync(host, ids.RunId, ids, operationId), PostAsync(host, ids.RunId, ids, operationId)];
        return new LocalCommitCompetition(host, repository, preparer, gate, ids, operationId, requests);
    }

    /// <summary>Asserts that the one admitted operation was admitted from <paramref name="chosen"/>'s own result and artifact, executed
    /// to exactly that commit, tree and index, and that its real cleanup then addressed exactly that artifact and returned success.</summary>
    internal async Task AssertDeliveredFromAsync(
        LocalCommitCompetition race, LocalCommitPreparationCompletion chosen, LocalCommitCleanupReport cleanup)
    {
        var facts = chosen.Result.Facts!;
        Assert.True(cleanup.Succeeded, "the real cleanup returned " + (cleanup.Result?.ToString() ?? "no result"));
        Assert.Equal(race.OperationId, cleanup.OperationId);
        Assert.Equal(chosen.ArtifactRelativePath, cleanup.ArtifactRelativePath);
        Assert.False(cleanup.RemoveOwnedLock);

        Assert.Equal(1, await OperationCountAsync(race.Host));
        Assert.Equal(1, await EventCountAsync(race.Ids.RunId, "local_commit.admitted"));
        var row = await OperationRowAsync(race.Ids.RunId);
        Assert.Equal(LocalCommitStatus.Completed, row.Status);
        Assert.Equal(race.OperationId, row.Id);
        Assert.Equal(chosen.ArtifactRelativePath, row.PreparedIndexRelativePath);
        Assert.Equal(facts.PreparedIndexSha256, row.PreparedIndexSha256);
        Assert.Equal(facts.IndexPreimageSha256, row.IndexPreimageSha256);
        Assert.Equal(facts.TreeSha, row.TreeSha);
        Assert.Equal(facts.CommitSha, row.CommitSha);
        Assert.Equal(Scene.BaselineCommit, row.ParentCommitSha);
        Assert.Equal(row.CommitSha, BranchTip());
        Assert.Equal(row.TreeSha, Scene.RunMainGit("rev-parse", row.CommitSha + "^{tree}").Trim());
        Assert.Equal(Scene.BaselineCommit, Scene.RunMainGit("rev-parse", row.CommitSha + "^").Trim());
        Assert.Equal(string.Empty, Scene.RunWorkspaceGit("diff", "--cached", "--name-only").Trim());
        Assert.False(File.Exists(Scene.IndexPath + ".lock"));
        Assert.Equal(RunLifecycle.Completed, (await RunRowAsync(race.Ids.RunId)).Lifecycle);
        Assert.Equal(WorkspaceStatus.Ready, (await WorkspaceRowAsync(race.Ids.WorkspaceId)).Status);
        Assert.False(Directory.Exists(ArtifactLeafOf(chosen)), "the recorded artifact's leaf was removed by the real cleanup");
    }

    /// <summary>The artifact leaf a captured preparation recorded. Only a Prepared completion has one.</summary>
    internal string ArtifactLeafOf(LocalCommitPreparationCompletion completion) =>
        Scene.ArtifactLeaf(completion.ArtifactRelativePath ?? throw new Xunit.Sdk.XunitException("the preparation supplied no artifact"));

    /// <summary>The names and SHA-256 of every file in an artifact leaf, to prove it is unchanged and not merely still present.</summary>
    internal static string[] ArtifactSnapshot(string leaf) =>
        Directory.GetFileSystemEntries(leaf, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(leaf, path) + ":"
                + (File.Exists(path) ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))) : "dir"))
            .Order(StringComparer.Ordinal)
            .ToArray();

    /// <summary>The captured results of two concurrent real preparations, asserted to be two distinct Prepared artifacts that both
    /// exist on disk before anything is admitted. A refusal is reported with its outcome instead of being counted.</summary>
    internal async Task<LocalCommitPreparationCompletion[]> TwoPreparedAsync(LocalCommitCompetition race)
    {
        var completed = await race.Preparer.WhenCompletedAsync(2).WaitAsync(LocalCommitCompetition.Bound);
        var outcomes = string.Join(", ", completed.Select(completion => $"#{completion.Sequence}:{completion.Result.Outcome}"));
        Assert.True(completed.All(completion => completion.IsPrepared), "both real preparations must be Prepared: " + outcomes);
        Assert.Equal(2, completed.Select(completion => completion.ArtifactRelativePath).Distinct(StringComparer.Ordinal).Count());
        var leaves = completed.Select(ArtifactLeafOf).ToArray();
        Assert.All(leaves, leaf => Assert.True(File.Exists(Path.Combine(leaf, "prepared.index")), leaf));
        Assert.Equal(leaves.Order(StringComparer.Ordinal), Scene.ArtifactLeaves().Order(StringComparer.Ordinal));
        return [.. completed];
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
