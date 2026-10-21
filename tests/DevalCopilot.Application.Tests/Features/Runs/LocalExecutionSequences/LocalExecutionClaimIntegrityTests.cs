using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Projects.Commands.ClaimVerificationExecution;
using DevalCopilot.Application.Features.Runs.Commands.ClaimLocalExecutionSequenceStep;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodeReviewAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateDiagnosisCorrectionAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateImplementationAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateReviewCorrectionAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateVerificationDiagnosisAttempt;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.FaultInjectingDbContext;
using static DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences.ScriptedResultKind;
using Step = DevalCopilot.Domain.Features.Runs.LocalExecutionStepKind;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;

/// <summary>
/// The claim integrity of every step kind (ADR-0060) through the PRODUCTION claim handlers over independent connections: the link, the
/// operation, its ordered inputs, artifact metadata, budget and any grant consumption commit in ONE transaction; a failure at any stage
/// leaves no link and no operation when the independent probe proves absence and keeps the sealed manifest whenever the claim persisted
/// or cannot be proved absent; a ticket whose fields disagree with the durable step refuses with its exact stable code and consumes
/// nothing; exhaustion never writes an escalation or touches a Human authorization.
/// </summary>
public sealed class LocalExecutionClaimIntegrityTests : IAsyncLifetime
{
    public static IEnumerable<object[]> AllKinds() => Enum.GetValues<Step>().Select(kind => new object[] { kind });

    public static IEnumerable<object[]> KindsAndFaults() =>
        from kind in Enum.GetValues<Step>()
        from fault in new[]
        {
            "save_update_exception", "save_cancellation_before", "save_cancellation_after", "commit_before", "commit_cancellation_before",
            "commit_after", "commit_cancellation_after",
        }
        select new object[] { kind, fault };

    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    private static LocalExecutionStepScript ScriptFor(Step kind) => kind switch
    {
        Step.VerificationDiagnosis or Step.DiagnosisCorrection => new LocalExecutionStepScript().Verify(1, 0, VerificationFailed),
        Step.ReviewCorrection => new LocalExecutionStepScript().On(Step.CodeReview, ReviewChangesRequested),
        _ => new LocalExecutionStepScript(),
    };

