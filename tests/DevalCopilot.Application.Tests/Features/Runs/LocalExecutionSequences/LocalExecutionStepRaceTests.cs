using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Projects.Commands.UpdateVerificationCommand;
using DevalCopilot.Application.Features.Runs.Commands.AbandonManualRun;
using DevalCopilot.Application.Features.Runs.Commands.CreateImplementationAttempt;
using DevalCopilot.Application.Features.Runs.Commands.SetClaudeMutationTurnLimit;
using DevalCopilot.Application.Features.Runs.Commands.SetExecutionCapacityLimit;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.ExecutionCapacity;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences.ScriptedResultKind;
using Step = DevalCopilot.Domain.Features.Runs.LocalExecutionStepKind;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;

/// <summary>
/// Races on independent SQLite connections around the claim and the dispatch of a local execution step (ADR-0060): each pair is decided
/// by the locks, exactly one side wins and the other leaves no residue, whichever order the scheduler picks. Capacity changes are
/// exercised in both orderings with the limit at one and at two.
/// </summary>
public sealed class LocalExecutionStepRaceTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    private async Task<(LocalExecutionSequenceTestScene Scene, Guid SequenceId)> ArmedAsync(SqliteDatabaseFixture? database = null, int recipeCount = 2)
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(database ?? fixture, recipeCount);
        var plan = await scene.AddAcceptedRootPlanAsync();
        return (scene, (await scene.ArmAsync(plan.ProposalId)).SequenceId);
    }

    private async Task<Result<SetExecutionCapacityLimitCommandResult>> SetLimitAsync(SqliteDatabaseFixture database, int limit)
    {
        await using var context = database.CreateContext();
        return await new SetExecutionCapacityLimitCommandHandler(context).HandleAsync(
            new SetExecutionCapacityLimitCommand(limit), CancellationToken.None);
    }

    // ---- claim against dispatch, expiry and replay -------------------------------------------------------------------------------

    [Fact]
    public async Task A_dispatch_marker_racing_its_own_claim_never_dispatches_an_unclaimed_operation()
    {
        for (var round = 0; round < 3; round++)
        {
            await using var database = new SqliteDatabaseFixture();
            await database.InitializeAsync();
            var (scene, sequenceId) = await ArmedAsync(database);
            var ticket = await scene.ObserveReservingAsync(sequenceId);

            var claim = Task.Run(() => scene.ClaimAsync(ticket));
            var dispatch = Task.Run(() => scene.DispatchAgentAsync(ticket.ReservedOperationId));
            await Task.WhenAll(claim, dispatch);

            Assert.True((await claim).IsSuccess);
            var attempt = await scene.ReadAttemptAsync(ticket.ReservedOperationId);
            Assert.Single(await scene.ReadLocalAttemptsAsync());
            // Either the marker found the committed claim and dispatched it, or it found nothing and left it untouched.
            Assert.Equal((await dispatch).IsSuccess, attempt.AgentDispatchedAtUtc is not null);
            await database.DisposeAsync();
        }
    }

    [Fact]
    public async Task A_dispatch_racing_the_expiry_observation_is_either_dispatched_or_refused_dedicatedly_never_both()
    {
        for (var round = 0; round < 3; round++)
        {
            await using var database = new SqliteDatabaseFixture();
            await database.InitializeAsync();
            var (scene, sequenceId) = await ArmedAsync(database);
            var ticket = await scene.ReserveAndClaimAsync(sequenceId);
            scene.Clock.Advance(TimeSpan.FromMinutes(30));

            var observe = Task.Run(() => scene.ObserveAsync(sequenceId));
            var dispatch = Task.Run(() => scene.DispatchAgentAsync(ticket.ReservedOperationId));
            await Task.WhenAll(observe, dispatch);

            Assert.True((await observe).IsSuccess);
            // The result deadline had already elapsed, so the dispatch marker is refused whichever transaction came first.
            Assert.True((await dispatch).IsFailure);
            Assert.Null((await scene.ReadAttemptAsync(ticket.ReservedOperationId)).AgentDispatchedAtUtc);
            Assert.True((await scene.RecordAgentDispatchRefusalAsync(ticket.ReservedOperationId)).IsSuccess);
            Assert.Equal(LocalExecutionSequenceState.NeedsAttention, (await scene.ReadSequenceAsync()).State);
            await database.DisposeAsync();
        }
    }

    [Fact]
    public async Task A_replayed_ticket_after_its_result_concluded_claims_nothing_and_changes_nothing()
    {
        var (scene, sequenceId) = await ArmedAsync();
        var ticket = await scene.ReserveAndClaimAsync(sequenceId);
        await scene.CompleteClaimedAsync(ticket);
        await scene.ObserveAsync(sequenceId);
        var before = await scene.FootprintAsync();

        var replay = await scene.ClaimAsync(ticket);

        Assert.True(replay.IsFailure);
        Assert.Equal(before.Attempts, (await scene.FootprintAsync()).Attempts);
        Assert.Equal(before.ClaimedSteps, (await scene.FootprintAsync()).ClaimedSteps);
    }

    // ---- manual writers against the claim -----------------------------------------------------------------------------------------

    [Fact]
    public async Task A_manual_claim_racing_the_sequence_claim_loses_and_the_reserved_identity_is_the_only_operation()
    {
        for (var round = 0; round < 3; round++)
        {
            await using var database = new SqliteDatabaseFixture();
            await database.InitializeAsync();
            var (scene, sequenceId) = await ArmedAsync(database);
            var ticket = await scene.ObserveReservingAsync(sequenceId);
            var plan = (await scene.ReadSequenceAsync()).PlanProposalMessageId;

            var sequenceClaim = Task.Run(() => scene.ClaimAsync(ticket));
            var manual = Task.Run(async () =>
            {
                await using var context = database.CreateContext();
                return await new CreateImplementationAttemptCommandHandler(context, scene.Evidence, scene.Artifacts, scene.Clock, scene.AttemptProbe)
                    .HandleAsync(new CreateImplementationAttemptCommand(scene.RunId, plan), CancellationToken.None);
            });
            await Task.WhenAll(sequenceClaim, manual);

            Assert.True((await sequenceClaim).IsSuccess);
            Assert.True((await manual).IsFailure);
            Assert.Equal(ticket.ReservedOperationId, Assert.Single(await scene.ReadLocalAttemptsAsync()).Id);
            await database.DisposeAsync();
        }
    }

    [Fact]
    public async Task An_abandonment_racing_the_claim_is_withheld_and_the_claim_commits()
    {
        for (var round = 0; round < 3; round++)
        {
            await using var database = new SqliteDatabaseFixture();
            await database.InitializeAsync();
            var (scene, sequenceId) = await ArmedAsync(database);
            var ticket = await scene.ObserveReservingAsync(sequenceId);

            var claim = Task.Run(() => scene.ClaimAsync(ticket));
            var abandon = Task.Run(async () =>
            {
                try
                {
                    await using var context = database.CreateContext();
                    return await new AbandonManualRunCommandHandler(context, scene.Clock).HandleAsync(
                        new AbandonManualRunCommand(scene.RunId, "No longer needed."), CancellationToken.None);
                }
                catch (DbUpdateException)
                {
                    return Result<AbandonManualRunCommandResult>.Failure(LocalExecutionSequenceErrors.ManualControlBlocked());
                }
            });
            await Task.WhenAll(claim, abandon);

            Assert.True((await claim).IsSuccess);
            Assert.True((await abandon).IsFailure);
            Assert.Equal(RunLifecycle.Running, (await scene.ReadRunAsync()).Lifecycle);
            await database.DisposeAsync();
        }
    }

    [Fact]
    public async Task A_recipe_edit_racing_the_verification_claim_is_withheld_and_the_pinned_recipe_runs()
    {
        for (var round = 0; round < 3; round++)
        {
            await using var database = new SqliteDatabaseFixture();
            await database.InitializeAsync();
            var (scene, sequenceId) = await ArmedAsync(database);
            await scene.StepAsync(sequenceId);
            var ticket = await scene.ObserveReservingAsync(sequenceId);
            var recipe = (await scene.ReadRecipesAsync())[0];

            var claim = Task.Run(() => scene.ClaimAsync(ticket));
            var edit = Task.Run(async () =>
            {
                try
                {
                    await using var context = database.CreateContext();
                    return await new UpdateVerificationCommandCommandHandler(context, scene.Clock).HandleAsync(
                        new UpdateVerificationCommandCommand(
                            scene.ProjectId, recipe.Id, recipe.Name, recipe.ExecutablePath, recipe.Arguments, recipe.TimeoutSeconds + 1, true),
                        CancellationToken.None);
                }
                catch (DbUpdateException)
                {
                    return null;
                }
            });
            await Task.WhenAll(claim, edit);

            Assert.True((await claim).IsSuccess);
            Assert.True((await edit) is not { IsSuccess: true });
            Assert.Equal(recipe.TimeoutSeconds, (await scene.ReadRecipesAsync())[0].TimeoutSeconds);
            Assert.Equal(recipe.TimeoutSeconds, (await scene.ReadExecutionAsync(ticket.ReservedOperationId)).TimeoutSeconds);
            await database.DisposeAsync();
        }
    }

    [Fact]
    public async Task A_setting_edit_racing_the_claim_is_withheld_and_the_pinned_settings_hold()
    {
        for (var round = 0; round < 3; round++)
        {
            await using var database = new SqliteDatabaseFixture();
            await database.InitializeAsync();
            var (scene, sequenceId) = await ArmedAsync(database);
            var ticket = await scene.ObserveReservingAsync(sequenceId);
            var before = await scene.ReadRunAsync();

            var claim = Task.Run(() => scene.ClaimAsync(ticket));
            var edit = Task.Run(async () =>
            {
                try
                {
                    await using var context = database.CreateContext();
                    return await new SetClaudeMutationTurnLimitCommandHandler(context, scene.Clock).HandleAsync(
                        new SetClaudeMutationTurnLimitCommand(scene.RunId, 7), CancellationToken.None);
                }
                catch (DbUpdateException)
                {
                    return null;
                }
            });
            await Task.WhenAll(claim, edit);

            Assert.True((await claim).IsSuccess);
            Assert.True((await edit) is not { IsSuccess: true });
            Assert.Equal(before.RequestedClaudeMaxTurns, (await scene.ReadRunAsync()).RequestedClaudeMaxTurns);
            await database.DisposeAsync();
        }
    }

    // ---- global capacity -------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(2, 2)]
    public async Task A_claim_is_admitted_exactly_while_the_recorded_owners_are_below_the_limit(int limit, int otherOwners)
    {
        var (scene, sequenceId) = await ArmedAsync();
        await ExecutionCapacitySeed.SetLimitAsync(fixture, limit);
        for (var owner = 0; owner < otherOwners; owner++)
        {
            await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.Agent);
        }

        var ticket = await scene.ObserveReservingAsync(sequenceId);
        var before = await scene.FootprintAsync();

        var claim = await scene.ClaimAsync(ticket);

        if (otherOwners < limit)
        {
            Assert.True(claim.IsSuccess, claim.IsFailure ? claim.Errors[0].Code : null);
        }
        else
        {
            Assert.True(claim.IsFailure);
            Assert.Equal(ExecutionCapacityErrors.LimitReachedCode, claim.Errors[0].Code);
            var after = await scene.FootprintAsync();
            Assert.Equal(before.Attempts, after.Attempts);
            Assert.Equal(before.ClaimedSteps, after.ClaimedSteps);
            // The conflict ends the sequence with attention and consumes no operation.
            await scene.ResolveAsync(ticket, claim.Errors[0].Code);
            Assert.Equal(LocalExecutionSequenceState.NeedsAttention, (await scene.ReadSequenceAsync()).State);
            Assert.Equal(LocalExecutionSequenceReason.CapacityRefused, (await scene.ReadSequenceAsync()).ReasonCode);
        }
    }

    [Fact]
    public async Task Lowering_the_limit_is_refused_while_a_linked_mutating_owner_and_another_owner_are_recorded()
    {
        var (scene, sequenceId) = await ArmedAsync();
        await ExecutionCapacitySeed.SetLimitAsync(fixture, 2);
        await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.Agent);
        await scene.ReserveAndClaimAsync(sequenceId);

        var lowered = await SetLimitAsync(fixture, 1);

        Assert.True(lowered.IsFailure);
        Assert.Equal(ExecutionCapacityErrors.LimitBelowRecordedOwnersCode, lowered.Errors[0].Code);
    }

    [Fact]
    public async Task A_quarantined_terminal_without_exit_proof_owner_still_blocks_lowering_and_counts_once()
    {
        var (scene, sequenceId) = await ArmedAsync();
        await ExecutionCapacitySeed.SetLimitAsync(fixture, 2);
        await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.Agent);
        await scene.DriveAsync(sequenceId, new LocalExecutionStepScript().On(Step.Implementation, ProviderFailedNoExit));
        await using (var context = fixture.CreateContext())
        {
            var reading = await ExecutionCapacityPolicy.ReadAsync(context, null, CancellationToken.None);
            Assert.Equal(2, reading.RecordedOwners);
        }

        var lowered = await SetLimitAsync(fixture, 1);

        Assert.Equal(ExecutionCapacityErrors.LimitBelowRecordedOwnersCode, lowered.Errors[0].Code);
    }

    [Fact]
    public async Task A_dispatch_excludes_exactly_its_own_owner_at_limit_one_and_two()
    {
        var (scene, sequenceId) = await ArmedAsync();
        var ticket = await scene.ReserveAndClaimAsync(sequenceId);
        await ExecutionCapacitySeed.SetLimitAsync(fixture, 1);

        var alone = await scene.DispatchAgentAsync(ticket.ReservedOperationId);

        Assert.True(alone.IsSuccess, alone.IsFailure ? alone.Errors[0].Code : null);
    }

    [Fact]
    public async Task A_dispatch_beside_a_full_limit_of_other_owners_is_refused_and_the_claim_stays_undispatched()
    {
        var (scene, sequenceId) = await ArmedAsync();
        var ticket = await scene.ReserveAndClaimAsync(sequenceId);
        await ExecutionCapacitySeed.SetLimitAsync(fixture, 2);
        await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.Agent);
        await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.Verification);

        var dispatch = await scene.DispatchAgentAsync(ticket.ReservedOperationId);

        Assert.True(dispatch.IsFailure);
        Assert.Null((await scene.ReadAttemptAsync(ticket.ReservedOperationId)).AgentDispatchedAtUtc);
    }

    [Fact]
    public async Task A_limit_change_racing_a_claim_is_decided_by_the_locks_in_either_order()
    {
        for (var round = 0; round < 3; round++)
        {
            await using var database = new SqliteDatabaseFixture();
            await database.InitializeAsync();
            var (scene, sequenceId) = await ArmedAsync(database);
            await ExecutionCapacitySeed.SetLimitAsync(database, 2);
            await ExecutionCapacitySeed.AddAsync(database, OwnerFamily.Agent);
            var ticket = await scene.ObserveReservingAsync(sequenceId);

            var claim = Task.Run(() => scene.ClaimAsync(ticket));
            var lower = Task.Run(() => SetLimitAsync(database, 1));
            await Task.WhenAll(claim, lower);

            // One other owner is recorded: lowering to one succeeds only before the claim's owner exists, and the claim then refuses.
            Assert.NotEqual((await claim).IsSuccess, (await lower).IsSuccess);
            var attempts = (await scene.ReadLocalAttemptsAsync()).Count;
            Assert.Equal((await claim).IsSuccess ? 1 : 0, attempts);
            await database.DisposeAsync();
        }
    }

    [Fact]
    public async Task A_limit_change_racing_a_dispatch_never_strands_the_admitted_owner()
    {
        for (var round = 0; round < 3; round++)
        {
            await using var database = new SqliteDatabaseFixture();
            await database.InitializeAsync();
            var (scene, sequenceId) = await ArmedAsync(database);
            await ExecutionCapacitySeed.SetLimitAsync(database, 2);
            var ticket = await scene.ReserveAndClaimAsync(sequenceId);

            var dispatch = Task.Run(() => scene.DispatchAgentAsync(ticket.ReservedOperationId));
            var lower = Task.Run(() => SetLimitAsync(database, 1));
            await Task.WhenAll(dispatch, lower);

            // The claimed owner is excluded from itself at dispatch, so lowering to one never blocks it and is never blocked by it.
            Assert.True((await dispatch).IsSuccess);
            Assert.True((await lower).IsSuccess);
            await database.DisposeAsync();
        }
    }
}
