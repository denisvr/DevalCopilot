using DevalCopilot.Application.Features.Projects.Policies.WorkspaceExecutions;
using DevalCopilot.Application.Features.Runs.Policies.Abandonment;
using DevalCopilot.Application.Features.Runs.Policies.ExecutionCapacity;
using DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences.ScriptedResultKind;
using Step = DevalCopilot.Domain.Features.Runs.LocalExecutionStepKind;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionOwnershipReleases;

/// <summary>
/// ADR-0065 test rig: drives a whole local execution cycle, Immediate or queued, through the production claims, dispatch markers (which
/// commit the dispatch witness) and result recorders until the step of one chosen kind records a terminal timeout or cancellation whose
/// physical termination the original host observed, then returns the ended scene. Every fact is the production one: nothing is seeded,
/// patched or fabricated, so the witness, observation and ownership the tests judge are exactly what a real host records.
/// </summary>
internal static class LocalExecutionOwnershipReleaseRig
{
    public static IEnumerable<Step> AllKinds =>
    [
        Step.Implementation,
        Step.Verification,
        Step.CodeReview,
        Step.VerificationDiagnosis,
        Step.ReviewCorrection,
        Step.DiagnosisCorrection,
    ];

    /// <summary>The script under which the step of <paramref name="kind"/> is the first to record a terminal result without ordinary
    /// Exited proof, with the physical termination observed. Earlier steps follow the production default path.</summary>
    public static LocalExecutionStepScript Script(Step kind, ScriptedResultKind agentResult = ProviderTimedOutTerminated) => kind switch
    {
        Step.Implementation => new LocalExecutionStepScript().On(Step.Implementation, agentResult),
        Step.Verification => new LocalExecutionStepScript().Verify(1, 0, VerificationTimedOutTerminated),
        Step.CodeReview => new LocalExecutionStepScript().On(Step.CodeReview, agentResult),
        Step.VerificationDiagnosis => new LocalExecutionStepScript().Verify(1, 0, VerificationFailed).On(Step.VerificationDiagnosis, agentResult),
        Step.ReviewCorrection => new LocalExecutionStepScript().On(Step.CodeReview, 1, ReviewChangesRequested).On(Step.ReviewCorrection, agentResult),
        Step.DiagnosisCorrection => new LocalExecutionStepScript().Verify(1, 0, VerificationFailed).On(Step.DiagnosisCorrection, agentResult),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>Whether the kind belongs to the verification family (all others are Agent attempts).</summary>
    public static bool IsVerification(Step kind) => kind == Step.Verification;

    public static bool IsMutating(Step kind) => LocalExecutionSequencePolicy.IsPotentiallyMutating(kind);

    public sealed record Ended(LocalExecutionSequenceTestScene Scene, LocalExecutionSequence Sequence, LocalExecutionSequenceStep Target);

    /// <summary>Drives a cycle to its end. The target is the last linked step, which recorded the scripted terminal result.</summary>
    public static async Task<Ended> EndAsync(
        SqliteDatabaseFixture fixture, Step kind, bool queued, ScriptedResultKind agentResult = ProviderTimedOutTerminated, int recipes = 2)
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture, recipes);
        var plan = await scene.AddAcceptedRootPlanAsync();
        var script = Script(kind, agentResult);
        if (queued)
        {
            await scene.QueueAsync(plan.ProposalId);
            await new LocalExecutionQueueHost((scene, script)).RunUntilAllEndedAsync();
        }
        else
        {
            await scene.ArmAndDriveAsync(plan.ProposalId, script);
        }

        var sequence = await scene.ReadSequenceAsync();
        var target = await scene.LastLinkedStepAsync();
        Assert.Equal(kind, target.Kind);
        return new Ended(scene, sequence, target);
    }

    /// <summary>Drives a cycle under an arbitrary script (at most <paramref name="maximumTicks"/> ticks) and returns the scene with its
    /// last linked step.</summary>
    public static async Task<Ended> DriveAsync(
        SqliteDatabaseFixture fixture, LocalExecutionStepScript script, bool queued = false, int recipes = 2, int maximumTicks = 120)
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture, recipes);
        var plan = await scene.AddAcceptedRootPlanAsync();
        if (queued)
        {
            await scene.QueueAsync(plan.ProposalId);
            await new LocalExecutionQueueHost((scene, script)).RunUntilAllEndedAsync();
        }
        else
        {
            var armed = await scene.ArmAsync(plan.ProposalId);
            await scene.DriveAsync(armed.SequenceId, script, maximumTicks);
        }

        return new Ended(scene, await scene.ReadSequenceAsync(), await scene.LastLinkedStepAsync());
    }

    public sealed record Ownership(WorkspaceExecutionStatus Workspace, int? MutatingOwners, bool AbandonmentBlocked);

    /// <summary>The three shared ownership readings: the workspace policy, the global capacity policy and the abandonment authority.</summary>
    public static async Task<Ownership> ReadOwnershipAsync(SqliteDatabaseFixture fixture, LocalExecutionSequenceTestScene scene)
    {
        await using var context = fixture.CreateContext();
        var workspace = await WorkspaceExecutionPolicy.AssessAsync(context, scene.ProjectId, scene.WorkspaceId, null, null, CancellationToken.None);
        var capacity = await ExecutionCapacityPolicy.ReadAsync(context, null, CancellationToken.None);
        var blocker = await RunAbandonmentAuthority.FindBlockerAsync(context, scene.ProjectId, CancellationToken.None);
        return new Ownership(workspace.Status, capacity.RecordedOwners, blocker is not null);
    }

    /// <summary>Whether the abandonment authority blocks the given project (the run abandonment policy's own database-side reading).</summary>
    public static async Task<bool> ReadAbandonmentBlockedAsync(SqliteDatabaseFixture fixture, Guid projectId)
    {
        await using var context = fixture.CreateContext();
        return await RunAbandonmentAuthority.FindBlockerAsync(context, projectId, CancellationToken.None) is not null;
    }

    public static WorkspaceExecutionStatus HeldWorkspace(Step kind) =>
        IsVerification(kind) ? WorkspaceExecutionStatus.VerificationActive : WorkspaceExecutionStatus.AgentAttemptActive;
}
