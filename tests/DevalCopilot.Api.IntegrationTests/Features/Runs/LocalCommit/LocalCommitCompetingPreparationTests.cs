using System.Net;
using System.Net.Http.Json;
using DevalCopilot.Api.Features.Runs.RequestLocalCommit;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// Two identical requests prepare at the same time through the real preparer and Git, before either is admitted (preparation precedes
/// the database transaction). Both real results are captured and shown to be Prepared with distinct artifacts before anything is
/// released; the test then releases exactly one captured result, in either order, so the admitted operation is the one the test chose
/// and its row, commit and recorded artifact are that result's own. The other preparation's artifact is never admitted: it must
/// survive the admitted operation's real cleanup, and a restart's recovery, byte for byte (documented limit: there is no janitor).
/// Refused preparations are covered by <see cref="LocalCommitRefusedPreparationTests"/>.
/// </summary>
public sealed class LocalCommitCompetingPreparationTests : LocalCommitTestBase
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_admitted_operation_executes_from_its_own_artifact_while_the_other_preparation_stays_inert(bool admitFirstCaptured)
    {
        var mainBefore = Scene.MainRepositoryFingerprint();
        using var race = await StartCompetitionAsync(runSupervisor: true);
        var prepared = await TwoPreparedAsync(race);
        race.AssertStillParked();
        var chosen = admitFirstCaptured ? prepared[0] : prepared[1];
        var other = admitFirstCaptured ? prepared[1] : prepared[0];
        var otherLeaf = ArtifactLeafOf(other);
        var otherBefore = ArtifactSnapshot(otherLeaf);

        race.Gate.Release(chosen);
        using var admittedResponse = await race.NextResponseAsync();
        Assert.Equal(HttpStatusCode.OK, admittedResponse.StatusCode);
        Assert.Equal(race.OperationId, (await admittedResponse.Content.ReadFromJsonAsync<LocalCommitOperationResponse>())!.OperationId);
        race.AssertStillParked();

        // The Completed status is durable before the cleanup returns, so the real cleanup is observed through its own signal.
        var cleanup = await race.Repository.CleanupReturned.WaitAsync(LocalCommitCompetition.Bound);
        await AssertDeliveredFromAsync(race, chosen, cleanup);
        Assert.Equal(mainBefore, Scene.MainRepositoryFingerprint());
        Assert.Equal(new[] { otherLeaf }, Scene.ArtifactLeaves());
        Assert.Equal(otherBefore, ArtifactSnapshot(otherLeaf));
        Assert.Single(race.Repository.CleanupReports);

        // The parked request reaches admission after the operation already completed: the late request is refused, never a server
        // failure, and it neither admits anything nor touches either artifact.
        race.Gate.Release(other);
        using var late = await race.NextResponseAsync();
        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
        Assert.Equal("local_commit.run_not_eligible", await ErrorCodeAsync(late));
        Assert.Equal(1, await OperationCountAsync(race.Host));
        Assert.Equal(1, await EventCountAsync(race.Ids.RunId, "local_commit.admitted"));
        Assert.Equal(chosen.ArtifactRelativePath, (await OperationRowAsync(race.Ids.RunId)).PreparedIndexRelativePath);
        Assert.Equal(new[] { otherLeaf }, Scene.ArtifactLeaves());
        Assert.Equal(otherBefore, ArtifactSnapshot(otherLeaf));
        Assert.Single(race.Repository.CleanupReports);
        Assert.Equal(mainBefore, Scene.MainRepositoryFingerprint());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_restart_after_the_competition_recovers_the_admitted_operation_and_removes_only_its_recorded_artifact(
        bool admitFirstCaptured)
    {
        var mainBefore = Scene.MainRepositoryFingerprint();
        using var race = await StartCompetitionAsync(runSupervisor: false);
        var ids = race.Ids;
        var prepared = await TwoPreparedAsync(race);
        var chosen = admitFirstCaptured ? prepared[0] : prepared[1];
        var other = admitFirstCaptured ? prepared[1] : prepared[0];
        var recordedLeaf = ArtifactLeafOf(chosen);
        var otherLeaf = ArtifactLeafOf(other);
        var otherBefore = ArtifactSnapshot(otherLeaf);

        race.Gate.Release(chosen);
        using (var admittedResponse = await race.NextResponseAsync())
        {
            Assert.Equal(HttpStatusCode.OK, admittedResponse.StatusCode);
        }

        race.AssertStillParked();
        var admitted = await OperationRowAsync(ids.RunId);
        Assert.Equal(LocalCommitStatus.Prepared, admitted.Status);
        Assert.Equal(race.OperationId, admitted.Id);
        Assert.Equal(chosen.ArtifactRelativePath, admitted.PreparedIndexRelativePath);
        Assert.Equal(chosen.Result.Facts!.CommitSha, admitted.CommitSha);
        Assert.Equal(recordedLeaf, Scene.ArtifactLeaf(admitted.PreparedIndexRelativePath));
        Assert.Equal(2, Scene.ArtifactLeaves().Length);

        // While the workspace is reserved by the admitted operation, the other request is the existing replay of that recorded
        // operation; it admits nothing and touches neither artifact.
        race.Gate.Release(other);
        using (var replay = await race.NextResponseAsync())
        {
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Equal(race.OperationId, (await replay.Content.ReadFromJsonAsync<LocalCommitOperationResponse>())!.OperationId);
        }

        Assert.Equal(1, await OperationCountAsync(race.Host));
        Assert.Equal(chosen.ArtifactRelativePath, (await OperationRowAsync(ids.RunId)).PreparedIndexRelativePath);
        Assert.True(File.Exists(Path.Combine(recordedLeaf, "prepared.index")));
        Assert.Equal(otherBefore, ArtifactSnapshot(otherLeaf));
        race.Host.Dispose();

        var (_, script) = StartScripted(supervisor: false);

        var recovered = await OperationRowAsync(ids.RunId);
        Assert.Equal(LocalCommitStatus.Interrupted, recovered.Status);
        Assert.Equal("local_commit.interrupted_before_execution", recovered.OutcomeReasonCode);
        Assert.Equal(chosen.ArtifactRelativePath, recovered.PreparedIndexRelativePath);
        Assert.Equal(WorkspaceStatus.Ready, (await WorkspaceRowAsync(ids.WorkspaceId)).Status);
        Assert.Equal(Scene.BaselineCommit, BranchTip());
        Assert.Equal(mainBefore, Scene.MainRepositoryFingerprint());

        // Recovery's own real cleanup addressed the recorded artifact only; the other successful preparation stays inert.
        var cleanup = Assert.Single(script.CleanupReports);
        Assert.True(cleanup.Succeeded);
        Assert.Equal(race.OperationId, cleanup.OperationId);
        Assert.Equal(chosen.ArtifactRelativePath, cleanup.ArtifactRelativePath);
        Assert.False(Directory.Exists(recordedLeaf), "only the recorded artifact's leaf was removed");
        Assert.Equal(new[] { otherLeaf }, Scene.ArtifactLeaves());
        Assert.Equal(otherBefore, ArtifactSnapshot(otherLeaf));
    }
}
