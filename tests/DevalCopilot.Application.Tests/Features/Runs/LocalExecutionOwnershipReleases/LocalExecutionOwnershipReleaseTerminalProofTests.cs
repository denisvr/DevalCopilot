using DevalCopilot.Application.Features.Projects.Policies.WorkspaceExecutions;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionOwnershipRelease;
using DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences.ScriptedResultKind;
using Rig = DevalCopilot.Application.Tests.Features.Runs.LocalExecutionOwnershipReleases.LocalExecutionOwnershipReleaseRig;
using Step = DevalCopilot.Domain.Features.Runs.LocalExecutionStepKind;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionOwnershipReleases;

/// <summary>
/// ADR-0065 correction LQR-R2: the terminal result, source and mode proof of a candidate, of the locked Human command and of a stored
/// receipt. Before any release, a result that is not consistent under the existing Domain result policies (a clean-exit outcome with a
/// timed-out process, a Completed status, an unknown or pre-invocation outcome, a different Agent checkpoint fingerprint, a verification
/// status or completion fingerprint that VerificationExecution.Complete could not have derived, or a run that is not ManualAgent) is
/// Unavailable, the confirmed command is refused with no receipt and no event, and the hold stays; the legitimate recognized failure
/// combinations (SourceChanged and the other post-invocation failure outcomes, a source-changed or cancelled verification) stay
/// releasable; a stored receipt whose canonical confirmation no longer binds the proved facts and the recorded expiry reads Unavailable.
/// </summary>
public sealed class LocalExecutionOwnershipReleaseTerminalProofTests : IAsyncLifetime
{
    private static readonly string OtherFingerprint = new('9', 64);

    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    private sealed record Change(Step Kind, string Table, string Sql, Func<LocalExecutionOwnershipReleaseTamper.Ids, object[]> Parameters);

    private static readonly Dictionary<string, Change> Refusals = new()
    {
        ["clean-exit-outcome-with-timed-out-process"] = new(Step.Implementation, "attempts",
            "UPDATE attempts SET \"AgentOutcome\" = 'Implemented' WHERE \"Id\" = {0}", ids => [ids.OperationId]),
        ["agent-checkpoint-fingerprint-differs-from-the-step"] = new(Step.Implementation, "attempts",
            "UPDATE attempts SET \"AgentCheckpointFingerprintSha256\" = {0} WHERE \"Id\" = {1}", ids => [OtherFingerprint, ids.OperationId]),
        ["run-is-not-a-manual-agent-run"] = new(Step.Implementation, "runs",
            "UPDATE runs SET \"ExecutionMode\" = 1 WHERE \"Id\" = {0}", ids => [ids.RunId]),
        ["status-completed-with-a-timed-out-process"] = new(Step.Implementation, "attempts",
            "UPDATE attempts SET \"Status\" = 'Completed' WHERE \"Id\" = {0}", ids => [ids.OperationId]),
        ["unknown-agent-outcome"] = new(Step.Implementation, "attempts",
            "UPDATE attempts SET \"AgentOutcome\" = 'Unrecognized' WHERE \"Id\" = {0}", ids => [ids.OperationId]),
        ["absent-agent-outcome"] = new(Step.Implementation, "attempts",
            "UPDATE attempts SET \"AgentOutcome\" = NULL WHERE \"Id\" = {0}", ids => [ids.OperationId]),
        ["pre-invocation-agent-outcome"] = new(Step.Implementation, "attempts",
            "UPDATE attempts SET \"AgentOutcome\" = 'InputAlreadyImplemented' WHERE \"Id\" = {0}", ids => [ids.OperationId]),
        ["review-approved-with-a-timed-out-process"] = new(Step.CodeReview, "attempts",
            "UPDATE attempts SET \"AgentOutcome\" = 'ReviewApproved' WHERE \"Id\" = {0}", ids => [ids.OperationId]),
        ["verification-failed-status-with-a-timed-out-process"] = new(Step.Verification, "verification_executions",
            "UPDATE verification_executions SET \"Status\" = 'Failed' WHERE \"Id\" = {0}", ids => [ids.OperationId]),
        ["verification-completion-fingerprint-differs-from-a-timeout"] = new(Step.Verification, "verification_executions",
            "UPDATE verification_executions SET \"CompletionFingerprintSha256\" = {0} WHERE \"Id\" = {1}", ids => [OtherFingerprint, ids.OperationId]),
        ["verification-completion-fingerprint-absent"] = new(Step.Verification, "verification_executions",
            "UPDATE verification_executions SET \"CompletionFingerprintSha256\" = NULL WHERE \"Id\" = {0}", ids => [ids.OperationId]),
        ["verification-source-changed-with-a-matching-completion"] = new(Step.Verification, "verification_executions",
            "UPDATE verification_executions SET \"Status\" = 'SourceChanged' WHERE \"Id\" = {0}", ids => [ids.OperationId]),
        ["verification-run-is-not-a-manual-agent-run"] = new(Step.Verification, "runs",
            "UPDATE runs SET \"ExecutionMode\" = 1 WHERE \"Id\" = {0}", ids => [ids.RunId]),
    };

