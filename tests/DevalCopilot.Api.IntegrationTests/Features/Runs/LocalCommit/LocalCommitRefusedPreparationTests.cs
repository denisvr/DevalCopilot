using System.Net;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// Competing preparations where the REAL preparer refuses one or both. The refusal is induced in the owned fixture by giving the
/// selected call a checkpoint fingerprint that cannot be current, so the production preparer itself returns
/// <c>CheckpointNotCurrent</c> with no facts; no result is faked and this does not reconstruct any historical failure or exercise a
/// provider. A refused preparation supplies no artifact authority, is never admitted, and in either completion order the other,
/// successful preparation is admitted and executes or recovers exactly as in the all-success case. Which request is admitted first is
/// chosen by releasing the captured results, never by timing.
/// </summary>
public sealed class LocalCommitRefusedPreparationTests : LocalCommitTestBase
{
    private const string RefusalCode = "local_commit.refused.checkpoint_not_current";

    private async Task<(LocalCommitPreparationCompletion Refused, LocalCommitPreparationCompletion Prepared)> OneRefusedOnePreparedAsync(
        LocalCommitCompetition race)
    {
        var completed = await race.Preparer.WhenCompletedAsync(2).WaitAsync(LocalCommitCompetition.Bound);
        var refused = Assert.Single(completed, completion => !completion.IsPrepared);
        var prepared = Assert.Single(completed, completion => completion.IsPrepared);
        Assert.Equal(LocalCommitPreparationOutcome.CheckpointNotCurrent, refused.Result.Outcome);
        Assert.Null(refused.Result.Facts);
        Assert.Null(refused.ArtifactRelativePath);
        Assert.Equal(0, race.Preparer.Faulted);

        // The refusal left no artifact behind and the successful preparation's artifact is the only one on disk.
        Assert.Equal(new[] { ArtifactLeafOf(prepared) }, Scene.ArtifactLeaves());
        Assert.True(race.Preparer.WhenPreparedAsync(1).IsCompleted);
        Assert.False(race.Preparer.WhenPreparedAsync(2).IsCompleted, "a refusal was counted as a Prepared result");
        return (refused, prepared);
    }

