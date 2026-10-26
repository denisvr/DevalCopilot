using System.Data.Common;
using DevalCopilot.Application.Features.Runs.Queries.GetPlanningSequenceInspection;
using DevalCopilot.Application.Features.Runs.Queries.GetPlanningStepInspection;
using DevalCopilot.Application.Tests.Features.Runs.PlanningSequences;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.PlanningSequenceInspection.PlanningSequenceInspectionHarness;

namespace DevalCopilot.Application.Tests.Features.Runs.PlanningSequenceInspection;

/// <summary>
/// A corrupt or incoherent structural set is Unavailable as a whole (ADR-0068): the index discloses no header, count or partial list and
/// a selected step is Unavailable with no step and no agent. Every case damages one stored fact of a completed five-step sequence through
/// an unpooled connection with checks off and every trigger dropped, exactly as damaged storage would hold it: unknown enums, REAL, TEXT
/// or hidden-NUL storage, ranges, gaps, a sixth row, link and predecessor shapes, malformed identifiers and times, foreign references and
/// wrongly typed or misattributed messages. Association damage leaves the index Recorded and refuses only the selected step. A revision
/// change during the structural read or the association proof withholds the answer without a retry.
/// </summary>
public sealed class PlanningSequenceInspectionDamageTests : IAsyncLifetime
{
    private const string Seq = SequenceTable;
    private const string Steps = StepTable;
    private const string NewGuid = "(SELECT lower(hex(randomblob(4))) || '-' || lower(hex(randomblob(2))) || '-4000-8000-' || lower(hex(randomblob(6))))";
    private const string NotGuid = "'zzzzzzzz-zzzz-4zzz-8zzz-zzzzzzzzzzzz'";
    private const string Ended = "'2026-10-19 09:30:00+00:00'";
    private static readonly string Fingerprint = new('d', 64);

    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    private static readonly Dictionary<string, string> StructureCases = new()
    {
        ["seq_state_unknown"] = $"UPDATE {Seq} SET State = 'Bogus'",
        ["seq_revision_text"] = $"UPDATE {Seq} SET Revision = 'abc'",
        ["seq_revision_real"] = $"UPDATE {Seq} SET Revision = 5.5",
        ["seq_revision_zero"] = $"UPDATE {Seq} SET Revision = 0",
        ["seq_revision_negative"] = $"UPDATE {Seq} SET Revision = -3",
        ["seq_revision_unsafe"] = $"UPDATE {Seq} SET Revision = 9007199254740993",
        ["seq_fingerprint_uppercase"] = $"UPDATE {Seq} SET FingerprintSha256 = upper(FingerprintSha256)",
        ["seq_fingerprint_short"] = $"UPDATE {Seq} SET FingerprintSha256 = substr(FingerprintSha256, 1, 63)",
        ["seq_fingerprint_hidden_nul"] = $"UPDATE {Seq} SET FingerprintSha256 = 'a' || char(0) || hex(zeroblob(100))",
        ["seq_fingerprint_oversized"] = $"UPDATE {Seq} SET FingerprintSha256 = hex(zeroblob(100))",
        ["seq_reason_unknown"] = $"UPDATE {Seq} SET ReasonCode = 'made_up'",
        ["seq_reason_oversized"] = $"UPDATE {Seq} SET ReasonCode = hex(zeroblob(200))",
        ["seq_reason_missing_when_ended"] = $"UPDATE {Seq} SET ReasonCode = NULL",
        ["seq_reason_present_when_live"] = $"UPDATE {Seq} SET State = 'AwaitingStepResult', EndedAtUtc = NULL",
        ["seq_ended_time_missing"] = $"UPDATE {Seq} SET EndedAtUtc = NULL",
        ["seq_ended_time_garbage"] = $"UPDATE {Seq} SET EndedAtUtc = 'garbage'",
        ["seq_ended_after_updated"] = $"UPDATE {Seq} SET EndedAtUtc = '2099-01-01 00:00:00+00:00'",
        ["seq_updated_before_armed"] = $"UPDATE {Seq} SET UpdatedAtUtc = '2020-01-01 00:00:00+00:00'",
        ["seq_armed_offset_not_utc"] = $"UPDATE {Seq} SET ArmedAtUtc = '2026-10-19 09:00:00+01:00'",
        ["seq_armed_t_separator"] = $"UPDATE {Seq} SET ArmedAtUtc = '2026-10-19T09:00:00+00:00'",
        ["seq_armed_oversized"] = $"UPDATE {Seq} SET ArmedAtUtc = hex(zeroblob(40))",
        ["seq_armed_hidden_nul"] = $"UPDATE {Seq} SET ArmedAtUtc = '2026-10-19 09:00:00' || char(0) || '+00:00'",
        ["seq_stop_on_a_live_state"] =
            $"UPDATE {Seq} SET State = 'AwaitingStepResult', ReasonCode = NULL, EndedAtUtc = NULL, StopRequestedAtUtc = {Ended}",
        ["seq_stop_before_armed"] = $"UPDATE {Seq} SET StopRequestedAtUtc = '2020-01-01 00:00:00+00:00'",
        ["seq_stopped_without_a_stop_time"] = $"UPDATE {Seq} SET State = 'Stopped', StopRequestedAtUtc = NULL",
        ["seq_stopping_with_an_end"] = $"UPDATE {Seq} SET State = 'Stopping', StopRequestedAtUtc = {Ended}",
        ["seq_project_foreign"] = $"UPDATE {Seq} SET ProjectId = {NewGuid}",
        ["seq_workspace_missing"] = $"UPDATE {Seq} SET WorkspaceId = {NewGuid}",
        ["seq_checkpoint_missing"] = $"UPDATE {Seq} SET CheckpointId = {NewGuid}",
        ["seq_workspace_not_a_guid"] = $"UPDATE {Seq} SET WorkspaceId = {NotGuid}",
        ["seq_workspace_empty_guid"] = $"UPDATE {Seq} SET WorkspaceId = '00000000-0000-0000-0000-000000000000'",
        ["seq_workspace_braced"] = $"UPDATE {Seq} SET WorkspaceId = '{{' || WorkspaceId || '}}'",
        ["run_not_manual"] = $"UPDATE runs SET ExecutionMode = '1' WHERE Id = (SELECT RunId FROM {Seq})",
        ["run_mode_text"] = $"UPDATE runs SET ExecutionMode = 'two' WHERE Id = (SELECT RunId FROM {Seq})",
        ["run_mode_out_of_range"] = $"UPDATE runs SET ExecutionMode = 7 WHERE Id = (SELECT RunId FROM {Seq})",
        ["run_mode_fractional"] = $"UPDATE runs SET ExecutionMode = 2.5 WHERE Id = (SELECT RunId FROM {Seq})",
        ["run_project_not_a_guid"] = $"UPDATE runs SET ProjectId = {NotGuid} WHERE Id = (SELECT RunId FROM {Seq})",
        ["workspace_foreign_project"] = $"UPDATE git_workspaces SET ProjectId = {NewGuid} WHERE Id = (SELECT WorkspaceId FROM {Seq})",
        ["workspace_project_not_a_guid"] = $"UPDATE git_workspaces SET ProjectId = {NotGuid} WHERE Id = (SELECT WorkspaceId FROM {Seq})",
        ["checkpoint_foreign_workspace"] = $"UPDATE git_checkpoints SET WorkspaceId = {NewGuid} WHERE Id = (SELECT CheckpointId FROM {Seq})",
        ["checkpoint_fingerprint_differs"] =
            $"UPDATE git_checkpoints SET FingerprintSha256 = '{Fingerprint}' WHERE Id = (SELECT CheckpointId FROM {Seq})",
        ["checkpoint_fingerprint_oversized"] =
            $"UPDATE git_checkpoints SET FingerprintSha256 = hex(zeroblob(100)) WHERE Id = (SELECT CheckpointId FROM {Seq})",
        ["step_role_wrong"] = $"UPDATE {Steps} SET Role = 'Resolver' WHERE Ordinal = 1",
        ["step_role_unknown"] = $"UPDATE {Steps} SET Role = 'Bogus' WHERE Ordinal = 1",
        ["step_role_of_another_agent"] = $"UPDATE {Steps} SET Role = 'Implementer' WHERE Ordinal = 2",
        ["step_state_unknown"] = $"UPDATE {Steps} SET State = 'Bogus' WHERE Ordinal = 4",
        ["step_ordinal_out_of_range"] = $"UPDATE {Steps} SET Ordinal = 7 WHERE Ordinal = 4",
        ["step_ordinal_text"] = $"UPDATE {Steps} SET Ordinal = 'x' WHERE Ordinal = 4",
        ["step_ordinal_real"] = $"UPDATE {Steps} SET Ordinal = 4.5 WHERE Ordinal = 4",
        ["step_ordinal_gap"] = $"DELETE FROM {Steps} WHERE Ordinal = 2",
        ["step_sixth_row_past_the_bound"] =
            $"INSERT INTO {Steps} (Id, SequenceId, Ordinal, Role, State, PredecessorAttemptId, InputProposalMessageId, ReservedAttemptId, "
            + "AttemptId, PreparationOwnerToken, PreparationReservedAtUtc, PreparationDeadlineUtc, ReasonCode, Revision, UpdatedAtUtc) "
            + $"SELECT {NewGuid}, SequenceId, 5, 'CriticalReviewer', 'Reserved', AttemptId, InputProposalMessageId, {NewGuid}, NULL, "
            + $"{NewGuid}, PreparationReservedAtUtc, PreparationDeadlineUtc, NULL, 1, UpdatedAtUtc FROM {Steps} WHERE Ordinal = 4",
        ["step_revision_zero"] = $"UPDATE {Steps} SET Revision = 0 WHERE Ordinal = 2",
        ["step_revision_text"] = $"UPDATE {Steps} SET Revision = 'abc' WHERE Ordinal = 2",
        ["step_revision_real"] = $"UPDATE {Steps} SET Revision = 2.5 WHERE Ordinal = 2",
        ["step_revision_unsafe"] = $"UPDATE {Steps} SET Revision = 9007199254740993 WHERE Ordinal = 2",
        ["step_attempt_missing_when_concluded"] = $"UPDATE {Steps} SET AttemptId = NULL WHERE Ordinal = 2",
        ["step_attempt_differs_from_reserved"] = $"UPDATE {Steps} SET AttemptId = {NewGuid} WHERE Ordinal = 2",
        ["step_attempt_present_when_reserved"] = $"UPDATE {Steps} SET State = 'Reserved' WHERE Ordinal = 4",
        ["step_attempt_not_a_guid"] = $"UPDATE {Steps} SET AttemptId = {NotGuid} WHERE Ordinal = 2",
        ["step_reserved_attempt_duplicated"] =
            "DROP INDEX ix_planning_sequence_steps_reserved_attempt; DROP INDEX ix_planning_sequence_steps_attempt; "
            + $"UPDATE {Steps} SET ReservedAttemptId = (SELECT ReservedAttemptId FROM {Steps} WHERE Ordinal = 1), "
            + $"AttemptId = (SELECT ReservedAttemptId FROM {Steps} WHERE Ordinal = 1) WHERE Ordinal = 2",
        ["step_reason_on_concluded"] = $"UPDATE {Steps} SET ReasonCode = 'step_failed' WHERE Ordinal = 2",
        ["step_reason_unknown_on_revoked"] = $"UPDATE {Steps} SET State = 'Revoked', AttemptId = NULL, ReasonCode = 'made_up' WHERE Ordinal = 4",
        ["step_reason_missing_on_revoked"] = $"UPDATE {Steps} SET State = 'Revoked', AttemptId = NULL, ReasonCode = NULL WHERE Ordinal = 4",
        ["step_reason_oversized"] = $"UPDATE {Steps} SET ReasonCode = hex(zeroblob(200)) WHERE Ordinal = 2",
        ["step_predecessor_missing"] = $"UPDATE {Steps} SET PredecessorAttemptId = NULL WHERE Ordinal = 3",
        ["step_predecessor_skipped"] =
            $"UPDATE {Steps} SET PredecessorAttemptId = (SELECT AttemptId FROM {Steps} WHERE Ordinal = 0) WHERE Ordinal = 3",
        ["step_planner_has_a_predecessor"] =
            $"UPDATE {Steps} SET PredecessorAttemptId = (SELECT AttemptId FROM {Steps} WHERE Ordinal = 1) WHERE Ordinal = 0",
        ["step_planner_has_an_input"] =
            $"UPDATE {Steps} SET InputProposalMessageId = (SELECT InputProposalMessageId FROM {Steps} WHERE Ordinal = 1) WHERE Ordinal = 0",
        ["step_input_missing"] = $"UPDATE {Steps} SET InputProposalMessageId = NULL WHERE Ordinal = 2",
        ["step_resolver_input_differs"] =
            $"UPDATE {Steps} SET InputProposalMessageId = (SELECT InputProposalMessageId FROM {Steps} WHERE Ordinal = 1) WHERE Ordinal = 4",
        ["step_input_not_a_guid"] = $"UPDATE {Steps} SET InputProposalMessageId = {NotGuid} WHERE Ordinal = 3",
        ["step_id_not_a_guid"] = $"UPDATE {Steps} SET Id = {NotGuid} WHERE Ordinal = 2",
        ["step_time_garbage"] = $"UPDATE {Steps} SET PreparationReservedAtUtc = 'nope' WHERE Ordinal = 2",
        ["step_time_offset_not_utc"] = $"UPDATE {Steps} SET PreparationReservedAtUtc = '2026-10-19 09:00:00+02:00' WHERE Ordinal = 2",
        ["step_deadline_before_reserved"] = $"UPDATE {Steps} SET PreparationDeadlineUtc = '2020-01-01 00:00:00+00:00' WHERE Ordinal = 2",
        ["step_updated_before_reserved"] = $"UPDATE {Steps} SET UpdatedAtUtc = '2020-01-01 00:00:00+00:00' WHERE Ordinal = 2",
        ["step_not_last_not_concluded"] = $"UPDATE {Steps} SET State = 'Claimed' WHERE Ordinal = 2",
        ["step_not_last_revoked"] = $"UPDATE {Steps} SET State = 'Revoked', AttemptId = NULL, ReasonCode = 'stopped_before_claim' WHERE Ordinal = 2",
        ["proposal_not_a_proposal"] =
            $"UPDATE collaboration_messages SET Type = 'Question' WHERE Id = (SELECT InputProposalMessageId FROM {Steps} WHERE Ordinal = 1)",
        ["proposal_of_another_run"] =
            $"UPDATE collaboration_messages SET RunId = {NewGuid} WHERE Id = (SELECT InputProposalMessageId FROM {Steps} WHERE Ordinal = 3)",
        ["proposal_run_not_a_guid"] =
            $"UPDATE collaboration_messages SET RunId = {NotGuid} WHERE Id = (SELECT InputProposalMessageId FROM {Steps} WHERE Ordinal = 3)",
        ["proposal_missing"] =
            $"DELETE FROM collaboration_messages WHERE Id = (SELECT InputProposalMessageId FROM {Steps} WHERE Ordinal = 3)",
        ["proposal_unattributed"] =
            $"UPDATE collaboration_messages SET AttemptId = NULL WHERE Id = (SELECT InputProposalMessageId FROM {Steps} WHERE Ordinal = 3)",
        ["proposal_attributed_to_another_attempt"] =
            $"UPDATE collaboration_messages SET AttemptId = (SELECT AttemptId FROM {Steps} WHERE Ordinal = 0) "
            + $"WHERE Id = (SELECT InputProposalMessageId FROM {Steps} WHERE Ordinal = 3)",
        ["proposal_attempt_not_a_guid"] =
            $"UPDATE collaboration_messages SET AttemptId = {NotGuid} WHERE Id = (SELECT InputProposalMessageId FROM {Steps} WHERE Ordinal = 3)",
    };

