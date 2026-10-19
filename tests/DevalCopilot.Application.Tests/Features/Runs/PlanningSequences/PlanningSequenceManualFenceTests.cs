using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Projects.Commands.ClaimVerificationExecution;
using DevalCopilot.Application.Features.Runs.Commands.AbandonManualRun;
using DevalCopilot.Application.Features.Runs.Commands.CreateChallengeResolutionAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateClaudeCriticalReviewAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodeReviewAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodexPlanningAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateDiagnosisCorrectionAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateImplementationAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateReviewCorrectionAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateVerificationDiagnosisAttempt;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.SecondChallengeRoundTestSupport;

namespace DevalCopilot.Application.Tests.Features.Runs.PlanningSequences;

/// <summary>
/// ADR-0055 manual coexistence. While a sequence can advance, drains a claimed step or has an unresolved linked attempt, every unrelated
/// Agent claim path (including each repair variant), verification, local commit and abandonment is refused, early before any external
/// work and again atomically at the durable seam by the database guard, in both directions of the race with arming. An unrelated
/// project is never blocked.
/// </summary>
public sealed class PlanningSequenceManualFenceTests : IAsyncLifetime
{
    private const string Blocked = PlanningSequenceErrors.ManualControlBlockedCode;

    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    public static TheoryData<string> ManualClaimPaths => new()
    {
        "codex-planning",
        "codex-planning-repair",
        "claude-critical-review",
        "claude-critical-review-repair",
        "challenge-resolution",
        "challenge-resolution-repair",
        "code-review",
        "code-review-repair",
        "implementation",
        "review-correction",
        "verification-diagnosis",
        "diagnosis-correction",
    };

    private async Task<Result> ManualClaimAsync(PlanningSequenceTestScene scene, string path)
    {
        await using var db = fixture.CreateContext();
        var evidence = scene.Evidence;
        var store = scene.Artifacts;
        var time = scene.Clock;
        var probe = scene.AttemptProbe;
        var run = scene.RunId;
        var any = Guid.NewGuid();
        switch (path)
        {
            case "codex-planning":
            case "codex-planning-repair":
                return Strip(await new CreateCodexPlanningAttemptCommandHandler(db, evidence, store, time, probe).HandleAsync(
                    new CreateCodexPlanningAttemptCommand(run, path.EndsWith("repair", StringComparison.Ordinal) ? any : null), default));
            case "claude-critical-review":
                return Strip(await new CreateClaudeCriticalReviewAttemptCommandHandler(db, evidence, store, time, probe).HandleAsync(
                    new CreateClaudeCriticalReviewAttemptCommand(run, any), default));
            case "claude-critical-review-repair":
                return Strip(await new CreateClaudeCriticalReviewAttemptCommandHandler(db, evidence, store, time, probe).HandleAsync(
                    CreateClaudeCriticalReviewAttemptCommand.ForRepair(run, any), default));
            case "challenge-resolution":
                return Strip(await new CreateChallengeResolutionAttemptCommandHandler(db, evidence, store, time, probe).HandleAsync(
                    new CreateChallengeResolutionAttemptCommand(run, any), default));
            case "challenge-resolution-repair":
                return Strip(await new CreateChallengeResolutionAttemptCommandHandler(db, evidence, store, time, probe).HandleAsync(
                    CreateChallengeResolutionAttemptCommand.ForRepair(run, any), default));
            case "code-review":
                return Strip(await new CreateCodeReviewAttemptCommandHandler(db, evidence, store, time, probe).HandleAsync(
                    new CreateCodeReviewAttemptCommand(run, any), default));
            case "code-review-repair":
                return Strip(await new CreateCodeReviewAttemptCommandHandler(db, evidence, store, time, probe).HandleAsync(
                    CreateCodeReviewAttemptCommand.ForRepair(run, any), default));
            case "implementation":
                return Strip(await new CreateImplementationAttemptCommandHandler(db, evidence, store, time, probe).HandleAsync(
                    new CreateImplementationAttemptCommand(run, any), default));
            case "review-correction":
                return Strip(await new CreateReviewCorrectionAttemptCommandHandler(db, evidence, store, time).HandleAsync(
                    new CreateReviewCorrectionAttemptCommand(run, any), default));
            case "verification-diagnosis":
                return Strip(await new CreateVerificationDiagnosisAttemptCommandHandler(db, evidence, store, time, probe).HandleAsync(
                    new CreateVerificationDiagnosisAttemptCommand(run, any), default));
            default:
                return Strip(await new CreateDiagnosisCorrectionAttemptCommandHandler(db, evidence, store, time, probe).HandleAsync(
                    new CreateDiagnosisCorrectionAttemptCommand(run, any), default));
        }
    }

