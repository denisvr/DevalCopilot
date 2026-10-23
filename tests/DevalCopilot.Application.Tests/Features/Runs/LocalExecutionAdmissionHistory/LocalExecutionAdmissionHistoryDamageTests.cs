using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionAdmissionHistory;
using DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.LocalExecutionAdmissionHistory.LocalExecutionAdmissionHistoryHarness;
using static DevalCopilot.Application.Tests.Features.Runs.LocalExecutionInspection.LocalExecutionInspectionHarness;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionAdmissionHistory;

/// <summary>
/// A corrupt or incoherent ledger is Unavailable as a whole (ADR-0063): no header, no count and no partial list. Every case damages one
/// stored fact of a completed twelve-admission queued cycle (both correction origins, two pinned recipes, three passes) through an
/// unpooled connection with checks off and the named immutability triggers dropped, exactly as damaged storage would hold it. These
/// are test-only storage writes that no production path performs; the reader is not weakened and no production hook exists. Unknown
/// enums, REAL, TEXT or hidden-NUL storage, ranges, ordering, gaps, duplicates, the complete admission/step tuple, link and lifecycle
/// shapes, foreign or wrongly typed references and an excessive ledger each make the answer Unavailable.
/// </summary>
public sealed class LocalExecutionAdmissionHistoryDamageTests : IAsyncLifetime
{
    private const string Admissions = "local_execution_admissions";
    private const string Steps = "local_execution_sequence_steps";
    private const string Seq = "local_execution_sequences";
    private const string Recipes = "local_execution_sequence_recipes";
    private const string ForeignGuid = "(SELECT lower(hex(randomblob(4))) || '-' || lower(hex(randomblob(2))) || '-4000-8000-' || lower(hex(randomblob(6))))";
    private const string StepsOrdinalIndex = "DROP INDEX IF EXISTS ix_local_execution_sequence_steps_membership;";
    private const string AdmissionColumns =
        "Id, SequenceId, AdmissionNumber, Ordinal, Kind, PassNumber, RecipeOrdinal, PredecessorStepId, SourceCheckpointId, "
        + "SourceFingerprintSha256, ReportMessageId, SubjectAttemptId, State, StepId, WaitingReason, WaitingReasonRecordedAtUtc, "
        + "EnqueuedAtUtc, ReasonCode, Revision, UpdatedAtUtc";

