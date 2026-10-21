using DevalCopilot.Application.Features.Projects.Policies.WorkspaceExecutions;
using DevalCopilot.Application.Features.Runs.Policies.ExecutionCapacity;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Step = DevalCopilot.Domain.Features.Runs.LocalExecutionStepKind;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;

/// <summary>
/// The dedicated dispatch refusal is recorded only for a proved, still undispatched claim of exactly one sequence (ADR-0060), by the
/// startup closure and by both live recorders: a valid claim still closes, while every identity mismatch, unproved link, dispatch or process
/// evidence leaves the operation exactly as recorded, writes no event (above all none against another run), fabricates nothing and releases
/// no ownership, so the shared ownership policies keep reading the operation as an owner or as Unavailable.
/// </summary>
public sealed class LocalExecutionRefusalProofTests : IAsyncLifetime
{
    public static IEnumerable<object[]> AgentBreaks() => new[]
    {
        "run_moved_to_sibling_run", "run_moved_to_another_project", "workspace_of_another_project", "checkpoint_other", "fingerprint_other",
        "wrong_contract", "step_reserved", "step_revoked", "step_unresolved", "reserved_id_differs", "process_evidence", "dispatched",
    }.Select(name => new object[] { name });

    public static IEnumerable<object[]> VerificationBreaks() => new[]
    {
        "recipe_command_mismatch", "checkpoint_other", "workspace_other", "project_other", "fingerprint_other", "outcome_present",
        "exit_code_present", "step_reserved", "step_revoked", "step_unresolved", "dispatched",
    }.Select(name => new object[] { name });

    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    private async Task<(LocalExecutionSequenceTestScene Scene, Guid SequenceId)> ArmedAsync()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture, recipeCount: 2);
        var plan = await scene.AddAcceptedRootPlanAsync();
        return (scene, (await scene.ArmAsync(plan.ProposalId)).SequenceId);
    }

    private async Task SqlAsync(string sql, params object[] parameters)
    {
        await using var context = fixture.CreateContext();
        await context.Database.OpenConnectionAsync();
        await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");
        await context.Database.ExecuteSqlRawAsync(sql, parameters);
    }

    private async Task BreakStepAsync(string kind, Guid stepId)
    {
        await SqlAsync("DROP TRIGGER trg_local_execution_step_immutable");
        await SqlAsync("DROP TRIGGER IF EXISTS trg_local_execution_step_operation_link");
        switch (kind)
        {
            case "step_reserved":
                await SqlAsync("UPDATE local_execution_sequence_steps SET State = 'Reserved', OperationId = NULL WHERE Id = {0}", stepId);
                break;
            case "step_revoked":
                await SqlAsync("UPDATE local_execution_sequence_steps SET State = 'Revoked', OperationId = NULL WHERE Id = {0}", stepId);
                break;
            default:
                await SqlAsync("UPDATE local_execution_sequence_steps SET State = 'Unresolved', OperationId = NULL WHERE Id = {0}", stepId);
                break;
        }
    }

    private async Task AssertOwnerKeptAsync(LocalExecutionSequenceTestScene scene)
    {
        await using var context = fixture.CreateContext();
        var workspace = await WorkspaceExecutionPolicy.AssessAsync(context, scene.ProjectId, scene.WorkspaceId, null, null, CancellationToken.None);
        var capacity = await ExecutionCapacityPolicy.ReadAsync(context, null, CancellationToken.None);
        Assert.NotEqual(WorkspaceExecutionStatus.Idle, workspace.Status);
        Assert.True(capacity.Status == ExecutionCapacityStatus.Unavailable || capacity.RecordedOwners > 0, "ownership must not be released");
    }

    // ---- the valid closures --------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_valid_undispatched_agent_claim_still_closes_at_startup()
    {
        var (scene, sequenceId) = await ArmedAsync();
        var ticket = await scene.ReserveAndClaimAsync(sequenceId);

        var closed = await scene.InterruptAsync();

        Assert.Equal(1, closed.RefusedUndispatchedCount);
        Assert.Equal(AgentOutcome.LocalExecutionSequenceDispatchRefused, (await scene.ReadAttemptAsync(ticket.ReservedOperationId)).AgentOutcome);
    }

    [Fact]
    public async Task A_valid_undispatched_verification_claim_still_closes_at_startup()
    {
        var (scene, sequenceId) = await ArmedAsync();
        await scene.StepAsync(sequenceId);
        var ticket = await scene.ReserveAndClaimAsync(sequenceId);

        var closed = await scene.InterruptAsync();

        Assert.Equal(1, closed.RefusedUndispatchedCount);
        Assert.Equal(
            VerificationExecutionStatus.LocalExecutionSequenceDispatchRefused,
            (await scene.ReadExecutionAsync(ticket.ReservedOperationId)).Status);
    }

    [Fact]
    public async Task A_valid_undispatched_claim_is_closed_by_the_live_recorders_once_the_sequence_stopped_authorizing()
    {
        var (scene, sequenceId) = await ArmedAsync();
        var agent = await scene.ReserveAndClaimAsync(sequenceId);
        scene.Clock.Advance(TimeSpan.FromMinutes(30));

        Assert.True((await scene.RecordAgentDispatchRefusalAsync(agent.ReservedOperationId)).IsSuccess);
        Assert.Equal(AgentOutcome.LocalExecutionSequenceDispatchRefused, (await scene.ReadAttemptAsync(agent.ReservedOperationId)).AgentOutcome);
    }

    // ---- the Agent family -----------------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AgentBreaks))]
    public async Task An_unproved_agent_claim_is_never_terminalized_by_startup_or_the_live_recorder(string kind)
    {
        var (scene, sequenceId) = await ArmedAsync();
        var other = await LocalExecutionSequenceTestScene.CreateAsync(fixture, recipeCount: 1);
        var ticket = await scene.ReserveAndClaimAsync(sequenceId);
        var id = ticket.ReservedOperationId;
        Guid siblingRun;
        await using (var context = fixture.CreateContext())
        {
            var sibling = Run.RecordClassifiedIntent(
                Guid.NewGuid(), scene.ProjectId, 900, RunExecutionMode.ManualAgent, "Sibling", LocalExecutionSequenceTestScene.Start);
            context.Runs.Add(sibling);
            await context.SaveChangesAsync();
            siblingRun = sibling.Id;
        }

        switch (kind)
        {
            case "run_moved_to_sibling_run":
                await SqlAsync("UPDATE attempts SET RunId = {0} WHERE Id = {1}", siblingRun, id);
                break;
            case "run_moved_to_another_project":
                await SqlAsync("UPDATE attempts SET RunId = {0} WHERE Id = {1}", other.RunId, id);
                break;
            case "workspace_of_another_project":
                await SqlAsync("UPDATE attempts SET AgentGitWorkspaceId = {0} WHERE Id = {1}", other.WorkspaceId, id);
                break;
            case "checkpoint_other":
                await SqlAsync("UPDATE attempts SET AgentGitCheckpointId = {0} WHERE Id = {1}", other.CheckpointId, id);
                break;
            case "fingerprint_other":
                await SqlAsync("UPDATE attempts SET AgentCheckpointFingerprintSha256 = {0} WHERE Id = {1}", new string('c', 64), id);
                break;
            case "wrong_contract":
                await SqlAsync("UPDATE attempts SET AgentResponseContract = 'ImplementationReview' WHERE Id = {0}", id);
                break;
            case "reserved_id_differs":
                await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(async () =>
                {
                    await SqlAsync("DROP TRIGGER trg_local_execution_step_immutable");
                    await SqlAsync("UPDATE local_execution_sequence_steps SET ReservedOperationId = {0} WHERE Id = {1}", Guid.NewGuid(), ticket.StepId);
                });
                return;
            case "process_evidence":
                await SqlAsync("UPDATE attempts SET AgentProcessOutcome = 'Exited', AgentProcessExitCode = 0, AgentProcessDuration = '00:00:01' WHERE Id = {0}", id);
                break;
            case "dispatched":
                await scene.DispatchAgentAsync(id);
                break;
            default:
                // The storage itself refuses a step whose state and operation link disagree, so the proof is never even asked.
                await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => BreakStepAsync(kind, ticket.StepId));
                return;
        }

        var before = await scene.ReadAttemptAsync(id);
        var otherEvents = (await other.FootprintAsync()).Events;
        var siblingEvents = await CountEventsOfRunAsync(siblingRun);
        scene.Clock.Advance(TimeSpan.FromMinutes(30));

        await scene.InterruptAsync();
        var recorded = await scene.RecordAgentDispatchRefusalAsync(id);

        Assert.True(recorded.IsFailure);
        var after = await scene.ReadAttemptAsync(id);
        Assert.Equal(AttemptStatus.Running, after.Status);
        Assert.Null(after.AgentOutcome);
        Assert.Null(after.CompletedAtUtc);
        Assert.Equal(before.RunId, after.RunId);
        Assert.Equal(otherEvents, (await other.FootprintAsync()).Events);
        Assert.Equal(siblingEvents, await CountEventsOfRunAsync(siblingRun));
        await AssertOwnerKeptAsync(scene);
    }

    private async Task<int> CountEventsOfRunAsync(Guid runId)
    {
        await using var context = fixture.CreateContext();
        return await context.Events.AsNoTracking().CountAsync(candidate => candidate.RunId == runId);
    }

    // ---- the verification family -----------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(VerificationBreaks))]
    public async Task An_unproved_verification_claim_is_never_terminalized_by_startup_or_the_live_recorder(string kind)
    {
        var (scene, sequenceId) = await ArmedAsync();
        var other = await LocalExecutionSequenceTestScene.CreateAsync(fixture, recipeCount: 1);
        await scene.StepAsync(sequenceId);
        var ticket = await scene.ReserveAndClaimAsync(sequenceId);
        Assert.Equal(Step.Verification, ticket.Kind);
        var id = ticket.ReservedOperationId;
        var recipes = await scene.ReadRecipesAsync();
        var execution = await scene.ReadExecutionAsync(id);
        var otherRecipe = recipes.Single(recipe => recipe.Id != execution.VerificationCommandId).Id;

        switch (kind)
        {
            case "recipe_command_mismatch":
                await SqlAsync("UPDATE verification_executions SET VerificationCommandId = {0} WHERE Id = {1}", otherRecipe, id);
                break;
            case "checkpoint_other":
                await SqlAsync("UPDATE verification_executions SET GitCheckpointId = {0} WHERE Id = {1}", other.CheckpointId, id);
                break;
            case "workspace_other":
                await SqlAsync("UPDATE verification_executions SET GitWorkspaceId = {0} WHERE Id = {1}", other.WorkspaceId, id);
                break;
            case "project_other":
                await SqlAsync("UPDATE verification_executions SET ProjectId = {0} WHERE Id = {1}", other.ProjectId, id);
                break;
            case "fingerprint_other":
                await SqlAsync("UPDATE verification_executions SET CheckpointFingerprintSha256 = {0} WHERE Id = {1}", new string('c', 64), id);
                break;
            case "outcome_present":
                await SqlAsync("UPDATE verification_executions SET Outcome = 'Exited' WHERE Id = {0}", id);
                break;
            case "exit_code_present":
                await SqlAsync("UPDATE verification_executions SET ExitCode = 0 WHERE Id = {0}", id);
                break;
            case "dispatched":
                await scene.DispatchVerificationAsync(id);
                break;
            default:
                await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => BreakStepAsync(kind, ticket.StepId));
                return;
        }

        var otherEvents = (await other.FootprintAsync()).Events;
        scene.Clock.Advance(TimeSpan.FromMinutes(30));

        await scene.InterruptAsync();
        var recorded = await scene.RecordVerificationDispatchRefusalAsync(id);

        Assert.True(recorded.IsFailure);
        var after = await scene.ReadExecutionAsync(id);
        Assert.Equal(VerificationExecutionStatus.Running, after.Status);
        Assert.Null(after.CompletedAtUtc);
        Assert.Null(after.CompletionFingerprintSha256);
        Assert.Equal(otherEvents, (await other.FootprintAsync()).Events);
        await AssertOwnerKeptAsync(scene);
    }
}
