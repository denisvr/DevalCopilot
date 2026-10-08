using System.Net;
using System.Net.Http.Json;
using DevalCopilot.Api.Features.Runs.RequestLocalCommit;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// Two identical requests prepare at the same time, before either is admitted (preparation precedes the database transaction). The one
/// that reaches admission first is the admitted operation; the other is parked after its preparation finished and is released only
/// after the admitted operation has been worked on. Each preparation owns its artifact leaf, so the admitted operation's recorded
/// artifact is its own, executes, and is recovered after a restart exactly as before; the other preparation's artifact is inert.
/// </summary>
public sealed class LocalCommitCompetingPreparationTests : LocalCommitTestBase
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);

    private sealed record Competition(
        LocalCommitHost Host,
        LocalCommitLineageIds Ids,
        Guid OperationId,
        Action ReleaseParked,
        Task<HttpResponseMessage> Parked,
        HttpResponseMessage Admitted);

    private async Task<Competition> CompeteAsync(bool runSupervisor)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = 0;
        var preparer = new ScriptedLocalCommitPreparer
        {
            // The request whose preparation finishes first parks here; the other one is admitted meanwhile.
            AfterPrepare = async () =>
            {
                if (Interlocked.Increment(ref finished) == 1)
                {
                    await release.Task.WaitAsync(Bound);
                }
            },
        };
        var host = StartHost(runSupervisor, decoratePreparer: inner => preparer.Attach(inner));
        var ids = await LocalCommitLineage.SeedAsync(host, Scene);
        var operationId = Guid.NewGuid();
        var one = PostAsync(host, ids.RunId, ids, operationId);
        var two = PostAsync(host, ids.RunId, ids, operationId);

        var first = await Task.WhenAny(one, two).WaitAsync(Bound);
        var admitted = await first;
        Assert.Equal(HttpStatusCode.OK, admitted.StatusCode);
        var parked = ReferenceEquals(first, one) ? two : one;
        Assert.False(parked.IsCompleted, "the other request is still parked after its own preparation");
        return new Competition(host, ids, operationId, () => release.TrySetResult(), parked, admitted);
    }

    [Fact]
    public async Task The_admitted_operation_executes_from_its_own_artifact_while_the_other_preparation_stays_inert()
    {
        var mainBefore = Scene.MainRepositoryFingerprint();
        var race = await CompeteAsync(runSupervisor: true);
        using var host = race.Host;
        var admitted = (await race.Admitted.Content.ReadFromJsonAsync<LocalCommitOperationResponse>())!;
        Assert.Equal(race.OperationId, admitted.OperationId);

        await WaitForAsync(host, race.Ids.RunId, status => status.Operation?.Status == "Completed");
        race.ReleaseParked();
        using var replay = await race.Parked.WaitAsync(Bound);

        // The parked request reaches admission after the operation already completed, so it is the existing late-request
        // arbitration (a refusal, not a replay); either way it is a defined answer and never a server failure.
        Assert.True(replay.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict, replay.StatusCode.ToString());
        Assert.Equal(1, await OperationCountAsync(host));
        Assert.Equal(1, (await EventTypesAsync(host, race.Ids.RunId)).Count(type => type == "local_commit.admitted"));
        var row = await OperationRowAsync(race.Ids.RunId);
        Assert.Equal(LocalCommitStatus.Completed, row.Status);
        Assert.Equal(row.CommitSha, BranchTip());
        Assert.Equal(RunLifecycle.Completed, (await RunRowAsync(race.Ids.RunId)).Lifecycle);
        Assert.Equal(WorkspaceStatus.Ready, (await WorkspaceRowAsync(race.Ids.WorkspaceId)).Status);
        Assert.Equal(mainBefore, Scene.MainRepositoryFingerprint());

        // Terminal cleanup removed the recorded artifact only. The other preparation's artifact was never admitted, is not
        // referenced by any row, and is left inert (documented limit: there is no janitor).
        Assert.False(Directory.Exists(Scene.ArtifactLeaf(row.PreparedIndexRelativePath)));
        var inert = Assert.Single(Scene.StorageLeaves());
        Assert.True(File.Exists(Path.Combine(inert, "prepared.index")));
        Assert.NotEqual(Scene.ArtifactLeaf(row.PreparedIndexRelativePath), inert);
    }

    [Fact]
    public async Task A_restart_after_the_competition_recovers_the_admitted_operation_and_removes_only_its_recorded_artifact()
    {
        var mainBefore = Scene.MainRepositoryFingerprint();
        var race = await CompeteAsync(runSupervisor: false);
        var ids = race.Ids;
        race.ReleaseParked();
        using (var replay = await race.Parked.WaitAsync(Bound))
        {
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        }

        var admitted = await OperationRowAsync(ids.RunId);
        Assert.Equal(LocalCommitStatus.Prepared, admitted.Status);
        var recordedLeaf = Scene.ArtifactLeaf(admitted.PreparedIndexRelativePath);
        Assert.True(File.Exists(Path.Combine(recordedLeaf, "prepared.index")));
        Assert.Equal(2, Scene.StorageLeaves().Length);
        race.Host.Dispose();

        using var restarted = StartHost(runSupervisor: false);

        var recovered = await OperationRowAsync(ids.RunId);
        Assert.Equal(LocalCommitStatus.Interrupted, recovered.Status);
        Assert.Equal("local_commit.interrupted_before_execution", recovered.OutcomeReasonCode);
        Assert.Equal(WorkspaceStatus.Ready, (await WorkspaceRowAsync(ids.WorkspaceId)).Status);
        Assert.Equal(Scene.BaselineCommit, BranchTip());
        Assert.Equal(mainBefore, Scene.MainRepositoryFingerprint());
        Assert.False(Directory.Exists(recordedLeaf), "only the recorded artifact's leaf was removed");
        var inert = Assert.Single(Scene.StorageLeaves());
        Assert.NotEqual(recordedLeaf, inert);
        Assert.True(File.Exists(Path.Combine(inert, "prepared.index")));
    }
}
