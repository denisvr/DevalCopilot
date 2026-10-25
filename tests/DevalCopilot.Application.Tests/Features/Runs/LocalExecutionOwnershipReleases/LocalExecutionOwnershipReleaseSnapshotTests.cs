using DevalCopilot.Application.Features.Projects.Policies.WorkspaceExecutions;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionOwnershipRelease;
using DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Rig = DevalCopilot.Application.Tests.Features.Runs.LocalExecutionOwnershipReleases.LocalExecutionOwnershipReleaseRig;
using Step = DevalCopilot.Domain.Features.Runs.LocalExecutionStepKind;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionOwnershipReleases;

/// <summary>
/// ADR-0066 (correction LQR-R1 and LQR-R2): the release row stores a typed snapshot of every canonical confirmed fact, including the
/// concrete Agent outcome or verification completion fingerprint. Before the release, a coherent-but-different recognized result changes
/// the candidate's digest, so the old confirmation is refused with no receipt and no event; after it, the same change leaves the
/// operation held and unreadable, and the old request cannot replay. The exact two-minute lifetime is a storage constraint at tick
/// precision. A substituted checksum alone, with the entire snapshot intact, is the one narrow exception: it is Unavailable and refuses
/// replay, but it never reacquires ownership.
/// </summary>
public sealed class LocalExecutionOwnershipReleaseSnapshotTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    private async Task<(int Releases, int Events)> RowsAsync()
    {
        await using var context = fixture.CreateContext();
        return (
            await context.LocalExecutionOwnershipReleases.CountAsync(),
            await context.Events.CountAsync(runEvent => runEvent.EventType == RunEventType.LocalExecutionOwnershipReleased));
    }

    /// <summary>The read, workspace, capacity, abandonment and actual SQL write seams, each asserted on its own.</summary>
    private async Task AssertHeldAsync(LocalExecutionSequenceTestScene scene, Step kind, string because)
    {
        var ownership = await Rig.ReadOwnershipAsync(fixture, scene);
        Assert.True(ownership.Workspace != WorkspaceExecutionStatus.Idle, $"workspace must stay held ({because})");
        if (Rig.IsMutating(kind))
        {
            // A null count is the capacity policy's fail-closed Unavailable reading; only an Available reading with no owner would admit.
            Assert.True(ownership.MutatingOwners is null or >= 1, $"capacity must not admit past the owner ({because}), owners={ownership.MutatingOwners}");
        }

        Assert.True(ownership.AbandonmentBlocked, $"abandonment must stay blocked ({because})");
        Assert.True(await LocalExecutionOwnershipReleaseTamper.ProbeIsBlockedAsync(fixture, scene.ProjectId), $"the live-fence write seam must refuse ({because})");
        Assert.True(
            await LocalExecutionOwnershipReleaseTamper.ProbeRunAbandonmentIsBlockedAsync(fixture, scene.RunId),
            $"the abandonment write seam must refuse ({because})");
    }

    private async Task AssertFreeAsync(LocalExecutionSequenceTestScene scene, string because)
    {
        var ownership = await Rig.ReadOwnershipAsync(fixture, scene);
        Assert.True(ownership.Workspace == WorkspaceExecutionStatus.Idle, $"workspace must stay free ({because}), was {ownership.Workspace}");
        Assert.True(ownership.MutatingOwners is 0, $"capacity must be Available with no owner ({because}), owners={ownership.MutatingOwners}");
        Assert.False(ownership.AbandonmentBlocked, $"abandonment must not be blocked ({because})");
        Assert.False(await LocalExecutionOwnershipReleaseTamper.ProbeIsBlockedAsync(fixture, scene.ProjectId), $"the live-fence write seam must accept ({because})");
        Assert.False(
            await LocalExecutionOwnershipReleaseTamper.ProbeRunAbandonmentIsBlockedAsync(fixture, scene.RunId),
            $"the abandonment write seam must accept ({because})");
    }

    // ---- LQR-R2: the concrete terminal result is bound, not only its coherence class ----------------------------------------------------

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task An_exact_confirmation_binds_the_concrete_terminal_result(bool verification, bool alreadyReleased)
    {
        var kind = verification ? Step.Verification : Step.Implementation;
        var ended = await Rig.EndAsync(fixture, kind, false);
        var scene = ended.Scene;
        var operationId = ended.Target.OperationId!.Value;
        if (verification)
        {
            // A legitimate SourceChanged verification: a valid completion fingerprint that differs from the pinned source's.
            await LocalExecutionOwnershipReleaseTamper.ApplyAsync(
                fixture, "verification_executions",
                "UPDATE verification_executions SET \"Status\" = 'SourceChanged', \"CompletionFingerprintSha256\" = {0} WHERE \"Id\" = {1}",
                new string('9', 64), operationId);
        }

        var before = await scene.ReadReleaseAsync(ended.Target.Id);
        Assert.Equal(LocalExecutionOwnershipReleaseStatus.Available, before.Status);
        var confirmed = scene.ConfirmedCommand(before);
        if (alreadyReleased)
        {
            Assert.True((await scene.ReleaseAsync(confirmed)).IsSuccess);
        }

        // The other legitimate recognized result: an Agent failure outcome becomes SourceChanged, or the completion fingerprint becomes
        // another valid SHA-256. Both stay coherent under the Domain policies; neither is what the Human confirmed.
        await LocalExecutionOwnershipReleaseTamper.ApplyAsync(
            fixture,
            verification ? "verification_executions" : "attempts",
            verification
                ? "UPDATE verification_executions SET \"CompletionFingerprintSha256\" = {0} WHERE \"Id\" = {1}"
                : "UPDATE attempts SET \"AgentOutcome\" = {0} WHERE \"Id\" = {1}",
            verification ? new string('8', 64) : "SourceChanged",
            operationId);

        var read = await scene.ReadReleaseAsync(ended.Target.Id);
        var result = await scene.ReleaseAsync(confirmed);
        var because = $"verification={verification}, alreadyReleased={alreadyReleased}";

        Assert.True(result.IsFailure, $"the old confirmation must be refused ({because})");
        if (alreadyReleased)
        {
            Assert.Equal(LocalExecutionOwnershipReleaseStatus.Unavailable, read.Status);
            Assert.Null(read.Release);
            Assert.Equal(LocalExecutionOwnershipReleaseErrors.UnavailableCode, result.Errors[0].Code);
            Assert.Equal((1, 1), await RowsAsync());
        }
        else
        {
            Assert.Equal(LocalExecutionOwnershipReleaseStatus.Available, read.Status);
            Assert.NotEqual(before.Candidate!.ConfirmationSha256, read.Candidate!.ConfirmationSha256);
            Assert.Equal(LocalExecutionOwnershipReleaseErrors.ConfirmationStaleCode, result.Errors[0].Code);
            Assert.Equal((0, 0), await RowsAsync());
        }

        await AssertHeldAsync(scene, kind, because);
    }

    [Theory]
    [InlineData(Step.CodeReview, false)]
    [InlineData(Step.Implementation, true)]
    public async Task A_changed_concrete_agent_outcome_before_the_release_gets_a_fresh_candidate_that_releases_exactly_once(Step kind, bool queued)
    {
        var ended = await Rig.EndAsync(fixture, kind, queued);
        var scene = ended.Scene;
        var stale = scene.ConfirmedCommand(await scene.ReadReleaseAsync(ended.Target.Id));
        await LocalExecutionOwnershipReleaseTamper.ApplyAsync(
            fixture, "attempts", "UPDATE attempts SET \"AgentOutcome\" = 'CheckpointEvidenceUnavailable' WHERE \"Id\" = {0}", ended.Target.OperationId!.Value);

        Assert.True((await scene.ReleaseAsync(stale)).IsFailure);
        Assert.Equal((0, 0), await RowsAsync());
        await AssertHeldAsync(scene, kind, "stale confirmation");

        var fresh = await scene.ReadReleaseAsync(ended.Target.Id);
        Assert.Equal(LocalExecutionOwnershipReleaseStatus.Available, fresh.Status);
        Assert.NotEqual(stale.ConfirmationSha256, fresh.Candidate!.ConfirmationSha256);
        Assert.Equal(AgentOutcome.CheckpointEvidenceUnavailable, fresh.Candidate.Facts.AgentOutcome);
        await scene.ReleaseStepAsync(ended.Target.Id);
        Assert.Equal(LocalExecutionOwnershipReleaseStatus.Released, (await scene.ReadReleaseAsync(ended.Target.Id)).Status);
        Assert.Equal((1, 1), await RowsAsync());
        await AssertFreeAsync(scene, "fresh confirmation");
    }

    [Theory]
    [InlineData(Step.CodeReview, false)]
    [InlineData(Step.Verification, false)]
    [InlineData(Step.Implementation, true)]
    public async Task The_receipt_stores_the_concrete_confirmed_facts_field_for_field_and_an_unchanged_replay_writes_nothing(Step kind, bool queued)
    {
        var ended = await Rig.EndAsync(fixture, kind, queued);
        var scene = ended.Scene;
        var read = await scene.ReadReleaseAsync(ended.Target.Id);
        var facts = read.Candidate!.Facts;
        var confirmed = scene.ConfirmedCommand(read);
        Assert.True((await scene.ReleaseAsync(confirmed)).IsSuccess);

        var receipt = (await scene.ReadReleasesAsync()).Single();
        Assert.Equal(LocalExecutionOwnershipRelease.CurrentIntentVersion, receipt.IntentVersion);
        Assert.Equal(facts.Family, receipt.IntentFamily);
        Assert.Equal(facts.OperationId, receipt.IntentOperationId);
        Assert.Equal(facts.SequenceId, receipt.IntentSequenceId);
        Assert.Equal(facts.RunId, receipt.IntentRunId);
        Assert.Equal(facts.ProjectId, receipt.IntentProjectId);
        Assert.Equal(facts.WorkspaceId, receipt.IntentWorkspaceId);
        Assert.Equal(facts.SequenceState, receipt.IntentSequenceState);
        Assert.Equal(facts.AdmissionMode, receipt.IntentAdmissionMode);
        Assert.Equal(facts.AdmissionId, receipt.IntentAdmissionId);
        Assert.Equal(facts.Kind, receipt.IntentKind);
        Assert.Equal(facts.Ordinal, receipt.IntentOrdinal);
        Assert.Equal(facts.PassNumber, receipt.IntentPassNumber);
        Assert.Equal(facts.RecipeOrdinal, receipt.IntentRecipeOrdinal);
        Assert.Equal(facts.PredecessorStepId, receipt.IntentPredecessorStepId);
        Assert.Equal(facts.SourceCheckpointId, receipt.IntentSourceCheckpointId);
        Assert.Equal(facts.SourceFingerprintSha256, receipt.IntentSourceFingerprintSha256);
        Assert.Equal(facts.ReportMessageId, receipt.IntentReportMessageId);
        Assert.Equal(facts.SubjectAttemptId, receipt.IntentSubjectAttemptId);
        Assert.Equal(facts.AgentResponseContract, receipt.IntentAgentResponseContract);
        Assert.Equal(facts.RecipeCommandId, receipt.IntentRecipeCommandId);
        Assert.Equal(facts.OperationStatus, receipt.IntentOperationStatus);
        Assert.Equal(facts.AgentOutcome, receipt.IntentAgentOutcome);
        Assert.Equal(facts.CompletionFingerprintSha256, receipt.IntentCompletionFingerprintSha256);
        Assert.Equal(facts.ProcessOutcome, receipt.IntentProcessOutcome);
        Assert.Equal(facts.SemanticExitCode, receipt.IntentSemanticExitCode);
        Assert.Equal(facts.ProcessDurationTicks, receipt.IntentProcessDuration.Ticks);
        Assert.Equal(facts.PhysicalExitCode, receipt.IntentPhysicalExitCode);
        Assert.Equal(facts.ObservedElapsedTicks, receipt.IntentObservedElapsed.Ticks);
        Assert.Equal(facts.DispatchedAtUtc, receipt.IntentDispatchedAtUtc);
        Assert.Equal(facts.RecordedAtUtc, receipt.IntentObservationRecordedAtUtc);
        Assert.Equal(read.Candidate.ConfirmationVersion, receipt.IntentConfirmationVersion);
        Assert.Equal(read.Candidate.ConfirmationExpiresAtUtc, receipt.IntentConfirmationExpiresAtUtc);
        Assert.Equal(receipt.ConfirmationExpiresAtUtc, receipt.IntentConfirmationExpiresAtUtc);

        // The unchanged replay still succeeds with no further write.
        Assert.True((await scene.ReleaseAsync(confirmed)).IsSuccess);
        Assert.Equal((1, 1), await RowsAsync());
        await AssertFreeAsync(scene, "unchanged replay");
    }

    // ---- LQR-R1: the exact lifetime is a storage constraint at tick precision ---------------------------------------------------------------

    [Theory]
    [InlineData(false, "ConfirmationExpiresAtUtc", "ck_local_execution_ownership_releases_time")]
    [InlineData(true, "ConfirmationExpiresAtUtc", "ck_local_execution_ownership_releases_time")]
    public async Task A_receipt_cannot_exceed_the_exact_two_minute_lifetime_by_one_tick(bool verification, string column, string constraint)
    {
        var kind = verification ? Step.Verification : Step.Implementation;
        var ended = await Rig.EndAsync(fixture, kind, false);
        var scene = ended.Scene;
        await scene.ReleaseStepAsync(ended.Target.Id);
        var receipt = (await scene.ReadReleasesAsync()).Single();

        var refusal = await Assert.ThrowsAsync<SqliteException>(() => LocalExecutionOwnershipReleaseTamper.ApplyAsync(
            fixture, "local_execution_ownership_releases",
            "UPDATE local_execution_ownership_releases SET \"" + column + "\" = {0} WHERE \"Id\" = {1}",
            receipt.ReleasedAtUtc.AddMinutes(2).AddTicks(1), receipt.Id));

        Assert.Equal(19, refusal.SqliteErrorCode);
        Assert.Contains(constraint, refusal.Message, StringComparison.Ordinal);
        Assert.Equal(LocalExecutionOwnershipReleaseStatus.Released, (await scene.ReadReleaseAsync(ended.Target.Id)).Status);
        await AssertFreeAsync(scene, "the refused update changed nothing");
    }

    // ---- LQR-R1: the completion fingerprint is proved complete in the ownership expression and in SQL ------------------------------------

    [Theory]
    [InlineData("not-a-sha256")]
    [InlineData("")]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdeg")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF")]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0")]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcde")]
    public async Task A_malformed_oversized_or_short_completion_fingerprint_after_the_release_is_never_a_release_anywhere(string fingerprint)
    {
        var ended = await Rig.EndAsync(fixture, Step.Verification, false);
        var scene = ended.Scene;
        var operationId = ended.Target.OperationId!.Value;
        await LocalExecutionOwnershipReleaseTamper.ApplyAsync(
            fixture, "verification_executions",
            "UPDATE verification_executions SET \"Status\" = 'SourceChanged', \"CompletionFingerprintSha256\" = {0} WHERE \"Id\" = {1}",
            new string('9', 64), operationId);
        await scene.ReleaseStepAsync(ended.Target.Id);
        await AssertFreeAsync(scene, "before the change");

        await LocalExecutionOwnershipReleaseTamper.ApplyAsync(
            fixture, "verification_executions",
            "UPDATE verification_executions SET \"CompletionFingerprintSha256\" = {0} WHERE \"Id\" = {1}", fingerprint, operationId);

        var read = await scene.ReadReleaseAsync(ended.Target.Id);
        Assert.Equal(LocalExecutionOwnershipReleaseStatus.Unavailable, read.Status);
        await AssertHeldAsync(scene, Step.Verification, "a completion fingerprint that is not a valid SHA-256");
    }

    // ---- ADR-0066: the checksum-only exception -------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(Step.CodeReview, false)]
    [InlineData(Step.Verification, false)]
    [InlineData(Step.Implementation, true)]
    public async Task A_substituted_checksum_alone_is_unavailable_and_refuses_replay_but_never_reacquires_ownership(Step kind, bool queued)
    {
        var ended = await Rig.EndAsync(fixture, kind, queued);
        var scene = ended.Scene;
        var confirmed = scene.ConfirmedCommand(await scene.ReadReleaseAsync(ended.Target.Id));
        await scene.ReleaseStepAsync(ended.Target.Id);
        var ids = await LocalExecutionOwnershipReleaseTamper.ReadIdsAsync(fixture, scene, ended.Target);
        await LocalExecutionOwnershipReleaseTamper.ApplyAsync(
            fixture, "local_execution_ownership_releases",
            "UPDATE local_execution_ownership_releases SET \"ConfirmationSha256\" = {0} WHERE \"Id\" = {1}", new string('a', 64), ids.ReleaseId);

        var read = await scene.ReadReleaseAsync(ended.Target.Id);
        var replay = await scene.ReleaseAsync(confirmed);

        Assert.Equal(LocalExecutionOwnershipReleaseStatus.Unavailable, read.Status);
        Assert.Null(read.Release);
        Assert.Null(read.Candidate);
        Assert.True(replay.IsFailure);
        Assert.Equal(LocalExecutionOwnershipReleaseErrors.UnavailableCode, replay.Errors[0].Code);
        Assert.Equal((1, 1), await RowsAsync());
        await AssertFreeAsync(scene, "only the checksum was substituted");
    }

    [Fact]
    public async Task A_substituted_expiry_alone_is_a_snapshot_mismatch_that_keeps_ownership_held()
    {
        var ended = await Rig.EndAsync(fixture, Step.Implementation, false);
        var scene = ended.Scene;
        var confirmed = scene.ConfirmedCommand(await scene.ReadReleaseAsync(ended.Target.Id));
        await scene.ReleaseStepAsync(ended.Target.Id);
        var ids = await LocalExecutionOwnershipReleaseTamper.ReadIdsAsync(fixture, scene, ended.Target);
        await LocalExecutionOwnershipReleaseTamper.ApplyAsync(
            fixture, "local_execution_ownership_releases",
            "UPDATE local_execution_ownership_releases SET \"ConfirmationExpiresAtUtc\" = strftime('%Y-%m-%d %H:%M:%S', \"ConfirmationExpiresAtUtc\", '-1 seconds') "
            + "|| '+00:00' WHERE \"Id\" = {0}", ids.ReleaseId);

        Assert.Equal(LocalExecutionOwnershipReleaseStatus.Unavailable, (await scene.ReadReleaseAsync(ended.Target.Id)).Status);
        Assert.True((await scene.ReleaseAsync(confirmed)).IsFailure);
        Assert.Equal((1, 1), await RowsAsync());
        await AssertHeldAsync(scene, Step.Implementation, "the stored expiry no longer equals the snapshot's");
    }
}