    public static TheoryData<string> RefusalNames
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var name in Refusals.Keys)
            {
                data.Add(name);
            }

            return data;
        }
    }

    private async Task<(int Releases, int Events)> RowsAsync()
    {
        await using var context = fixture.CreateContext();
        return (
            await context.LocalExecutionOwnershipReleases.CountAsync(),
            await context.Events.CountAsync(runEvent => runEvent.EventType == RunEventType.LocalExecutionOwnershipReleased));
    }

    [Theory]
    [MemberData(nameof(RefusalNames))]
    public async Task An_inconsistent_terminal_result_is_never_a_candidate_and_a_confirmed_command_releases_nothing(string name)
    {
        var change = Refusals[name];
        var ended = await Rig.EndAsync(fixture, change.Kind, false);
        var scene = ended.Scene;
        var before = await scene.ReadReleaseAsync(ended.Target.Id);
        Assert.Equal(LocalExecutionOwnershipReleaseStatus.Available, before.Status);
        var confirmed = scene.ConfirmedCommand(before);
        var ids = await TidyIdsAsync(ended);

        await LocalExecutionOwnershipReleaseTamper.ApplyAsync(fixture, change.Table, change.Sql, change.Parameters(ids));

        var read = await scene.ReadReleaseAsync(ended.Target.Id);
        Assert.Equal(LocalExecutionOwnershipReleaseStatus.Unavailable, read.Status);
        Assert.Null(read.Candidate);
        var refused = await scene.ReleaseAsync(confirmed);
        Assert.True(refused.IsFailure, name);
        Assert.Equal((0, 0), await RowsAsync());
        Assert.NotEqual(WorkspaceExecutionStatus.Idle, (await Rig.ReadOwnershipAsync(fixture, scene)).Workspace);
        Assert.True(await LocalExecutionOwnershipReleaseTamper.ProbeIsBlockedAsync(fixture, scene.ProjectId));
    }

    /// <summary>The identities the pre-release tampers need; there is no receipt yet, so the witness and observation come from the
    /// recorded facts and the receipt and event are placeholders.</summary>
    private async Task<LocalExecutionOwnershipReleaseTamper.Ids> TidyIdsAsync(LocalExecutionOwnershipReleaseRig.Ended ended)
    {
        await using var context = fixture.CreateContext();
        var witness = await context.LocalExecutionDispatchWitnesses.AsNoTracking().SingleAsync(candidate => candidate.StepId == ended.Target.Id);
        var observation = await context.LocalExecutionTerminationObservations.AsNoTracking().SingleAsync(candidate => candidate.WitnessId == witness.Id);
        return new LocalExecutionOwnershipReleaseTamper.Ids(
            ended.Scene.RunId, ended.Scene.ProjectId, ended.Scene.WorkspaceId, witness.SequenceId, ended.Target.Id, witness.OperationId, witness.Id,
            observation.Id, Guid.Empty, Guid.Empty, Guid.NewGuid(), ended.Target.SourceCheckpointId, Guid.NewGuid(), witness.AdmissionId);
    }

    // ---- the legitimate recognized failure combinations stay releasable ---------------------------------------------------------------

    [Theory]
    [InlineData("SourceChanged")]
    [InlineData("ProviderInvocationFailed")]
    [InlineData("CheckpointEvidenceUnavailable")]
    [InlineData("VerificationEvidenceChanged")]
    public async Task Every_post_invocation_failure_outcome_of_an_agent_attempt_stays_releasable(string outcome)
    {
        var ended = await Rig.EndAsync(fixture, Step.Implementation, false);
        var scene = ended.Scene;
        var ids = await TidyIdsAsync(ended);
        await LocalExecutionOwnershipReleaseTamper.ApplyAsync(
            fixture, "attempts", "UPDATE attempts SET \"AgentOutcome\" = {0} WHERE \"Id\" = {1}", outcome, ids.OperationId);

        var read = await scene.ReadReleaseAsync(ended.Target.Id);
        Assert.Equal(LocalExecutionOwnershipReleaseStatus.Available, read.Status);
        await scene.ReleaseStepAsync(ended.Target.Id);

        Assert.Equal(LocalExecutionOwnershipReleaseStatus.Released, (await scene.ReadReleaseAsync(ended.Target.Id)).Status);
        Assert.Equal(WorkspaceExecutionStatus.Idle, (await Rig.ReadOwnershipAsync(fixture, scene)).Workspace);
        Assert.False(await LocalExecutionOwnershipReleaseTamper.ProbeIsBlockedAsync(fixture, scene.ProjectId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_source_changed_verification_with_a_timed_out_or_cancelled_process_stays_releasable(bool cancelled)
    {
        var script = new LocalExecutionStepScript().Verify(1, 0, cancelled ? VerificationCancelledTerminated : VerificationTimedOutTerminated);
        var ended = await Rig.DriveAsync(fixture, script);
        var scene = ended.Scene;
        var ids = await TidyIdsAsync(ended);
        await LocalExecutionOwnershipReleaseTamper.ApplyAsync(
            fixture, "verification_executions",
            "UPDATE verification_executions SET \"Status\" = 'SourceChanged', \"CompletionFingerprintSha256\" = {0} WHERE \"Id\" = {1}",
            OtherFingerprint, ids.OperationId);

        var read = await scene.ReadReleaseAsync(ended.Target.Id);
        Assert.Equal(LocalExecutionOwnershipReleaseStatus.Available, read.Status);
        Assert.Equal(cancelled ? ProcessOutcome.Cancelled : ProcessOutcome.TimedOut, read.Candidate!.Facts.ProcessOutcome);
        await scene.ReleaseStepAsync(ended.Target.Id);

        Assert.Equal(WorkspaceExecutionStatus.Idle, (await Rig.ReadOwnershipAsync(fixture, scene)).Workspace);
        Assert.False(await LocalExecutionOwnershipReleaseTamper.ProbeIsBlockedAsync(fixture, scene.ProjectId));
    }

    [Fact]
    public async Task A_cancelled_verification_and_a_cancelled_agent_attempt_are_releasable_through_the_same_proof()
    {
        var verification = await Rig.DriveAsync(fixture, new LocalExecutionStepScript().Verify(1, 0, VerificationCancelledTerminated));
        Assert.Equal(LocalExecutionOwnershipReleaseStatus.Available, (await verification.Scene.ReadReleaseAsync(verification.Target.Id)).Status);
        await verification.Scene.ReleaseStepAsync(verification.Target.Id);
        Assert.Equal(WorkspaceExecutionStatus.Idle, (await Rig.ReadOwnershipAsync(fixture, verification.Scene)).Workspace);

        await using var other = new SqliteDatabaseFixture();
        await other.InitializeAsync();
        var agent = await Rig.EndAsync(other, Step.CodeReview, false, ProviderCancelledTerminated);
        Assert.Equal(LocalExecutionOwnershipReleaseStatus.Available, (await agent.Scene.ReadReleaseAsync(agent.Target.Id)).Status);
        await agent.Scene.ReleaseStepAsync(agent.Target.Id);
        Assert.Equal(WorkspaceExecutionStatus.Idle, (await Rig.ReadOwnershipAsync(other, agent.Scene)).Workspace);
    }

    // ---- the stored receipt binds the proved facts and the recorded expiry ---------------------------------------------------------------

    [Theory]
    [InlineData("substituted-digest")]
    [InlineData("substituted-expiry")]
    public async Task A_stored_receipt_whose_canonical_confirmation_no_longer_binds_the_facts_is_never_shown_as_released(string name)
    {
        var ended = await Rig.EndAsync(fixture, Step.Implementation, false);
        var scene = ended.Scene;
        var confirmed = scene.ConfirmedCommand(await scene.ReadReleaseAsync(ended.Target.Id));
        await scene.ReleaseStepAsync(ended.Target.Id);
        var ids = await LocalExecutionOwnershipReleaseTamper.ReadIdsAsync(fixture, scene, ended.Target);
        if (name == "substituted-digest")
        {
            await LocalExecutionOwnershipReleaseTamper.ApplyAsync(
                fixture, "local_execution_ownership_releases",
                "UPDATE local_execution_ownership_releases SET \"ConfirmationSha256\" = {0} WHERE \"Id\" = {1}", new string('a', 64), ids.ReleaseId);
        }
        else
        {
            // One second later keeps the receipt chronologically coherent and inside the two-minute lifetime, but it is no longer the expiry
            // the confirmation digest bound.
            await LocalExecutionOwnershipReleaseTamper.ApplyAsync(
                fixture, "local_execution_ownership_releases",
                "UPDATE local_execution_ownership_releases SET \"ConfirmationExpiresAtUtc\" = strftime('%Y-%m-%d %H:%M:%S', \"ConfirmationExpiresAtUtc\", '-1 seconds') "
                + "|| '+00:00' WHERE \"Id\" = {0}", ids.ReleaseId);
        }

        var read = await scene.ReadReleaseAsync(ended.Target.Id);

        Assert.Equal(LocalExecutionOwnershipReleaseStatus.Unavailable, read.Status);
        Assert.Null(read.Release);
        Assert.True((await scene.ReleaseAsync(confirmed)).IsFailure);
        Assert.Equal(1, (await RowsAsync()).Releases);
    }

    // ---- the vocabulary the database fence uses is the one the Domain policy derives -------------------------------------------------

    [Fact]
    public async Task The_fence_vocabulary_of_releasable_agent_outcomes_is_the_one_the_Domain_result_policy_derives()
    {
        await using var context = fixture.CreateContext();
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'trigger' AND name = 'trg_local_execution_guard_attempts';";
        var trigger = (string)(await command.ExecuteScalarAsync())!;
        var vocabulary = LocalExecutionReleaseTerminal.AgentOutcomes.Select(outcome => outcome.ToString()).Order(StringComparer.Ordinal).ToArray();

        foreach (var outcome in Enum.GetValues<AgentOutcome>())
        {
            var named = trigger.Contains("'" + outcome + "'", StringComparison.Ordinal);
            Assert.Equal(vocabulary.Contains(outcome.ToString()), named);
        }
    }
}
