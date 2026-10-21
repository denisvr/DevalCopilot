using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;

/// <summary>ADR-0060 availability and arming: every fixed refusal in its fixed order, the exact confirmation drift rules, atomic
/// recording of the sequence, its pinned recipes and one Human event, the once-per-run replay and conflict rules, and the guarantee that
/// arming starts, claims and consumes nothing.</summary>
public sealed class LocalExecutionSequenceArmingTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    [Fact]
    public async Task Arming_records_the_sequence_its_pinned_recipes_and_one_human_event_and_consumes_nothing()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture, recipeCount: 3);
        var plan = await scene.AddAcceptedRootPlanAsync();
        var work = await scene.CountWorkAsync();

        var armed = await scene.ArmAsync(plan.ProposalId);

        Assert.False(armed.Replayed);
        Assert.Equal(LocalExecutionSequenceState.Armed, armed.State);
        Assert.Equal(scene.RunId, armed.RunId);
        Assert.Equal(plan.ProposalId, armed.PlanProposalMessageId);
        Assert.Equal(scene.WorkspaceId, armed.WorkspaceId);
        Assert.Equal(scene.CheckpointId, armed.CheckpointId);
        Assert.Equal(LocalExecutionSequenceTestScene.StartFingerprint, armed.FingerprintSha256);
        Assert.Equal(3, armed.RecipeCount);
        Assert.Equal(2, armed.CorrectionAllowance);
        Assert.Equal(1, armed.Revision);
        Assert.Equal(armed.ArmedAtUtc + LocalExecutionSequencePolicy.AdvancementWindow, armed.AdvancementDeadlineUtc);

        var sequence = await scene.ReadSequenceAsync();
        Assert.Equal(scene.ProjectId, sequence.ProjectId);
        Assert.Equal(LocalExecutionPlanAuthority.AcceptedRoot, sequence.PlanAuthority);
        Assert.Equal(armed.ConfirmationSha256, sequence.ConfirmationSha256);
        Assert.StartsWith(LocalExecutionSettingsSnapshot.Version, sequence.SettingsSnapshot, StringComparison.Ordinal);
        Assert.Empty(await scene.ReadStepsAsync());

        // The whole enabled set is pinned by value, in CommandNumber order.
        var pins = await scene.ReadPinnedRecipesAsync();
        var recipes = await scene.ReadRecipesAsync();
        Assert.Equal(recipes.Select(recipe => recipe.Id), pins.Select(pin => pin.VerificationCommandId));
        Assert.Equal([0, 1, 2], pins.Select(pin => pin.Ordinal));
        Assert.All(pins.Zip(recipes), pair => Assert.True(pair.First.Matches(pair.Second)));

        // One Human event, and no provider, process, claim or budget consumption of any kind.
        Assert.Equal(1, await scene.CountEventsAsync(RunEventType.LocalExecutionSequenceArmed));
        Assert.Equal(work.Attempts, (await scene.CountWorkAsync()).Attempts);
        Assert.Equal(0, (await scene.CountWorkAsync()).Executions);
        Assert.Equal(0, scene.Artifacts.SealCount);
        Assert.Equal(1, scene.Evidence.Captures);
        Assert.Equal(RunLifecycle.Running, (await scene.ReadRunAsync()).Lifecycle);
    }

    [Fact]
    public async Task The_availability_discloses_the_exact_candidate_and_never_the_executable_or_arguments()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture, recipeCount: 2);
        var plan = await scene.AddAcceptedRootPlanAsync();

        var availability = await scene.ReadAvailabilityAsync(plan.ProposalId);

        Assert.True(availability.Available);
        Assert.Null(availability.RefusalCode);
        Assert.Equal(LocalExecutionPlanAuthority.AcceptedRoot, availability.PlanAuthority);
        Assert.Equal(scene.WorkspaceId, availability.WorkspaceId);
        Assert.Equal(scene.CheckpointId, availability.CheckpointId);
        Assert.Equal(2, availability.CorrectionAllowance);
        Assert.Equal(12, availability.MaximumSteps);
        Assert.Equal(["Verification 1", "Verification 2"], availability.Recipes!.Select(recipe => recipe.Name));
        Assert.Equal(64, availability.ConfirmationSha256!.Length);
        // The availability reads recorded rows only.
        Assert.Equal(0, scene.Evidence.Captures);
    }

    [Fact]
    public async Task The_correction_allowance_is_the_smaller_of_two_and_the_remaining_shared_allowance()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture, maximumReviewCorrectionAttempts: 1);
        var plan = await scene.AddAcceptedRootPlanAsync();

        var armed = await scene.ArmAsync(plan.ProposalId);

        Assert.Equal(1, armed.CorrectionAllowance);
        Assert.Equal(1, (await scene.ReadSequenceAsync()).CorrectionAllowance);
    }

    [Fact]
    public async Task Repeating_the_identical_request_returns_the_recorded_receipt_without_rearming()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture);
        var plan = await scene.AddAcceptedRootPlanAsync();
        var command = await scene.ArmCommandAsync(plan.ProposalId);
        var first = (await scene.TryArmAsync(command)).Value;
        var captures = scene.Evidence.Captures;

        var replay = await scene.TryArmAsync(command);

        Assert.True(replay.IsSuccess);
        Assert.True(replay.Value.Replayed);
        Assert.Equal(first.SequenceId, replay.Value.SequenceId);
        Assert.Equal(first.ArmedAtUtc, replay.Value.ArmedAtUtc);
        Assert.Equal(1, await scene.CountEventsAsync(RunEventType.LocalExecutionSequenceArmed));
        Assert.Equal(1, (await scene.ReadSequenceAsync()).Revision);
        Assert.Equal(captures, scene.Evidence.Captures);
    }

    [Fact]
    public async Task A_replay_after_the_sequence_ended_returns_the_recorded_receipt_and_never_rearms()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture);
        var plan = await scene.AddAcceptedRootPlanAsync();
        var command = await scene.ArmCommandAsync(plan.ProposalId);
        var first = (await scene.TryArmAsync(command)).Value;
        Assert.True((await scene.StopAsync(first.SequenceId)).IsSuccess);

        var replay = await scene.TryArmAsync(command);

        Assert.True(replay.IsSuccess);
        Assert.True(replay.Value.Replayed);
        Assert.Equal(LocalExecutionSequenceState.Stopped, replay.Value.State);
        Assert.Equal(LocalExecutionSequenceState.Stopped, (await scene.ReadSequenceAsync()).State);
        Assert.Equal(1, await scene.CountEventsAsync(RunEventType.LocalExecutionSequenceArmed));
    }

    [Fact]
    public async Task A_different_tuple_for_an_armed_run_conflicts_and_changes_nothing()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture);
        var plan = await scene.AddAcceptedRootPlanAsync();
        var command = await scene.ArmCommandAsync(plan.ProposalId);
        var first = (await scene.TryArmAsync(command)).Value;

        var results = new[]
        {
            await scene.TryArmAsync(command with { FingerprintSha256 = new string('c', 64) }),
            await scene.TryArmAsync(command with { CheckpointId = Guid.NewGuid() }),
            await scene.TryArmAsync(command with { WorkspaceId = Guid.NewGuid() }),
            await scene.TryArmAsync(command with { PlanProposalMessageId = Guid.NewGuid() }),
            await scene.TryArmAsync(command with { ConfirmationSha256 = new string('d', 64) }),
        };

        Assert.All(results, result => Assert.Equal(LocalExecutionSequenceErrors.TupleConflictCode, result.Errors[0].Code));
        Assert.Equal(first.SequenceId, (await scene.ReadSequenceAsync()).Id);
        Assert.Equal(1, (await scene.ReadSequenceAsync()).Revision);
        Assert.Equal(1, await scene.CountEventsAsync(RunEventType.LocalExecutionSequenceArmed));
    }

    [Fact]
    public async Task A_run_with_a_sequence_reports_it_already_armed_in_the_availability()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture);
        var plan = await scene.AddAcceptedRootPlanAsync();
        await scene.ArmAsync(plan.ProposalId);

        var availability = await scene.ReadAvailabilityAsync(plan.ProposalId);

        Assert.False(availability.Available);
        Assert.Equal(LocalExecutionSequenceErrors.AlreadyArmedCode, availability.RefusalCode);
    }

    [Fact]
    public async Task An_unknown_run_is_one_fixed_not_found_with_no_external_work()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture);
        var plan = await scene.AddAcceptedRootPlanAsync();
        var command = await scene.ArmCommandAsync(plan.ProposalId);

        var result = await scene.TryArmAsync(command with { RunId = Guid.NewGuid() });

        Assert.Equal(LocalExecutionSequenceErrors.NotFoundCode, result.Errors[0].Code);
        Assert.Null(await scene.FindSequenceAsync());
        Assert.Equal(0, scene.Evidence.Captures);
    }

    // ---- the fixed refusals, in order ------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(RunExecutionMode.Legacy)]
    [InlineData(RunExecutionMode.Simulated)]
    public async Task A_run_that_is_not_ManualAgent_is_refused_before_any_external_work(RunExecutionMode mode)
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture, mode: mode);
        var plan = await scene.AddAcceptedRootPlanAsync();

        await AssertRefusedAsync(scene, plan.ProposalId, LocalExecutionSequenceErrors.RunNotManualCode);
    }

    [Fact]
    public async Task A_run_that_was_never_claimed_is_not_running()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture, claimRun: false);
        var plan = await scene.AddAcceptedRootPlanAsync();

        await AssertRefusedAsync(scene, plan.ProposalId, LocalExecutionSequenceErrors.RunNotRunningCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Any_earlier_mutating_claim_or_running_attempt_of_the_run_is_a_prior_mutation(bool running)
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture);
        var plan = await scene.AddAcceptedRootPlanAsync();
        await AddImplementationAttemptAsync(scene, dispatched: true, complete: !running);

        await AssertRefusedAsync(scene, plan.ProposalId, LocalExecutionSequenceErrors.PriorMutationCode);
    }

    [Fact]
    public async Task A_running_verification_execution_of_the_project_makes_it_busy()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture);
        var plan = await scene.AddAcceptedRootPlanAsync();
        await WorkspaceExecutionRaceSupport.AddAsync(fixture, scene.RunId, CompetingOwner.Verification);

        await AssertRefusedAsync(scene, plan.ProposalId, LocalExecutionSequenceErrors.ProjectBusyCode);
    }

    [Fact]
    public async Task A_running_attempt_of_another_run_of_the_project_makes_it_busy()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture);
        var plan = await scene.AddAcceptedRootPlanAsync();
        await WorkspaceExecutionRaceSupport.AddAsync(fixture, scene.RunId, CompetingOwner.AgentOfAnotherRun);

        await AssertRefusedAsync(scene, plan.ProposalId, LocalExecutionSequenceErrors.ProjectBusyCode);
    }

    [Fact]
    public async Task A_workspace_that_needs_attention_is_not_ready()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture);
        var plan = await scene.AddAcceptedRootPlanAsync();
        await scene.ExecuteSqlAsync("UPDATE git_workspaces SET Status = 'NeedsAttention' WHERE Id = {0}", scene.WorkspaceId);

        var availability = await scene.ReadAvailabilityAsync(plan.ProposalId);

        Assert.False(availability.Available);
        Assert.Contains(
            availability.RefusalCode, new[] { LocalExecutionSequenceErrors.WorkspaceNotReadyCode, LocalExecutionSequenceErrors.ProjectBusyCode });
    }

    [Fact]
    public async Task A_released_lease_refuses_the_arming()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture);
        var plan = await scene.AddAcceptedRootPlanAsync();
        await scene.ExecuteSqlAsync("UPDATE repository_mutation_leases SET Status = 'Released' WHERE WorkspaceId = {0}", scene.WorkspaceId);

        await AssertRefusedAsync(scene, plan.ProposalId, LocalExecutionSequenceErrors.LeaseNotActiveCode);
    }

    [Fact]
    public async Task A_newer_workspace_without_a_checkpoint_refuses_the_arming()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture);
        var plan = await scene.AddAcceptedRootPlanAsync();
        await using (var context = fixture.CreateContext())
        {
            var workspace = GitWorkspace.Prepare(
                Guid.NewGuid(), scene.ProjectId, 2, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", LocalExecutionSequenceTestScene.Head, "main",
                LocalExecutionSequenceTestScene.Start);
            workspace.MarkReady();
            context.GitWorkspaces.Add(workspace);
            context.RepositoryMutationLeases.Add(RepositoryMutationLease.Acquire(
                Guid.NewGuid(), scene.ProjectId, workspace.Id, BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0), Guid.NewGuid().ToByteArray(),
                LocalExecutionSequenceTestScene.Start));
            await context.SaveChangesAsync();
        }

        await AssertRefusedAsync(scene, plan.ProposalId, LocalExecutionSequenceErrors.CheckpointMissingCode);
    }

    [Fact]
    public async Task A_recorded_global_limit_reached_by_another_project_is_capacity_unavailable()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture);
        var plan = await scene.AddAcceptedRootPlanAsync();
        await ExecutionCapacitySeed.SetLimitAsync(fixture, 1);
        await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.Agent);

        await AssertRefusedAsync(scene, plan.ProposalId, LocalExecutionSequenceErrors.CapacityUnavailableCode);
    }

    [Fact]
    public async Task An_unknown_plan_message_is_plan_not_found()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture);
        await scene.AddAcceptedRootPlanAsync();

        await AssertRefusedAsync(scene, Guid.NewGuid(), LocalExecutionSequenceErrors.PlanNotFoundCode);
    }

    [Fact]
    public async Task An_accepted_depth_one_revision_is_eligible_with_its_exact_acceptance()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture);
        var plan = await scene.AddAcceptedRevisionPlanAsync();

        await scene.ArmAsync(plan.ProposalId);

        Assert.Equal(LocalExecutionPlanAuthority.AcceptedRevision, (await scene.ReadSequenceAsync()).PlanAuthority);
    }

    [Fact]
    public async Task A_human_authorized_depth_two_plan_is_eligible_and_pins_its_unconsumed_grant()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture);
        var plan = await scene.AddHumanAuthorizedDepthTwoPlanAsync();

        var armed = await scene.ArmAsync(plan.ProposalId);

        var sequence = await scene.ReadSequenceAsync();
        Assert.Equal(LocalExecutionPlanAuthority.HumanAuthorizedRevision, sequence.PlanAuthority);
        Assert.Equal(armed.SequenceId, sequence.Id);
        await using var context = fixture.CreateContext();
        var grant = await context.PlanningImplementationAuthorizations.AsNoTracking().SingleAsync(candidate => candidate.RunId == scene.RunId);
        Assert.Equal(plan.GrantId, grant.Id);
        Assert.True(grant.IsAvailable);
    }

    [Fact]
    public async Task An_unreviewed_first_revision_is_refused_although_an_ordinary_implementation_admits_it()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture);
        var plan = await scene.AddUnreviewedRevisionPlanAsync();

        await AssertRefusedAsync(scene, plan.ProposalId, LocalExecutionSequenceErrors.PlanNotEligibleCode);
    }

    [Fact]
    public async Task A_challenged_plan_is_refused()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture);
        var plan = await scene.AddChallengedPlanAsync();

        await AssertRefusedAsync(scene, plan.ProposalId, LocalExecutionSequenceErrors.PlanNotEligibleCode);
    }

    [Fact]
    public async Task A_human_authorized_plan_superseded_by_a_newer_planner_proposal_is_refused()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture);
        var plan = await scene.AddHumanAuthorizedDepthTwoPlanAsync();
        await scene.AddNewerPlannerProposalAsync();

        await AssertRefusedAsync(scene, plan.ProposalId, LocalExecutionSequenceErrors.PlanNotEligibleCode);
    }

    [Fact]
    public async Task An_accepted_root_stays_eligible_beside_a_newer_proposal_exactly_like_an_ordinary_claim()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture);
        var plan = await scene.AddAcceptedRootWithNewerProposalAsync();

        var availability = await scene.ReadAvailabilityAsync(plan.ProposalId);

        Assert.True(availability.Available);
    }

    [Fact]
    public async Task A_non_proposal_message_is_not_an_eligible_plan()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture);
        var plan = await scene.AddAcceptedRootPlanAsync();

        await AssertRefusedAsync(scene, plan.AcceptanceId!.Value, LocalExecutionSequenceErrors.PlanNotEligibleCode);
    }

    [Fact]
    public async Task A_plan_whose_workspace_checkpoint_moved_is_refused()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture);
        var plan = await scene.AddAcceptedRootPlanAsync();
        await AddCheckpointAsync(scene, number: 2, fingerprint: new string('c', 64));

        await AssertRefusedAsync(scene, plan.ProposalId, LocalExecutionSequenceErrors.PlanNotEligibleCode);
    }

    [Fact]
    public async Task A_project_without_enabled_recipes_is_recipes_missing_and_a_disabled_recipe_does_not_count()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture, recipeCount: 0);
        var plan = await scene.AddAcceptedRootPlanAsync();
        await scene.AddRecipeAsync(1, enabled: false);

        await AssertRefusedAsync(scene, plan.ProposalId, LocalExecutionSequenceErrors.RecipesMissingCode);
    }

    [Fact]
    public async Task Exactly_thirty_two_recipes_are_pinned_and_thirty_three_are_refused_instead_of_truncated()
    {
        var thirtyTwo = await LocalExecutionSequenceTestScene.CreateAsync(fixture, recipeCount: 32);
        var plan = await thirtyTwo.AddAcceptedRootPlanAsync();
        var armed = await thirtyTwo.ArmAsync(plan.ProposalId);
        Assert.Equal(32, armed.RecipeCount);
        Assert.Equal(32, (await thirtyTwo.ReadPinnedRecipesAsync()).Count);
        Assert.Equal(102, LocalExecutionSequencePolicy.MaximumSteps(armed.RecipeCount));

        await using var other = new SqliteDatabaseFixture();
        await other.InitializeAsync();
        var thirtyThree = await LocalExecutionSequenceTestScene.CreateAsync(other, recipeCount: 33);
        var plan33 = await thirtyThree.AddAcceptedRootPlanAsync();
        await AssertRefusedAsync(thirtyThree, plan33.ProposalId, LocalExecutionSequenceErrors.RecipesTooManyCode);
        await other.DisposeAsync();
    }

    [Fact]
    public async Task A_recipe_with_an_unusable_stored_timeout_makes_the_set_incoherent()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture, recipeCount: 2);
        var plan = await scene.AddAcceptedRootPlanAsync();
        await scene.ExecuteSqlAsync("UPDATE verification_commands SET TimeoutSeconds = 0 WHERE ProjectId = {0} AND CommandNumber = 2", scene.ProjectId);

        await AssertRefusedAsync(scene, plan.ProposalId, LocalExecutionSequenceErrors.RecipesIncoherentCode);
    }

    [Fact]
    public async Task A_malformed_stored_turn_limit_refuses_with_settings_unavailable()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture);
        var plan = await scene.AddAcceptedRootPlanAsync();
        await scene.ExecuteSqlAsync("UPDATE runs SET RequestedClaudeMaxTurns = 'not-a-number' WHERE Id = {0}", scene.RunId);

        await AssertRefusedAsync(scene, plan.ProposalId, LocalExecutionSequenceErrors.SettingsUnavailableCode);
    }

    // ---- tuple and confirmation drift ------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_stale_requested_tuple_is_refused_without_any_capture_or_write()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture);
        var plan = await scene.AddAcceptedRootPlanAsync();
        var command = await scene.ArmCommandAsync(plan.ProposalId);

        var checkpoint = await scene.TryArmAsync(command with { CheckpointId = Guid.NewGuid() });
        var workspace = await scene.TryArmAsync(command with { WorkspaceId = Guid.NewGuid() });
        var fingerprint = await scene.TryArmAsync(command with { FingerprintSha256 = new string('c', 64) });

        Assert.All(new[] { checkpoint, workspace, fingerprint }, result =>
            Assert.Equal(LocalExecutionSequenceErrors.TupleStaleCode, result.Errors[0].Code));
        Assert.Equal(0, scene.Evidence.Captures);
        Assert.Null(await scene.FindSequenceAsync());
    }

    [Fact]
    public async Task A_fresh_git_capture_that_differs_from_the_confirmed_fingerprint_is_tuple_stale()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture);
        var plan = await scene.AddAcceptedRootPlanAsync();
        var command = await scene.ArmCommandAsync(plan.ProposalId);
        scene.Evidence.Fingerprint = new string('9', 64);

        var result = await scene.TryArmAsync(command);

        Assert.Equal(LocalExecutionSequenceErrors.TupleStaleCode, result.Errors[0].Code);
        Assert.Null(await scene.FindSequenceAsync());
        Assert.Equal(1, scene.Evidence.Captures);
    }

    [Fact]
    public async Task A_recipe_edit_between_display_and_submission_is_a_stale_confirmation()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture);
        var plan = await scene.AddAcceptedRootPlanAsync();
        var command = await scene.ArmCommandAsync(plan.ProposalId);
        var recipes = await scene.ReadRecipesAsync();
        await scene.EditRecipeAsync(recipes[0].Id, timeoutSeconds: 123);

        await AssertConfirmationStaleAsync(scene, command);
    }

    [Fact]
    public async Task A_recipe_added_or_disabled_between_display_and_submission_is_a_stale_confirmation()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture);
        var plan = await scene.AddAcceptedRootPlanAsync();
        var command = await scene.ArmCommandAsync(plan.ProposalId);
        await scene.AddRecipeAsync(9);

        await AssertConfirmationStaleAsync(scene, command);

        var fresh = await scene.ArmCommandAsync(plan.ProposalId);
        await scene.EditRecipeAsync((await scene.ReadRecipesAsync())[0].Id, enabled: false);
        await AssertConfirmationStaleAsync(scene, fresh);
    }

    [Fact]
    public async Task Each_pinned_run_setting_edit_between_display_and_submission_is_a_stale_confirmation()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture);
        var plan = await scene.AddAcceptedRootPlanAsync();

        foreach (var edit in new Action<Run>[]
                 {
                     run => run.SetRequestedClaudeMaxTurns(7),
                     run => run.SetRequestedCodexAssignment("gpt-5", "high"),
                     run => run.SetRequestedClaudeModelRequest("sonnet", "high"),
                     run => run.SetCodexAccountUsageStopPercent(90),
                 })
        {
            var command = await scene.ArmCommandAsync(plan.ProposalId);
            await using (var context = fixture.CreateContext())
            {
                var run = await context.Runs.SingleAsync(candidate => candidate.Id == scene.RunId);
                edit(run);
                await context.SaveChangesAsync();
            }

            await AssertConfirmationStaleAsync(scene, command);
        }

        Assert.Null(await scene.FindSequenceAsync());
    }

    [Fact]
    public async Task A_plan_that_stops_being_eligible_between_display_and_submission_is_refused()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture);
        var plan = await scene.AddHumanAuthorizedDepthTwoPlanAsync();
        var command = await scene.ArmCommandAsync(plan.ProposalId);
        await scene.AddNewerPlannerProposalAsync();

        var result = await scene.TryArmAsync(command);

        Assert.Equal(LocalExecutionSequenceErrors.PlanNotEligibleCode, result.Errors[0].Code);
        Assert.Null(await scene.FindSequenceAsync());
    }

    [Fact]
    public async Task A_new_checkpoint_between_display_and_submission_is_a_stale_tuple()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture);
        var plan = await scene.AddAcceptedRootPlanAsync();
        var command = await scene.ArmCommandAsync(plan.ProposalId);
        await AddCheckpointAsync(scene, number: 2, fingerprint: new string('c', 64));

        var result = await scene.TryArmAsync(command);

        Assert.Contains(
            result.Errors[0].Code,
            new[] { LocalExecutionSequenceErrors.TupleStaleCode, LocalExecutionSequenceErrors.PlanNotEligibleCode });
        Assert.Null(await scene.FindSequenceAsync());
    }

    // ---- helpers ---------------------------------------------------------------------------------------------------------------

    private static async Task AssertRefusedAsync(LocalExecutionSequenceTestScene scene, Guid planId, string code)
    {
        var availability = await scene.ReadAvailabilityAsync(planId);
        Assert.False(availability.Available);
        Assert.Equal(code, availability.RefusalCode);

        // Arming decides by the same reader and in the same order, and writes nothing.
        var armed = await scene.TryArmAsync(new(scene.RunId, planId, scene.WorkspaceId, scene.CheckpointId, scene.FingerprintSha256, new string('e', 64)));
        Assert.True(armed.IsFailure);
        Assert.Equal(code, armed.Errors[0].Code);
        Assert.Null(await scene.FindSequenceAsync());
    }

    private static async Task AssertConfirmationStaleAsync(
        LocalExecutionSequenceTestScene scene, Application.Features.Runs.Commands.ArmLocalExecutionSequence.ArmLocalExecutionSequenceCommand command)
    {
        var result = await scene.TryArmAsync(command);
        Assert.Equal(LocalExecutionSequenceErrors.ConfirmationStaleCode, result.Errors[0].Code);
        Assert.Null(await scene.FindSequenceAsync());
    }

    private async Task AddCheckpointAsync(LocalExecutionSequenceTestScene scene, int number, string fingerprint)
    {
        await using var context = fixture.CreateContext();
        var workspace = await context.GitWorkspaces.SingleAsync(candidate => candidate.Id == scene.WorkspaceId);
        Assert.Equal(number, workspace.ReserveCheckpointNumber());
        context.GitCheckpoints.Add(GitCheckpoint.Capture(
            Guid.NewGuid(), workspace.Id, number, LocalExecutionSequenceTestScene.Start, LocalExecutionSequenceTestScene.Head, fingerprint, []));
        await context.SaveChangesAsync();
    }

    /// <summary>A completed or running, dispatched Implementation attempt of the run, as a manual claim would have left it.</summary>
    private async Task AddImplementationAttemptAsync(LocalExecutionSequenceTestScene scene, bool dispatched, bool complete)
    {
        await using var context = fixture.CreateContext();
        var attempt = Attempt.ClaimAgentImplementation(
            Guid.NewGuid(), scene.RunId, 90, scene.WorkspaceId, scene.CheckpointId, LocalExecutionSequenceTestScene.StartFingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 1024, 2048, LocalExecutionSequenceTestScene.Start, 90);
        if (dispatched)
        {
            attempt.MarkAgentDispatched(LocalExecutionSequenceTestScene.Start);
        }

        if (complete)
        {
            attempt.CompleteImplementation(
                AgentOutcome.ProviderInvocationFailed, null, LocalExecutionSequenceTestScene.Start, processEvidence: TestProcessEvidence.CleanExit);
        }

        context.Attempts.Add(attempt);
        await context.SaveChangesAsync();
    }
}
