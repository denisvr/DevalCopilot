using DevalCopilot.Application.Features.Projects.Policies.WorkspaceExecutions;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionReleaseInspection;
using DevalCopilot.Application.Tests.Features.Runs.LocalExecutionInspection;
using DevalCopilot.Application.Tests.Features.Runs.LocalExecutionOwnershipReleases;
using DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Runs;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.LocalExecutionReleaseInspection.LocalExecutionReleaseInspectionRig;
using Ids = DevalCopilot.Application.Tests.Features.Runs.LocalExecutionOwnershipReleases.LocalExecutionOwnershipReleaseTamper.Ids;
using Rig = DevalCopilot.Application.Tests.Features.Runs.LocalExecutionOwnershipReleases.LocalExecutionOwnershipReleaseRig;
using Step = DevalCopilot.Domain.Features.Runs.LocalExecutionStepKind;
using Tamper = DevalCopilot.Application.Tests.Features.Runs.LocalExecutionOwnershipReleases.LocalExecutionOwnershipReleaseTamper;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionReleaseInspection;

/// <summary>
/// ADR-0067: a PRESENT receipt that fails any part of the release proof is Unavailable with no payload, never NotRecorded, and never
/// partially disclosed. Starting from a real Released operation, each case changes exactly one stored fact (the typed snapshot, the
/// step-keyed or witness-keyed association, the Human event, the operation, the observation, the step or the sequence) through a direct
/// guarded write. The unchanged ownership readings are asserted independently for a valid receipt, a snapshot drift and ADR-0066's
/// checksum-only mismatch, which stays Unavailable here while the exact relational grant still releases ownership.
/// </summary>
public sealed class LocalExecutionReleaseInspectionDamageTests : IAsyncLifetime
{
    private static readonly string OtherFingerprint = new('9', 64);
    private const string EarlierTime = "2000-01-01 00:00:00+00:00";

    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    private sealed record Damage(Step Kind, bool Queued, string Table, Func<Ids, (string Sql, object[] Parameters)> Build, bool WithoutChecks = false);