    /// <summary>A scene driven until the next reservation is the given kind; the returned ticket is still unclaimed.</summary>
    private async Task<(LocalExecutionSequenceTestScene Scene, Guid SequenceId, LocalExecutionStepTicket Ticket)> ReservedAsync(
        Step kind, int recipeCount = 2, int maximumCorrections = 2)
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture, recipeCount, maximumReviewCorrectionAttempts: maximumCorrections);
        var plan = await scene.AddAcceptedRootPlanAsync();
        var sequenceId = (await scene.ArmAsync(plan.ProposalId)).SequenceId;
        var ticket = await scene.ReserveUntilAsync(sequenceId, kind, ScriptFor(kind));
        return (scene, sequenceId, ticket);
    }

    private static bool IsAgent(Step kind) => kind != Step.Verification;

    private async Task<(Result<ClaimLocalExecutionSequenceStepCommandResult>? Result, Exception? Thrown)> ClaimFaultedAsync(
        LocalExecutionSequenceTestScene scene, LocalExecutionStepTicket ticket, string fault)
    {
        var mediator = scene.MediatorWith(() =>
        {
            var context = new FaultInjectingDbContext(fixture.CreateContext());
            switch (fault)
            {
                case "save_update_exception":
                    context.SaveChangesFailure = SaveChangesFailureMode.UpdateExceptionBeforeSave;
                    break;
                case "save_cancellation_before":
                    context.SaveChangesFailure = SaveChangesFailureMode.CancellationBeforeSave;
                    break;
                case "save_cancellation_after":
                    context.SaveChangesFailure = SaveChangesFailureMode.CancellationAfterSave;
                    break;
                case "commit_before":
                    context.CommitFailure = CommitFailureMode.BeforeCommit;
                    break;
                case "commit_cancellation_before":
                    context.CommitFailure = CommitFailureMode.CancellationBeforeCommit;
                    break;
                case "commit_after":
                    context.CommitFailure = CommitFailureMode.AfterCommit;
                    break;
                case "commit_cancellation_after":
                    context.CommitFailure = CommitFailureMode.CancellationAfterCommit;
                    break;
            }

            return context;
        });
        try
        {
            return (await scene.ClaimAsync(ticket, mediator: mediator), null);
        }
        catch (OperationCanceledException exception)
        {
            return (null, exception);
        }
    }

    private async Task<int> OperationCountAsync(LocalExecutionSequenceTestScene scene, Step kind)
    {
        var footprint = await scene.FootprintAsync();
        return IsAgent(kind) ? footprint.Attempts : footprint.Executions;
    }

    // ---- one transaction ------------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllKinds))]
    public async Task A_committed_claim_links_the_operation_inputs_artifacts_and_budget_in_one_commit(Step kind)
    {
        var (scene, sequenceId, ticket) = await ReservedAsync(kind);
        var before = await scene.FootprintAsync();
        var slotsBefore = (await scene.ReadAttemptsAsync()).Count(attempt => attempt.AgentBudgetSlot != null);

        var claim = await scene.ClaimAsync(ticket);

        Assert.True(claim.IsSuccess, claim.IsFailure ? claim.Errors[0].Code : null);
        var after = await scene.FootprintAsync();
        Assert.Equal(ticket.ReservedOperationId, claim.Value.OperationId);
        var step = (await scene.ReadStepsAsync()).Single(candidate => candidate.Id == ticket.StepId);
        Assert.Equal(LocalExecutionSequenceStepState.Claimed, step.State);
        Assert.Equal(ticket.ReservedOperationId, step.OperationId);
        Assert.NotNull(step.ResultDeadlineUtc);
        Assert.Equal(LocalExecutionSequenceState.AwaitingStepResult, (await scene.ReadSequenceAsync()).State);
        Assert.Equal(before.ClaimedSteps + 1, after.ClaimedSteps);
        Assert.Equal(before.Attempts + (IsAgent(kind) ? 1 : 0), after.Attempts);
        Assert.Equal(before.Executions + (IsAgent(kind) ? 0 : 1), after.Executions);
        Assert.Equal(before.Artifacts + (IsAgent(kind) ? 1 : 0), after.Artifacts);
        Assert.Equal(before.Seals + (IsAgent(kind) ? 1 : 0), after.Seals);
        Assert.Equal(before.DeletedSeals, after.DeletedSeals);
        Assert.True(after.Events > before.Events);
        if (IsAgent(kind))
        {
            Assert.Equal(slotsBefore + 1, (await scene.ReadAttemptsAsync()).Count(attempt => attempt.AgentBudgetSlot != null));
            var attempt = await scene.ReadAttemptAsync(ticket.ReservedOperationId);
            Assert.Equal(AttemptStatus.Running, attempt.Status);
            Assert.Equal(LocalExecutionSequencePolicy.ContractOf(kind), attempt.AgentResponseContract);
            Assert.NotEmpty(await scene.ReadInputsAsync(ticket.ReservedOperationId));
            Assert.Equal(scene.WorkspaceId, attempt.AgentGitWorkspaceId);
            Assert.Equal(ticket.SourceCheckpointId, attempt.AgentGitCheckpointId);
        }
        else
        {
            var execution = await scene.ReadExecutionAsync(ticket.ReservedOperationId);
            Assert.Equal(VerificationExecutionStatus.Running, execution.Status);
            Assert.Equal(ticket.SourceCheckpointId, execution.GitCheckpointId);
        }

        Assert.Equal(sequenceId, ticket.SequenceId);
    }

    [Theory]
    [MemberData(nameof(AllKinds))]
    public async Task A_ticket_claim_composes_exactly_one_existing_command_of_its_kind(Step kind)
    {
        var (scene, _, ticket) = await ReservedAsync(kind);
        scene.Mediator.Dispatched.Clear();

        await scene.ClaimAsync(ticket);

        var expected = kind switch
        {
            Step.Implementation => nameof(CreateImplementationAttemptCommand),
            Step.Verification => nameof(ClaimVerificationExecutionCommand),
            Step.CodeReview => nameof(CreateCodeReviewAttemptCommand),
            Step.VerificationDiagnosis => nameof(CreateVerificationDiagnosisAttemptCommand),
            Step.ReviewCorrection => nameof(CreateReviewCorrectionAttemptCommand),
            _ => nameof(CreateDiagnosisCorrectionAttemptCommand),
        };
        Assert.Equal([expected], scene.Mediator.Dispatched);
    }

    // ---- injected failures ----------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(KindsAndFaults))]
    public async Task An_injected_failure_leaves_either_the_whole_claim_or_none_of_it_and_never_a_second_claim(Step kind, string fault)
    {
        var (scene, _, ticket) = await ReservedAsync(kind);
        var before = await scene.FootprintAsync();
        var persistedByFault = fault is "commit_after" or "commit_cancellation_after";

        var (faulted, _) = await ClaimFaultedAsync(scene, ticket, fault);

        var probe = await scene.StepProbe.CheckAsync(ticket.StepId, CancellationToken.None);
        var after = await scene.FootprintAsync();
        var step = (await scene.ReadStepsAsync()).Single(candidate => candidate.Id == ticket.StepId);
        if (persistedByFault)
        {
            Assert.Equal(Application.Data.LocalExecutionStepProbeResult.Claimed, probe);
            Assert.Equal(LocalExecutionSequenceStepState.Claimed, step.State);
            Assert.Equal(ticket.ReservedOperationId, step.OperationId);
            Assert.Equal(before.Attempts + (IsAgent(kind) ? 1 : 0), after.Attempts);
            Assert.Equal(before.Executions + (IsAgent(kind) ? 0 : 1), after.Executions);
            // A persisted claim keeps the sealed manifest it references: never deleted.
            Assert.Equal(before.DeletedSeals, after.DeletedSeals);
        }
        else
        {
            Assert.Equal(Application.Data.LocalExecutionStepProbeResult.NotClaimed, probe);
            Assert.Equal(LocalExecutionSequenceStepState.Reserved, step.State);
            Assert.Null(step.OperationId);
            Assert.Equal(before.Attempts, after.Attempts);
            Assert.Equal(before.Executions, after.Executions);
            Assert.Equal(before.Inputs, after.Inputs);
            Assert.Equal(before.Artifacts, after.Artifacts);
            Assert.Equal(before.VerificationEvidence, after.VerificationEvidence);
            Assert.Equal(before.ClaimedSteps, after.ClaimedSteps);
            Assert.Equal(before.Events, after.Events);
            Assert.Equal(before.SequenceRevision, after.SequenceRevision);
            Assert.Equal(LocalExecutionSequenceState.PreparingStep, (await scene.ReadSequenceAsync()).State);
            // Definite absence deletes exactly the manifest this claim sealed (an Agent claim only).
            Assert.Equal(after.Seals - before.Seals, after.DeletedSeals - before.DeletedSeals);
        }

        // The coordinator settles the one preparation with the independent probe and never claims twice, whichever way it ended.
        var failureCode = faulted is { IsFailure: true } ? faulted.Errors[0].Code : "attempts.persistence_failed";
        await scene.ResolveAsync(ticket, failureCode);
        var again = await scene.ClaimAsync(ticket);
        var final = await scene.FootprintAsync();
        Assert.True(again.IsFailure);
        Assert.Equal(persistedByFault ? 1 : 0, await OperationCountAsync(scene, kind) - (IsAgent(kind) ? before.Attempts : before.Executions));
        Assert.Equal(after.Attempts, final.Attempts);
        Assert.Equal(after.Executions, final.Executions);
        Assert.Equal(persistedByFault ? LocalExecutionSequenceStepState.Claimed : LocalExecutionSequenceStepState.Revoked,
            (await scene.ReadStepsAsync()).Single(candidate => candidate.Id == ticket.StepId).State);
    }

    [Theory]
    [InlineData(Step.Implementation)]
    [InlineData(Step.Verification)]
    public async Task An_unresolved_probe_keeps_the_sealed_manifest_and_authorizes_no_second_claim(Step kind)
    {
        var (scene, _, ticket) = await ReservedAsync(kind);
        var unresolved = new ConfigurableLocalExecutionStepProbe(Application.Data.LocalExecutionStepProbeResult.Unresolved);
        var mediator = new LocalExecutionTestMediator(
            () => new FaultInjectingDbContext(fixture.CreateContext()) { CommitFailure = CommitFailureMode.BeforeCommit },
            scene.Evidence, scene.Artifacts, scene.Clock, scene.AttemptProbe, unresolved);
        var before = await scene.FootprintAsync();

        var claim = await scene.ClaimAsync(ticket, mediator: mediator);

        Assert.True(claim.IsFailure);
        var after = await scene.FootprintAsync();
        // The probe could neither prove presence nor absence: the manifest must be kept, and nothing was committed.
        Assert.Equal(before.DeletedSeals, after.DeletedSeals);
        Assert.Equal(before.Attempts, after.Attempts);
        Assert.Equal(before.Executions, after.Executions);
        Assert.True(unresolved.Calls >= 1 || !IsAgent(kind));
    }

    [Fact]
    public async Task A_competing_worker_that_claimed_first_keeps_the_manifest_even_when_this_claim_fails_after_sealing()
    {
        var (scene, _, ticket) = await ReservedAsync(Step.Implementation);
        var first = await scene.ClaimAsync(ticket);
        Assert.True(first.IsSuccess);
        var before = await scene.FootprintAsync();

        // A second worker with the same ticket fails (the row is Claimed); the exact manifest the winner references is never deleted.
        var second = await scene.ClaimAsync(ticket);

        Assert.True(second.IsFailure);
        var after = await scene.FootprintAsync();
        Assert.Equal(before.Attempts, after.Attempts);
        Assert.Equal(before.Artifacts, after.Artifacts);
        Assert.Equal(before.DeletedSeals, after.DeletedSeals);
    }

    // ---- wrong ticket fields --------------------------------------------------------------------------------------------------

    public static IEnumerable<object[]> KindsAndForgeries() =>
        from kind in Enum.GetValues<Step>()
        from forgery in new[] { "owner_token", "operation_id", "source_checkpoint", "kind", "ordinal", "sequence", "step" }
        select new object[] { kind, forgery };

    [Theory]
    [MemberData(nameof(KindsAndForgeries))]
    public async Task A_ticket_that_disagrees_with_the_durable_step_is_refused_and_consumes_nothing(Step kind, string forgery)
    {
        var (scene, _, real) = await ReservedAsync(kind);
        var before = await scene.FootprintAsync();
        var forged = forgery switch
        {
            "owner_token" => LocalExecutionSequenceTestScene.Forge(real, ownerToken: Guid.NewGuid()),
            "operation_id" => LocalExecutionSequenceTestScene.Forge(real, reservedOperationId: Guid.NewGuid()),
            "source_checkpoint" => LocalExecutionSequenceTestScene.Forge(real, sourceCheckpointId: Guid.NewGuid()),
            "kind" => LocalExecutionSequenceTestScene.Forge(real, kind: kind == Step.Implementation ? Step.Verification : Step.Implementation),
            "ordinal" => LocalExecutionSequenceTestScene.Forge(real, ordinal: real.Ordinal + 1),
            "sequence" => LocalExecutionSequenceTestScene.Forge(real, sequenceId: Guid.NewGuid()),
            _ => LocalExecutionSequenceTestScene.Forge(real, stepId: Guid.NewGuid()),
        };

        var claim = await scene.ClaimAsync(forged);

        Assert.True(claim.IsFailure);
        // A verification of a checkpoint that does not exist is refused by its own ordinary existence check first.
        var expected = kind == Step.Verification && forgery == "source_checkpoint"
            ? "verification.not_found"
            : LocalExecutionSequenceErrors.StepAuthorityLostCode;
        Assert.Equal(expected, claim.Errors[0].Code);
        Assert.Equal(before, await scene.FootprintAsync());
        Assert.Equal(LocalExecutionSequenceStepState.Reserved, (await scene.ReadStepsAsync()).Single(step => step.Id == real.StepId).State);
    }

    [Theory]
    [InlineData(Step.CodeReview)]
    [InlineData(Step.VerificationDiagnosis)]
    [InlineData(Step.ReviewCorrection)]
    [InlineData(Step.DiagnosisCorrection)]
    public async Task A_report_or_subject_attempt_that_is_not_the_recorded_one_is_refused(Step kind)
    {
        var (scene, _, ticket) = await ReservedAsync(kind);
        var before = await scene.FootprintAsync();
        var other = Guid.NewGuid();

        var code = kind switch
        {
            Step.CodeReview => await CodeOf(scene, new CreateCodeReviewAttemptCommand(scene.RunId, other) { LocalStep = ticket }),
            Step.VerificationDiagnosis => await CodeOf(
                scene, new CreateVerificationDiagnosisAttemptCommand(scene.RunId, other) { LocalStep = ticket }),
            Step.ReviewCorrection => await CodeOf(
                scene, new CreateReviewCorrectionAttemptCommand(scene.RunId, other) { LocalStep = ticket }),
            _ => await CodeOf(scene, new CreateDiagnosisCorrectionAttemptCommand(scene.RunId, other) { LocalStep = ticket }),
        };

        Assert.Equal(LocalExecutionSequenceErrors.StepInputMismatchCode, code);
        Assert.Equal(before, await scene.FootprintAsync());
    }

    [Fact]
    public async Task A_ticket_naming_another_report_than_the_durable_step_is_refused_as_a_lost_authority()
    {
        var (scene, _, real) = await ReservedAsync(Step.ReviewCorrection);
        var before = await scene.FootprintAsync();
        var forged = LocalExecutionSequenceTestScene.Forge(real, reportMessageId: Guid.NewGuid());

        var claim = await scene.ClaimAsync(forged);

        Assert.True(claim.IsFailure);
        Assert.Equal(LocalExecutionSequenceErrors.StepAuthorityLostCode, claim.Errors[0].Code);
        Assert.Equal(before, await scene.FootprintAsync());
    }

    [Fact]
    public async Task An_ordinary_verification_recipe_other_than_the_pinned_one_is_refused_for_a_step()
    {
        var (scene, _, ticket) = await ReservedAsync(Step.Verification);
        var before = await scene.FootprintAsync();
        var otherRecipe = (await scene.ReadRecipesAsync())[1].Id;

        await using var context = fixture.CreateContext();
        var result = await new ClaimVerificationExecutionCommandHandler(context, scene.Evidence, scene.Clock, scene.StepProbe).HandleAsync(
            new ClaimVerificationExecutionCommand(scene.ProjectId, otherRecipe, ticket.SourceCheckpointId) { LocalStep = ticket },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(LocalExecutionSequenceErrors.StepInputMismatchCode, result.Errors[0].Code);
        Assert.Equal(before, await scene.FootprintAsync());
    }

    private async Task<string> CodeOf<TResult>(LocalExecutionSequenceTestScene scene, Devalente.Shared.Cqrs.ICommand<Result<TResult>> command)
    {
        var result = await scene.Mediator.SendAsync(command);
        Assert.True(result.IsFailure);
        return result.Errors[0].Code;
    }

    // ---- durable drift between reservation and claim --------------------------------------------------------------------------

    public static IEnumerable<object[]> KindsAndDrifts() =>
        from kind in Enum.GetValues<Step>()
        from drift in new[] { "expired", "stopped", "ended", "checkpoint", "recipe", "setting" }
        select new object[] { kind, drift };

    [Theory]
    [MemberData(nameof(KindsAndDrifts))]
    public async Task Durable_drift_between_the_reservation_and_the_claim_refuses_with_its_exact_code_and_consumes_nothing(
        Step kind, string drift)
    {
        var (scene, sequenceId, ticket) = await ReservedAsync(kind);
        string expected;
        switch (drift)
        {
            case "expired":
                scene.Clock.Advance(LocalExecutionSequencePolicy.PreparationDeadline);
                expected = LocalExecutionSequenceErrors.StepExpiredCode;
                break;
            case "stopped":
                await scene.StopAsync(sequenceId);
                expected = LocalExecutionSequenceErrors.StepAuthorityLostCode;
                break;
            case "ended":
                scene.Clock.Advance(LocalExecutionSequencePolicy.PreparationDeadline);
                await scene.ObserveAsync(sequenceId);
                Assert.Equal(LocalExecutionSequenceState.NeedsAttention, (await scene.ReadSequenceAsync()).State);
                expected = LocalExecutionSequenceErrors.StepAuthorityLostCode;
                break;
            case "checkpoint":
                await scene.ExecuteSqlAsync("DROP TRIGGER trg_local_execution_guard_checkpoints");
                await using (var context = fixture.CreateContext())
                {
                    var workspace = await context.GitWorkspaces.SingleAsync(candidate => candidate.Id == scene.WorkspaceId);
                    context.GitCheckpoints.Add(GitCheckpoint.Capture(
                        Guid.NewGuid(), workspace.Id, workspace.ReserveCheckpointNumber(), LocalExecutionSequenceTestScene.Start,
                        LocalExecutionSequenceTestScene.Head, new string('9', 64), []));
                    await context.SaveChangesAsync();
                }

                expected = LocalExecutionSequenceErrors.StepSourceChangedCode;
                break;
            case "recipe":
                await scene.ExecuteSqlAsync("DROP TRIGGER trg_local_execution_guard_verification_commands_update");
                await scene.EditRecipeAsync((await scene.ReadRecipesAsync())[0].Id, timeoutSeconds: 31);
                expected = LocalExecutionSequenceErrors.StepSourceChangedCode;
                break;
            default:
                await scene.ExecuteSqlAsync("DROP TRIGGER trg_local_execution_guard_run_settings");
                await using (var context = fixture.CreateContext())
                {
                    var run = await context.Runs.SingleAsync(candidate => candidate.Id == scene.RunId);
                    run.SetRequestedClaudeMaxTurns(9);
                    await context.SaveChangesAsync();
                }

                expected = LocalExecutionSequenceErrors.StepSourceChangedCode;
                break;
        }

        var before = await scene.FootprintAsync();

        var claim = await scene.ClaimAsync(ticket);

        Assert.True(claim.IsFailure);
        Assert.Equal(expected, claim.Errors[0].Code);
        var after = await scene.FootprintAsync();
        Assert.Equal(before.Attempts, after.Attempts);
        Assert.Equal(before.Executions, after.Executions);
        Assert.Equal(before.Inputs, after.Inputs);
        Assert.Equal(before.Artifacts, after.Artifacts);
        Assert.Equal(before.ClaimedSteps, after.ClaimedSteps);
        Assert.Equal(before.ConsumedPlanGrants, after.ConsumedPlanGrants);
        // A refusal found before the seal deletes nothing; one found at the seam deletes exactly its own sealed manifest.
        Assert.Equal(after.Seals - before.Seals, after.DeletedSeals - before.DeletedSeals);
    }

    [Theory]
    [InlineData("inputs_hash")]
    [InlineData("authority")]
    [InlineData("proposal")]
    public async Task A_plan_that_no_longer_equals_the_pinned_authority_is_refused_as_an_input_mismatch(string drift)
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture);
        var plan = await scene.AddAcceptedRootPlanAsync();
        var otherProposal = await scene.AddNewerPlannerProposalAsync();
        var sequenceId = (await scene.ArmAsync(plan.ProposalId)).SequenceId;
        var ticket = await scene.ObserveReservingAsync(sequenceId);
        await scene.ExecuteSqlAsync("DROP TRIGGER trg_local_execution_sequence_immutable");
        switch (drift)
        {
            case "inputs_hash":
                await scene.ExecuteSqlAsync("UPDATE local_execution_sequences SET PlanInputsSha256 = {0} WHERE Id = {1}", new string('f', 64), sequenceId);
                break;
            case "authority":
                await scene.ExecuteSqlAsync("UPDATE local_execution_sequences SET PlanAuthority = 'AcceptedRevision' WHERE Id = {0}", sequenceId);
                break;
            default:
                await scene.ExecuteSqlAsync("UPDATE local_execution_sequences SET PlanProposalMessageId = {0} WHERE Id = {1}", otherProposal, sequenceId);
                break;
        }

        var before = await scene.FootprintAsync();

        var claim = await scene.ClaimAsync(ticket);

        Assert.True(claim.IsFailure);
        // A different pinned proposal is a plan the ordinary eligibility itself does not admit (it was never reviewed).
        Assert.Equal(
            drift == "proposal" ? "agent_attempts.no_accepted_review_found" : LocalExecutionSequenceErrors.StepInputMismatchCode,
            claim.Errors[0].Code);
        var after = await scene.FootprintAsync();
        Assert.Equal(before.Attempts, after.Attempts);
        Assert.Equal(before.ClaimedSteps, after.ClaimedSteps);
        Assert.Equal(before.ConsumedPlanGrants, after.ConsumedPlanGrants);
    }

    [Fact]
    public async Task A_plan_that_lost_its_acceptance_is_no_longer_implementable_and_nothing_is_consumed()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture);
        var plan = await scene.AddAcceptedRevisionPlanAsync();
        var sequenceId = (await scene.ArmAsync(plan.ProposalId)).SequenceId;
        var ticket = await scene.ObserveReservingAsync(sequenceId);
        // The revision's Acceptance stops being one: the plan is now an unreviewed revision, which the opt-in surface never admits.
        await scene.ExecuteSqlAsync("UPDATE collaboration_messages SET Type = 'Question' WHERE Id = {0}", plan.AcceptanceId!.Value);
        var before = await scene.FootprintAsync();

        var claim = await scene.ClaimAsync(ticket);

        Assert.True(claim.IsFailure);
        var after = await scene.FootprintAsync();
        Assert.Equal(before.Attempts, after.Attempts);
        Assert.Equal(before.ClaimedSteps, after.ClaimedSteps);
        Assert.Equal(LocalExecutionSequenceStepState.Reserved, (await scene.ReadStepsAsync()).Single().State);
    }

    // ---- verification pass membership -----------------------------------------------------------------------------------------

    [Theory]
    [InlineData(Step.CodeReview)]
    [InlineData(Step.VerificationDiagnosis)]
    public async Task A_pass_whose_recorded_membership_is_not_the_selected_executions_element_wise_is_refused(Step kind)
    {
        var (scene, _, ticket) = await ReservedAsync(kind);
        // Swap the two executions' recipes: both recipes still have one execution bound to the checkpoint (the counts are equal) but
        // the selected, recipe-ordered membership is no longer element-wise the recorded pass.
        var members = (await scene.ReadStepsAsync()).Where(step => step.Kind == Step.Verification).OrderBy(step => step.RecipeOrdinal).ToList();
        Assert.Equal(2, members.Count);
        var first = await scene.ReadExecutionAsync(members[0].OperationId!.Value);
        var second = await scene.ReadExecutionAsync(members[1].OperationId!.Value);
        await scene.ExecuteSqlAsync(
            "UPDATE verification_executions SET VerificationCommandId = {0} WHERE Id = {1}", second.VerificationCommandId, first.Id);
        await scene.ExecuteSqlAsync(
            "UPDATE verification_executions SET VerificationCommandId = {0} WHERE Id = {1}", first.VerificationCommandId, second.Id);
        var before = await scene.FootprintAsync();

        var claim = await scene.ClaimAsync(ticket);

        Assert.True(claim.IsFailure);
        Assert.Equal(LocalExecutionSequenceErrors.StepInputMismatchCode, claim.Errors[0].Code);
        var after = await scene.FootprintAsync();
        Assert.Equal(before.Attempts, after.Attempts);
        Assert.Equal(before.ClaimedSteps, after.ClaimedSteps);
        Assert.Equal(after.Seals - before.Seals, after.DeletedSeals - before.DeletedSeals);
    }

    [Fact]
    public async Task A_finding_set_that_is_not_complete_for_the_review_is_refused_as_an_input_mismatch()
    {
        var (scene, _, ticket) = await ReservedAsync(Step.ReviewCorrection);
        var before = await scene.FootprintAsync();
        // One recorded ReviewFinding of the review is withdrawn: the correction must consume the whole applicable set or nothing.
        var finding = (await scene.ReadFindingMessagesAsync()).First();
        await scene.ExecuteSqlAsync("UPDATE collaboration_messages SET Type = 'Question' WHERE Id = {0}", finding.Id);

        var claim = await scene.ClaimAsync(ticket);

        Assert.True(claim.IsFailure);
        Assert.Equal(before.Attempts, (await scene.FootprintAsync()).Attempts);
        Assert.Equal(before.ClaimedSteps, (await scene.FootprintAsync()).ClaimedSteps);
    }

    // ---- allowance, exhaustion and grants -------------------------------------------------------------------------------------

    [Theory]
    [InlineData(Step.ReviewCorrection, Step.ReviewCorrection)]
    [InlineData(Step.DiagnosisCorrection, Step.DiagnosisCorrection)]
    [InlineData(Step.ReviewCorrection, Step.DiagnosisCorrection)]
    [InlineData(Step.DiagnosisCorrection, Step.ReviewCorrection)]
    public async Task A_second_correction_claimed_with_no_allowance_left_is_refused_without_an_escalation_or_a_grant_read(
        Step first, Step second)
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture, recipeCount: 2);
        var plan = await scene.AddAcceptedRootPlanAsync();
        var sequenceId = (await scene.ArmAsync(plan.ProposalId)).SequenceId;
        var script = new LocalExecutionStepScript();
        Script(script, first, 1);
        Script(script, second, 2);
        var ticket = await scene.ReserveUntilAsync(sequenceId, second, script, occurrence: first == second ? 2 : 1);
        Assert.Equal(1, await scene.CountCorrectionAttemptsAsync());
        // The sequence's pinned allowance is one correction: the first shared correction already spent it.
        await scene.ExecuteSqlAsync("DROP TRIGGER trg_local_execution_sequence_immutable");
        await scene.ExecuteSqlAsync("UPDATE local_execution_sequences SET CorrectionAllowance = 1 WHERE Id = {0}", sequenceId);
        var before = await scene.FootprintAsync();

        var claim = await scene.ClaimAsync(ticket);

        Assert.True(claim.IsFailure);
        Assert.Equal(LocalExecutionSequenceErrors.AllowanceReachedCode, claim.Errors[0].Code);
        var after = await scene.FootprintAsync();
        Assert.Equal(before.Attempts, after.Attempts);
        Assert.Equal(before.ClaimedSteps, after.ClaimedSteps);
        Assert.Equal(before.ConsumedPlanGrants, after.ConsumedPlanGrants);
        Assert.Equal(0, after.Escalations);
        Assert.Equal(0, after.ReviewCorrectionEscalations);
        Assert.Equal(0, after.DiagnosisCorrectionEscalations);
        Assert.Equal(0, after.ReviewCorrectionAuthorizations);
        Assert.Equal(1, await scene.CountCorrectionAttemptsAsync());
        Assert.Equal(after.Seals - before.Seals, after.DeletedSeals - before.DeletedSeals);
    }

    /// <summary>Makes verification pass <paramref name="pass"/> end in the given correction origin: a requested change for the review
    /// origin, a failed first recipe (diagnosed with findings) for the diagnosis origin.</summary>
    private static void Script(LocalExecutionStepScript script, Step origin, int pass)
    {
        if (origin == Step.ReviewCorrection)
        {
            script.On(Step.CodeReview, pass, ReviewChangesRequested);
        }
        else
        {
            script.Verify(pass, 0, VerificationFailed);
        }
    }

    [Fact]
    public async Task Both_correction_origins_and_mixed_origins_share_the_allowance_of_two_and_the_run_wide_maximum()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture, recipeCount: 1);
        var plan = await scene.AddAcceptedRootPlanAsync();
        var sequenceId = (await scene.ArmAsync(plan.ProposalId)).SequenceId;
        // Pass one: a failed recipe, diagnosed (diagnosis origin). Pass two: a review requests changes (review origin).
        var script = new LocalExecutionStepScript().Verify(1, 0, VerificationFailed).On(Step.CodeReview, ReviewChangesRequested);

        var ended = await scene.DriveAsync(sequenceId, script);

        Assert.Equal(2, await scene.CountCorrectionAttemptsAsync());
        var corrections = (await scene.ReadStepsAsync()).Where(step => LocalExecutionSequencePolicy.IsCorrectionKind(step.Kind)).ToList();
        Assert.Equal([Step.DiagnosisCorrection, Step.ReviewCorrection], corrections.Select(step => step.Kind));
        Assert.Equal(LocalExecutionSequenceState.HumanEscalation, ended.State);
        // The exhaustion is the one fixed host message and never an existing escalation row or a Human authorization.
        Assert.Single(await scene.ReadMessagesAsync(CollaborationMessageType.Escalation));
        var footprint = await scene.FootprintAsync();
        Assert.Equal(0, footprint.ReviewCorrectionEscalations);
        Assert.Equal(0, footprint.DiagnosisCorrectionEscalations);
        Assert.Equal(0, footprint.ReviewCorrectionAuthorizations);
    }

    [Fact]
    public async Task A_Human_authorized_depth_two_plan_consumes_its_grant_exactly_once_with_the_sequences_implementation()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture, recipeCount: 1);
        var plan = await scene.AddHumanAuthorizedDepthTwoPlanAsync();
        var armed = await scene.ArmAsync(plan.ProposalId);
        var before = await scene.FootprintAsync();
        Assert.Equal(0, before.ConsumedPlanGrants);

        var ended = await scene.DriveAsync(armed.SequenceId);

        Assert.Equal(LocalExecutionSequenceState.ReadyForHumanApproval, ended.State);
        await using var context = fixture.CreateContext();
        var grant = await context.PlanningImplementationAuthorizations.AsNoTracking().SingleAsync(candidate => candidate.Id == plan.GrantId);
        var implementation = (await scene.ReadStepsAsync()).Single(step => step.Kind == Step.Implementation);
        Assert.Equal(implementation.OperationId, grant.ConsumedByAttemptId);
        Assert.NotNull(grant.ConsumedAtUtc);
        Assert.Equal(1, (await scene.FootprintAsync()).ConsumedPlanGrants);
    }

    [Theory]
    [InlineData("commit_before", false)]
    [InlineData("save_update_exception", false)]
    [InlineData("commit_after", true)]
    public async Task The_plan_grant_is_consumed_in_the_claims_one_transaction_or_not_at_all(string fault, bool persisted)
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture, recipeCount: 1);
        var plan = await scene.AddHumanAuthorizedDepthTwoPlanAsync();
        var sequenceId = (await scene.ArmAsync(plan.ProposalId)).SequenceId;
        var ticket = await scene.ObserveReservingAsync(sequenceId);

        await ClaimFaultedAsync(scene, ticket, fault);

        await using var context = fixture.CreateContext();
        var grant = await context.PlanningImplementationAuthorizations.AsNoTracking().SingleAsync(candidate => candidate.Id == plan.GrantId);
        Assert.Equal(persisted, grant.ConsumedByAttemptId == ticket.ReservedOperationId);
        Assert.Equal(persisted, !grant.IsAvailable);
        Assert.Equal(persisted ? 1 : 0, (await scene.FootprintAsync()).ClaimedSteps);
    }

    [Fact]
    public async Task A_refused_implementation_ticket_never_consumes_the_Human_grant()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture, recipeCount: 1);
        var plan = await scene.AddHumanAuthorizedDepthTwoPlanAsync();
        var sequenceId = (await scene.ArmAsync(plan.ProposalId)).SequenceId;
        var ticket = await scene.ObserveReservingAsync(sequenceId);
        await scene.StopAsync(sequenceId);

        var claim = await scene.ClaimAsync(ticket);

        Assert.True(claim.IsFailure);
        await using var context = fixture.CreateContext();
        Assert.True((await context.PlanningImplementationAuthorizations.AsNoTracking().SingleAsync(candidate => candidate.Id == plan.GrantId)).IsAvailable);
    }
}
