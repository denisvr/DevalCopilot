using DevalCopilot.Application.Features.Projects.Commands.MarkVerificationExecutionDispatched;
using DevalCopilot.Application.Features.Projects.Policies.WorkspaceExecutions;
using DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Step = DevalCopilot.Domain.Features.Runs.LocalExecutionStepKind;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;

/// <summary>
/// The dispatch gates and the dedicated refusal recorders (ADR-0060) at command level: a linked operation is dispatchable only with
/// its exact step, source, pinned recipes and settings and an unexpired deadline while the sequence is AwaitingStepResult or Stopping;
/// the recorders write only for a proved linked undispatched operation, once, with no protocol result and nothing refunded, and an
/// unknown association is refused with nothing fabricated; ordinary refusals keep their own reasons.
/// </summary>
public sealed class LocalExecutionDispatchGateTests : IAsyncLifetime
{
    public static IEnumerable<object[]> Kinds() => new[] { Step.Implementation, Step.Verification }.Select(kind => new object[] { kind });

    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    private async Task<(LocalExecutionSequenceTestScene Scene, Guid SequenceId, LocalExecutionStepTicket Ticket)> ClaimedAsync(Step kind)
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture, recipeCount: 2);
        var plan = await scene.AddAcceptedRootPlanAsync();
        var sequenceId = (await scene.ArmAsync(plan.ProposalId)).SequenceId;
        var ticket = kind == Step.Implementation
            ? await scene.ObserveReservingAsync(sequenceId)
            : await scene.ReserveUntilAsync(sequenceId, Step.Verification);
        var claim = await scene.ClaimAsync(ticket);
        Assert.True(claim.IsSuccess, claim.IsFailure ? claim.Errors[0].Code : null);
        return (scene, sequenceId, ticket);
    }

    private static async Task<(bool Succeeded, string? Code)> DispatchAsync(LocalExecutionSequenceTestScene scene, LocalExecutionStepTicket ticket)
    {
        if (ticket.Kind == Step.Verification)
        {
            var verification = await scene.DispatchVerificationAsync(ticket.ReservedOperationId);
            return (verification.IsSuccess, verification.IsFailure ? verification.Errors[0].Code : null);
        }

        var agent = await scene.DispatchAgentAsync(ticket.ReservedOperationId);
        return (agent.IsSuccess, agent.IsFailure ? agent.Errors[0].Code : null);
    }

    private static async Task<bool> IsDispatchedAsync(LocalExecutionSequenceTestScene scene, LocalExecutionStepTicket ticket) =>
        ticket.Kind == Step.Verification
            ? (await scene.ReadExecutionAsync(ticket.ReservedOperationId)).DispatchedAtUtc is not null
            : (await scene.ReadAttemptAsync(ticket.ReservedOperationId)).AgentDispatchedAtUtc is not null;

    private static async Task<bool> RecordRefusalAsync(LocalExecutionSequenceTestScene scene, LocalExecutionStepTicket ticket) =>
        ticket.Kind == Step.Verification
            ? (await scene.RecordVerificationDispatchRefusalAsync(ticket.ReservedOperationId)).IsSuccess
            : (await scene.RecordAgentDispatchRefusalAsync(ticket.ReservedOperationId)).IsSuccess;

    // ---- dispatchable ----------------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task An_exact_claimed_step_is_dispatchable_while_awaiting_its_result(Step kind)
    {
        var (scene, _, ticket) = await ClaimedAsync(kind);

        var dispatch = await DispatchAsync(scene, ticket);

        Assert.True(dispatch.Succeeded, dispatch.Code);
        Assert.True(await IsDispatchedAsync(scene, ticket));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task A_stopping_sequence_still_lets_its_one_claimed_operation_dispatch(Step kind)
    {
        var (scene, sequenceId, ticket) = await ClaimedAsync(kind);
        await scene.StopAsync(sequenceId);
        Assert.Equal(LocalExecutionSequenceState.Stopping, (await scene.ReadSequenceAsync()).State);

        var dispatch = await DispatchAsync(scene, ticket);

        Assert.True(dispatch.Succeeded, dispatch.Code);
    }

    // ---- refused ---------------------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task An_elapsed_unobserved_deadline_refuses_the_dispatch_dedicatedly_and_the_recorder_closes_it(Step kind)
    {
        var (scene, _, ticket) = await ClaimedAsync(kind);
        scene.Clock.Advance(TimeSpan.FromMinutes(30));

        var dispatch = await DispatchAsync(scene, ticket);

        Assert.False(dispatch.Succeeded);
        Assert.Equal(
            kind == Step.Verification
                ? MarkVerificationExecutionDispatchedCommandHandler.LocalExecutionSequenceDispatchRefusedCode
                : MarkAgentAttemptDispatchedCommandHandler.LocalExecutionSequenceDispatchRefusedCode,
            dispatch.Code);
        Assert.False(await IsDispatchedAsync(scene, ticket));
        Assert.True(await RecordRefusalAsync(scene, ticket));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task A_sequence_that_needs_attention_refuses_the_dispatch(Step kind)
    {
        var (scene, sequenceId, ticket) = await ClaimedAsync(kind);
        scene.Clock.Advance(TimeSpan.FromMinutes(30));
        await scene.ObserveAsync(sequenceId);
        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, (await scene.ReadSequenceAsync()).State);
        scene.Clock.Advance(-TimeSpan.FromMinutes(30));

        var dispatch = await DispatchAsync(scene, ticket);

        Assert.False(dispatch.Succeeded);
        Assert.False(await IsDispatchedAsync(scene, ticket));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task An_interrupted_sequence_refuses_the_dispatch_and_the_startup_closure_already_recorded_the_refusal(Step kind)
    {
        var (scene, _, ticket) = await ClaimedAsync(kind);
        await scene.InterruptAsync();

        var dispatch = await DispatchAsync(scene, ticket);

        Assert.False(dispatch.Succeeded);
        Assert.False(await IsDispatchedAsync(scene, ticket));
    }

    [Theory]
    [InlineData("recipe")]
    [InlineData("setting")]
    public async Task A_pinned_recipe_or_setting_drift_refuses_the_dispatch_of_a_verification_and_of_an_agent(string drift)
    {
        foreach (var kind in new[] { Step.Implementation, Step.Verification })
        {
            await using var database = new SqliteDatabaseFixture();
            await database.InitializeAsync();
            var scene = await LocalExecutionSequenceTestScene.CreateAsync(database, recipeCount: 2);
            var plan = await scene.AddAcceptedRootPlanAsync();
            var sequenceId = (await scene.ArmAsync(plan.ProposalId)).SequenceId;
            var ticket = kind == Step.Implementation ? await scene.ObserveReservingAsync(sequenceId) : await scene.ReserveUntilAsync(sequenceId, kind);
            Assert.True((await scene.ClaimAsync(ticket)).IsSuccess);
            if (drift == "recipe")
            {
                await scene.ExecuteSqlAsync("DROP TRIGGER trg_local_execution_guard_verification_commands_update");
                await scene.EditRecipeAsync((await scene.ReadRecipesAsync())[1].Id, timeoutSeconds: 31);
            }
            else
            {
                await scene.ExecuteSqlAsync("DROP TRIGGER trg_local_execution_guard_run_settings");
                await using var context = database.CreateContext();
                var run = await context.Runs.SingleAsync(candidate => candidate.Id == scene.RunId);
                run.SetRequestedClaudeMaxTurns(9);
                await context.SaveChangesAsync();
            }

            var dispatch = await DispatchAsync(scene, ticket);

            Assert.False(dispatch.Succeeded);
            Assert.False(await IsDispatchedAsync(scene, ticket));
            await database.DisposeAsync();
        }
    }

    [Fact]
    public async Task An_ordinary_workspace_refusal_keeps_its_own_reason_and_is_not_the_dedicated_one()
    {
        var (scene, _, ticket) = await ClaimedAsync(Step.Implementation);
        await WorkspaceExecutionRaceSupport.AddAsync(fixture, scene.RunId, CompetingOwner.AgentOfAnotherRun);

        var dispatch = await DispatchAsync(scene, ticket);

        Assert.False(dispatch.Succeeded);
        Assert.NotEqual(MarkAgentAttemptDispatchedCommandHandler.LocalExecutionSequenceDispatchRefusedCode, dispatch.Code);
        Assert.Equal(WorkspaceExecutionErrors.AgentAttemptActiveCode, dispatch.Code);
        // The ordinary refusal is not the dedicated proof: the recorder refuses it while the sequence still authorizes the dispatch.
        Assert.False(await RecordRefusalAsync(scene, ticket));
    }

    // ---- recorders ---------------------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task A_recorder_writes_once_with_no_protocol_result_and_refunds_nothing(Step kind)
    {
        var (scene, _, ticket) = await ClaimedAsync(kind);
        scene.Clock.Advance(TimeSpan.FromMinutes(30));
        var before = await scene.FootprintAsync();
        var slots = (await scene.ReadAttemptsAsync()).Count(attempt => attempt.AgentBudgetSlot is not null);

        Assert.True(await RecordRefusalAsync(scene, ticket));
        var afterFirst = await scene.FootprintAsync();
        // Idempotent: a repeat writes nothing (the verification recorder reports the recorded refusal as success, the Agent one as a no-op).
        await RecordRefusalAsync(scene, ticket);
        var afterSecond = await scene.FootprintAsync();

        Assert.Equal(afterFirst, afterSecond);
        Assert.Equal(before.Attempts, afterFirst.Attempts);
        Assert.Equal(before.Executions, afterFirst.Executions);
        Assert.Equal(before.Inputs, afterFirst.Inputs);
        Assert.Equal(slots, (await scene.ReadAttemptsAsync()).Count(attempt => attempt.AgentBudgetSlot is not null));
        Assert.DoesNotContain(await scene.ReadMessagesAsync(), message => message.AttemptId == ticket.ReservedOperationId);
        if (kind == Step.Verification)
        {
            var execution = await scene.ReadExecutionAsync(ticket.ReservedOperationId);
            Assert.Equal(VerificationExecutionStatus.LocalExecutionSequenceDispatchRefused, execution.Status);
            Assert.Null(execution.Outcome);
            Assert.Null(execution.ExitCode);
            Assert.Null(execution.CompletionFingerprintSha256);
        }
        else
        {
            var attempt = await scene.ReadAttemptAsync(ticket.ReservedOperationId);
            Assert.Equal(AgentOutcome.LocalExecutionSequenceDispatchRefused, attempt.AgentOutcome);
            Assert.Null(attempt.AgentProcessOutcome);
            Assert.Null(attempt.AgentProcessExitCode);
        }
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task A_recorder_refuses_a_dispatched_operation_and_changes_nothing(Step kind)
    {
        var (scene, _, ticket) = await ClaimedAsync(kind);
        await scene.DispatchAsync(kind, ticket.ReservedOperationId);
        scene.Clock.Advance(TimeSpan.FromMinutes(30));
        var before = await scene.FootprintAsync();

        Assert.False(await RecordRefusalAsync(scene, ticket));

        Assert.Equal(before, await scene.FootprintAsync());
        Assert.True(await IsDispatchedAsync(scene, ticket));
    }

    [Fact]
    public async Task A_recorder_refuses_an_unknown_association_and_an_unlinked_ordinary_operation_without_fabricating_anything()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture, recipeCount: 1);
        await scene.AddAcceptedRootPlanAsync();
        // An ordinary, unlinked, undispatched verification claim of the project.
        await using (var context = fixture.CreateContext())
        {
            var recipe = (await scene.ReadRecipesAsync())[0];
            var claim = await new Application.Features.Projects.Commands.ClaimVerificationExecution.ClaimVerificationExecutionCommandHandler(
                context, scene.Evidence, scene.Clock).HandleAsync(
                new(scene.ProjectId, recipe.Id, scene.CheckpointId), CancellationToken.None);
            Assert.True(claim.IsSuccess);
        }

        var unlinked = (await scene.ReadExecutionsAsync()).Single();
        var before = await scene.FootprintAsync();

        Assert.True((await scene.RecordVerificationDispatchRefusalAsync(unlinked.Id)).IsFailure);
        Assert.True((await scene.RecordVerificationDispatchRefusalAsync(Guid.NewGuid())).IsFailure);
        Assert.True((await scene.RecordAgentDispatchRefusalAsync(Guid.NewGuid())).IsFailure);

        Assert.Equal(before, await scene.FootprintAsync());
        Assert.Equal(VerificationExecutionStatus.Running, (await scene.ReadExecutionAsync(unlinked.Id)).Status);
    }
}