    private static readonly string[] InsertTriggers =
    [
        "trg_local_execution_admission_insert_shape", "trg_local_execution_admission_insert_bound", AdmissionImmutable,
    ];

    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    private static readonly Dictionary<string, (string Sql, string[] Triggers)> Cases = new()
    {
        // ---- one admission's own columns ----
        ["adm_ordinal_gap"] = ($"UPDATE {Admissions} SET Ordinal = 40 WHERE Ordinal = 11", [AdmissionImmutable]),
        ["adm_ordinal_text"] = ($"UPDATE {Admissions} SET Ordinal = 'x' WHERE Ordinal = 5", [AdmissionImmutable]),
        ["adm_ordinal_real"] = ($"UPDATE {Admissions} SET Ordinal = 5.5 WHERE Ordinal = 5", [AdmissionImmutable]),
        ["adm_ordinal_blob"] = ($"UPDATE {Admissions} SET Ordinal = zeroblob(2) WHERE Ordinal = 5", [AdmissionImmutable]),
        ["adm_number_zero"] = ($"UPDATE {Admissions} SET AdmissionNumber = 0 WHERE Ordinal = 5", [AdmissionImmutable]),
        ["adm_number_negative"] = ($"UPDATE {Admissions} SET AdmissionNumber = -4 WHERE Ordinal = 5", [AdmissionImmutable]),
        ["adm_number_real"] = ($"UPDATE {Admissions} SET AdmissionNumber = 5.5 WHERE Ordinal = 5", [AdmissionImmutable]),
        ["adm_number_text"] = ($"UPDATE {Admissions} SET AdmissionNumber = 'x' WHERE Ordinal = 5", [AdmissionImmutable]),
        ["adm_number_not_increasing"] = ($"UPDATE {Admissions} SET AdmissionNumber = 99999 WHERE Ordinal = 2", [AdmissionImmutable]),
        ["adm_revision_zero"] = ($"UPDATE {Admissions} SET Revision = 0 WHERE Ordinal = 5", [AdmissionImmutable]),
        ["adm_revision_real"] = ($"UPDATE {Admissions} SET Revision = 2.5 WHERE Ordinal = 5", [AdmissionImmutable]),
        ["adm_revision_text"] = ($"UPDATE {Admissions} SET Revision = 'x' WHERE Ordinal = 5", [AdmissionImmutable]),
        ["adm_kind_unknown"] = ($"UPDATE {Admissions} SET Kind = 'Bogus' WHERE Ordinal = 3", [AdmissionImmutable]),
        ["adm_kind_shape_lost"] = ($"UPDATE {Admissions} SET Kind = 'CodeReview' WHERE Ordinal = 2", [AdmissionImmutable]),
        ["adm_state_unknown"] = ($"UPDATE {Admissions} SET State = 'Bogus' WHERE Ordinal = 3", [AdmissionImmutable]),
        ["adm_state_waiting_before_the_last"] = ($"UPDATE {Admissions} SET State = 'Waiting' WHERE Ordinal = 3", [AdmissionImmutable]),
        ["adm_state_retired_before_the_last"] = (
            $"UPDATE {Admissions} SET State = 'Retired', ReasonCode = 'stopped_before_claim' WHERE Ordinal = 3", [AdmissionImmutable]),
        ["adm_reason_on_a_consumed_row"] = ($"UPDATE {Admissions} SET ReasonCode = 'stopped_before_claim' WHERE Ordinal = 3", [AdmissionImmutable]),
        ["adm_waiting_reason_unknown"] = ($"UPDATE {Admissions} SET WaitingReason = 'Bogus' WHERE Ordinal = 3", [AdmissionImmutable]),
        ["adm_waiting_reason_hidden_nul"] = (
            $"UPDATE {Admissions} SET WaitingReason = 'WaitingForCapacity' || char(0) || 'x' WHERE Ordinal = 3", [AdmissionImmutable]),
        ["adm_pass_out_of_range"] = ($"UPDATE {Admissions} SET PassNumber = 9 WHERE Ordinal = 5", [AdmissionImmutable]),
        ["adm_pass_real"] = ($"UPDATE {Admissions} SET PassNumber = 2.5 WHERE Ordinal = 5", [AdmissionImmutable]),
        ["adm_fingerprint_uppercase"] = ($"UPDATE {Admissions} SET SourceFingerprintSha256 = upper(SourceFingerprintSha256) WHERE Ordinal = 5", [AdmissionImmutable]),
        ["adm_fingerprint_short"] = ($"UPDATE {Admissions} SET SourceFingerprintSha256 = substr(SourceFingerprintSha256, 1, 63) WHERE Ordinal = 5", [AdmissionImmutable]),
        ["adm_fingerprint_oversized"] = ($"UPDATE {Admissions} SET SourceFingerprintSha256 = hex(zeroblob(100)) WHERE Ordinal = 5", [AdmissionImmutable]),
        ["adm_fingerprint_hidden_nul"] = ($"UPDATE {Admissions} SET SourceFingerprintSha256 = 'a' || char(0) || hex(zeroblob(100)) WHERE Ordinal = 5", [AdmissionImmutable]),
        ["adm_reason_unknown_on_retired"] = (
            $"UPDATE {Admissions} SET State = 'Retired', ReasonCode = 'made_up', StepId = NULL WHERE Ordinal = 11", [AdmissionImmutable]),
        ["adm_reason_oversized"] = ($"UPDATE {Admissions} SET ReasonCode = hex(zeroblob(200)) WHERE Ordinal = 3", [AdmissionImmutable]),
        ["adm_enqueued_after_recorded"] = ($"UPDATE {Admissions} SET EnqueuedAtUtc = '2099-01-01 00:00:00+00:00' WHERE Ordinal = 5", [AdmissionImmutable]),
        ["adm_waiting_recorded_before_enqueued"] = (
            $"UPDATE {Admissions} SET WaitingReasonRecordedAtUtc = '2020-01-01 00:00:00+00:00' WHERE Ordinal = 5", [AdmissionImmutable]),
        ["adm_updated_not_utc"] = ($"UPDATE {Admissions} SET UpdatedAtUtc = '2099-01-01 00:00:00+02:00' WHERE Ordinal = 5", [AdmissionImmutable]),
        ["adm_predecessor_missing"] = ($"UPDATE {Admissions} SET PredecessorStepId = NULL WHERE Ordinal = 5", [AdmissionImmutable]),
        ["adm_implementation_with_report"] = (
            $"UPDATE {Admissions} SET ReportMessageId = (SELECT ReportMessageId FROM {Admissions} WHERE Ordinal = 1) WHERE Ordinal = 0", [AdmissionImmutable]),

        // ---- the complete admission/step tuple, one member at a time (the step side differs) ----
        ["tuple_sequence"] = ($"UPDATE {Steps} SET SequenceId = {ForeignGuid} WHERE Ordinal = 5", [StepImmutable]),
        ["tuple_ordinal"] = ($"UPDATE {Steps} SET Ordinal = 40 WHERE Ordinal = 5", [StepImmutable]),
        ["tuple_kind"] = ($"UPDATE {Steps} SET Kind = 'CodeReview' WHERE Ordinal = 2", [StepImmutable]),
        ["tuple_pass"] = ($"{StepsOrdinalIndex} UPDATE {Steps} SET PassNumber = 3 WHERE Ordinal = 5", [StepImmutable]),
        ["tuple_recipe"] = (
            $"{StepsOrdinalIndex} UPDATE {Steps} SET RecipeOrdinal = 0 WHERE Ordinal = 6", [StepImmutable]),
        ["tuple_predecessor"] = (
            $"UPDATE {Steps} SET PredecessorStepId = (SELECT Id FROM {Steps} WHERE Ordinal = 0) WHERE Ordinal = 5", [StepImmutable]),
        ["tuple_source_checkpoint"] = ($"UPDATE {Steps} SET SourceCheckpointId = {ForeignGuid} WHERE Ordinal = 5", [StepImmutable]),
        ["tuple_source_fingerprint"] = ($"UPDATE {Steps} SET SourceFingerprintSha256 = '{new string('d', 64)}' WHERE Ordinal = 5", [StepImmutable]),
        ["tuple_report"] = (
            $"UPDATE {Steps} SET ReportMessageId = (SELECT Id FROM collaboration_messages WHERE Type = 'Acceptance' LIMIT 1) WHERE Ordinal = 7",
            [StepImmutable]),
        ["tuple_subject"] = (
            $"UPDATE {Steps} SET SubjectAttemptId = (SELECT OperationId FROM {Steps} WHERE Ordinal = 0) WHERE Ordinal = 8", [StepImmutable]),

        // ---- the link and its lifecycle shape ----
        ["link_step_missing"] = ($"DELETE FROM {Steps} WHERE Ordinal = 5", [StepImmutable]),
        ["link_consumed_without_operation"] = ($"UPDATE {Steps} SET OperationId = NULL WHERE Ordinal = 3", [StepImmutable]),
        ["link_operation_differs_from_reserved"] = ($"UPDATE {Steps} SET OperationId = {ForeignGuid} WHERE Ordinal = 3", [StepImmutable]),
        ["link_claim_time_missing"] = ($"UPDATE {Steps} SET ClaimedAtUtc = NULL WHERE Ordinal = 3", [StepImmutable]),
        ["link_result_deadline_missing"] = ($"UPDATE {Steps} SET ResultDeadlineUtc = NULL WHERE Ordinal = 3", [StepImmutable]),
        ["link_consumed_with_revoked_step"] = (
            $"UPDATE {Steps} SET State = 'Revoked', ReasonCode = 'stopped_before_claim', OperationId = NULL, ClaimedAtUtc = NULL, ResultDeadlineUtc = NULL WHERE Ordinal = 3",
            [StepImmutable]),
        ["link_step_not_concluded_before_the_last"] = ($"UPDATE {Steps} SET State = 'Claimed' WHERE Ordinal = 3", [StepImmutable]),
        ["link_step_reason_on_consumed"] = ($"UPDATE {Steps} SET ReasonCode = 'operation_failed' WHERE Ordinal = 3", [StepImmutable]),
        ["link_waiting_with_a_step"] = ($"UPDATE {Admissions} SET State = 'Waiting', ReasonCode = NULL WHERE Ordinal = 11", [AdmissionImmutable]),
        ["link_duplicate_step"] = (
            $"DROP INDEX IF EXISTS ix_local_execution_admissions_step; UPDATE {Admissions} SET StepId = (SELECT StepId FROM {Admissions} WHERE Ordinal = 3) WHERE Ordinal = 4;",
            [AdmissionImmutable]),
        ["link_reserved_operation_repeated"] = (
            $"DROP INDEX IF EXISTS ix_local_execution_sequence_steps_reserved_operation; DROP INDEX IF EXISTS ix_local_execution_sequence_steps_operation; UPDATE {Steps} SET ReservedOperationId = (SELECT ReservedOperationId FROM {Steps} WHERE Ordinal = 3), OperationId = (SELECT ReservedOperationId FROM {Steps} WHERE Ordinal = 3) WHERE Ordinal = 4;",
            [StepImmutable]),
        ["link_step_exists_without_its_admission"] = (
            $"UPDATE {Seq} SET State = 'Stopped', ReasonCode = 'stopped_before_claim'; UPDATE {Admissions} SET State = 'Retired', ReasonCode = 'stopped_before_claim', StepId = NULL WHERE Ordinal = 11;",
            [AdmissionImmutable, SequenceImmutable]),

        // ---- the ledger as a set ----
        ["ledger_gap_in_the_middle"] = ($"DELETE FROM {Admissions} WHERE Ordinal = 4", []),
        ["ledger_empty_for_a_queued_sequence"] = ($"DELETE FROM {Admissions}", []),
        ["ledger_beyond_the_recipe_dependent_maximum"] = (
            $"INSERT INTO {Admissions} ({AdmissionColumns}) SELECT {ForeignGuid}, SequenceId, (SELECT MAX(AdmissionNumber) FROM {Admissions}) + 1, 12, "
            + "Kind, PassNumber, RecipeOrdinal, PredecessorStepId, SourceCheckpointId, SourceFingerprintSha256, ReportMessageId, SubjectAttemptId, "
            + $"'Waiting', NULL, WaitingReason, WaitingReasonRecordedAtUtc, EnqueuedAtUtc, NULL, 1, UpdatedAtUtc FROM {Admissions} WHERE Ordinal = 11",
            InsertTriggers),
        ["ledger_beyond_the_absolute_maximum"] = (
            "DROP INDEX IF EXISTS ix_local_execution_admissions_ordinal; DROP INDEX IF EXISTS ix_local_execution_admissions_outstanding; "
            + $"WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n WHERE i < 110) INSERT INTO {Admissions} ({AdmissionColumns}) "
            + $"SELECT printf('%08x-0000-4000-8000-%012d', n.i, n.i), SequenceId, (SELECT MAX(AdmissionNumber) FROM {Admissions}) + n.i, 11, Kind, PassNumber, RecipeOrdinal, "
            + "PredecessorStepId, SourceCheckpointId, SourceFingerprintSha256, ReportMessageId, SubjectAttemptId, 'Waiting', NULL, WaitingReason, "
            + $"WaitingReasonRecordedAtUtc, EnqueuedAtUtc, NULL, 1, UpdatedAtUtc FROM {Admissions}, n WHERE Ordinal = 11",
            InsertTriggers),
        ["ledger_for_an_immediate_sequence"] = ($"UPDATE {Seq} SET AdmissionMode = 'Immediate'", [SequenceImmutable]),
        ["ledger_for_an_armed_queued_sequence"] = ($"UPDATE {Seq} SET State = 'Armed', ReasonCode = NULL, EndedAtUtc = NULL", [SequenceImmutable]),
        ["lifecycle_waiting_sequence_with_a_consumed_tail"] = (
            $"UPDATE {Seq} SET State = 'WaitingForAdmission', ReasonCode = NULL, EndedAtUtc = NULL", [SequenceImmutable]),
        ["lifecycle_preparing_sequence_with_a_consumed_tail"] = (
            $"UPDATE {Seq} SET State = 'PreparingStep', ReasonCode = NULL, EndedAtUtc = NULL", [SequenceImmutable]),
        ["lifecycle_approval_with_a_retired_tail"] = (
            $"UPDATE {Admissions} SET State = 'Retired', ReasonCode = 'stopped_before_claim' WHERE Ordinal = 11", [AdmissionImmutable]),
        ["lifecycle_ended_sequence_with_a_waiting_tail"] = (
            $"UPDATE {Seq} SET State = 'Stopped', ReasonCode = 'stopped_before_claim'; UPDATE {Admissions} SET State = 'Waiting', StepId = NULL, ReasonCode = NULL WHERE Ordinal = 11; DELETE FROM {Steps} WHERE Ordinal = 11;",
            [AdmissionImmutable, SequenceImmutable, StepImmutable]),

        // ---- the root ----
        ["root_run_not_manual"] = ("UPDATE runs SET ExecutionMode = '1'", []),
        ["root_run_mode_text"] = ("UPDATE runs SET ExecutionMode = 'two'", []),
        ["root_run_mode_out_of_range"] = ("UPDATE runs SET ExecutionMode = 7", []),
        ["root_run_mode_fractional"] = ("UPDATE runs SET ExecutionMode = 2.5", []),
        ["root_sequence_project_foreign"] = ($"UPDATE {Seq} SET ProjectId = {ForeignGuid}", [SequenceImmutable]),
        ["root_workspace_missing"] = ($"UPDATE {Seq} SET WorkspaceId = {ForeignGuid}", [SequenceImmutable]),
        ["root_workspace_of_another_project"] = (
            $"UPDATE git_workspaces SET ProjectId = {ForeignGuid} WHERE Id = (SELECT WorkspaceId FROM {Seq})", []),
        ["root_initial_checkpoint_missing"] = ($"UPDATE {Seq} SET CheckpointId = {ForeignGuid}", [SequenceImmutable]),
        ["root_initial_checkpoint_in_another_workspace"] = (
            $"UPDATE git_checkpoints SET WorkspaceId = {ForeignGuid} WHERE Id = (SELECT CheckpointId FROM {Seq})", []),
        ["root_initial_fingerprint_differs"] = ($"UPDATE {Seq} SET FingerprintSha256 = '{new string('d', 64)}'", [SequenceImmutable]),
        ["root_fingerprint_uppercase"] = ($"UPDATE {Seq} SET FingerprintSha256 = upper(FingerprintSha256)", [SequenceImmutable]),
        ["root_mode_unknown"] = ($"UPDATE {Seq} SET AdmissionMode = 'Bogus'", [SequenceImmutable]),
        ["root_state_unknown"] = ($"UPDATE {Seq} SET State = 'Bogus'", [SequenceImmutable]),
        ["root_recipe_count_zero"] = ($"UPDATE {Seq} SET RecipeCount = 0", [SequenceImmutable]),
        ["root_recipe_count_over_the_maximum"] = ($"UPDATE {Seq} SET RecipeCount = 33", [SequenceImmutable]),
        ["root_recipe_count_real"] = ($"UPDATE {Seq} SET RecipeCount = 2.5", [SequenceImmutable]),
        ["root_recipe_count_text"] = ($"UPDATE {Seq} SET RecipeCount = 'two'", [SequenceImmutable]),
        ["root_recipe_count_differs_from_the_pinned_set"] = ($"UPDATE {Seq} SET RecipeCount = 3", [SequenceImmutable]),
        ["root_revision_zero"] = ($"UPDATE {Seq} SET Revision = 0", [SequenceImmutable]),
        ["root_revision_real"] = ($"UPDATE {Seq} SET Revision = 5.5", [SequenceImmutable]),
        ["root_revision_text"] = ($"UPDATE {Seq} SET Revision = 'abc'", [SequenceImmutable]),
        ["root_revision_unsafe"] = ($"UPDATE {Seq} SET Revision = 9007199254740993", [SequenceImmutable]),
        ["root_advancement_deadline_differs"] = ($"UPDATE {Seq} SET AdvancementDeadlineUtc = '2099-01-01 00:00:00+00:00'", [SequenceImmutable]),
        ["root_reason_unknown"] = ($"UPDATE {Seq} SET ReasonCode = 'made_up'", [SequenceImmutable]),
        ["root_reason_oversized"] = ($"UPDATE {Seq} SET ReasonCode = hex(zeroblob(200))", [SequenceImmutable]),
        ["root_reason_missing_when_ended"] = ($"UPDATE {Seq} SET ReasonCode = NULL", [SequenceImmutable]),
        ["root_ended_time_missing"] = ($"UPDATE {Seq} SET EndedAtUtc = NULL", [SequenceImmutable]),
        ["root_updated_before_armed"] = ($"UPDATE {Seq} SET UpdatedAtUtc = '2020-01-01 00:00:00+00:00'", [SequenceImmutable]),
        ["root_armed_not_utc"] = ($"UPDATE {Seq} SET ArmedAtUtc = '2020-01-01 00:00:00+02:00'", [SequenceImmutable]),

        // ---- the pinned recipes ----
        ["recipe_missing"] = ($"DELETE FROM {Recipes} WHERE Ordinal = 1", [RecipeImmutable]),
        ["recipe_ordinal_gap"] = ($"UPDATE {Recipes} SET Ordinal = 5 WHERE Ordinal = 1", [RecipeImmutable]),
        ["recipe_name_blank"] = ($"UPDATE {Recipes} SET CommandName = '   ' WHERE Ordinal = 1", [RecipeImmutable]),
        ["recipe_name_over_200_units"] = ($"UPDATE {Recipes} SET CommandName = hex(zeroblob(101)) WHERE Ordinal = 1", [RecipeImmutable]),
        ["recipe_name_over_the_byte_guard"] = ($"UPDATE {Recipes} SET CommandName = hex(zeroblob(400)) WHERE Ordinal = 1", [RecipeImmutable]),
        ["recipe_name_hidden_nul"] = ($"UPDATE {Recipes} SET CommandName = 'a' || char(0) || hex(zeroblob(400)) WHERE Ordinal = 1", [RecipeImmutable]),
    };