    private static readonly Dictionary<string, string> AssociationCases = new()
    {
        ["attempt_role_wrong"] = "UPDATE attempts SET AgentRole = 'Resolver' WHERE Id = (SELECT AttemptId FROM {Steps} WHERE Ordinal = 1)",
        ["attempt_provider_wrong"] = "UPDATE attempts SET AgentProvider = 'Codex' WHERE Id = (SELECT AttemptId FROM {Steps} WHERE Ordinal = 1)",
        ["attempt_contract_wrong"] =
            "UPDATE attempts SET AgentResponseContract = 'Proposal' WHERE Id = (SELECT AttemptId FROM {Steps} WHERE Ordinal = 1)",
        ["attempt_role_unknown"] = "UPDATE attempts SET AgentRole = 'Bogus' WHERE Id = (SELECT AttemptId FROM {Steps} WHERE Ordinal = 1)",
        ["attempt_status_unknown"] = "UPDATE attempts SET Status = 'Bogus' WHERE Id = (SELECT AttemptId FROM {Steps} WHERE Ordinal = 1)",
        ["attempt_number_zero"] = "UPDATE attempts SET AttemptNumber = 0 WHERE Id = (SELECT AttemptId FROM {Steps} WHERE Ordinal = 1)",
        ["attempt_not_an_agent"] = "UPDATE attempts SET Kind = 'Process' WHERE Id = (SELECT AttemptId FROM {Steps} WHERE Ordinal = 1)",
        ["attempt_of_another_run"] = "UPDATE attempts SET RunId = {NewGuid} WHERE Id = (SELECT AttemptId FROM {Steps} WHERE Ordinal = 1)",
        ["attempt_missing"] = "DELETE FROM attempts WHERE Id = (SELECT AttemptId FROM {Steps} WHERE Ordinal = 1)",
        ["attempt_workspace_foreign"] =
            "UPDATE attempts SET AgentGitWorkspaceId = {NewGuid} WHERE Id = (SELECT AttemptId FROM {Steps} WHERE Ordinal = 1)",
        ["attempt_workspace_missing"] =
            "UPDATE attempts SET AgentGitWorkspaceId = NULL WHERE Id = (SELECT AttemptId FROM {Steps} WHERE Ordinal = 1)",
        ["attempt_workspace_not_a_guid"] =
            "UPDATE attempts SET AgentGitWorkspaceId = {NotGuid} WHERE Id = (SELECT AttemptId FROM {Steps} WHERE Ordinal = 1)",
        ["attempt_checkpoint_foreign"] =
            "UPDATE attempts SET AgentGitCheckpointId = {NewGuid} WHERE Id = (SELECT AttemptId FROM {Steps} WHERE Ordinal = 1)",
        ["attempt_checkpoint_missing"] =
            "UPDATE attempts SET AgentGitCheckpointId = NULL WHERE Id = (SELECT AttemptId FROM {Steps} WHERE Ordinal = 1)",
        ["attempt_fingerprint_differs"] =
            "UPDATE attempts SET AgentCheckpointFingerprintSha256 = '{Fingerprint}' WHERE Id = (SELECT AttemptId FROM {Steps} WHERE Ordinal = 1)",
        ["attempt_fingerprint_missing"] =
            "UPDATE attempts SET AgentCheckpointFingerprintSha256 = NULL WHERE Id = (SELECT AttemptId FROM {Steps} WHERE Ordinal = 1)",
        ["attempt_fingerprint_oversized"] =
            "UPDATE attempts SET AgentCheckpointFingerprintSha256 = hex(zeroblob(100)) WHERE Id = (SELECT AttemptId FROM {Steps} WHERE Ordinal = 1)",
        ["attempt_claimed_time_garbage"] =
            "UPDATE attempts SET ClaimedAtUtc = 'garbage' WHERE Id = (SELECT AttemptId FROM {Steps} WHERE Ordinal = 1)",
        ["attempt_claimed_time_oversized"] =
            "UPDATE attempts SET ClaimedAtUtc = hex(zeroblob(40)) WHERE Id = (SELECT AttemptId FROM {Steps} WHERE Ordinal = 1)",
        ["attempt_completed_time_garbage"] =
            "UPDATE attempts SET CompletedAtUtc = 'garbage' WHERE Id = (SELECT AttemptId FROM {Steps} WHERE Ordinal = 1)",
        ["attempt_dispatched_time_hidden_nul"] =
            "UPDATE attempts SET AgentDispatchedAtUtc = '2026-10-19 09:00:00' || char(0) || '+00:00' "
            + "WHERE Id = (SELECT AttemptId FROM {Steps} WHERE Ordinal = 1)",
        ["attempt_fingerprint_hidden_nul"] =
            "UPDATE attempts SET AgentCheckpointFingerprintSha256 = 'a' || char(0) || hex(zeroblob(100)) "
            + "WHERE Id = (SELECT AttemptId FROM {Steps} WHERE Ordinal = 1)",
    };

