using DevalCopilot.Application.Features.Projects.Commands.ClaimVerificationExecution;
using DevalCopilot.Application.Features.Projects.Commands.ReconcileInterruptedVerificationExecutions;
using DevalCopilot.Application.Features.Projects.Commands.RecoverInterruptedVerificationOutputArtifacts;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Commands.ReconcileInterruptedAgentAttempts;
using DevalCopilot.Application.Features.Runs.Commands.ReconcileInterruptedImplementationAttempts;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionSequence;
using DevalCopilot.Application.Features.Runs.Queries.GetRunningAgentAttempts;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences.ScriptedResultKind;
using Step = DevalCopilot.Domain.Features.Runs.LocalExecutionStepKind;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;

/// <summary>
/// Startup with the REAL recovery handlers (ADR-0060): the sequence closure runs first, then the ordinary reconciliation and artifact
/// recovery handlers see only unlinked rows. A linked dispatched operation stays Running with its owner, partial output, workspace and
/// run untouched; a proved-undispatched claim is closed with the dedicated refusal and no adapter call; tampered links quarantine
/// instead of falling back to generic recovery; unlinked rows keep their old recovery.
/// </summary>
public sealed class LocalExecutionRestartRecoveryTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    private async Task<(LocalExecutionSequenceTestScene Scene, Guid SequenceId)> ArmedAsync(int recipeCount = 2)
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture, recipeCount);
        var plan = await scene.AddAcceptedRootPlanAsync();
        return (scene, (await scene.ArmAsync(plan.ProposalId)).SequenceId);
    }

    /// <summary>The ordinary startup recovery handlers in the host's order, after the sequence closure.</summary>
    private async Task<RecordingStore> RecoverAsync(LocalExecutionSequenceTestScene scene)
    {
        var store = new RecordingStore();
        await scene.InterruptAsync();
        await using (var context = fixture.CreateContext())
        {
            var recovered = await new RecoverInterruptedVerificationOutputArtifactsCommandHandler(context, store, scene.Clock)
                .HandleAsync(new RecoverInterruptedVerificationOutputArtifactsCommand(), CancellationToken.None);
            Assert.True(recovered.IsSuccess);
        }

        await using (var context = fixture.CreateContext())
        {
            Assert.True((await new ReconcileInterruptedAgentAttemptsCommandHandler(context, scene.Clock)
                .HandleAsync(new ReconcileInterruptedAgentAttemptsCommand(), CancellationToken.None)).IsSuccess);
            await context.SaveChangesAsync();
        }

        await using (var context = fixture.CreateContext())
        {
            Assert.True((await new ReconcileInterruptedImplementationAttemptsCommandHandler(context, scene.Evidence, scene.Clock)
                .HandleAsync(new ReconcileInterruptedImplementationAttemptsCommand(), CancellationToken.None)).IsSuccess);
        }

        await using (var context = fixture.CreateContext())
        {
            Assert.True((await new ReconcileInterruptedVerificationExecutionsCommandHandler(context, scene.Clock)
                .HandleAsync(new ReconcileInterruptedVerificationExecutionsCommand(), CancellationToken.None)).IsSuccess);
            await context.SaveChangesAsync();
        }

        return store;
    }

    private async Task AssertWorkspaceAndRunUntouchedAsync(LocalExecutionSequenceTestScene scene)
    {
        Assert.Equal(RunLifecycle.Running, (await scene.ReadRunAsync()).Lifecycle);
        await using var context = fixture.CreateContext();
        Assert.Equal(WorkspaceStatus.Ready, (await context.GitWorkspaces.FindAsync(scene.WorkspaceId))!.Status);
    }

    [Fact]
    public async Task A_dispatched_mutation_with_an_unchanged_fingerprint_stays_running_and_nothing_is_sealed_or_imported()
    {
        var (scene, sequenceId) = await ArmedAsync();
        var ticket = await scene.ReserveAndClaimAsync(sequenceId);
        await scene.DispatchAgentAsync(ticket.ReservedOperationId);
        var before = await scene.FootprintAsync();

        await RecoverAsync(scene);

        var attempt = await scene.ReadAttemptAsync(ticket.ReservedOperationId);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Null(attempt.AgentOutcome);
        Assert.NotNull(attempt.AgentDispatchedAtUtc);
        await AssertWorkspaceAndRunUntouchedAsync(scene);
        var after = await scene.FootprintAsync();
        Assert.Equal(before.Attempts, after.Attempts);
        Assert.Equal(before.Artifacts, after.Artifacts);
        Assert.Equal(before.Seals, after.Seals);
        Assert.Equal(before.DeletedSeals, after.DeletedSeals);
        Assert.Equal(LocalExecutionSequenceManualFence.Blocked, (await scene.ReadStatusAsync()).ManualFence);
        // The ordinary artifact-recovery feed never lists a linked operation, so its partial output is never sealed or imported.
        await using var feed = fixture.CreateContext();
        Assert.Empty(await new GetRunningAgentAttemptsQueryHandler(feed).HandleAsync(new GetRunningAgentAttemptsQuery(), CancellationToken.None));
    }

    [Fact]
    public async Task A_dispatched_read_only_review_stays_running_and_the_run_is_not_marked_interrupted()
    {
        var (scene, sequenceId) = await ArmedAsync(recipeCount: 1);
        var ticket = await scene.ReserveUntilAsync(sequenceId, Step.CodeReview);
        Assert.True((await scene.ClaimAsync(ticket)).IsSuccess);
        await scene.DispatchAgentAsync(ticket.ReservedOperationId);

        await RecoverAsync(scene);

        Assert.Equal(AttemptStatus.Running, (await scene.ReadAttemptAsync(ticket.ReservedOperationId)).Status);
        await AssertWorkspaceAndRunUntouchedAsync(scene);
    }

    [Fact]
    public async Task A_dispatched_verification_stays_running_and_its_partial_output_is_neither_sealed_imported_nor_deleted()
    {
        var (scene, sequenceId) = await ArmedAsync();
        await scene.StepAsync(sequenceId);
        var ticket = await scene.ReserveAndClaimAsync(sequenceId);
        await scene.DispatchVerificationAsync(ticket.ReservedOperationId);

        var store = await RecoverAsync(scene);

        Assert.Equal(VerificationExecutionStatus.Running, (await scene.ReadExecutionAsync(ticket.ReservedOperationId)).Status);
        Assert.Empty(store.Touched);
        await AssertWorkspaceAndRunUntouchedAsync(scene);
    }

    [Fact]
    public async Task A_terminal_row_without_exit_proof_keeps_its_ownership_and_its_partial_files_across_recovery()
    {
        var (scene, sequenceId) = await ArmedAsync(recipeCount: 1);
        await scene.DriveAsync(sequenceId, new LocalExecutionStepScript().Verify(1, 0, VerificationTimedOut));

        var store = await RecoverAsync(scene);

        Assert.Equal(VerificationExecutionStatus.TimedOut, (await scene.ReadExecutionsAsync()).Single().Status);
        Assert.Empty(store.Touched);
        Assert.Equal(LocalExecutionSequenceManualFence.Blocked, (await scene.ReadStatusAsync()).ManualFence);
    }

    [Fact]
    public async Task A_proved_undispatched_claim_is_closed_with_the_dedicated_refusal_and_recovery_has_nothing_left_to_do()
    {
        var (scene, sequenceId) = await ArmedAsync();
        var ticket = await scene.ReserveAndClaimAsync(sequenceId);
        var captures = scene.Evidence.Captures;

        await RecoverAsync(scene);

        var attempt = await scene.ReadAttemptAsync(ticket.ReservedOperationId);
        Assert.Equal(AgentOutcome.LocalExecutionSequenceDispatchRefused, attempt.AgentOutcome);
        Assert.Null(attempt.AgentDispatchedAtUtc);
        Assert.Equal(captures, scene.Evidence.Captures);
        await AssertWorkspaceAndRunUntouchedAsync(scene);
        Assert.Single(await scene.ReadLocalAttemptsAsync());
    }

    [Theory]
    [InlineData("before_reservation")]
    [InlineData("preparation")]
    [InlineData("claim")]
    [InlineData("after_dispatch")]
    [InlineData("result_known")]
    public async Task No_boundary_of_the_restart_creates_a_successor_or_a_duplicate_invocation(string boundary)
    {
        var (scene, sequenceId) = await ArmedAsync();
        LocalExecutionStepTicket? ticket = null;
        if (boundary != "before_reservation")
        {
            ticket = boundary == "preparation" ? await scene.ObserveReservingAsync(sequenceId) : await scene.ReserveAndClaimAsync(sequenceId);
        }

        if (boundary is "after_dispatch" or "result_known")
        {
            await scene.DispatchAsync(ticket!.Kind, ticket.ReservedOperationId);
        }

        if (boundary == "result_known")
        {
            await scene.RecordImplementationAsync(ticket!.ReservedOperationId, Implemented);
        }

        var before = await scene.FootprintAsync();
        await RecoverAsync(scene);
        var observed = await scene.ObserveAsync(sequenceId);

        Assert.Null(observed.Value.ReservedStep);
        var after = await scene.FootprintAsync();
        Assert.Equal(before.Attempts, after.Attempts);
        Assert.Equal(before.Executions, after.Executions);
        Assert.Equal(LocalExecutionSequenceState.Interrupted, (await scene.ReadSequenceAsync()).State);
        Assert.True((await scene.ReadStepsAsync()).Count <= 1);
        // A completed result known at restart is recorded but never classified into a successor.
        Assert.Empty(await scene.ReadExecutionsAsync());
    }

    [Fact]
    public async Task A_forged_link_quarantines_the_sequence_and_never_falls_back_to_generic_recovery()
    {
        var (scene, sequenceId) = await ArmedAsync();
        var ticket = await scene.ReserveAndClaimAsync(sequenceId);
        await scene.DispatchAgentAsync(ticket.ReservedOperationId);
        await scene.ExecuteSqlAsync("UPDATE attempts SET AgentResponseContract = 'Proposal' WHERE Id = {0}", ticket.ReservedOperationId);

        await RecoverAsync(scene);

        Assert.Equal(AttemptStatus.Running, (await scene.ReadAttemptAsync(ticket.ReservedOperationId)).Status);
        await AssertWorkspaceAndRunUntouchedAsync(scene);
        Assert.NotEqual(LocalExecutionSequenceManualFence.Released, (await scene.ReadStatusAsync()).ManualFence);
    }

    [Fact]
    public async Task An_unlinked_running_verification_keeps_the_old_recovery_exactly()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture, recipeCount: 1);
        await scene.AddAcceptedRootPlanAsync();
        await using (var context = fixture.CreateContext())
        {
            var recipe = (await scene.ReadRecipesAsync())[0];
            var claim = await new ClaimVerificationExecutionCommandHandler(context, scene.Evidence, scene.Clock)
                .HandleAsync(new(scene.ProjectId, recipe.Id, scene.CheckpointId), CancellationToken.None);
            Assert.True(claim.IsSuccess);
        }

        await RecoverAsync(scene);

        Assert.Equal(VerificationExecutionStatus.Interrupted, (await scene.ReadExecutionsAsync()).Single().Status);
    }

    [Fact]
    public async Task A_late_return_recorded_by_the_ordinary_recorder_never_reopens_the_ended_sequence()
    {
        var (scene, sequenceId) = await ArmedAsync();
        var ticket = await scene.ReserveAndClaimAsync(sequenceId);
        await scene.DispatchAgentAsync(ticket.ReservedOperationId);
        await RecoverAsync(scene);

        await scene.RecordImplementationAsync(ticket.ReservedOperationId, Implemented);

        Assert.Equal(AttemptStatus.Completed, (await scene.ReadAttemptAsync(ticket.ReservedOperationId)).Status);
        Assert.Equal(LocalExecutionSequenceState.Interrupted, (await scene.ReadSequenceAsync()).State);
        Assert.Null((await scene.ObserveAsync(sequenceId)).Value.ReservedStep);
        Assert.Empty(await scene.ReadExecutionsAsync());
    }

    private sealed class RecordingStore : IVerificationOutputArtifactStore
    {
        public List<string> Touched { get; } = [];

        public string GetPartialPath(Guid verificationExecutionId, VerificationOutputPurpose purpose) => $"partial/{verificationExecutionId}/{purpose}";

        public void DeletePartialFile(Guid verificationExecutionId, VerificationOutputPurpose purpose) => Touched.Add($"delete:{verificationExecutionId}");

        public Task<SealedVerificationOutputFile?> SealAsync(Guid verificationExecutionId, VerificationOutputPurpose purpose, CancellationToken cancellationToken)
        {
            Touched.Add($"seal:{verificationExecutionId}");
            return Task.FromResult<SealedVerificationOutputFile?>(null);
        }

        public Task<SealedVerificationOutputFile?> DescribeSealedFileAsync(
            Guid verificationExecutionId, VerificationOutputPurpose purpose, CancellationToken cancellationToken)
        {
            Touched.Add($"describe:{verificationExecutionId}");
            return Task.FromResult<SealedVerificationOutputFile?>(null);
        }
    }
}