    public static TheoryData<string> CaseNames => new(Cases.Keys);

    private async Task<LocalExecutionAdmissionHistoryHarness> EndedCycleAsync()
    {
        var (harness, _) = await EndedAsync(fixture, 2, MixedOriginsScript());
        Assert.Equal(12, (await harness.Scene.ReadAdmissionsAsync()).Count);
        return harness;
    }

    [Fact]
    public async Task The_undamaged_twelve_admission_cycle_is_recorded()
    {
        var harness = await EndedCycleAsync();

        var history = await harness.HistoryAsync();

        await harness.AssertMirrorsStorageAsync(history);
        Assert.Equal(12, history.Admissions.Count);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public async Task A_damaged_fact_makes_the_whole_ledger_unavailable_with_no_partial_disclosure(string name)
    {
        var harness = await EndedCycleAsync();
        var (sql, triggers) = Cases[name];

        await harness.CorruptAsync(sql, null, triggers);

        AssertShowsNothing(await harness.HistoryAsync(), LocalExecutionAdmissionHistoryStatus.Unavailable);
    }

    // ---- references that need a real second row of the same database ----

    private async Task<(Guid Id, string Fingerprint)> ForeignCheckpointAsync(LocalExecutionAdmissionHistoryHarness harness)
    {
        var other = await LocalExecutionSequenceTestScene.CreateAsync(fixture);
        await using var context = fixture.CreateContext();
        var checkpoint = await context.GitCheckpoints.SingleAsync(candidate => candidate.Id == other.CheckpointId);
        Assert.NotEqual(harness.Scene.WorkspaceId, checkpoint.WorkspaceId);
        return (checkpoint.Id, checkpoint.FingerprintSha256);
    }

    [Fact]
    public async Task A_source_checkpoint_of_another_workspace_is_unavailable_even_when_admission_step_and_fingerprint_agree()
    {
        var harness = await EndedCycleAsync();
        var (foreignId, foreignFingerprint) = await ForeignCheckpointAsync(harness);
        Assert.NotEqual(foreignFingerprint, (await harness.Scene.ReadAdmissionsAsync())[1].SourceFingerprintSha256);

        await harness.CorruptAsync(
            $"UPDATE {Admissions} SET SourceCheckpointId = $p0, SourceFingerprintSha256 = $p1 WHERE Ordinal = 1; "
            + $"UPDATE {Steps} SET SourceCheckpointId = $p0, SourceFingerprintSha256 = $p1 WHERE Ordinal = 1;",
            [foreignId.ToString("D").ToUpperInvariant(), foreignFingerprint],
            AdmissionImmutable, StepImmutable);

        AssertShowsNothing(await harness.HistoryAsync(), LocalExecutionAdmissionHistoryStatus.Unavailable);
    }

    [Fact]
    public async Task A_source_checkpoint_that_does_not_exist_is_unavailable_even_when_admission_and_step_agree()
    {
        var harness = await EndedCycleAsync();

        await harness.CorruptAsync(
            $"UPDATE {Admissions} SET SourceCheckpointId = $p0 WHERE Ordinal = 5; UPDATE {Steps} SET SourceCheckpointId = $p0 WHERE Ordinal = 5;",
            [Guid.NewGuid().ToString("D").ToUpperInvariant()],
            AdmissionImmutable, StepImmutable);

        AssertShowsNothing(await harness.HistoryAsync(), LocalExecutionAdmissionHistoryStatus.Unavailable);
    }

    [Fact]
    public async Task A_report_of_another_run_is_unavailable_even_when_admission_and_step_agree()
    {
        var harness = await EndedCycleAsync();
        var other = await LocalExecutionSequenceTestScene.CreateAsync(fixture);
        var plan = await other.AddAcceptedRootPlanAsync();
        await other.ArmAndDriveAsync(plan.ProposalId);
        var foreignReport = (await other.ReadMessagesAsync(CollaborationMessageType.ExecutionReport)).First();

        await harness.CorruptAsync(
            $"UPDATE {Admissions} SET ReportMessageId = $p0 WHERE Ordinal = 7; UPDATE {Steps} SET ReportMessageId = $p0 WHERE Ordinal = 7;",
            [foreignReport.Id.ToString("D").ToUpperInvariant()],
            AdmissionImmutable, StepImmutable);

        AssertShowsNothing(await harness.HistoryAsync(), LocalExecutionAdmissionHistoryStatus.Unavailable);
    }

    [Theory]
    [InlineData("Acceptance")]
    [InlineData("Proposal")]
    public async Task A_report_that_is_not_an_execution_report_is_unavailable_even_when_admission_and_step_agree(string type)
    {
        var harness = await EndedCycleAsync();

        await harness.CorruptAsync(
            $"UPDATE {Admissions} SET ReportMessageId = (SELECT Id FROM collaboration_messages WHERE Type = $p0 LIMIT 1) WHERE Ordinal = 7; "
            + $"UPDATE {Steps} SET ReportMessageId = (SELECT Id FROM collaboration_messages WHERE Type = $p0 LIMIT 1) WHERE Ordinal = 7;",
            [type],
            AdmissionImmutable, StepImmutable);

        AssertShowsNothing(await harness.HistoryAsync(), LocalExecutionAdmissionHistoryStatus.Unavailable);
    }

    [Fact]
    public async Task A_report_that_does_not_exist_is_unavailable_even_when_admission_and_step_agree()
    {
        var harness = await EndedCycleAsync();

        await harness.CorruptAsync(
            $"UPDATE {Admissions} SET ReportMessageId = $p0 WHERE Ordinal = 7; UPDATE {Steps} SET ReportMessageId = $p0 WHERE Ordinal = 7;",
            [Guid.NewGuid().ToString("D").ToUpperInvariant()],
            AdmissionImmutable, StepImmutable);

        AssertShowsNothing(await harness.HistoryAsync(), LocalExecutionAdmissionHistoryStatus.Unavailable);
    }

    [Fact]
    public async Task A_predecessor_that_skips_the_previous_step_is_unavailable_even_when_admission_and_step_agree()
    {
        var harness = await EndedCycleAsync();

        await harness.CorruptAsync(
            $"UPDATE {Admissions} SET PredecessorStepId = (SELECT StepId FROM {Admissions} WHERE Ordinal = 0) WHERE Ordinal = 5; "
            + $"UPDATE {Steps} SET PredecessorStepId = (SELECT Id FROM {Steps} WHERE Ordinal = 0) WHERE Ordinal = 5;",
            null,
            AdmissionImmutable, StepImmutable);

        AssertShowsNothing(await harness.HistoryAsync(), LocalExecutionAdmissionHistoryStatus.Unavailable);
    }

    [Theory]
    [InlineData(0, "arbitrary")]
    [InlineData(3, "wrong-origin")]
    [InlineData(99, "unknown")]
    public async Task A_correction_subject_that_is_not_an_earlier_linked_attempt_of_its_origin_is_unavailable(int subjectOrdinal, string label)
    {
        var harness = await EndedCycleAsync();
        Assert.NotEmpty(label);
        var subject = subjectOrdinal == 99
            ? Guid.NewGuid().ToString("D").ToUpperInvariant()
            : (await harness.Scene.ReadStepsAsync())[subjectOrdinal].OperationId!.Value.ToString("D").ToUpperInvariant();

        // Ordinal 8 is the review correction, whose origin is the review of ordinal 7; ordinal 3 is a diagnosis, ordinal 0 the implementation.
        await harness.CorruptAsync(
            $"UPDATE {Admissions} SET SubjectAttemptId = $p0 WHERE Ordinal = 8; UPDATE {Steps} SET SubjectAttemptId = $p0 WHERE Ordinal = 8;",
            [subject],
            AdmissionImmutable, StepImmutable);

        AssertShowsNothing(await harness.HistoryAsync(), LocalExecutionAdmissionHistoryStatus.Unavailable);
    }

    [Fact]
    public async Task A_verification_beyond_the_pinned_recipe_count_is_unavailable_even_when_admission_and_step_agree()
    {
        var harness = await EndedCycleAsync();

        await harness.CorruptAsync(
            $"UPDATE {Admissions} SET RecipeOrdinal = 7 WHERE Ordinal = 1; UPDATE {Steps} SET RecipeOrdinal = 7 WHERE Ordinal = 1;",
            null,
            AdmissionImmutable, StepImmutable);

        AssertShowsNothing(await harness.HistoryAsync(), LocalExecutionAdmissionHistoryStatus.Unavailable);
    }

    [Fact]
    public async Task A_repeated_pass_and_recipe_membership_is_unavailable_even_when_admission_and_step_agree()
    {
        var harness = await EndedCycleAsync();

        await harness.CorruptAsync(
            $"{StepsOrdinalIndex} UPDATE {Admissions} SET RecipeOrdinal = 0 WHERE Ordinal = 2; UPDATE {Steps} SET RecipeOrdinal = 0 WHERE Ordinal = 2;",
            null,
            AdmissionImmutable, StepImmutable);

        AssertShowsNothing(await harness.HistoryAsync(), LocalExecutionAdmissionHistoryStatus.Unavailable);
    }

    [Fact]
    public async Task An_implementation_that_does_not_operate_on_the_starting_checkpoint_is_unavailable()
    {
        var harness = await EndedCycleAsync();
        var otherCheckpoint = (await harness.Scene.ReadStepsAsync())[1];

        // The result checkpoint the verification ran on belongs to the pinned workspace with its own recorded fingerprint, so only the
        // implementation's pin to the starting checkpoint can refuse it.
        await harness.CorruptAsync(
            $"UPDATE {Admissions} SET SourceCheckpointId = $p0, SourceFingerprintSha256 = $p1 WHERE Ordinal = 0; "
            + $"UPDATE {Steps} SET SourceCheckpointId = $p0, SourceFingerprintSha256 = $p1 WHERE Ordinal = 0;",
            [otherCheckpoint.SourceCheckpointId.ToString("D").ToUpperInvariant(), otherCheckpoint.SourceFingerprintSha256],
            AdmissionImmutable, StepImmutable);

        AssertShowsNothing(await harness.HistoryAsync(), LocalExecutionAdmissionHistoryStatus.Unavailable);
    }

    [Fact]
    public async Task A_second_sequence_row_for_the_run_is_unavailable_never_a_choice_between_two()
    {
        var harness = await EndedCycleAsync();

        await harness.CorruptAsync(
            "DROP INDEX IF EXISTS ix_local_execution_sequences_run; DROP INDEX IF EXISTS ix_local_execution_sequences_coordination; "
            + $"CREATE TEMP TABLE copy AS SELECT * FROM {Seq}; UPDATE copy SET Id = $p0, CoordinationNumber = CoordinationNumber + 1000; "
            + $"INSERT INTO {Seq} SELECT * FROM copy;",
            [Guid.NewGuid().ToString("D").ToUpperInvariant()],
            "trg_local_execution_guard_arming", "trg_local_execution_guard_tuple", "trg_local_execution_queue_bound");

        AssertShowsNothing(await harness.HistoryAsync(), LocalExecutionAdmissionHistoryStatus.Unavailable);
    }

    [Fact]
    public async Task A_reload_of_damaged_storage_is_deterministic_and_stays_unavailable()
    {
        var harness = await EndedCycleAsync();
        await harness.CorruptAsync($"UPDATE {Admissions} SET Kind = 'Bogus' WHERE Ordinal = 3", null, AdmissionImmutable);

        var first = await harness.HistoryAsync();
        var second = await harness.HistoryAsync();

        AssertShowsNothing(first, LocalExecutionAdmissionHistoryStatus.Unavailable);
        AssertShowsNothing(second, LocalExecutionAdmissionHistoryStatus.Unavailable);
    }
}