    private static async Task AssertRefusalAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(RefusalCode, await ErrorCodeAsync(response));
    }

    private async Task AssertNothingAdmittedAsync(LocalCommitCompetition race)
    {
        Assert.Equal(0, await OperationCountAsync(race.Host));
        Assert.DoesNotContain(
            await EventTypeRowsAsync(race.Ids.RunId), type => type.StartsWith("local_commit.", StringComparison.Ordinal));
        Assert.Equal(WorkspaceStatus.Ready, (await WorkspaceRowAsync(race.Ids.WorkspaceId)).Status);
        Assert.Equal(RunLifecycle.Running, (await RunRowAsync(race.Ids.RunId)).Lifecycle);
        Assert.Equal(Scene.BaselineCommit, BranchTip());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_refused_preparation_supplies_no_artifact_and_the_successful_one_executes_in_either_completion_order(
        bool refusedReleasedFirst)
    {
        var mainBefore = Scene.MainRepositoryFingerprint();
        using var race = await StartCompetitionAsync(runSupervisor: true, refuse: ordinal => ordinal == 1);
        var (refused, prepared) = await OneRefusedOnePreparedAsync(race);
        race.AssertStillParked();

        if (refusedReleasedFirst)
        {
            race.Gate.Release(refused);
            using var refusal = await race.NextResponseAsync();
            await AssertRefusalAsync(refusal);
            await AssertNothingAdmittedAsync(race);
            race.AssertStillParked();
        }

        race.Gate.Release(prepared);
        using var admittedResponse = await race.NextResponseAsync();
        Assert.Equal(HttpStatusCode.OK, admittedResponse.StatusCode);
        var cleanup = await race.Repository.CleanupReturned.WaitAsync(LocalCommitCompetition.Bound);
        await AssertDeliveredFromAsync(race, prepared, cleanup);

        if (!refusedReleasedFirst)
        {
            race.Gate.Release(refused);
            using var refusal = await race.NextResponseAsync();
            await AssertRefusalAsync(refusal);
        }

        // No other successful artifact ever existed, so an empty artifact set after the recorded one's cleanup is exact.
        Assert.Empty(Scene.ArtifactLeaves());
        Assert.Equal(1, await OperationCountAsync(race.Host));
        Assert.Equal(1, await EventCountAsync(race.Ids.RunId, "local_commit.admitted"));
        Assert.Single(race.Repository.CleanupReports);
        Assert.Equal(mainBefore, Scene.MainRepositoryFingerprint());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_restart_recovers_the_successful_preparation_and_the_refused_one_never_owned_an_artifact(
        bool refusedReleasedFirst)
    {
        var mainBefore = Scene.MainRepositoryFingerprint();
        using var race = await StartCompetitionAsync(runSupervisor: false, refuse: ordinal => ordinal == 1);
        var (refused, prepared) = await OneRefusedOnePreparedAsync(race);
        var recordedLeaf = ArtifactLeafOf(prepared);

        if (refusedReleasedFirst)
        {
            race.Gate.Release(refused);
            using var refusal = await race.NextResponseAsync();
            await AssertRefusalAsync(refusal);
            await AssertNothingAdmittedAsync(race);
        }

        race.Gate.Release(prepared);
        using (var admittedResponse = await race.NextResponseAsync())
        {
            Assert.Equal(HttpStatusCode.OK, admittedResponse.StatusCode);
        }

        if (!refusedReleasedFirst)
        {
            race.Gate.Release(refused);
            using var refusal = await race.NextResponseAsync();
            await AssertRefusalAsync(refusal);
        }

        var admitted = await OperationRowAsync(race.Ids.RunId);
        Assert.Equal(LocalCommitStatus.Prepared, admitted.Status);
        Assert.Equal(prepared.ArtifactRelativePath, admitted.PreparedIndexRelativePath);
        Assert.Equal(1, await OperationCountAsync(race.Host));
        Assert.Equal(new[] { recordedLeaf }, Scene.ArtifactLeaves());
        race.Host.Dispose();

        var (_, script) = StartScripted(supervisor: false);

        var recovered = await OperationRowAsync(race.Ids.RunId);
        Assert.Equal(LocalCommitStatus.Interrupted, recovered.Status);
        Assert.Equal("local_commit.interrupted_before_execution", recovered.OutcomeReasonCode);
        Assert.Equal(WorkspaceStatus.Ready, (await WorkspaceRowAsync(race.Ids.WorkspaceId)).Status);
        Assert.Equal(Scene.BaselineCommit, BranchTip());
        Assert.Equal(mainBefore, Scene.MainRepositoryFingerprint());
        var cleanup = Assert.Single(script.CleanupReports);
        Assert.True(cleanup.Succeeded);
        Assert.Equal(prepared.ArtifactRelativePath, cleanup.ArtifactRelativePath);
        Assert.False(Directory.Exists(recordedLeaf));
        Assert.Empty(Scene.ArtifactLeaves());
    }

    [Fact]
    public async Task When_both_preparations_are_refused_nothing_is_admitted_and_no_artifact_ref_or_index_changes()
    {
        var mainBefore = Scene.MainRepositoryFingerprint();
        var indexBefore = File.ReadAllBytes(Scene.IndexPath);
        using var race = await StartCompetitionAsync(runSupervisor: true, refuse: _ => true);

        var completed = await race.Preparer.WhenCompletedAsync(2).WaitAsync(LocalCommitCompetition.Bound);

        Assert.All(completed, completion =>
        {
            Assert.False(completion.IsPrepared);
            Assert.Equal(LocalCommitPreparationOutcome.CheckpointNotCurrent, completion.Result.Outcome);
            Assert.Null(completion.Result.Facts);
            Assert.Null(completion.ArtifactRelativePath);
        });
        Assert.False(race.Preparer.WhenPreparedAsync(1).IsCompleted, "a refusal was counted as a Prepared result");
        Assert.Empty(Scene.ArtifactLeaves());
        race.AssertStillParked();

        foreach (var completion in completed)
        {
            race.Gate.Release(completion);
            using var refusal = await race.NextResponseAsync();
            await AssertRefusalAsync(refusal);
        }

        await AssertNothingAdmittedAsync(race);
        Assert.Empty(race.Repository.CleanupReports);
        Assert.Empty(Scene.ArtifactLeaves());
        Assert.Equal(indexBefore, File.ReadAllBytes(Scene.IndexPath));
        Assert.False(File.Exists(Scene.IndexPath + ".lock"));
        Assert.Equal(mainBefore, Scene.MainRepositoryFingerprint());
    }
}