    private static Result Strip<T>(Result<T> result) => result.IsSuccess ? Result.Success() : Result.Failure(result.Errors);

    [Theory]
    [MemberData(nameof(ManualClaimPaths))]
    public async Task Every_manual_agent_claim_path_is_refused_early_while_a_sequence_holds_the_run(string path)
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        await scene.ArmAsync();
        var capturesAfterArming = scene.Evidence.Captures;

        var result = await ManualClaimAsync(scene, path);

        Assert.True(result.IsFailure);
        Assert.Equal(Blocked, result.Errors[0].Code);
        Assert.Equal(capturesAfterArming, scene.Evidence.Captures);
        Assert.Equal(0, scene.Artifacts.Seals);
        Assert.Empty(await scene.ReadAttemptsAsync());
    }

    [Fact]
    public async Task A_manual_claim_that_lost_the_race_to_arming_at_the_seam_is_refused_by_the_database_guard_and_persists_nothing()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        await using var raced = fixture.CreateContext();
        var injecting = new PlanningSequenceEvidenceReader(onCapture: async () =>
        {
            // The run is still pristine when the manual claim started: arming wins between the claim's reads and its commit.
            var armed = await scene.TryArmAsync();
            Assert.True(armed.IsSuccess, armed.IsFailure ? armed.Errors[0].Code : null);
        });

        var result = Strip(await new CreateCodexPlanningAttemptCommandHandler(
            raced, injecting, scene.Artifacts, scene.Clock, scene.AttemptProbe).HandleAsync(new CreateCodexPlanningAttemptCommand(scene.RunId), default));

        Assert.True(result.IsFailure);
        Assert.Equal(Blocked, result.Errors[0].Code);
        Assert.Empty(await scene.ReadAttemptsAsync());
        var sequence = Assert.Single(await ReadSequencesAsync());
        Assert.Equal(PlanningSequenceState.Armed, sequence.State);
        Assert.Single(scene.Artifacts.DeletedSealedFiles);
        await using var context = fixture.CreateContext();
        Assert.Empty(await context.Artifacts.AsNoTracking().Where(artifact => artifact.RunId == scene.RunId).ToListAsync());
        Assert.Equal(RunLifecycle.Created, (await ReadRunAsync(scene.RunId)).Lifecycle);
    }

    [Fact]
    public async Task Verification_is_refused_for_the_pinned_workspace_while_a_sequence_holds_it()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        VerificationCommand recipe;
        await using (var context = fixture.CreateContext())
        {
            var project = await context.Projects.SingleAsync(candidate => candidate.Id == scene.ProjectId);
            recipe = VerificationCommand.Configure(
                Guid.NewGuid(), project.Id, project.ReserveVerificationCommandNumber(), "Tests", @"C:\dotnet.exe", ["test"], 60, true,
                PlanningSequenceTestScene.Start);
            context.VerificationCommands.Add(recipe);
            await context.SaveChangesAsync();
        }

        await scene.ArmAsync();
        var capturesAfterArming = scene.Evidence.Captures;

        await using var db = fixture.CreateContext();
        var result = await new ClaimVerificationExecutionCommandHandler(db, scene.Evidence, scene.Clock).HandleAsync(
            new ClaimVerificationExecutionCommand(scene.ProjectId, recipe.Id, scene.CheckpointId), default);

        Assert.True(result.IsFailure);
        Assert.Equal(Blocked, result.Errors[0].Code);
        Assert.Equal(capturesAfterArming, scene.Evidence.Captures);
        Assert.Empty(await fixture.CreateContext().VerificationExecutions.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Verification_that_won_the_race_refuses_arming_and_one_that_arrives_after_arming_is_refused_by_the_database_guard()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        VerificationCommand recipe;
        await using (var context = fixture.CreateContext())
        {
            var project = await context.Projects.SingleAsync(candidate => candidate.Id == scene.ProjectId);
            recipe = VerificationCommand.Configure(
                Guid.NewGuid(), project.Id, project.ReserveVerificationCommandNumber(), "Tests", @"C:\dotnet.exe", ["test"], 60, true,
                PlanningSequenceTestScene.Start);
            context.VerificationCommands.Add(recipe);
            await context.SaveChangesAsync();
        }

        // Verification first: a running execution of the project makes arming refuse.
        await using (var db = fixture.CreateContext())
        {
            var claimed = await new ClaimVerificationExecutionCommandHandler(db, scene.Evidence, scene.Clock).HandleAsync(
                new ClaimVerificationExecutionCommand(scene.ProjectId, recipe.Id, scene.CheckpointId), default);
            Assert.True(claimed.IsSuccess, claimed.IsFailure ? claimed.Errors[0].Code : null);
        }

        var refused = await scene.TryArmAsync();
        Assert.Equal(PlanningSequenceErrors.ProjectBusyCode, refused.Errors[0].Code);

        // The database guard refuses a verification execution row inserted for a workspace a live sequence pins.
        var other = await PlanningSequenceTestScene.CreateAsync(fixture);
        await other.ArmAsync();
        await using var guard = fixture.CreateContext();
        var workspace = await guard.GitWorkspaces.AsNoTracking().SingleAsync(candidate => candidate.Id == other.WorkspaceId);
        var checkpoint = await guard.GitCheckpoints.AsNoTracking().SingleAsync(candidate => candidate.Id == other.CheckpointId);
        var otherRecipe = VerificationCommand.Configure(
            Guid.NewGuid(), other.ProjectId, 1, "Tests", @"C:\dotnet.exe", ["test"], 60, true, PlanningSequenceTestScene.Start);
        guard.VerificationCommands.Add(otherRecipe);
        guard.VerificationExecutions.Add(VerificationExecution.Claim(
            Guid.NewGuid(), other.ProjectId, 1, workspace, checkpoint, otherRecipe, PlanningSequenceTestScene.Start));
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => guard.SaveChangesAsync());
        Assert.Contains(Blocked, exception.GetBaseException().Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_local_commit_operation_is_refused_by_the_database_guard_for_a_workspace_a_live_sequence_pins()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        await scene.ArmAsync();

        await using var context = fixture.CreateContext();
        var project = await context.Projects.SingleAsync(candidate => candidate.Id == scene.ProjectId);
        var workspace = await context.GitWorkspaces.SingleAsync(candidate => candidate.Id == scene.WorkspaceId);
        var lease = await context.RepositoryMutationLeases.SingleAsync(candidate => candidate.WorkspaceId == scene.WorkspaceId);

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => LocalCommitRowsSeed.AddOperationAsync(
            context, project, workspace, lease, LocalCommitStatus.Executing, new string('a', 40), new string('d', 40)));
        Assert.Contains(Blocked, exception.GetBaseException().Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Abandonment_is_refused_while_automatic_authority_remains_live_and_admitted_after_it_closes()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();

        await using (var db = fixture.CreateContext())
        {
            var refused = await new AbandonManualRunCommandHandler(db, scene.Clock).HandleAsync(
                new AbandonManualRunCommand(scene.RunId, "Not needed."), default);
            Assert.True(refused.IsFailure);
            Assert.Equal(Blocked, refused.Errors[0].Code);
        }

        Assert.Equal(RunLifecycle.Created, (await ReadRunAsync(scene.RunId)).Lifecycle);

        Assert.True((await scene.StopAsync(armed.SequenceId)).IsSuccess);

        await using var released = fixture.CreateContext();
        var admitted = await new AbandonManualRunCommandHandler(released, scene.Clock).HandleAsync(
            new AbandonManualRunCommand(scene.RunId, "Not needed."), default);
        Assert.True(admitted.IsSuccess, admitted.IsFailure ? admitted.Errors[0].Code : null);
        Assert.Equal(RunLifecycle.Abandoned, (await ReadRunAsync(scene.RunId)).Lifecycle);
    }

    [Fact]
    public async Task Abandonment_that_wins_the_race_refuses_arming_and_a_sequence_blocks_the_database_transition()
    {
        var abandonedFirst = await PlanningSequenceTestScene.CreateAsync(fixture);
        await using (var db = fixture.CreateContext())
        {
            var abandoned = await new AbandonManualRunCommandHandler(db, abandonedFirst.Clock).HandleAsync(
                new AbandonManualRunCommand(abandonedFirst.RunId, "Stop."), default);
            Assert.True(abandoned.IsSuccess);
        }

        Assert.Equal(PlanningSequenceErrors.RunNotPristineCode, (await abandonedFirst.TryArmAsync()).Errors[0].Code);

        var armedFirst = await PlanningSequenceTestScene.CreateAsync(fixture);
        await armedFirst.ArmAsync();
        await using var raw = fixture.CreateContext();
        var run = await raw.Runs.SingleAsync(candidate => candidate.Id == armedFirst.RunId);
        run.Abandon("Stop.", PlanningSequenceTestScene.Start);
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => raw.SaveChangesAsync());
        Assert.Contains(Blocked, exception.GetBaseException().Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unrelated_project_is_never_blocked_by_a_live_sequence()
    {
        var held = await PlanningSequenceTestScene.CreateAsync(fixture);
        await held.ArmAsync();
        var unrelated = await PlanningSequenceTestScene.CreateAsync(fixture);

        var codex = await ManualClaimAsync(unrelated, "codex-planning");
        var armed = await unrelated.TryArmAsync();

        // The unrelated run passed the fence: it fails for its own reasons (no provider was set up for the claim), never as blocked.
        Assert.True(codex.IsSuccess || codex.Errors[0].Code != Blocked);
        Assert.True(armed.IsSuccess || armed.Errors[0].Code != Blocked);
    }

    [Fact]
    public async Task The_fence_releases_only_when_the_sequence_ended_and_no_linked_attempt_remains_running()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        var (_, claim) = await scene.ReserveAndClaimAsync(armed.SequenceId);

        Assert.Equal(Blocked, (await ManualClaimAsync(scene, "claude-critical-review")).Errors[0].Code);

        // Stopping retains the grant and the fence; only a drained, ended sequence releases it.
        Assert.True((await scene.StopAsync(armed.SequenceId)).IsSuccess);
        Assert.Equal(PlanningSequenceState.Stopping, (await scene.ReadSequenceAsync(armed.SequenceId)).State);
        Assert.Equal(Blocked, (await ManualClaimAsync(scene, "claude-critical-review")).Errors[0].Code);

        await scene.CompleteStepAsync(claim.AttemptId, StepResult.Proposed);
        Assert.True((await scene.ObserveAsync(armed.SequenceId)).IsSuccess);

        Assert.Equal(PlanningSequenceState.Stopped, (await scene.ReadSequenceAsync(armed.SequenceId)).State);
        var released = await ManualClaimAsync(scene, "claude-critical-review");
        Assert.NotEqual(Blocked, released.IsFailure ? released.Errors[0].Code : null);
    }

    [Fact]
    public async Task An_ended_sequence_with_a_still_running_linked_attempt_keeps_blocking_manual_claims()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        await scene.ReserveAndClaimAsync(armed.SequenceId);
        await using (var context = fixture.CreateContext())
        {
            // The coordinator observed an elapsed result deadline and ended the sequence; the attempt itself stays Running.
            var sequence = await context.PlanningSequences.SingleAsync(candidate => candidate.Id == armed.SequenceId);
            sequence.End(PlanningSequenceState.NeedsAttention, PlanningSequenceReason.ResultDeadlineElapsed, null, PlanningSequenceTestScene.Start);
            await context.SaveChangesAsync();
        }

        var result = await ManualClaimAsync(scene, "claude-critical-review");

        Assert.Equal(Blocked, result.Errors[0].Code);
        Assert.Equal(AttemptStatus.Running, Assert.Single(await scene.ReadAttemptsAsync()).Status);
    }

    private async Task<List<PlanningSequence>> ReadSequencesAsync()
    {
        await using var context = fixture.CreateContext();
        return await context.PlanningSequences.AsNoTracking().ToListAsync();
    }

    private async Task<Run> ReadRunAsync(Guid runId)
    {
        await using var context = fixture.CreateContext();
        return await context.Runs.AsNoTracking().SingleAsync(candidate => candidate.Id == runId);
    }
}