    public static TheoryData<string> StructureNames => new(StructureCases.Keys);

    public static TheoryData<string> AssociationNames => new(AssociationCases.Keys);

    private static string Expand(string sql) =>
        sql.Replace("{Steps}", Steps, StringComparison.Ordinal)
            .Replace("{NewGuid}", NewGuid, StringComparison.Ordinal)
            .Replace("{NotGuid}", NotGuid, StringComparison.Ordinal)
            .Replace("{Fingerprint}", Fingerprint, StringComparison.Ordinal);

    private async Task<(PlanningSequenceInspectionHarness Harness, Guid StepId)> EndedAsync()
    {
        var harness = await ArmedAsync(fixture);
        await harness.DriveStepsAsync(5);
        await harness.Scene.ObserveAsync(harness.SequenceId);
        return (harness, (await harness.RowsAsync())[1].Id);
    }

    [Theory]
    [MemberData(nameof(StructureNames))]
    public async Task A_damaged_structural_fact_makes_the_whole_set_unavailable_for_the_index_and_a_selected_step(string name)
    {
        var (harness, stepId) = await EndedAsync();
        await PlanningSequenceTestScene.CreateAsync(fixture);
        var healthy = await harness.IndexAsync();
        Assert.Equal(PlanningSequenceInspectionStatus.Recorded, healthy.Status);
        Assert.Equal(PlanningStepInspectionStatus.Recorded, (await harness.StepAsync(stepId)).Status);

        await harness.CorruptAsync(Expand(StructureCases[name]));

        var index = await harness.IndexAsync();
        Assert.Equal(PlanningSequenceInspectionStatus.Unavailable, index.Status);
        Assert.Null(index.Sequence);
        Assert.Empty(index.Steps);

        // A damaged step identity can no longer name its row; every other damaged fact keeps the selected step a member of the sequence.
        var selected = await harness.TryStepAsync(stepId);
        if (name == "step_id_not_a_guid" || name == "step_ordinal_gap")
        {
            Assert.True(selected.IsSuccess || selected.Errors[0].Code == "planning_sequence_inspection.not_found");
            return;
        }

        Assert.True(selected.IsSuccess, selected.IsFailure ? selected.Errors[0].Code : null);
        var answer = selected.Value;
        Assert.Equal(PlanningStepInspectionStatus.Unavailable, answer.Status);
        Assert.Equal(harness.SequenceId, answer.SequenceId);
        Assert.Null(answer.Step);
        Assert.Null(answer.Agent);
    }

