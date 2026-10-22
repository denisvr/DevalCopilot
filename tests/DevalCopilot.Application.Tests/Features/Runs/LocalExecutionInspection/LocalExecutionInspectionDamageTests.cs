using System.Data.Common;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionSequenceInspection;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionStepInspection;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionVerificationOutputWindow;
using DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Projects;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.LocalExecutionInspection.LocalExecutionInspectionHarness;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionInspection;

/// <summary>
/// A corrupt or incoherent structural set is Unavailable as a whole (ADR-0061): the index discloses no header, count or partial list, a
/// selected step is Unavailable and a window is Unavailable with no execution identity and zero artifact reads. Every case damages one
/// stored fact of a completed six-kind cycle through an unpooled connection with checks off, exactly as damaged storage would hold it:
/// unknown enums, REAL, TEXT or hidden-NUL storage, ranges, gaps, link shapes, foreign references and wrongly typed messages. A revision
/// change during the read withholds the answer without a retry.
/// </summary>
public sealed class LocalExecutionInspectionDamageTests : IAsyncLifetime
{
    private const string Seq = "local_execution_sequences";
    private const string Steps = "local_execution_sequence_steps";
    private const string Recipes = "local_execution_sequence_recipes";
    private const string ForeignGuid = "(SELECT lower(hex(randomblob(4))) || '-' || lower(hex(randomblob(2))) || '-4000-8000-' || lower(hex(randomblob(6))))";

    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    private static readonly Dictionary<string, (string Sql, string[] Triggers)> Cases = new()
    {
        ["seq_state_unknown"] = ($"UPDATE {Seq} SET State = 'Bogus'", [SequenceImmutable]),
        ["seq_authority_unknown"] = ($"UPDATE {Seq} SET PlanAuthority = 'Bogus'", [SequenceImmutable]),
        ["seq_recipe_count_real"] = ($"UPDATE {Seq} SET RecipeCount = 2.5", [SequenceImmutable]),
        ["seq_recipe_count_text"] = ($"UPDATE {Seq} SET RecipeCount = 'two'", [SequenceImmutable]),
        ["seq_recipe_count_mismatch"] = ($"UPDATE {Seq} SET RecipeCount = 3", [SequenceImmutable]),
        ["seq_allowance_text"] = ($"UPDATE {Seq} SET CorrectionAllowance = 'x'", [SequenceImmutable]),
        ["seq_allowance_over"] = ($"UPDATE {Seq} SET CorrectionAllowance = 7", [SequenceImmutable]),
        ["seq_revision_text"] = ($"UPDATE {Seq} SET Revision = 'abc'", [SequenceImmutable]),
        ["seq_revision_real"] = ($"UPDATE {Seq} SET Revision = 5.5", [SequenceImmutable]),
        ["seq_revision_zero"] = ($"UPDATE {Seq} SET Revision = 0", [SequenceImmutable]),
        ["seq_revision_unsafe"] = ($"UPDATE {Seq} SET Revision = 9007199254740993", [SequenceImmutable]),
        ["seq_fingerprint_uppercase"] = ($"UPDATE {Seq} SET FingerprintSha256 = upper(FingerprintSha256)", [SequenceImmutable]),
        ["seq_fingerprint_short"] = ($"UPDATE {Seq} SET FingerprintSha256 = substr(FingerprintSha256, 1, 63)", [SequenceImmutable]),
        ["seq_fingerprint_hidden_nul"] = ($"UPDATE {Seq} SET FingerprintSha256 = 'a' || char(0) || hex(zeroblob(100))", [SequenceImmutable]),
        ["seq_reason_unknown"] = ($"UPDATE {Seq} SET ReasonCode = 'made_up'", [SequenceImmutable]),
        ["seq_reason_oversized"] = ($"UPDATE {Seq} SET ReasonCode = hex(zeroblob(200))", [SequenceImmutable]),
        ["seq_reason_missing_when_ended"] = ($"UPDATE {Seq} SET ReasonCode = NULL", [SequenceImmutable]),
        ["seq_ended_time_missing"] = ($"UPDATE {Seq} SET EndedAtUtc = NULL", [SequenceImmutable]),
        ["seq_updated_before_armed"] = ($"UPDATE {Seq} SET UpdatedAtUtc = '2020-01-01 00:00:00+00:00'", [SequenceImmutable]),
        ["seq_project_foreign"] = ($"UPDATE {Seq} SET ProjectId = {ForeignGuid}", [SequenceImmutable]),
        ["seq_workspace_missing"] = ($"UPDATE {Seq} SET WorkspaceId = {ForeignGuid}", [SequenceImmutable]),
        ["seq_checkpoint_missing"] = ($"UPDATE {Seq} SET CheckpointId = {ForeignGuid}", [SequenceImmutable]),
        ["run_not_manual"] = ("UPDATE runs SET ExecutionMode = '1'", []),
        ["run_mode_text"] = ("UPDATE runs SET ExecutionMode = 'two'", []),
        ["run_mode_out_of_range"] = ("UPDATE runs SET ExecutionMode = 7", []),
        ["run_mode_fractional"] = ("UPDATE runs SET ExecutionMode = 2.5", []),
        ["workspace_foreign_project"] = ($"UPDATE git_workspaces SET ProjectId = {ForeignGuid} WHERE Id = (SELECT WorkspaceId FROM {Seq})", []),
        ["checkpoint_foreign_workspace"] = ($"UPDATE git_checkpoints SET WorkspaceId = {ForeignGuid} WHERE Id = (SELECT CheckpointId FROM {Seq})", []),
        ["plan_not_a_proposal"] = ($"UPDATE collaboration_messages SET Type = 'Question' WHERE Id = (SELECT PlanProposalMessageId FROM {Seq})", []),
        ["plan_of_another_run"] = ($"UPDATE collaboration_messages SET RunId = {ForeignGuid} WHERE Id = (SELECT PlanProposalMessageId FROM {Seq})", []),
        ["step_kind_unknown"] = ($"UPDATE {Steps} SET Kind = 'Bogus' WHERE Ordinal = 3", [StepImmutable]),
        ["step_state_unknown"] = ($"UPDATE {Steps} SET State = 'Bogus' WHERE Ordinal = 3", [StepImmutable]),
        ["step_ordinal_gap"] = ($"UPDATE {Steps} SET Ordinal = 20 WHERE Ordinal = 11", [StepImmutable]),
        ["step_ordinal_text"] = ($"UPDATE {Steps} SET Ordinal = 'x' WHERE Ordinal = 11", [StepImmutable]),
        ["step_ordinal_real"] = ($"UPDATE {Steps} SET Ordinal = 11.5 WHERE Ordinal = 11", [StepImmutable]),
        ["step_pass_out_of_range"] = ($"UPDATE {Steps} SET PassNumber = 9 WHERE Ordinal = 5", [StepImmutable]),
        ["step_pass_real"] = ($"UPDATE {Steps} SET PassNumber = 2.5 WHERE Ordinal = 5", [StepImmutable]),
        ["step_recipe_out_of_range"] = ($"UPDATE {Steps} SET RecipeOrdinal = 7 WHERE Ordinal = 1", [StepImmutable]),
        ["step_recipe_text"] = ($"UPDATE {Steps} SET RecipeOrdinal = 'x' WHERE Ordinal = 1", [StepImmutable]),
        ["step_verification_shape_lost"] = ($"UPDATE {Steps} SET Kind = 'CodeReview' WHERE Ordinal = 2", [StepImmutable]),
        ["step_predecessor_missing"] = ($"UPDATE {Steps} SET PredecessorStepId = NULL WHERE Ordinal = 5", [StepImmutable]),
        ["step_predecessor_skipped"] = ($"UPDATE {Steps} SET PredecessorStepId = (SELECT Id FROM {Steps} WHERE Ordinal = 0) WHERE Ordinal = 5", [StepImmutable]),
        ["step_link_foreign"] = ($"UPDATE {Steps} SET OperationId = {ForeignGuid} WHERE Ordinal = 2", [StepImmutable]),
        ["step_link_missing_when_concluded"] = ($"UPDATE {Steps} SET OperationId = NULL WHERE Ordinal = 2", [StepImmutable]),
        ["step_not_last_not_concluded"] = ($"UPDATE {Steps} SET State = 'Reserved', OperationId = NULL, ClaimedAtUtc = NULL, ResultDeadlineUtc = NULL WHERE Ordinal = 3", [StepImmutable]),
        ["step_reason_on_concluded"] = ($"UPDATE {Steps} SET ReasonCode = 'operation_failed' WHERE Ordinal = 3", [StepImmutable]),
        ["step_reason_unknown_on_revoked"] = ($"UPDATE {Steps} SET State = 'Revoked', ReasonCode = 'made_up', OperationId = NULL, ClaimedAtUtc = NULL, ResultDeadlineUtc = NULL WHERE Ordinal = 11", [StepImmutable]),
        ["step_report_wrong_type"] = ($"UPDATE {Steps} SET ReportMessageId = (SELECT Id FROM collaboration_messages WHERE Type = 'Acceptance' LIMIT 1) WHERE Ordinal = 7", [StepImmutable]),
        ["step_report_missing"] = ($"UPDATE {Steps} SET ReportMessageId = {ForeignGuid} WHERE Ordinal = 7", [StepImmutable]),
        ["step_report_other_run"] = ($"UPDATE collaboration_messages SET RunId = {ForeignGuid} WHERE Id = (SELECT ReportMessageId FROM {Steps} WHERE Ordinal = 7)", []),
        ["step_subject_arbitrary_attempt"] = ($"UPDATE {Steps} SET SubjectAttemptId = (SELECT OperationId FROM {Steps} WHERE Ordinal = 0) WHERE Ordinal = 8", [StepImmutable]),
        ["step_subject_wrong_origin"] = ($"UPDATE {Steps} SET SubjectAttemptId = (SELECT OperationId FROM {Steps} WHERE Ordinal = 3) WHERE Ordinal = 8", [StepImmutable]),
        ["step_source_checkpoint_foreign"] = ($"UPDATE {Steps} SET SourceCheckpointId = {ForeignGuid} WHERE Ordinal = 5", [StepImmutable]),
        ["step_source_fingerprint_differs"] = ($"UPDATE {Steps} SET SourceFingerprintSha256 = '{new string('d', 64)}' WHERE Ordinal = 5", [StepImmutable]),
        ["step_source_fingerprint_uppercase"] = ($"UPDATE {Steps} SET SourceFingerprintSha256 = upper(SourceFingerprintSha256) WHERE Ordinal = 5", [StepImmutable]),
        ["step_implementation_not_on_start"] = ($"UPDATE {Steps} SET SourceFingerprintSha256 = '{new string('d', 64)}' WHERE Ordinal = 0", [StepImmutable]),
        ["step_claim_time_before_reservation"] = ($"UPDATE {Steps} SET ClaimedAtUtc = '2020-01-01 00:00:00+00:00' WHERE Ordinal = 3", [StepImmutable]),
        ["step_extra_row_past_the_bound"] = (
            $"INSERT INTO {Steps} (Id, SequenceId, Ordinal, Kind, PassNumber, RecipeOrdinal, PredecessorStepId, SourceCheckpointId, "
            + "SourceFingerprintSha256, ReportMessageId, SubjectAttemptId, State, ReservedOperationId, OperationId, PreparationOwnerToken, "
            + "PreparationReservedAtUtc, PreparationDeadlineUtc, ClaimedAtUtc, ResultDeadlineUtc, ReasonCode, Revision, UpdatedAtUtc) "
            + $"SELECT {ForeignGuid}, SequenceId, 12, 'CodeReview', 3, NULL, Id, SourceCheckpointId, SourceFingerprintSha256, ReportMessageId, "
            + $"NULL, 'Reserved', {ForeignGuid}, NULL, {ForeignGuid}, PreparationReservedAtUtc, PreparationDeadlineUtc, NULL, NULL, NULL, 1, UpdatedAtUtc "
            + $"FROM {Steps} WHERE Ordinal = 11",
            ["trg_local_execution_step_bounds"]),
        ["recipe_missing"] = ($"DELETE FROM {Recipes} WHERE Ordinal = 1", [RecipeImmutable]),
        ["recipe_ordinal_gap"] = ($"UPDATE {Recipes} SET Ordinal = 5 WHERE Ordinal = 1", [RecipeImmutable]),
        ["recipe_numbers_not_ascending"] = ($"UPDATE {Recipes} SET CommandNumber = CommandNumber + 100 WHERE Ordinal = 0", [RecipeImmutable]),
        ["recipe_number_text"] = ($"UPDATE {Recipes} SET CommandNumber = 'x' WHERE Ordinal = 1", [RecipeImmutable]),
        ["recipe_name_blank"] = ($"UPDATE {Recipes} SET CommandName = '   ' WHERE Ordinal = 1", [RecipeImmutable]),
        ["recipe_name_over_200_units"] = ($"UPDATE {Recipes} SET CommandName = hex(zeroblob(101)) WHERE Ordinal = 1", [RecipeImmutable]),
        ["recipe_name_over_the_byte_guard"] = ($"UPDATE {Recipes} SET CommandName = hex(zeroblob(400)) WHERE Ordinal = 1", [RecipeImmutable]),
        ["recipe_name_hidden_nul"] = ($"UPDATE {Recipes} SET CommandName = 'a' || char(0) || hex(zeroblob(400)) WHERE Ordinal = 1", [RecipeImmutable]),
        ["recipe_enabled_flag_not_one"] = ($"UPDATE {Recipes} SET IsEnabled = 2 WHERE Ordinal = 1", [RecipeImmutable]),
        ["recipe_enabled_flag_text"] = ($"UPDATE {Recipes} SET IsEnabled = 'yes' WHERE Ordinal = 1", [RecipeImmutable]),
        ["recipe_disabled"] = ($"UPDATE {Recipes} SET IsEnabled = 0 WHERE Ordinal = 1", [RecipeImmutable]),
    };

