using Devalente.Shared.Results;
using DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetCodexModelCatalog;
using DevalCopilot.Application.Features.Projects.Commands.CaptureGitWorkspaceCheckpoint;
using DevalCopilot.Application.Features.Projects.Commands.ClaimVerificationExecution;
using DevalCopilot.Application.Features.Projects.Commands.ConfigureVerificationCommand;
using DevalCopilot.Application.Features.Projects.Commands.DeleteVerificationCommand;
using DevalCopilot.Application.Features.Projects.Commands.UpdateVerificationCommand;
using DevalCopilot.Application.Features.Runs.Commands.AbandonManualRun;
using DevalCopilot.Application.Features.Runs.Commands.CreateChallengeResolutionAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateClaudeCriticalReviewAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodeReviewAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodexPlanningAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateDiagnosisCorrectionAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateImplementationAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateReviewCorrectionAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateVerificationDiagnosisAttempt;
using DevalCopilot.Application.Features.Runs.Commands.RequestLocalCommit;
using DevalCopilot.Application.Features.Runs.Commands.SetClaudeModelPreference;
using DevalCopilot.Application.Features.Runs.Commands.SetClaudeMutationTurnLimit;
using DevalCopilot.Application.Features.Runs.Commands.SetCodexAccountUsageStop;
using DevalCopilot.Application.Features.Runs.Commands.SetCodexAccountUsageWarning;
using DevalCopilot.Application.Features.Runs.Commands.SetCodexAssignmentPreference;
using DevalCopilot.Application.Features.Runs.Commands.SetTokenStopThreshold;
using DevalCopilot.Application.Features.Runs.Commands.SetTokenWarningThreshold;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionSequence;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences.ScriptedResultKind;
using Step = DevalCopilot.Domain.Features.Runs.LocalExecutionStepKind;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;

/// <summary>
/// The manual-control fence of a local execution sequence (ADR-0060), exercised through the exact manual entrances it must withhold:
/// every unrelated Agent claim of the run, workspace verification, local commit, manual checkpoint capture, abandonment, recipe
/// create/update/delete and the assignment, Claude turn, token stop and Codex account stop settings. Warnings are never fenced. The
/// fence stays held after the sequence ended while a linked operation is unresolved or quarantined, and releases only after coherent
/// host exit proof; it never releases an operation and offers no recovery.
/// </summary>
public sealed class LocalExecutionSequenceFenceTests : IAsyncLifetime
{
    private const string DatabaseGuard = "database_guard";
    private const string Succeeded = "succeeded";

    public static IEnumerable<object[]> FencedEntrances() => new[]
    {
        "implementation", "code_review", "verification_diagnosis", "review_correction", "diagnosis_correction", "codex_planning",
        "claude_critical_review", "challenge_resolution", "verification", "abandon", "checkpoint_capture", "recipe_create",
        "recipe_update", "recipe_delete", "codex_assignment", "claude_model", "claude_turns", "token_stop", "codex_account_stop",
    }.Select(name => new object[] { name });