    [Theory]
    [MemberData(nameof(AssociationNames))]
    public async Task A_damaged_attempt_association_leaves_the_index_recorded_and_refuses_only_the_selected_step(string name)
    {
        var (harness, stepId) = await EndedAsync();
        Assert.Equal(PlanningStepInspectionStatus.Recorded, (await harness.StepAsync(stepId)).Status);

        await harness.CorruptAsync(Expand(AssociationCases[name]));

        var index = await harness.IndexAsync();
        Assert.Equal(PlanningSequenceInspectionStatus.Recorded, index.Status);
        Assert.Equal(5, index.Steps.Count);
        var answer = await harness.StepAsync(stepId);
        Assert.Equal(PlanningStepInspectionStatus.Unavailable, answer.Status);
        Assert.Equal(harness.SequenceId, answer.SequenceId);
        Assert.Null(answer.Step);
        Assert.Null(answer.Agent);

        // The damage is local to the selected association: a sibling step is still proved.
        var sibling = await harness.StepAsync((await harness.RowsAsync())[0].Id);
        Assert.Equal(PlanningStepInspectionStatus.Recorded, sibling.Status);
    }

    [Fact]
    public async Task A_sequence_revision_change_during_the_structural_read_withholds_the_answer_without_a_retry()
    {
        var harness = await ArmedAsync(fixture);
        await harness.Scene.ObserveReservingAsync(harness.SequenceId);
        var bump = new RevisionBumper(fixture.ConnectionString, $"FROM \"{Steps}\"");

        var changed = await harness.IndexAsync(null, bump);

        Assert.True(bump.Fired);
        Assert.Equal(PlanningSequenceInspectionStatus.Unavailable, changed.Status);
        Assert.Null(changed.Sequence);
        Assert.Empty(changed.Steps);

        var reload = await harness.IndexAsync();

        Assert.Equal(PlanningSequenceInspectionStatus.Recorded, reload.Status);
        Assert.Single(reload.Steps);
        Assert.Equal((await harness.Scene.ReadSequenceAsync(harness.SequenceId)).Revision, reload.Sequence!.Revision);
    }