    private static readonly Dictionary<string, Damage> Catalog = new()
    {
        // ---- the typed snapshot -------------------------------------------------------------------------------------------------------
        ["intent-source-fingerprint-sha256"] = new(Step.CodeReview, false, "local_execution_ownership_releases",
            ids => ("UPDATE local_execution_ownership_releases SET \"IntentSourceFingerprintSha256\" = {1} WHERE \"Id\" = {0}", [ids.ReleaseId, OtherFingerprint])),
        ["intent-process-duration"] = new(Step.Verification, false, "local_execution_ownership_releases",
            ids => ("UPDATE local_execution_ownership_releases SET \"IntentProcessDuration\" = \"IntentProcessDuration\" + 1 WHERE \"Id\" = {0}", [ids.ReleaseId])),
        ["intent-physical-exit-code"] = new(Step.Implementation, false, "local_execution_ownership_releases",
            ids => ("UPDATE local_execution_ownership_releases SET \"IntentPhysicalExitCode\" = \"IntentPhysicalExitCode\" + 1 WHERE \"Id\" = {0}", [ids.ReleaseId])),
        ["intent-agent-outcome"] = new(Step.CodeReview, false, "local_execution_ownership_releases",
            ids => ("UPDATE local_execution_ownership_releases SET \"IntentAgentOutcome\" = 'SourceChanged' WHERE \"Id\" = {0}", [ids.ReleaseId])),
        ["intent-completion-fingerprint-sha256"] = new(Step.Verification, false, "local_execution_ownership_releases",
            ids => ("UPDATE local_execution_ownership_releases SET \"IntentCompletionFingerprintSha256\" = {1} WHERE \"Id\" = {0}", [ids.ReleaseId, OtherFingerprint])),
        ["intent-admission-id"] = new(Step.Implementation, true, "local_execution_ownership_releases",
            ids => ("UPDATE local_execution_ownership_releases SET \"IntentAdmissionId\" = {1} WHERE \"Id\" = {0}", [ids.ReleaseId, Guid.NewGuid()])),
        ["intent-confirmation-expires-at-utc"] = new(Step.CodeReview, false, "local_execution_ownership_releases",
            ids => ("UPDATE local_execution_ownership_releases SET \"IntentConfirmationExpiresAtUtc\" = strftime('%Y-%m-%d %H:%M:%S', \"IntentConfirmationExpiresAtUtc\", '-1 seconds') || '+00:00' WHERE \"Id\" = {0}",
                [ids.ReleaseId])),
        ["release-before-observation"] = new(Step.CodeReview, false, "local_execution_ownership_releases",
            ids => ("UPDATE local_execution_ownership_releases SET \"ReleasedAtUtc\" = {0}, \"ConfirmationExpiresAtUtc\" = {1} WHERE \"Id\" = {2}",
                [EarlierTime, "2000-01-01 00:01:00+00:00", ids.ReleaseId])),

        // ---- the step-keyed and witness-keyed associations and the Human event ------------------------------------------------------------
        ["release-witness"] = new(Step.CodeReview, false, "local_execution_ownership_releases",
            ids => ("UPDATE local_execution_ownership_releases SET \"WitnessId\" = {0} WHERE \"Id\" = {1}", [Guid.NewGuid(), ids.ReleaseId])),
        ["release-observation"] = new(Step.Verification, false, "local_execution_ownership_releases",
            ids => ("UPDATE local_execution_ownership_releases SET \"ObservationId\" = {0} WHERE \"Id\" = {1}", [Guid.NewGuid(), ids.ReleaseId])),
        ["release-step"] = new(Step.CodeReview, false, "local_execution_ownership_releases",
            ids => ("UPDATE local_execution_ownership_releases SET \"StepId\" = {0} WHERE \"Id\" = {1}", [ids.OtherStepId, ids.ReleaseId])),
        ["release-event-missing"] = new(Step.CodeReview, false, "local_execution_ownership_releases",
            ids => ("UPDATE local_execution_ownership_releases SET \"EventId\" = {0} WHERE \"Id\" = {1}", [Guid.NewGuid(), ids.ReleaseId])),
        ["release-event-of-another-type"] = new(Step.CodeReview, false, "local_execution_ownership_releases",
            ids => ("UPDATE local_execution_ownership_releases SET \"EventId\" = (SELECT \"Id\" FROM events WHERE \"EventType\" = 'local_execution_sequence.armed' LIMIT 1) "
                + "WHERE \"Id\" = {0}", [ids.ReleaseId])),
        ["event-type"] = new(Step.CodeReview, false, "events",
            ids => ("UPDATE events SET \"EventType\" = 'run.started' WHERE \"Id\" = {0}", [ids.EventId])),
        ["event-actor"] = new(Step.Verification, false, "events",
            ids => ("UPDATE events SET \"ActorKind\" = 'Orchestrator' WHERE \"Id\" = {0}", [ids.EventId])),
        ["event-run"] = new(Step.CodeReview, false, "events",
            ids => ("UPDATE events SET \"RunId\" = {0} WHERE \"Id\" = {1}", [Guid.NewGuid(), ids.EventId])),

        // ---- the chain the receipt names ------------------------------------------------------------------------------------------------
        ["witness-step"] = new(Step.CodeReview, false, "local_execution_dispatch_witnesses",
            ids => ("UPDATE local_execution_dispatch_witnesses SET \"StepId\" = {0} WHERE \"Id\" = {1}", [Guid.NewGuid(), ids.WitnessId])),
        ["observation-outcome"] = new(Step.CodeReview, false, "local_execution_termination_observations",
            ids => ("UPDATE local_execution_termination_observations SET \"ProcessOutcome\" = 'Cancelled' WHERE \"Id\" = {0}", [ids.ObservationId])),
        ["attempt-running"] = new(Step.CodeReview, false, "attempts",
            ids => ("UPDATE attempts SET \"Status\" = 'Running' WHERE \"Id\" = {0}", [ids.OperationId])),
        ["authority-agent-outcome-after-release"] = new(Step.Implementation, false, "attempts",
            ids => ("UPDATE attempts SET \"AgentOutcome\" = 'SourceChanged' WHERE \"Id\" = {0}", [ids.OperationId])),
        ["authority-sequence-state-after-release"] = new(Step.CodeReview, false, "local_execution_sequences",
            ids => ("UPDATE local_execution_sequences SET \"State\" = 'Stopped' WHERE \"Id\" = {0}", [ids.SequenceId])),
        ["sequence-live"] = new(Step.CodeReview, false, "local_execution_sequences",
            ids => ("UPDATE local_execution_sequences SET \"State\" = 'Stopping' WHERE \"Id\" = {0}", [ids.SequenceId])),
        ["step-fingerprint"] = new(Step.CodeReview, false, "local_execution_sequence_steps",
            ids => ("UPDATE local_execution_sequence_steps SET \"SourceFingerprintSha256\" = {0} WHERE \"Id\" = {1}", [OtherFingerprint, ids.StepId])),
        ["admission-not-consumed"] = new(Step.Implementation, true, "local_execution_admissions",
            ids => ("UPDATE local_execution_admissions SET \"State\" = 'Reserved' WHERE \"Id\" = {0}", [ids.AdmissionId!.Value])),

        // ---- a receipt beside a step that no longer names an operation -------------------------------------------------------------------------
        ["receipt-beside-a-revoked-step"] = new(Step.CodeReview, false, "local_execution_sequence_steps",
            ids => ("UPDATE local_execution_sequence_steps SET \"State\" = 'Revoked', \"OperationId\" = NULL WHERE \"Id\" = {0}", [ids.StepId]), WithoutChecks: true),
        ["receipt-beside-an-unlinked-step"] = new(Step.Verification, false, "local_execution_sequence_steps",
            ids => ("UPDATE local_execution_sequence_steps SET \"OperationId\" = NULL WHERE \"Id\" = {0}", [ids.StepId]), WithoutChecks: true),
    };