    private readonly SqliteDatabaseFixture fixture = new();
    private Guid challengedReviewId;
    private Guid unreviewedProposalId;

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    private async Task<(LocalExecutionSequenceTestScene Scene, LocalExecutionSequenceTestScene.SeededPlan Plan, Guid SequenceId)> ArmedAsync()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture, recipeCount: 2);
        var plan = await scene.AddAcceptedRootPlanAsync();
        challengedReviewId = (await scene.AddChallengedPlanAsync()).ReviewAttemptId!.Value;
        unreviewedProposalId = await scene.AddNewerPlannerProposalAsync();
        return (scene, plan, (await scene.ArmAsync(plan.ProposalId)).SequenceId);
    }

    /// <summary>Runs one manual entrance exactly as its host pipeline does and reports the stable refusal code, or
    /// <see cref="DatabaseGuard"/> when only the database guard refused the save, or <see cref="Succeeded"/>.</summary>
    private async Task<string> RunAsync(LocalExecutionSequenceTestScene scene, LocalExecutionSequenceTestScene.SeededPlan plan, string entrance)
    {
        var recipe = (await scene.ReadRecipesAsync())[0];
        try
        {
            return entrance switch
            {
                "implementation" => Code(await Manual(ctx => new CreateImplementationAttemptCommandHandler(
                    ctx, scene.Evidence, scene.Artifacts, scene.Clock, scene.AttemptProbe).HandleAsync(
                    new CreateImplementationAttemptCommand(scene.RunId, plan.ProposalId), CancellationToken.None))),
                "code_review" => Code(await Manual(ctx => new CreateCodeReviewAttemptCommandHandler(
                    ctx, scene.Evidence, scene.Artifacts, scene.Clock, scene.AttemptProbe).HandleAsync(
                    new CreateCodeReviewAttemptCommand(scene.RunId, Guid.NewGuid()), CancellationToken.None))),
                "verification_diagnosis" => Code(await Manual(ctx => new CreateVerificationDiagnosisAttemptCommandHandler(
                    ctx, scene.Evidence, scene.Artifacts, scene.Clock, scene.AttemptProbe).HandleAsync(
                    new CreateVerificationDiagnosisAttemptCommand(scene.RunId, Guid.NewGuid()), CancellationToken.None))),
                "review_correction" => Code(await Manual(ctx => new CreateReviewCorrectionAttemptCommandHandler(
                    ctx, scene.Evidence, scene.Artifacts, scene.Clock).HandleAsync(
                    new CreateReviewCorrectionAttemptCommand(scene.RunId, Guid.NewGuid()), CancellationToken.None))),
                "diagnosis_correction" => Code(await Manual(ctx => new CreateDiagnosisCorrectionAttemptCommandHandler(
                    ctx, scene.Evidence, scene.Artifacts, scene.Clock, scene.AttemptProbe).HandleAsync(
                    new CreateDiagnosisCorrectionAttemptCommand(scene.RunId, Guid.NewGuid()), CancellationToken.None))),
                "codex_planning" => Code(await Manual(ctx => new CreateCodexPlanningAttemptCommandHandler(
                    ctx, scene.Evidence, scene.Artifacts, scene.Clock, scene.AttemptProbe).HandleAsync(
                    new CreateCodexPlanningAttemptCommand(scene.RunId), CancellationToken.None))),
                "claude_critical_review" => Code(await Manual(ctx => new CreateClaudeCriticalReviewAttemptCommandHandler(
                    ctx, scene.Evidence, scene.Artifacts, scene.Clock, scene.AttemptProbe).HandleAsync(
                    new CreateClaudeCriticalReviewAttemptCommand(scene.RunId, unreviewedProposalId), CancellationToken.None))),
                "challenge_resolution" => Code(await Manual(ctx => new CreateChallengeResolutionAttemptCommandHandler(
                    ctx, scene.Evidence, scene.Artifacts, scene.Clock, scene.AttemptProbe).HandleAsync(
                    new CreateChallengeResolutionAttemptCommand(scene.RunId, challengedReviewId), CancellationToken.None))),
                "verification" => Code(await Manual(ctx => new ClaimVerificationExecutionCommandHandler(ctx, scene.Evidence, scene.Clock).HandleAsync(
                    new ClaimVerificationExecutionCommand(scene.ProjectId, recipe.Id, scene.CheckpointId), CancellationToken.None))),
                "local_commit" => Code(await Manual(ctx => new RequestLocalCommitCommandHandler(ctx, new ThrowingCommitPreparer(), scene.Clock).HandleAsync(
                    new RequestLocalCommitCommand(scene.RunId, Guid.NewGuid(), scene.CheckpointId, Guid.NewGuid(), Guid.NewGuid(), "message"),
                    CancellationToken.None))),
                "abandon" => Code(await Manual(ctx => new AbandonManualRunCommandHandler(ctx, scene.Clock).HandleAsync(
                    new AbandonManualRunCommand(scene.RunId, "No longer needed."), CancellationToken.None))),
                "checkpoint_capture" => Code(await Manual(ctx => new CaptureGitWorkspaceCheckpointCommandHandler(ctx, scene.Evidence, scene.Clock).HandleAsync(
                    new CaptureGitWorkspaceCheckpointCommand(scene.ProjectId), CancellationToken.None))),
                "recipe_create" => Code(await scene.PipelineAsync(ctx => new ConfigureVerificationCommandCommandHandler(ctx, scene.Clock).HandleAsync(
                    new ConfigureVerificationCommandCommand(scene.ProjectId, "Extra", @"C:\tools\extra.exe", ["--x"], 60, true), CancellationToken.None))),
                "recipe_update" => Code(await scene.PipelineAsync(ctx => new UpdateVerificationCommandCommandHandler(ctx, scene.Clock).HandleAsync(
                    new UpdateVerificationCommandCommand(
                        scene.ProjectId, recipe.Id, recipe.Name, recipe.ExecutablePath, recipe.Arguments, recipe.TimeoutSeconds + 1, true),
                    CancellationToken.None))),
                "recipe_delete" => Code(await scene.PipelineAsync(ctx => new DeleteVerificationCommandCommandHandler(ctx).HandleAsync(
                    new DeleteVerificationCommandCommand(scene.ProjectId, recipe.Id), CancellationToken.None))),
                "codex_assignment" => Code(await Manual(ctx => new SetCodexAssignmentPreferenceCommandHandler(
                    ctx, new ThrowingCatalogAdapter(), scene.Clock).HandleAsync(
                    new SetCodexAssignmentPreferenceCommand(scene.RunId, "gpt-5", "high"), CancellationToken.None))),
                "claude_model" => Code(await Manual(ctx => new SetClaudeModelPreferenceCommandHandler(ctx, scene.Clock).HandleAsync(
                    new SetClaudeModelPreferenceCommand(scene.RunId, "sonnet", "high"), CancellationToken.None))),
                "claude_turns" => Code(await Manual(ctx => new SetClaudeMutationTurnLimitCommandHandler(ctx, scene.Clock).HandleAsync(
                    new SetClaudeMutationTurnLimitCommand(scene.RunId, 5), CancellationToken.None))),
                "token_stop" => Code(await Manual(ctx => new SetTokenStopThresholdCommandHandler(ctx, scene.Clock).HandleAsync(
                    new SetTokenStopThresholdCommand(scene.RunId, "Codex", 1_000_000), CancellationToken.None))),
                "codex_account_stop" => Code(await Manual(ctx => new SetCodexAccountUsageStopCommandHandler(ctx, scene.Clock).HandleAsync(
                    new SetCodexAccountUsageStopCommand(scene.RunId, 80), CancellationToken.None))),
                "codex_account_warning" => Code(await Manual(ctx => new SetCodexAccountUsageWarningCommandHandler(ctx, scene.Clock).HandleAsync(
                    new SetCodexAccountUsageWarningCommand(scene.RunId, 50), CancellationToken.None))),
                "token_warning" => Code(await Manual(ctx => new SetTokenWarningThresholdCommandHandler(ctx, scene.Clock).HandleAsync(
                    new SetTokenWarningThresholdCommand(scene.RunId, "Codex", 500_000), CancellationToken.None))),
                _ => throw new ArgumentOutOfRangeException(nameof(entrance), entrance, null),
            };
        }
        catch (DbUpdateException)
        {
            return DatabaseGuard;
        }
    }

    private async Task<Result<T>> Manual<T>(Func<Infrastructure.Persistence.DevalCopilotDbContext, Task<Result<T>>> handle)
    {
        await using var context = fixture.CreateContext();
        return await handle(context);
    }

    private static string Code<T>(Result<T> result) => result.IsSuccess ? Succeeded : result.Errors[0].Code;

    private static string Code(Result result) => result.IsSuccess ? Succeeded : result.Errors[0].Code;

    [Theory]
    [MemberData(nameof(FencedEntrances))]
    public async Task A_live_sequence_withholds_the_manual_entrance_and_changes_nothing(string entrance)
    {
        var (scene, plan, _) = await ArmedAsync();
        var before = await scene.CountWorkAsync();
        var recipesBefore = (await scene.ReadRecipesAsync()).Select(recipe => (recipe.Id, recipe.UpdatedAtUtc, recipe.TimeoutSeconds)).ToList();
        var runBefore = await scene.ReadRunAsync();

        var outcome = await RunAsync(scene, plan, entrance);

        // The readable refusal is the stable fence code; where an entrance has no early read the database guard alone must refuse.
        // The three read-only planning claims have no early fence read: the database guard refuses their save and their handler reports
        // its own fixed persistence refusal. Every other entrance reads the fence first.
        var planningClaim = entrance is "codex_planning" or "claude_critical_review" or "challenge_resolution";
        Assert.Contains(
            outcome,
            new[] { LocalExecutionSequenceErrors.ManualControlBlockedCode, DatabaseGuard }.Concat(planningClaim ? ["attempts.persistence_failed"] : []));
        Assert.Equal(before.Attempts, (await scene.CountWorkAsync()).Attempts);
        Assert.Equal(0, (await scene.CountWorkAsync()).Executions);
        Assert.Equal(recipesBefore, (await scene.ReadRecipesAsync()).Select(recipe => (recipe.Id, recipe.UpdatedAtUtc, recipe.TimeoutSeconds)).ToList());
        var runAfter = await scene.ReadRunAsync();
        Assert.Equal(runBefore.Lifecycle, runAfter.Lifecycle);
        Assert.Equal(runBefore.RequestedClaudeModel, runAfter.RequestedClaudeModel);
        Assert.Equal(runBefore.CodexTokenStopThreshold, runAfter.CodexTokenStopThreshold);
        Assert.Equal(LocalExecutionSequenceState.Armed, (await scene.ReadSequenceAsync()).State);
    }

    [Fact]
    public async Task A_live_sequence_withholds_a_local_commit_at_its_workspace_fence_and_at_the_database_seam()
    {
        var (scene, _, _) = await ArmedAsync();
        await using var read = fixture.CreateContext();
        Assert.True(await LocalExecutionSequenceFence.IsWorkspaceBlockedAsync(read, scene.WorkspaceId, CancellationToken.None));
        Assert.Equal(
            LocalExecutionSequenceErrors.ManualControlBlockedCode,
            (await LocalExecutionSequenceFence.CheckWorkspaceAsync(read, scene.WorkspaceId, CancellationToken.None))!.Code);
        Assert.True(await LocalExecutionSequenceFence.IsProjectBlockedAsync(read, scene.ProjectId, CancellationToken.None));
        Assert.True(await LocalExecutionSequenceFence.IsRunBlockedAsync(read, scene.RunId, CancellationToken.None));
        Assert.False(await LocalExecutionSequenceFence.IsRunBlockedAsync(read, Guid.NewGuid(), CancellationToken.None));

        // Neither a commit operation nor its checkpoint rows can be written for the fenced workspace.
        await using var db = fixture.CreateContext();
        var project = await db.Projects.SingleAsync(candidate => candidate.Id == scene.ProjectId);
        var workspace = await db.GitWorkspaces.SingleAsync(candidate => candidate.Id == scene.WorkspaceId);
        var lease = await db.RepositoryMutationLeases.SingleAsync(candidate => candidate.WorkspaceId == scene.WorkspaceId);
        var refused = await Assert.ThrowsAsync<DbUpdateException>(() => LocalCommitRowsSeed.AddOperationAsync(
            db, project, workspace, lease, LocalCommitStatus.Prepared, LocalExecutionSequenceTestScene.Head, new string('d', 40)));
        Assert.Contains(LocalExecutionSequenceErrors.ManualControlBlockedCode, refused.InnerException!.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("codex_account_warning")]
    [InlineData("token_warning")]
    public async Task Warnings_are_not_fenced(string entrance)
    {
        var (scene, plan, _) = await ArmedAsync();

        var outcome = await RunAsync(scene, plan, entrance);

        Assert.Equal(Succeeded, outcome);
    }

    [Theory]
    [InlineData("abandon")]
    [InlineData("recipe_update")]
    [InlineData("claude_turns")]
    [InlineData("checkpoint_capture")]
    [InlineData("verification")]
    public async Task A_stopped_sequence_without_a_linked_operation_releases_the_fence(string entrance)
    {
        var (scene, plan, sequenceId) = await ArmedAsync();
        await scene.StopAsync(sequenceId);
        Assert.Equal(LocalExecutionSequenceManualFence.Released, (await scene.ReadStatusAsync()).ManualFence);

        var outcome = await RunAsync(scene, plan, entrance);

        Assert.NotEqual(LocalExecutionSequenceErrors.ManualControlBlockedCode, outcome);
        Assert.NotEqual(DatabaseGuard, outcome);
    }

    [Fact]
    public async Task A_sequence_awaiting_a_step_result_holds_the_fence_for_the_exact_linked_operation_only()
    {
        var (scene, plan, sequenceId) = await ArmedAsync();
        var ticket = await scene.ReserveAndClaimAsync(sequenceId);

        // The linked operation still dispatches and records its result under the fence (exact link exemption).
        await scene.DispatchAsync(ticket.Kind, ticket.ReservedOperationId);
        await scene.RecordImplementationAsync(ticket.ReservedOperationId, Implemented);

        Assert.Equal(LocalExecutionSequenceManualFence.Blocked, (await scene.ReadStatusAsync()).ManualFence);
        Assert.Equal(LocalExecutionSequenceErrors.ManualControlBlockedCode, await RunAsync(scene, plan, "abandon"));
    }

    [Fact]
    public async Task An_ended_sequence_with_a_quarantined_linked_operation_keeps_the_fence_and_offers_no_recovery()
    {
        var (scene, plan, sequenceId) = await ArmedAsync();
        await scene.DriveAsync(sequenceId, new LocalExecutionStepScript().On(Step.Implementation, ProviderFailedNoExit));
        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, (await scene.ReadSequenceAsync()).State);

        foreach (var entrance in new[] { "abandon", "recipe_delete", "claude_model", "codex_planning", "implementation" })
        {
            var outcome = await RunAsync(scene, plan, entrance);
            Assert.NotEqual(Succeeded, outcome);
        }

        var status = await scene.ReadStatusAsync();
        Assert.Equal(LocalExecutionSequenceManualFence.Blocked, status.ManualFence);
        Assert.True(status.Sequence!.AwaitingReconciliation);
        Assert.Equal("no_supported_recovery_action_in_this_version", status.Sequence.NextActionCode);
    }

    [Fact]
    public async Task An_ended_sequence_whose_linked_operation_recorded_coherent_exit_proof_releases_the_fence()
    {
        var (scene, plan, sequenceId) = await ArmedAsync();
        await scene.DriveAsync(sequenceId, new LocalExecutionStepScript().On(Step.Implementation, ProviderFailedWithExit));
        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, (await scene.ReadSequenceAsync()).State);

        var status = await scene.ReadStatusAsync();

        Assert.Equal(LocalExecutionSequenceManualFence.Released, status.ManualFence);
        Assert.False(status.Sequence!.AwaitingReconciliation);
        var outcome = await RunAsync(scene, plan, "claude_turns");
        Assert.NotEqual(LocalExecutionSequenceErrors.ManualControlBlockedCode, outcome);
        Assert.NotEqual(DatabaseGuard, outcome);
    }

    [Fact]
    public async Task A_dispatched_verification_without_exit_proof_keeps_the_fence_after_the_sequence_ended()
    {
        var (scene, _, sequenceId) = await ArmedAsync();
        var script = new LocalExecutionStepScript().Verify(1, 0, VerificationTimedOut);
        await scene.DriveAsync(sequenceId, script);

        var status = await scene.ReadStatusAsync();

        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, status.State);
        Assert.Equal(LocalExecutionSequenceManualFence.Blocked, status.ManualFence);
        Assert.Contains(status.Sequence!.Steps, receipt => receipt.Kind == Step.Verification && receipt.Quarantined);
    }

    [Fact]
    public async Task An_unresolved_preparation_keeps_the_fence_even_though_the_sequence_ended()
    {
        var (scene, _, sequenceId) = await ArmedAsync();
        var ticket = await scene.ObserveReservingAsync(sequenceId);
        await scene.ResolveAsync(
            ticket, "attempts.persistence_unresolved", new ConfigurableLocalExecutionStepProbe(Application.Data.LocalExecutionStepProbeResult.Unresolved));

        var status = await scene.ReadStatusAsync();

        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, status.State);
        Assert.Equal(LocalExecutionSequenceManualFence.Blocked, status.ManualFence);
    }

    [Fact]
    public async Task Another_projects_work_and_history_reads_are_never_fenced()
    {
        var (scene, _, _) = await ArmedAsync();
        await using var other = new SqliteDatabaseFixture();
        await other.InitializeAsync();
        var unrelated = await LocalExecutionSequenceTestScene.CreateAsync(other, recipeCount: 1);

        await using var context = other.CreateContext();
        var result = await new UpdateVerificationCommandCommandHandler(context, unrelated.Clock).HandleAsync(
            new UpdateVerificationCommandCommand(
                unrelated.ProjectId, (await unrelated.ReadRecipesAsync())[0].Id, "Renamed", @"C:\tools\renamed.exe", ["x"], 90, true),
            CancellationToken.None);

        Assert.True(result.IsSuccess || result.Errors[0].Code != LocalExecutionSequenceErrors.ManualControlBlockedCode);
        // Reading history while fenced is always available.
        Assert.Equal(LocalExecutionSequenceManualFence.Blocked, (await scene.ReadStatusAsync()).ManualFence);
        await other.DisposeAsync();
    }

    private sealed class ThrowingCommitPreparer : ILocalCommitPreparer
    {
        public Task<LocalCommitPreparationResult> PrepareAsync(LocalCommitPreparationRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A fenced local commit must not reach its preparer.");
    }

    private sealed class ThrowingCatalogAdapter : ICodexModelCatalogAdapter
    {
        public Task<CodexModelCatalogObservation> ObserveAsync(string executablePath, string? scriptPath, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A fenced assignment change must not reach the catalog.");
    }
}