    [Fact]
    public async Task A_selected_step_read_during_a_structural_revision_change_is_unavailable_even_for_a_no_link_step()
    {
        var harness = await ArmedAsync(fixture);
        await harness.Scene.ObserveReservingAsync(harness.SequenceId);
        var stepId = (await harness.RowsAsync()).Single().Id;
        var bump = new RevisionBumper(fixture.ConnectionString, $"FROM \"{Steps}\"");

        var answer = await harness.StepAsync(stepId, null, bump);

        Assert.True(bump.Fired);
        Assert.Equal(PlanningStepInspectionStatus.Unavailable, answer.Status);
        Assert.Null(answer.Step);
        Assert.Null(answer.Agent);
    }

    [Fact]
    public async Task A_revision_change_during_the_selected_association_proof_withholds_the_answer_without_a_retry()
    {
        var harness = await ArmedAsync(fixture);
        await harness.Scene.ReserveAndClaimAsync(harness.SequenceId);
        var stepId = (await harness.RowsAsync()).Single().Id;
        Assert.Equal(PlanningStepInspectionStatus.Recorded, (await harness.StepAsync(stepId)).Status);
        var bump = new RevisionBumper(fixture.ConnectionString, "FROM \"attempts\"");

        var changed = await harness.StepAsync(stepId, null, bump);

        Assert.True(bump.Fired);
        Assert.Equal(PlanningStepInspectionStatus.Unavailable, changed.Status);
        Assert.Null(changed.Step);
        Assert.Null(changed.Agent);

        var reload = await harness.StepAsync(stepId);

        Assert.Equal(PlanningStepInspectionStatus.Recorded, reload.Status);
        Assert.NotNull(reload.Agent);
    }

    /// <summary>A deterministic independent SQLite writer: at the first statement that reads the named table it advances the sequence
    /// revision through a connection of its own, exactly as a concurrent transition would commit between two of the reads.</summary>
    private sealed class RevisionBumper(string connectionString, string trigger) : DbCommandInterceptor
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
            if (Fired || !command.CommandText.Contains(trigger, StringComparison.Ordinal))
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