    public static TheoryData<string> Cases
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var name in Catalog.Keys)
            {
                data.Add(name);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task A_present_receipt_that_fails_any_proof_is_unavailable_with_no_payload(string name)
    {
        var damage = Catalog[name];
        var ended = await Rig.EndAsync(fixture, damage.Kind, damage.Queued);
        var scene = ended.Scene;
        await scene.ReleaseStepAsync(ended.Target.Id);
        Assert.Equal(LocalExecutionReleaseInspectionStatus.Recorded, (await InspectAsync(fixture, scene.RunId, ended.Target.Id)).Status);
        var ids = await Tamper.ReadIdsAsync(fixture, scene, ended.Target);
        var (sql, parameters) = damage.Build(ids);
        if (damage.WithoutChecks)
        {
            await Tamper.ApplyWithoutCheckConstraintsAsync(fixture, damage.Table, sql, parameters);
        }
        else
        {
            await Tamper.ApplyAsync(fixture, damage.Table, sql, parameters);
        }

        var before = await FingerprintAsync(fixture, scene);
        var log = new StatementLog();

        var inspected = await InspectAsync(fixture, scene.RunId, ended.Target.Id, log);

        Assert.True(inspected.Status == LocalExecutionReleaseInspectionStatus.Unavailable, $"{name}: {inspected.Status}");
        Assert.Null(inspected.Receipt);
        AssertOneReadOnlyStatement(log);
        Assert.Equal(before, await FingerprintAsync(fixture, scene));
    }

    [Fact]
    public async Task A_step_keyed_receipt_that_the_witness_does_not_agree_with_is_unavailable_from_both_steps()
    {
        var ended = await Rig.EndAsync(fixture, Step.CodeReview, queued: false);
        var scene = ended.Scene;
        await scene.ReleaseStepAsync(ended.Target.Id);
        var ids = await Tamper.ReadIdsAsync(fixture, scene, ended.Target);
        await Tamper.ApplyAsync(
            fixture, "local_execution_ownership_releases",
            "UPDATE local_execution_ownership_releases SET \"StepId\" = {0} WHERE \"Id\" = {1}", ids.OtherStepId, ids.ReleaseId);

        // The target now has no step-keyed receipt but the witness-keyed one still exists: absence cannot be claimed.
        var target = await InspectAsync(fixture, scene.RunId, ended.Target.Id);
        var other = await InspectAsync(fixture, scene.RunId, ids.OtherStepId);

        Assert.Equal(LocalExecutionReleaseInspectionStatus.Unavailable, target.Status);
        Assert.Equal(LocalExecutionReleaseInspectionStatus.Unavailable, other.Status);
        Assert.Null(target.Receipt);
        Assert.Null(other.Receipt);
    }

    [Fact]
    public async Task A_stored_value_this_build_cannot_materialize_is_unavailable_never_absent()
    {
        var ended = await Rig.EndAsync(fixture, Step.Implementation, queued: false);
        var scene = ended.Scene;
        await scene.ReleaseStepAsync(ended.Target.Id);
        var ids = await Tamper.ReadIdsAsync(fixture, scene, ended.Target);
        await Tamper.ApplyWithoutCheckConstraintsAsync(
            fixture, "local_execution_ownership_releases",
            "UPDATE local_execution_ownership_releases SET \"IntentOperationId\" = 'not-a-guid' WHERE \"Id\" = {0}", ids.ReleaseId);

        var inspected = await InspectAsync(fixture, scene.RunId, ended.Target.Id);

        Assert.Equal(LocalExecutionReleaseInspectionStatus.Unavailable, inspected.Status);
        Assert.Null(inspected.Receipt);
    }

    // ---- Independent ownership assertions for the representative cases -----------------------------------------------------------------

    [Theory]
    [InlineData("valid")]
    [InlineData("snapshot-drift")]
    [InlineData("checksum-only")]
    public async Task Inspecting_changes_no_ownership_reading_for_a_valid_receipt_a_snapshot_drift_and_a_checksum_only_mismatch(string name)
    {
        var ended = await Rig.EndAsync(fixture, Step.Implementation, queued: false);
        var scene = ended.Scene;
        await scene.ReleaseStepAsync(ended.Target.Id);
        var ids = await Tamper.ReadIdsAsync(fixture, scene, ended.Target);
        switch (name)
        {
            case "snapshot-drift":
                await Tamper.ApplyAsync(
                    fixture, "local_execution_ownership_releases",
                    "UPDATE local_execution_ownership_releases SET \"IntentProcessDuration\" = \"IntentProcessDuration\" + 1 WHERE \"Id\" = {0}",
                    ids.ReleaseId);
                break;
            case "checksum-only":
                await Tamper.ApplyAsync(
                    fixture, "local_execution_ownership_releases",
                    "UPDATE local_execution_ownership_releases SET \"ConfirmationSha256\" = {0} WHERE \"Id\" = {1}", new string('a', 64), ids.ReleaseId);
                break;
        }

        var ownership = await Rig.ReadOwnershipAsync(fixture, scene);
        var fenceBlocked = await Tamper.ProbeIsBlockedAsync(fixture, scene.ProjectId);
        var abandonmentBlocked = await Tamper.ProbeRunAbandonmentIsBlockedAsync(fixture, scene.RunId);
        var before = await FingerprintAsync(fixture, scene);

        var inspected = await InspectAsync(fixture, scene.RunId, ended.Target.Id);

        Assert.Equal(
            name == "valid" ? LocalExecutionReleaseInspectionStatus.Recorded : LocalExecutionReleaseInspectionStatus.Unavailable,
            inspected.Status);
        Assert.Equal(before, await FingerprintAsync(fixture, scene));
        Assert.Equal(ownership, await Rig.ReadOwnershipAsync(fixture, scene));
        Assert.Equal(fenceBlocked, await Tamper.ProbeIsBlockedAsync(fixture, scene.ProjectId));
        Assert.Equal(abandonmentBlocked, await Tamper.ProbeRunAbandonmentIsBlockedAsync(fixture, scene.RunId));
        // The negative states establish neither held nor released ownership: only the relational proof decides it.
        var released = name != "snapshot-drift";
        Assert.Equal(released, ownership.Workspace == WorkspaceExecutionStatus.Idle);
        Assert.Equal(!released, fenceBlocked);
        Assert.Equal(!released, abandonmentBlocked);
    }
}
