using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// ADR-0060 write-seam guards against the real file-backed SQLite schema: while a sequence is live for a scope the database itself
/// refuses every unrelated attempt, verification execution, local commit, manual checkpoint, abandonment, setting change and recipe
/// change, admits only the exact reserved operation of a step, never affects an unrelated project, and holds its fence after the
/// sequence ended until every linked operation is not running and every linked dispatched operation shows coherent host Exited proof.
/// </summary>
public sealed class LocalExecutionSequenceGuardTests : LocalExecutionSequenceMigrationTestBase
{
    private sealed record Live(Scene Scene, Guid SequenceId, Guid StepId, Guid ReservedOperationId);

    private async Task<Live> StartAsync(int commandCount = 2)
    {
        await MigrateToAsync(null);
        await using var seed = CreateContext();
        var scene = await SeedSceneAsync(seed, commandCount: commandCount);
        var sequence = AddArmed(seed, scene, commandCount);
        await seed.SaveChangesAsync();
        var step = ImplementationStep(sequence.Id, scene);
        seed.LocalExecutionSequenceSteps.Add(step);
        await seed.SaveChangesAsync();
        return new Live(scene, sequence.Id, step.Id, step.ReservedOperationId);
    }

    private static async Task<DbUpdateException> RefusedAsync(DevalCopilotDbContextHolder holder)
    {
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => holder.Context.SaveChangesAsync());
        Assert.Contains(BlockedMessage, FirstMessage(exception), StringComparison.Ordinal);
        return exception;
    }

    private sealed class DevalCopilotDbContextHolder(DevalCopilot.Infrastructure.Persistence.DevalCopilotDbContext context) : IAsyncDisposable
    {
        public DevalCopilot.Infrastructure.Persistence.DevalCopilotDbContext Context { get; } = context;

        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    [Fact]
    public async Task A_live_sequence_refuses_every_attempt_of_its_run_except_the_exact_reserved_agent_operation_of_a_step()
    {
        var live = await StartAsync();
        Scene unrelated;
        await using (var other = CreateContext())
        {
            unrelated = await SeedSceneAsync(other, runNumber: 2);
        }

        await using (var manual = new DevalCopilotDbContextHolder(CreateContext()))
        {
            manual.Context.Attempts.Add(PlannerAttempt(live.Scene, Guid.NewGuid()));
            await RefusedAsync(manual);
        }

        await using (var wrongKind = new DevalCopilotDbContextHolder(CreateContext()))
        {
            wrongKind.Context.Attempts.Add(ReviewAttempt(live.Scene, Guid.NewGuid()));
            await RefusedAsync(wrongKind);
        }

        // An unrelated project's run is never blocked.
        await using (var other = CreateContext())
        {
            other.Attempts.Add(PlannerAttempt(unrelated, Guid.NewGuid()));
            await other.SaveChangesAsync();
        }

        // The exact reserved attempt of the still-Reserved step is admitted; the claim links it in the same transaction.
        await using (var claim = CreateContext())
        {
            claim.Attempts.Add(ImplementationAttempt(live.Scene, live.ReservedOperationId));
            await claim.SaveChangesAsync();
        }

        Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM attempts WHERE \"Id\" = {Q(live.ReservedOperationId)}"));
    }

    [Fact]
    public async Task The_reserved_attempt_is_admitted_in_the_same_save_that_links_the_step_and_a_later_second_attempt_is_not()
    {
        var live = await StartAsync();

        await using (var claim = CreateContext())
        {
            var step = await claim.LocalExecutionSequenceSteps.SingleAsync(candidate => candidate.Id == live.StepId);
            claim.Attempts.Add(ImplementationAttempt(live.Scene, live.ReservedOperationId));
            Claim(step);
            await claim.SaveChangesAsync();
        }

        await using var second = new DevalCopilotDbContextHolder(CreateContext());
        second.Context.Attempts.Add(ImplementationAttempt(live.Scene, Guid.NewGuid()));
        await RefusedAsync(second);
    }

    [Fact]
    public async Task A_revoked_step_no_longer_admits_its_reserved_attempt()
    {
        var live = await StartAsync();
        await using (var revoke = CreateContext())
        {
            var step = await revoke.LocalExecutionSequenceSteps.SingleAsync(candidate => candidate.Id == live.StepId);
            step.Revoke(LocalExecutionSequenceReason.StoppedBeforeClaim, Now);
            await revoke.SaveChangesAsync();
        }

        await using var late = new DevalCopilotDbContextHolder(CreateContext());
        late.Context.Attempts.Add(ImplementationAttempt(live.Scene, live.ReservedOperationId));
        await RefusedAsync(late);
    }

    [Fact]
    public async Task A_verification_steps_reserved_identity_is_never_admitted_as_an_attempt_nor_an_agent_identity_as_an_execution()
    {
        await MigrateToAsync(null);
        Scene scene;
        Guid verificationOperation;
        Guid agentOperation;
        await using (var seed = CreateContext())
        {
            scene = await SeedSceneAsync(seed);
            var sequence = AddArmed(seed, scene);
            await seed.SaveChangesAsync();
            var first = ImplementationStep(sequence.Id, scene);
            agentOperation = first.ReservedOperationId;
            seed.LocalExecutionSequenceSteps.Add(first);
            await seed.SaveChangesAsync();
            Claim(first);
            await seed.SaveChangesAsync();
            first.Conclude(Now.AddMinutes(1));
            await seed.SaveChangesAsync();
            var verification = VerificationStep(sequence.Id, scene, first.Id);
            verificationOperation = verification.ReservedOperationId;
            seed.LocalExecutionSequenceSteps.Add(verification);
            await seed.SaveChangesAsync();
        }

        await using (var asAttempt = new DevalCopilotDbContextHolder(CreateContext()))
        {
            asAttempt.Context.Attempts.Add(ReviewAttempt(scene, verificationOperation));
            await RefusedAsync(asAttempt);
        }

        await using (var asExecution = new DevalCopilotDbContextHolder(CreateContext()))
        {
            asExecution.Context.VerificationExecutions.Add(Execution(scene, agentOperation));
            await RefusedAsync(asExecution);
        }

        await using (var manual = new DevalCopilotDbContextHolder(CreateContext()))
        {
            manual.Context.VerificationExecutions.Add(Execution(scene, Guid.NewGuid()));
            await RefusedAsync(manual);
        }

        await using var exact = CreateContext();
        exact.VerificationExecutions.Add(Execution(scene, verificationOperation));
        await exact.SaveChangesAsync();
        Assert.Equal(1, await exact.VerificationExecutions.CountAsync());
    }

    [Fact]
    public async Task A_verification_execution_is_scoped_to_the_workspace_of_the_sequence()
    {
        var live = await StartAsync();
        Scene unrelated;
        GitWorkspace second;
        GitCheckpoint secondCheckpoint;
        await using (var other = CreateContext())
        {
            unrelated = await SeedSceneAsync(other, runNumber: 2);
            second = GitWorkspace.Prepare(
                Guid.NewGuid(), live.Scene.Project.Id, 2, $@"C:\workspaces\{Guid.NewGuid():N}", "branch2", new string('a', 40), "main", Now);
            second.MarkReady();
            secondCheckpoint = GitCheckpoint.Capture(Guid.NewGuid(), second.Id, second.ReserveCheckpointNumber(), Now, new string('a', 40), Fingerprint, []);
            other.GitWorkspaces.Add(second);
            other.GitCheckpoints.Add(secondCheckpoint);
            await other.SaveChangesAsync();
        }

        await using (var manual = new DevalCopilotDbContextHolder(CreateContext()))
        {
            manual.Context.VerificationExecutions.Add(Execution(live.Scene, Guid.NewGuid()));
            await RefusedAsync(manual);
        }

        await using var unaffected = CreateContext();
        unaffected.VerificationExecutions.Add(Execution(unrelated, Guid.NewGuid()));
        unaffected.VerificationExecutions.Add(VerificationExecution.Claim(
            Guid.NewGuid(), live.Scene.Project.Id, 50, second, secondCheckpoint, live.Scene.Commands[0], Now));
        await unaffected.SaveChangesAsync();
    }

    [Fact]
    public async Task A_local_commit_is_refused_for_the_workspace_of_a_live_sequence_only()
    {
        var live = await StartAsync();
        Scene unrelated;
        await using (var other = CreateContext())
        {
            unrelated = await SeedSceneAsync(other, runNumber: 2);
        }

        var blocked = await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(
            $"INSERT INTO local_commit_operations (\"Id\", \"GitWorkspaceId\") VALUES ({Q(Guid.NewGuid())}, {Q(live.Scene.Workspace.Id)});", foreignKeys: false));
        Assert.Contains(BlockedMessage, blocked.Message, StringComparison.Ordinal);

        // The unrelated workspace passes this guard and is then refused only by the table's own NOT NULL columns.
        var other2 = await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(
            $"INSERT INTO local_commit_operations (\"Id\", \"GitWorkspaceId\") VALUES ({Q(Guid.NewGuid())}, {Q(unrelated.Workspace.Id)});", foreignKeys: false));
        Assert.DoesNotContain(BlockedMessage, other2.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Abandonment_is_refused_while_the_sequence_is_live_and_an_unrelated_run_is_unaffected()
    {
        var live = await StartAsync();
        Scene unrelated;
        await using (var other = CreateContext())
        {
            unrelated = await SeedSceneAsync(other, runNumber: 2);
        }

        Assert.True(await IsRunHeldAsync(live.Scene.Run.Id));
        Assert.False(await IsRunHeldAsync(unrelated.Run.Id));
        await using var verify = CreateContext();
        Assert.Equal(RunLifecycle.Running, (await verify.Runs.AsNoTracking().SingleAsync(run => run.Id == live.Scene.Run.Id)).Lifecycle);
        Assert.Equal(RunLifecycle.Abandoned, (await verify.Runs.AsNoTracking().SingleAsync(run => run.Id == unrelated.Run.Id)).Lifecycle);
    }

    [Theory]
    [InlineData("RequestedClaudeMaxTurns", "'3'")]
    [InlineData("RequestedCodexModel", "'gpt-x'")]
    [InlineData("RequestedCodexEffort", "'high'")]
    [InlineData("RequestedClaudeModel", "'sonnet'")]
    [InlineData("RequestedClaudeEffort", "'high'")]
    [InlineData("CodexAccountUsageStopPercent", "'50'")]
    [InlineData("CodexTokenStopThreshold", "1000")]
    [InlineData("ClaudeTokenStopThreshold", "1000")]
    public async Task A_value_changing_run_setting_write_is_refused_while_live(string column, string value)
    {
        var live = await StartAsync();

        var exception = await Assert.ThrowsAsync<SqliteException>(
            () => ExecuteAsync($"UPDATE runs SET \"{column}\" = {value} WHERE \"Id\" = {Q(live.Scene.Run.Id)}"));
        Assert.Contains(BlockedMessage, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_same_value_setting_write_and_a_warning_threshold_write_are_not_guarded()
    {
        var live = await StartAsync();

        // The sequence's own claims compare these tokens without altering them; a warning is not a stop or an assignment.
        await ExecuteAsync(
            $"UPDATE runs SET \"RequestedClaudeMaxTurns\" = \"RequestedClaudeMaxTurns\", \"CodexTokenStopThreshold\" = \"CodexTokenStopThreshold\", "
            + $"\"CodexAccountUsageStopPercent\" = \"CodexAccountUsageStopPercent\" WHERE \"Id\" = {Q(live.Scene.Run.Id)}");
        await ExecuteAsync($"UPDATE runs SET \"CodexAccountUsageWarningPercent\" = '70' WHERE \"Id\" = {Q(live.Scene.Run.Id)}");
        await ExecuteAsync($"UPDATE runs SET \"CodexTokenWarningThreshold\" = 5000 WHERE \"Id\" = {Q(live.Scene.Run.Id)}");

        Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM runs WHERE \"Id\" = {Q(live.Scene.Run.Id)} AND \"CodexAccountUsageWarningPercent\" = '70'"));
    }

    [Fact]
    public async Task A_setting_write_of_an_unrelated_run_is_not_guarded()
    {
        await StartAsync();
        Scene unrelated;
        await using (var other = CreateContext())
        {
            unrelated = await SeedSceneAsync(other, runNumber: 2);
        }

        await ExecuteAsync($"UPDATE runs SET \"RequestedClaudeMaxTurns\" = '3' WHERE \"Id\" = {Q(unrelated.Run.Id)}");
    }

    [Fact]
    public async Task A_manual_checkpoint_is_refused_and_a_result_checkpoint_is_admitted_only_while_a_linked_claimed_attempt_is_running()
    {
        await MigrateToAsync(null);
        Scene scene;
        Guid sequenceId;
        LocalExecutionSequenceStep step;
        await using (var seed = CreateContext())
        {
            scene = await SeedSceneAsync(seed);
            var sequence = AddArmed(seed, scene);
            sequenceId = sequence.Id;
            await seed.SaveChangesAsync();
            step = ImplementationStep(sequence.Id, scene);
            seed.LocalExecutionSequenceSteps.Add(step);
            await seed.SaveChangesAsync();
        }

        // Reserved, no claimed attempt yet: nothing may capture a checkpoint.
        await using (var manual = new DevalCopilotDbContextHolder(CreateContext()))
        {
            manual.Context.GitCheckpoints.Add(GitCheckpoint.Capture(Guid.NewGuid(), scene.Workspace.Id, 20, Now, new string('a', 40), new string('e', 64), []));
            await RefusedAsync(manual);
        }

        await using (var claim = CreateContext())
        {
            var tracked = await claim.LocalExecutionSequenceSteps.SingleAsync(candidate => candidate.Id == step.Id);
            claim.Attempts.Add(ImplementationAttempt(scene, step.ReservedOperationId));
            Claim(tracked);
            await claim.SaveChangesAsync();
        }

        // The claimed Agent attempt is Running: its result checkpoint of this workspace is admitted.
        await using (var result = CreateContext())
        {
            result.GitCheckpoints.Add(GitCheckpoint.Capture(Guid.NewGuid(), scene.Workspace.Id, 21, Now, new string('a', 40), new string('f', 64), []));
            await result.SaveChangesAsync();
        }

        // A checkpoint of an unrelated workspace is never this guard's concern.
        await using (var other = CreateContext())
        {
            var unrelated = await SeedSceneAsync(other, runNumber: 2);
            other.GitCheckpoints.Add(GitCheckpoint.Capture(Guid.NewGuid(), unrelated.Workspace.Id, 30, Now, new string('a', 40), new string('e', 64), []));
            await other.SaveChangesAsync();
        }

        await using (var finish = CreateContext())
        {
            var attempt = await finish.Attempts.SingleAsync(candidate => candidate.Id == step.ReservedOperationId);
            attempt.CompleteLocalExecutionSequenceDispatchRefused(Now.AddMinutes(1));
            await finish.SaveChangesAsync();
        }

        // The attempt is no longer Running, though the sequence still awaits: a late checkpoint is a manual capture.
        await using var late = new DevalCopilotDbContextHolder(CreateContext());
        late.Context.GitCheckpoints.Add(GitCheckpoint.Capture(Guid.NewGuid(), scene.Workspace.Id, 22, Now, new string('a', 40), new string('9', 64), []));
        await RefusedAsync(late);
        Assert.NotEqual(Guid.Empty, sequenceId);
    }

    [Fact]
    public async Task A_verification_step_never_admits_a_result_checkpoint()
    {
        await MigrateToAsync(null);
        Scene scene;
        await using (var seed = CreateContext())
        {
            scene = await SeedSceneAsync(seed);
            var sequence = AddArmed(seed, scene);
            await seed.SaveChangesAsync();
            var first = ImplementationStep(sequence.Id, scene);
            seed.LocalExecutionSequenceSteps.Add(first);
            await seed.SaveChangesAsync();
            Claim(first);
            await seed.SaveChangesAsync();
            first.Conclude(Now.AddMinutes(1));
            await seed.SaveChangesAsync();
            var verification = VerificationStep(sequence.Id, scene, first.Id);
            seed.LocalExecutionSequenceSteps.Add(verification);
            await seed.SaveChangesAsync();
            seed.VerificationExecutions.Add(Execution(scene, verification.ReservedOperationId));
            Claim(verification);
            await seed.SaveChangesAsync();
        }

        await using var capture = new DevalCopilotDbContextHolder(CreateContext());
        capture.Context.GitCheckpoints.Add(GitCheckpoint.Capture(Guid.NewGuid(), scene.Workspace.Id, 40, Now, new string('a', 40), new string('e', 64), []));
        await RefusedAsync(capture);
    }

    [Fact]
    public async Task Verification_recipe_changes_are_refused_while_live_and_an_unrelated_project_is_unaffected()
    {
        var live = await StartAsync();
        Scene unrelated;
        await using (var other = CreateContext())
        {
            unrelated = await SeedSceneAsync(other, runNumber: 2);
        }

        await using (var insert = new DevalCopilotDbContextHolder(CreateContext()))
        {
            insert.Context.VerificationCommands.Add(VerificationCommand.Configure(
                Guid.NewGuid(), live.Scene.Project.Id, 9, "New", @"C:\tools\dotnet.exe", [], 60, true, Now));
            await RefusedAsync(insert);
        }

        await using (var update = new DevalCopilotDbContextHolder(CreateContext()))
        {
            var command = await update.Context.VerificationCommands.SingleAsync(candidate => candidate.Id == live.Scene.Commands[0].Id);
            command.Update("Renamed", command.ExecutablePath, command.Arguments.ToArray(), 90, true, Now.AddMinutes(1));
            await RefusedAsync(update);
        }

        await using (var delete = new DevalCopilotDbContextHolder(CreateContext()))
        {
            var command = await delete.Context.VerificationCommands.SingleAsync(candidate => candidate.Id == live.Scene.Commands[1].Id);
            delete.Context.VerificationCommands.Remove(command);
            await RefusedAsync(delete);
        }

        await using (var other = CreateContext())
        {
            other.VerificationCommands.Add(VerificationCommand.Configure(
                Guid.NewGuid(), unrelated.Project.Id, 9, "New", @"C:\tools\dotnet.exe", [], 60, true, Now));
            var command = await other.VerificationCommands.FirstAsync(candidate => candidate.ProjectId == unrelated.Project.Id);
            command.Update("Renamed", command.ExecutablePath, command.Arguments.ToArray(), 90, true, Now.AddMinutes(1));
            await other.SaveChangesAsync();
            var removable = await other.VerificationCommands.FirstAsync(candidate => candidate.ProjectId == unrelated.Project.Id && candidate.CommandNumber == 2);
            other.VerificationCommands.Remove(removable);
            await other.SaveChangesAsync();
        }

        await using var verify = CreateContext();
        Assert.Equal(2, await verify.VerificationCommands.AsNoTracking().CountAsync(candidate => candidate.ProjectId == live.Scene.Project.Id));
        Assert.Equal("Check 1", (await verify.VerificationCommands.AsNoTracking().SingleAsync(candidate => candidate.Id == live.Scene.Commands[0].Id)).Name);
    }

    // ---- The liveness definition: how the fence behaves after the sequence ended. ----

    public enum AttemptScenario
    {
        Running,
        DispatchedRunning,
        NeverDispatchedRefused,
        DispatchedExitedNonZero,
        DispatchedExitedZeroFailed,
        DispatchedTimedOut,
        DispatchedCancelled,
        DispatchedFailedWithoutEvidence,
    }

    private async Task<Scene> EndedWithAttemptAsync(AttemptScenario scenario)
    {
        await MigrateToAsync(null);
        await using var context = CreateContext();
        var scene = await SeedSceneAsync(context);
        var sequence = AddArmed(context, scene);
        await context.SaveChangesAsync();
        var step = ImplementationStep(sequence.Id, scene);
        context.LocalExecutionSequenceSteps.Add(step);
        await context.SaveChangesAsync();
        var attempt = ImplementationAttempt(scene, step.ReservedOperationId);
        context.Attempts.Add(attempt);
        sequence.BeginPreparing(Now);
        sequence.MarkAwaitingResult(Now);
        Claim(step);
        await context.SaveChangesAsync();

        switch (scenario)
        {
            case AttemptScenario.DispatchedRunning:
                attempt.MarkAgentDispatched(Now.AddSeconds(20));
                break;
            case AttemptScenario.NeverDispatchedRefused:
                attempt.CompleteLocalExecutionSequenceDispatchRefused(Now.AddSeconds(20));
                break;
            case AttemptScenario.DispatchedExitedNonZero:
                attempt.MarkAgentDispatched(Now.AddSeconds(20));
                attempt.CompleteAgent(
                    AgentOutcome.ProviderInvocationFailed, null, Now.AddMinutes(1),
                    AgentProcessExecutionEvidence.Create(ProcessOutcome.Exited, 1, TimeSpan.FromSeconds(30)));
                break;
            case AttemptScenario.DispatchedExitedZeroFailed:
                attempt.MarkAgentDispatched(Now.AddSeconds(20));
                attempt.CompleteAgent(
                    AgentOutcome.ProviderInvocationFailed, null, Now.AddMinutes(1),
                    AgentProcessExecutionEvidence.Create(ProcessOutcome.Exited, 0, TimeSpan.Zero));
                break;
            case AttemptScenario.DispatchedTimedOut:
                attempt.MarkAgentDispatched(Now.AddSeconds(20));
                attempt.CompleteAgent(
                    AgentOutcome.ProviderInvocationFailed, null, Now.AddMinutes(1),
                    AgentProcessExecutionEvidence.Create(ProcessOutcome.TimedOut, null, TimeSpan.FromMinutes(20)));
                break;
            case AttemptScenario.DispatchedCancelled:
                attempt.MarkAgentDispatched(Now.AddSeconds(20));
                attempt.CompleteAgent(
                    AgentOutcome.ProviderInvocationFailed, null, Now.AddMinutes(1),
                    AgentProcessExecutionEvidence.Create(ProcessOutcome.Cancelled, null, TimeSpan.FromSeconds(5)));
                break;
            case AttemptScenario.DispatchedFailedWithoutEvidence:
                attempt.MarkAgentDispatched(Now.AddSeconds(20));
                attempt.CompleteAgent(AgentOutcome.ProviderInvocationFailed, null, Now.AddMinutes(1));
                break;
            default:
                break;
        }

        step.Conclude(Now.AddMinutes(2));
        sequence.End(LocalExecutionSequenceState.NeedsAttention, LocalExecutionSequenceReason.OperationFailed, null, Now.AddMinutes(2));
        await context.SaveChangesAsync();
        return scene;
    }

    [Theory]
    [InlineData(AttemptScenario.Running, true)]
    [InlineData(AttemptScenario.DispatchedRunning, true)]
    [InlineData(AttemptScenario.DispatchedTimedOut, true)]
    [InlineData(AttemptScenario.DispatchedCancelled, true)]
    [InlineData(AttemptScenario.DispatchedFailedWithoutEvidence, true)]
    [InlineData(AttemptScenario.NeverDispatchedRefused, false)]
    [InlineData(AttemptScenario.DispatchedExitedNonZero, false)]
    [InlineData(AttemptScenario.DispatchedExitedZeroFailed, false)]
    public async Task An_ended_sequence_holds_its_fence_until_every_linked_agent_operation_has_coherent_exit_proof_or_never_dispatched(
        AttemptScenario scenario, bool held)
    {
        var scene = await EndedWithAttemptAsync(scenario);

        Assert.Equal(held, await IsRunHeldAsync(scene.Run.Id));
    }

    [Theory]
    [InlineData(AttemptScenario.DispatchedTimedOut)]
    [InlineData(AttemptScenario.DispatchedFailedWithoutEvidence)]
    public async Task A_quarantined_attempt_also_holds_the_attempt_workspace_and_recipe_seams(AttemptScenario scenario)
    {
        var scene = await EndedWithAttemptAsync(scenario);

        await using (var attempt = new DevalCopilotDbContextHolder(CreateContext()))
        {
            attempt.Context.Attempts.Add(PlannerAttempt(scene, Guid.NewGuid()));
            await RefusedAsync(attempt);
        }

        await using (var execution = new DevalCopilotDbContextHolder(CreateContext()))
        {
            execution.Context.VerificationExecutions.Add(Execution(scene, Guid.NewGuid()));
            await RefusedAsync(execution);
        }

        await using var recipe = new DevalCopilotDbContextHolder(CreateContext());
        var command = await recipe.Context.VerificationCommands.FirstAsync(candidate => candidate.ProjectId == scene.Project.Id);
        recipe.Context.VerificationCommands.Remove(command);
        await RefusedAsync(recipe);
    }

    [Fact]
    public async Task A_released_sequence_admits_the_manual_seams_again()
    {
        var scene = await EndedWithAttemptAsync(AttemptScenario.DispatchedExitedNonZero);

        await using var context = CreateContext();
        context.Attempts.Add(PlannerAttempt(scene, Guid.NewGuid()));
        context.VerificationExecutions.Add(Execution(scene, Guid.NewGuid()));
        context.GitCheckpoints.Add(GitCheckpoint.Capture(Guid.NewGuid(), scene.Workspace.Id, 60, Now, new string('a', 40), new string('e', 64), []));
        await context.SaveChangesAsync();
        await ExecuteAsync($"UPDATE runs SET \"RequestedClaudeMaxTurns\" = '4' WHERE \"Id\" = {Q(scene.Run.Id)}");
        Assert.False(await IsRunHeldAsync(scene.Run.Id));
    }

    public enum ExecutionScenario
    {
        RunningUndispatched,
        RunningDispatched,
        Refused,
        InterruptedBeforeDispatch,
        PassedExited,
        FailedExited,
        TimedOut,
        InterruptedAfterDispatch,
    }

    private async Task<Scene> EndedWithExecutionAsync(ExecutionScenario scenario)
    {
        await MigrateToAsync(null);
        await using var context = CreateContext();
        var scene = await SeedSceneAsync(context);
        var sequence = AddArmed(context, scene);
        await context.SaveChangesAsync();
        var first = ImplementationStep(sequence.Id, scene);
        context.LocalExecutionSequenceSteps.Add(first);
        await context.SaveChangesAsync();
        Claim(first);
        await context.SaveChangesAsync();
        first.Conclude(Now.AddMinutes(1));
        await context.SaveChangesAsync();
        var step = VerificationStep(sequence.Id, scene, first.Id);
        context.LocalExecutionSequenceSteps.Add(step);
        await context.SaveChangesAsync();
        var execution = Execution(scene, step.ReservedOperationId);
        context.VerificationExecutions.Add(execution);
        Claim(step);
        await context.SaveChangesAsync();

        switch (scenario)
        {
            case ExecutionScenario.RunningDispatched:
                execution.MarkDispatched(Now.AddSeconds(20));
                break;
            case ExecutionScenario.Refused:
                execution.MarkLocalExecutionSequenceDispatchRefused(Now.AddSeconds(20));
                break;
            case ExecutionScenario.InterruptedBeforeDispatch:
                execution.Interrupt(Now.AddSeconds(20));
                break;
            case ExecutionScenario.PassedExited:
                execution.MarkDispatched(Now.AddSeconds(20));
                execution.Complete(VerificationExecutionOutcome.Exited, 0, Fingerprint, Now.AddMinutes(1));
                break;
            case ExecutionScenario.FailedExited:
                execution.MarkDispatched(Now.AddSeconds(20));
                execution.Complete(VerificationExecutionOutcome.Exited, 1, Fingerprint, Now.AddMinutes(1));
                break;
            case ExecutionScenario.TimedOut:
                execution.MarkDispatched(Now.AddSeconds(20));
                execution.Complete(VerificationExecutionOutcome.TimedOut, null, Fingerprint, Now.AddMinutes(1));
                break;
            case ExecutionScenario.InterruptedAfterDispatch:
                execution.MarkDispatched(Now.AddSeconds(20));
                execution.Interrupt(Now.AddMinutes(1));
                break;
            default:
                break;
        }

        step.Conclude(Now.AddMinutes(2));
        sequence.End(LocalExecutionSequenceState.NeedsAttention, LocalExecutionSequenceReason.OperationFailed, null, Now.AddMinutes(2));
        await context.SaveChangesAsync();
        return scene;
    }

    [Theory]
    [InlineData(ExecutionScenario.RunningUndispatched, true)]
    [InlineData(ExecutionScenario.RunningDispatched, true)]
    [InlineData(ExecutionScenario.TimedOut, true)]
    [InlineData(ExecutionScenario.InterruptedAfterDispatch, true)]
    [InlineData(ExecutionScenario.Refused, false)]
    [InlineData(ExecutionScenario.InterruptedBeforeDispatch, false)]
    [InlineData(ExecutionScenario.PassedExited, false)]
    [InlineData(ExecutionScenario.FailedExited, false)]
    public async Task An_ended_sequence_holds_its_fence_until_every_linked_verification_has_coherent_exit_proof_or_never_dispatched(
        ExecutionScenario scenario, bool held)
    {
        var scene = await EndedWithExecutionAsync(scenario);

        Assert.Equal(held, await IsRunHeldAsync(scene.Run.Id));
    }

    [Fact]
    public async Task A_quarantined_verification_holds_the_workspace_and_a_released_one_does_not()
    {
        var quarantined = await EndedWithExecutionAsync(ExecutionScenario.InterruptedAfterDispatch);
        await using (var blocked = new DevalCopilotDbContextHolder(CreateContext()))
        {
            blocked.Context.VerificationExecutions.Add(Execution(quarantined, Guid.NewGuid()));
            await RefusedAsync(blocked);
        }

        var exception = await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(
            $"INSERT INTO local_commit_operations (\"Id\", \"GitWorkspaceId\") VALUES ({Q(Guid.NewGuid())}, {Q(quarantined.Workspace.Id)});", foreignKeys: false));
        Assert.Contains(BlockedMessage, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_released_verification_sequence_admits_a_manual_execution_again()
    {
        var scene = await EndedWithExecutionAsync(ExecutionScenario.FailedExited);

        await using var context = CreateContext();
        context.VerificationExecutions.Add(Execution(scene, Guid.NewGuid()));
        await context.SaveChangesAsync();
        Assert.False(await IsRunHeldAsync(scene.Run.Id));
    }

    public enum StepScenario
    {
        ReservedOnEndedSequence,
        UnresolvedOnEndedSequence,
        RevokedOnEndedSequence,
        ConcludedWithoutOperationRows,
    }

    [Theory]
    [InlineData(StepScenario.ReservedOnEndedSequence, true)]
    [InlineData(StepScenario.UnresolvedOnEndedSequence, true)]
    [InlineData(StepScenario.RevokedOnEndedSequence, false)]
    [InlineData(StepScenario.ConcludedWithoutOperationRows, false)]
    public async Task An_unresolved_or_reserved_step_holds_the_fence_after_the_sequence_ended(StepScenario scenario, bool held)
    {
        await MigrateToAsync(null);
        Scene scene;
        await using (var context = CreateContext())
        {
            scene = await SeedSceneAsync(context);
            var sequence = AddArmed(context, scene);
            await context.SaveChangesAsync();
            var step = ImplementationStep(sequence.Id, scene);
            context.LocalExecutionSequenceSteps.Add(step);
            await context.SaveChangesAsync();
            switch (scenario)
            {
                case StepScenario.UnresolvedOnEndedSequence:
                    step.MarkUnresolved(LocalExecutionSequenceReason.PreparationUnresolved, Now.AddMinutes(1));
                    break;
                case StepScenario.RevokedOnEndedSequence:
                    step.Revoke(LocalExecutionSequenceReason.StoppedBeforeClaim, Now.AddMinutes(1));
                    break;
                case StepScenario.ConcludedWithoutOperationRows:
                    Claim(step);
                    await context.SaveChangesAsync();
                    step.Conclude(Now.AddMinutes(1));
                    break;
                default:
                    break;
            }

            sequence.End(LocalExecutionSequenceState.NeedsAttention, LocalExecutionSequenceReason.PreparationExpired, null, Now.AddMinutes(2));
            await context.SaveChangesAsync();
        }

        Assert.Equal(held, await IsRunHeldAsync(scene.Run.Id));
    }

    [Theory]
    [InlineData(LocalExecutionSequenceState.Armed)]
    [InlineData(LocalExecutionSequenceState.PreparingStep)]
    [InlineData(LocalExecutionSequenceState.AwaitingStepResult)]
    [InlineData(LocalExecutionSequenceState.Stopping)]
    public async Task Every_live_sequence_state_holds_the_fence_even_without_any_step(LocalExecutionSequenceState state)
    {
        await MigrateToAsync(null);
        Scene scene;
        await using (var context = CreateContext())
        {
            scene = await SeedSceneAsync(context);
            var sequence = AddArmed(context, scene);
            await context.SaveChangesAsync();
            if (state >= LocalExecutionSequenceState.PreparingStep)
            {
                sequence.BeginPreparing(Now);
            }

            if (state >= LocalExecutionSequenceState.AwaitingStepResult)
            {
                sequence.MarkAwaitingResult(Now);
            }

            if (state == LocalExecutionSequenceState.Stopping)
            {
                sequence.BeginStopping(Now);
            }

            await context.SaveChangesAsync();
        }

        Assert.True(await IsRunHeldAsync(scene.Run.Id));
    }

    [Theory]
    [InlineData(LocalExecutionSequenceState.Stopped, LocalExecutionSequenceReason.StoppedBeforeClaim)]
    [InlineData(LocalExecutionSequenceState.Interrupted, LocalExecutionSequenceReason.HostRestarted)]
    [InlineData(LocalExecutionSequenceState.NeedsAttention, LocalExecutionSequenceReason.AuthorityChanged)]
    public async Task An_ended_sequence_without_any_outstanding_step_releases_the_fence(LocalExecutionSequenceState end, string reason)
    {
        await MigrateToAsync(null);
        Scene scene;
        await using (var context = CreateContext())
        {
            scene = await SeedSceneAsync(context);
            var sequence = AddArmed(context, scene);
            await context.SaveChangesAsync();
            sequence.End(end, reason, null, Now);
            await context.SaveChangesAsync();
        }

        Assert.False(await IsRunHeldAsync(scene.Run.Id));
    }
}