    public static TheoryData<string> CaseNames => new(Cases.Keys);

    [Theory]
    [MemberData(nameof(CaseNames))]
    public async Task A_damaged_structural_fact_makes_the_whole_set_unavailable_for_the_index_a_step_and_a_window(string name)
    {
        var (harness, sequenceId) = await ArmedAsync(fixture);
        await LocalExecutionSequenceTestScene.CreateAsync(fixture);
        await harness.Scene.DriveAsync(sequenceId, AllKindsScript());
        var review = (await harness.RowAsync(7)).Id;
        var verification = await harness.RowAsync(9);
        var missingStream = new[] { VerificationOutputPurpose.StandardOutput, VerificationOutputPurpose.StandardError };
        var (sql, triggers) = Cases[name];

        await harness.CorruptAsync(sql, null, triggers);

        var index = await harness.IndexAsync();
        Assert.Equal(LocalExecutionSequenceInspectionStatus.Unavailable, index.Status);
        Assert.Null(index.Sequence);
        Assert.Empty(index.Steps);

        var step = await harness.StepAsync(review);
        Assert.Equal(LocalExecutionStepInspectionStatus.Unavailable, step.Status);
        Assert.Equal(sequenceId, step.SequenceId);
        Assert.Null(step.Step);
        Assert.Null(step.Agent);
        Assert.Null(step.Verification);

        foreach (var stream in missingStream)
        {
            var window = await harness.WindowAsync(verification.Id, stream, 3);
            Assert.Equal(LocalExecutionVerificationOutputWindowStatus.Unavailable, window.Status);
            Assert.Null(window.ExecutionId);
            Assert.Null(window.Artifact);
            Assert.Equal(string.Empty, window.Text);
            Assert.Equal(3, window.NextOffset);
            Assert.Equal(0, window.TotalLength);
        }

        Assert.Empty(harness.Store.Reads);
    }

