using DevalCopilot.Application.Features.Projects.Policies.WorkspaceExecutions;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionOwnershipRelease;
using DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Step = DevalCopilot.Domain.Features.Runs.LocalExecutionStepKind;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionOwnershipReleases;

/// <summary>
/// ADR-0065 end to end through the production claims, dispatch markers, result recorders, query and command handlers over real file-backed
/// SQLite: for all six step kinds, Immediate and queued, the terminal operation stays held after its physical termination was observed,
/// one exact Human confirmation alone releases it (consistently in the workspace, capacity and abandonment readings), the failed result
/// and the ended cycle are untouched, exactly one release and one Human event exist, and an identical request replays.
/// </summary>
public sealed class LocalExecutionOwnershipReleaseCycleTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture fixture = new();

    public static TheoryData<Step, bool> Matrix
    {
        get
        {
            var data = new TheoryData<Step, bool>();
            foreach (var kind in LocalExecutionOwnershipReleaseRig.AllKinds)
            {
                data.Add(kind, false);
                data.Add(kind, true);
            }

            return data;
        }
    }

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task A_physically_terminated_operation_stays_held_until_one_human_confirmation_releases_it(Step kind, bool queued)
    {
        var ended = await LocalExecutionOwnershipReleaseRig.EndAsync(fixture, kind, queued);
        var scene = ended.Scene;
        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, ended.Sequence.State);

        // Physical termination was observed and recorded, yet the quarantine still holds until the Human decides.
        var held = await LocalExecutionOwnershipReleaseRig.ReadOwnershipAsync(fixture, scene);
        Assert.Equal(LocalExecutionOwnershipReleaseRig.HeldWorkspace(kind), held.Workspace);
        Assert.Equal(LocalExecutionOwnershipReleaseRig.IsMutating(kind) ? 1 : 0, held.MutatingOwners);
        Assert.True(held.AbandonmentBlocked);
        Assert.Single(await scene.ReadObservationsAsync());

        var read = await scene.ReadReleaseAsync(ended.Target.Id);
        Assert.Equal(LocalExecutionOwnershipReleaseStatus.Available, read.Status);
        var facts = read.Candidate!.Facts;
        Assert.Equal(LocalExecutionOwnershipReleaseRig.IsVerification(kind) ? LocalExecutionOperationFamily.Verification : LocalExecutionOperationFamily.Agent, facts.Family);
        Assert.Equal(kind, facts.Kind);
        Assert.Equal(ended.Target.OperationId, facts.OperationId);
        Assert.Equal(queued ? LocalExecutionAdmissionMode.WaitForAdmission : LocalExecutionAdmissionMode.Immediate, facts.AdmissionMode);
        Assert.Equal(queued, facts.AdmissionId is not null);
        Assert.Equal(137, facts.PhysicalExitCode);
        Assert.Null(facts.SemanticExitCode);
        Assert.NotEqual(ProcessOutcome.Exited, facts.ProcessOutcome);

        var before = await SnapshotAsync(scene, ended.Target);
        var released = await scene.ReleaseAsync(scene.ConfirmedCommand(read));

        Assert.True(released.IsSuccess, released.IsFailure ? released.Errors[0].Code : null);
        var free = await LocalExecutionOwnershipReleaseRig.ReadOwnershipAsync(fixture, scene);
        Assert.Equal(WorkspaceExecutionStatus.Idle, free.Workspace);
        Assert.Equal(0, free.MutatingOwners);
        Assert.False(free.AbandonmentBlocked);

        // Release changes no result, cycle, workspace, lease or step: only the one receipt and the one Human event were added.
        Assert.Equal(before, await SnapshotAsync(scene, ended.Target));
        Assert.Single(await scene.ReadReleasesAsync());
        Assert.Equal(1, await scene.CountEventsAsync(RunEventType.LocalExecutionOwnershipReleased));
        var status = await scene.ReadStatusAsync();
        Assert.False(status.Sequence!.Steps.Single(step => step.StepId == ended.Target.Id).Quarantined);

        var receipt = await scene.ReadReleaseAsync(ended.Target.Id);
        Assert.Equal(LocalExecutionOwnershipReleaseStatus.Released, receipt.Status);
        Assert.Null(receipt.Candidate);
        Assert.Equal(released.Value.ReleaseId, receipt.Release!.ReleaseId);
        Assert.Equal(released.Value.EventSequence, receipt.Release.EventSequence);
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task An_identical_request_replays_the_receipt_with_no_write_and_a_changed_one_conflicts(Step kind, bool queued)
    {
        var ended = await LocalExecutionOwnershipReleaseRig.EndAsync(fixture, kind, queued);
        var scene = ended.Scene;
        var command = scene.ConfirmedCommand(await scene.ReadReleaseAsync(ended.Target.Id));
        var first = await scene.ReleaseAsync(command);
        Assert.True(first.IsSuccess);

        // The confirmation expired long ago; the identical normalized request still returns the recorded receipt.
        scene.Clock.Advance(TimeSpan.FromHours(3));
        var replay = await scene.ReleaseAsync(command);

        Assert.True(replay.IsSuccess, replay.IsFailure ? replay.Errors[0].Code : null);
        Assert.Equal(first.Value.ReleaseId, replay.Value.ReleaseId);
        Assert.Equal(first.Value.EventSequence, replay.Value.EventSequence);
        Assert.Equal(first.Value.ReleasedAtUtc, replay.Value.ReleasedAtUtc);
        Assert.Single(await scene.ReadReleasesAsync());
        Assert.Equal(1, await scene.CountEventsAsync(RunEventType.LocalExecutionOwnershipReleased));

        var changed = await scene.ReleaseAsync(command with { ConfirmationSha256 = new string('0', 64) });
        Assert.True(changed.IsFailure);
        Assert.Equal("local_execution_ownership_release.confirmation_conflict", changed.Errors[0].Code);
        var otherObservation = await scene.ReleaseAsync(command with { ObservationId = Guid.NewGuid() });
        Assert.Equal("local_execution_ownership_release.confirmation_conflict", otherObservation.Errors[0].Code);
        Assert.Single(await scene.ReadReleasesAsync());
        Assert.Equal(1, await scene.CountEventsAsync(RunEventType.LocalExecutionOwnershipReleased));
    }

    /// <summary>Everything a release must leave untouched: the operation row, the step, the sequence and the workspace.</summary>
    private async Task<string> SnapshotAsync(LocalExecutionSequenceTestScene scene, LocalExecutionSequenceStep target)
    {
        await using var context = fixture.CreateContext();
        var sequence = await context.LocalExecutionSequences.AsNoTracking().SingleAsync();
        var steps = await context.LocalExecutionSequenceSteps.AsNoTracking().OrderBy(step => step.Ordinal).ToListAsync();
        var attempts = await context.Attempts.AsNoTracking().OrderBy(attempt => attempt.AttemptNumber).ToListAsync();
        var executions = await context.VerificationExecutions.AsNoTracking().OrderBy(execution => execution.ExecutionNumber).ToListAsync();
        var workspace = await context.GitWorkspaces.AsNoTracking().SingleAsync();
        var lease = await context.RepositoryMutationLeases.AsNoTracking().SingleAsync();
        var admissions = await context.LocalExecutionAdmissions.AsNoTracking().OrderBy(admission => admission.AdmissionNumber).ToListAsync();
        return string.Join(
            "|",
            $"{sequence.State}/{sequence.ReasonCode}/{sequence.Revision}",
            string.Join(",", steps.Select(step => $"{step.Id}:{step.State}:{step.Revision}")),
            string.Join(",", attempts.Select(attempt => $"{attempt.Id}:{attempt.Status}:{attempt.AgentOutcome}:{attempt.AgentProcessOutcome}:{attempt.AgentProcessExitCode}:{attempt.AgentProcessDuration?.Ticks}")),
            string.Join(",", executions.Select(execution => $"{execution.Id}:{execution.Status}:{execution.Outcome}:{execution.ExitCode}")),
            $"{workspace.Status}/{lease.Status}",
            string.Join(",", admissions.Select(admission => $"{admission.Id}:{admission.State}:{admission.Revision}")),
            target.Id);
    }
}
