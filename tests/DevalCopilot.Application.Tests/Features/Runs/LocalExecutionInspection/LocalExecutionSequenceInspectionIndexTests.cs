using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionSequenceInspection;
using DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences.ScriptedResultKind;
using Step = DevalCopilot.Domain.Features.Runs.LocalExecutionStepKind;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionInspection;

/// <summary>The recorded index of one run's local execution sequence (ADR-0061) over real file-backed SQLite and the whole ADR-0060 cycle
/// driven through the production commands: NotRecorded, every step state and kind, exact members, historical stability after legitimate
/// newer facts, and a read-only, no-forbidden-column proof.</summary>
public sealed class LocalExecutionSequenceInspectionIndexTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    [Fact]
    public async Task An_unknown_run_is_one_fixed_not_found()
    {
        var (harness, _) = await LocalExecutionInspectionHarness.ArmedAsync(fixture);

        var result = await harness.TryIndexAsync(Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(LocalExecutionInspectionErrors.NotFoundCode, result.Errors[0].Code);
    }

    [Theory]
    [InlineData(RunExecutionMode.ManualAgent)]
    [InlineData(RunExecutionMode.Legacy)]
    public async Task A_known_run_without_a_sequence_is_not_recorded_with_no_header_and_no_steps(RunExecutionMode mode)
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture, mode: mode);

        var index = await new LocalExecutionInspectionHarness(scene).IndexAsync();

        Assert.Equal(scene.RunId, index.RunId);
        Assert.Equal(LocalExecutionSequenceInspectionStatus.NotRecorded, index.Status);
        Assert.Null(index.Sequence);
        Assert.Empty(index.Steps);
    }

    [Fact]
    public async Task An_armed_sequence_without_steps_is_recorded_with_its_exact_stored_header()
    {
        var (harness, sequenceId) = await LocalExecutionInspectionHarness.ArmedAsync(fixture, recipeCount: 3);
        var stored = await harness.Scene.ReadSequenceAsync();

        var index = await harness.IndexAsync();

        Assert.Equal(LocalExecutionSequenceInspectionStatus.Recorded, index.Status);
        Assert.Empty(index.Steps);
        var header = index.Sequence!;
        Assert.Equal(sequenceId, header.SequenceId);
        Assert.Equal(stored.PlanProposalMessageId, header.PlanProposalMessageId);
        Assert.Equal(LocalExecutionPlanAuthority.AcceptedRoot, header.PlanAuthority);
        Assert.Equal(harness.Scene.WorkspaceId, header.WorkspaceId);
        Assert.Equal(harness.Scene.CheckpointId, header.CheckpointId);
        Assert.Equal(LocalExecutionSequenceTestScene.StartFingerprint, header.FingerprintSha256);
        Assert.Equal(LocalExecutionSequenceState.Armed, header.RecordedState);
        Assert.Null(header.ReasonCode);
        Assert.Equal(stored.Revision, header.Revision);
        Assert.Equal(3, header.RecipeCount);
        Assert.Equal(2, header.CorrectionAllowance);
        Assert.Equal(stored.ArmedAtUtc, header.ArmedAtUtc);
        Assert.Equal(stored.UpdatedAtUtc, header.UpdatedAtUtc);
        Assert.Null(header.EndedAtUtc);
    }

    [Fact]
    public async Task A_reserved_step_and_a_claimed_step_show_their_recorded_state_and_only_the_claimed_one_a_link()
    {
        var (harness, sequenceId) = await LocalExecutionInspectionHarness.ArmedAsync(fixture);
        var ticket = await harness.Scene.ObserveReservingAsync(sequenceId);

        var reserved = Assert.Single((await harness.IndexAsync()).Steps);

        Assert.Equal(ticket.StepId, reserved.StepId);
        Assert.Equal(0, reserved.Ordinal);
        Assert.Equal(Step.Implementation, reserved.Kind);
        Assert.Equal(LocalExecutionSequenceStepState.Reserved, reserved.RecordedState);
        Assert.Null(reserved.OperationId);
        Assert.Null(reserved.ClaimedAtUtc);
        Assert.Null(reserved.ResultDeadlineUtc);
        Assert.Null(reserved.ReportMessageId);
        Assert.Null(reserved.SubjectAttemptId);
        Assert.Equal(harness.Scene.CheckpointId, reserved.SourceCheckpointId);

        await harness.Scene.ClaimAsync(ticket);
        var claimed = Assert.Single((await harness.IndexAsync()).Steps);

        Assert.Equal(LocalExecutionSequenceStepState.Claimed, claimed.RecordedState);
        Assert.Equal(ticket.ReservedOperationId, claimed.OperationId);
        Assert.NotNull(claimed.ClaimedAtUtc);
        Assert.NotNull(claimed.ResultDeadlineUtc);
    }

    [Fact]
    public async Task A_whole_cycle_lists_every_step_kind_in_order_with_members_equal_to_the_recorded_rows()
    {
        var (harness, sequenceId) = await LocalExecutionInspectionHarness.ArmedAsync(fixture);
        var ended = await harness.Scene.DriveAsync(sequenceId, LocalExecutionInspectionHarness.AllKindsScript());
        Assert.Equal(LocalExecutionSequenceState.ReadyForHumanApproval, ended.State);
        var rows = await harness.Scene.ReadStepsAsync();
        var recipes = await harness.Scene.ReadPinnedRecipesAsync();

        var index = await harness.IndexAsync();

        Assert.Equal(LocalExecutionSequenceInspectionStatus.Recorded, index.Status);
        Assert.Equal(LocalExecutionSequenceState.ReadyForHumanApproval, index.Sequence!.RecordedState);
        Assert.Equal(LocalExecutionSequenceReason.ReviewApproved, index.Sequence.ReasonCode);
        Assert.Equal(ended.EndedAtUtc, index.Sequence.EndedAtUtc);
        Assert.Equal(ended.Revision, index.Sequence.Revision);
        Assert.Equal(6, rows.Select(row => row.Kind).Distinct().Count());
        Assert.Equal(rows.Count, index.Steps.Count);
        for (var position = 0; position < rows.Count; position++)
        {
            var row = rows[position];
            var member = index.Steps[position];
            Assert.Equal(row.Id, member.StepId);
            Assert.Equal(position, member.Ordinal);
            Assert.Equal(row.Kind, member.Kind);
            Assert.Equal(row.PassNumber, member.PassNumber);
            Assert.Equal(row.RecipeOrdinal, member.RecipeOrdinal);
            Assert.Equal(row.RecipeOrdinal is { } recipe ? recipes[recipe].CommandName : null, member.RecipeName);
            Assert.Equal(row.State, member.RecordedState);
            Assert.Equal(row.ReasonCode, member.ReasonCode);
            Assert.Equal(row.SourceCheckpointId, member.SourceCheckpointId);
            Assert.Equal(row.SourceFingerprintSha256, member.SourceFingerprintSha256);
            Assert.Equal(row.ReportMessageId, member.ReportMessageId);
            Assert.Equal(row.SubjectAttemptId, member.SubjectAttemptId);
            Assert.Equal(row.OperationId, member.OperationId);
            Assert.Equal(row.PreparationReservedAtUtc, member.PreparationReservedAtUtc);
            Assert.Equal(row.PreparationDeadlineUtc, member.PreparationDeadlineUtc);
            Assert.Equal(row.ClaimedAtUtc, member.ClaimedAtUtc);
            Assert.Equal(row.ResultDeadlineUtc, member.ResultDeadlineUtc);
            Assert.Equal(LocalExecutionSequenceStepState.Concluded, member.RecordedState);
        }

        Assert.Contains(index.Steps, member => member.Kind == Step.DiagnosisCorrection);
        Assert.Contains(index.Steps, member => member.Kind == Step.ReviewCorrection);
        Assert.Contains(index.Steps, member => member.Kind == Step.VerificationDiagnosis);
        Assert.Equal(2, index.Steps.Count(member => member.Kind == Step.CodeReview));
    }

    [Fact]
    public async Task A_revoked_and_an_unresolved_preparation_have_no_link_and_their_fixed_reason()
    {
        var (revokedHarness, revokedSequence) = await LocalExecutionInspectionHarness.ArmedAsync(fixture);
        var revokedTicket = await revokedHarness.Scene.ObserveReservingAsync(revokedSequence);
        await revokedHarness.Scene.ResolveAsync(
            revokedTicket, "execution_capacity.limit_reached", new ConfigurableLocalExecutionStepProbe(LocalExecutionStepProbeResult.NotClaimed));

        var revoked = await revokedHarness.IndexAsync();

        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, revoked.Sequence!.RecordedState);
        var revokedStep = Assert.Single(revoked.Steps);
        Assert.Equal(LocalExecutionSequenceStepState.Revoked, revokedStep.RecordedState);
        Assert.Equal(LocalExecutionSequenceReason.CapacityRefused, revokedStep.ReasonCode);
        Assert.Null(revokedStep.OperationId);

        await using var other = new SqliteDatabaseFixture();
        await other.InitializeAsync();
        var (unresolvedHarness, unresolvedSequence) = await LocalExecutionInspectionHarness.ArmedAsync(other);
        var unresolvedTicket = await unresolvedHarness.Scene.ObserveReservingAsync(unresolvedSequence);
        await unresolvedHarness.Scene.ResolveAsync(
            unresolvedTicket, "attempts.persistence_failed", new ConfigurableLocalExecutionStepProbe(LocalExecutionStepProbeResult.Unresolved));

        var unresolved = await unresolvedHarness.IndexAsync();

        var unresolvedStep = Assert.Single(unresolved.Steps);
        Assert.Equal(LocalExecutionSequenceStepState.Unresolved, unresolvedStep.RecordedState);
        Assert.Equal(LocalExecutionSequenceReason.PreparationUnresolved, unresolvedStep.ReasonCode);
        Assert.Null(unresolvedStep.OperationId);
        Assert.Equal(LocalExecutionSequenceReason.PreparationUnresolved, unresolved.Sequence!.ReasonCode);
    }

    [Fact]
    public async Task A_stopped_and_a_restart_closed_sequence_read_their_stored_end_without_any_live_authority()
    {
        var (harness, sequenceId) = await LocalExecutionInspectionHarness.ArmedAsync(fixture);
        var ticket = await harness.Scene.ReserveAndClaimAsync(sequenceId);
        await harness.Scene.StopAsync(sequenceId);
        var stopping = await harness.IndexAsync();
        Assert.Equal(LocalExecutionSequenceState.Stopping, stopping.Sequence!.RecordedState);

        await harness.Scene.InterruptAsync();
        var interrupted = await harness.IndexAsync();

        Assert.Equal(LocalExecutionSequenceState.Interrupted, interrupted.Sequence!.RecordedState);
        Assert.Equal(LocalExecutionSequenceReason.HostRestarted, interrupted.Sequence.ReasonCode);
        Assert.NotNull(interrupted.Sequence.EndedAtUtc);
        Assert.Equal(ticket.ReservedOperationId, Assert.Single(interrupted.Steps).OperationId);
    }

    [Fact]
    public async Task Reading_the_index_executes_only_selects_selects_no_forbidden_column_and_changes_nothing()
    {
        var (harness, sequenceId) = await LocalExecutionInspectionHarness.ArmedAsync(fixture);
        await harness.Scene.DriveAsync(sequenceId, LocalExecutionInspectionHarness.AllKindsScript());
        var before = await harness.ChangeFingerprintAsync();
        var log = new StatementLog();

        var index = await harness.IndexAsync(null, log);

        Assert.Equal(LocalExecutionSequenceInspectionStatus.Recorded, index.Status);
        Assert.Equal(before, await harness.ChangeFingerprintAsync());
        Assert.NotEmpty(log.Statements);
        log.AssertReadOnly();
        log.AssertNeverSelects(
            "\"PreparationOwnerToken\"", "\"SettingsSnapshot\"", "\"ConfirmationSha256\"", "\"PlanInputsSha256\"", "\"DetailCode\"",
            "\"ExecutablePath\"", "\"ArgumentsJson\"", "\"Arguments\"", "\"WorkspacePath\"", "\"RelativeStoragePath\"", "\"ContentHash\"",
            "\"StructuredContentJson\"", "\"Summary\"", "\"FinalReportMessageId\"");
        Assert.True(log.Statements.Count <= 10, string.Join("\n", log.Statements));
    }

    [Fact]
    public async Task Historical_members_survive_newer_checkpoints_recipe_edits_and_a_newer_run()
    {
        var (harness, sequenceId) = await LocalExecutionInspectionHarness.ArmedAsync(fixture);
        await harness.Scene.DriveAsync(sequenceId, LocalExecutionInspectionHarness.AllKindsScript());
        var before = await harness.IndexAsync();

        var recipes = await harness.Scene.ReadRecipesAsync();
        await harness.Scene.EditRecipeAsync(recipes[0].Id, executablePath: @"C:\tools\renamed.exe", timeoutSeconds: 120);
        await harness.Scene.AddRecipeAsync(9, "A later recipe");
        await using (var context = fixture.CreateContext())
        {
            var workspace = await context.GitWorkspaces.FindAsync(harness.Scene.WorkspaceId);
            var number = workspace!.ReserveCheckpointNumber();
            context.GitCheckpoints.Add(GitCheckpoint.Capture(
                Guid.NewGuid(), workspace.Id, number, LocalExecutionSequenceTestScene.Start.AddHours(1),
                LocalExecutionSequenceTestScene.Head, new string('9', 64), []));
            await context.SaveChangesAsync();
        }

        var later = await LocalExecutionSequenceTestScene.CreateAsync(fixture);
        var after = await harness.IndexAsync();

        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.Sequence, after.Sequence);
        Assert.Equal(before.Steps, after.Steps);
        Assert.NotEqual(later.RunId, after.RunId);
    }
}