    [Fact]
    public async Task A_sequence_revision_change_during_the_structural_read_withholds_the_answer_without_a_retry()
    {
        var (harness, sequenceId) = await ArmedAsync(fixture);
        await harness.Scene.ObserveReservingAsync(sequenceId);
        var bump = new RevisionBumper(fixture.ConnectionString);

        var changed = await harness.IndexAsync(null, bump);

        Assert.True(bump.Fired);
        Assert.Equal(LocalExecutionSequenceInspectionStatus.Unavailable, changed.Status);
        Assert.Null(changed.Sequence);
        Assert.Empty(changed.Steps);

        var reload = await harness.IndexAsync();

        Assert.Equal(LocalExecutionSequenceInspectionStatus.Recorded, reload.Status);
        Assert.Single(reload.Steps);
        Assert.Equal((await harness.Scene.ReadSequenceAsync()).Revision, reload.Sequence!.Revision);
    }

    [Fact]
    public async Task A_selected_step_read_and_a_window_read_during_a_revision_change_are_unavailable_too()
    {
        var (harness, sequenceId) = await ArmedAsync(fixture);
        await harness.Scene.ObserveReservingAsync(sequenceId);
        var stepId = (await harness.RowAsync(0)).Id;

        var step = await harness.StepAsync(stepId, null, new RevisionBumper(fixture.ConnectionString));

        Assert.Equal(LocalExecutionStepInspectionStatus.Unavailable, step.Status);
    }

    private sealed class RevisionBumper(string connectionString) : DbCommandInterceptor
    {
        public bool Fired { get; private set; }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Bump(command);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Bump(command);
            return ValueTask.FromResult(result);
        }

        private void Bump(DbCommand command)
        {
            if (Fired || !command.CommandText.Contains("FROM \"local_execution_sequence_steps\"", StringComparison.Ordinal))
            {
                return;
            }

            Fired = true;
            using var connection = new SqliteConnection(connectionString + ";Pooling=False");
            connection.Open();
            using var update = connection.CreateCommand();
            update.CommandText = $"UPDATE {Seq} SET Revision = Revision + 1";
            update.ExecuteNonQuery();
        }
    }
}
