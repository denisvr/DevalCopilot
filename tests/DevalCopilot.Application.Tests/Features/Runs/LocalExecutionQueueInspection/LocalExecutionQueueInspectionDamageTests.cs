using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionQueueInspection;
using DevalCopilot.Application.Tests.Features.Runs.LocalExecutionAdmissionHistory;
using DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Runs;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.LocalExecutionInspection.LocalExecutionInspectionHarness;
using static DevalCopilot.Application.Tests.Features.Runs.LocalExecutionQueueInspection.LocalExecutionQueueInspectionHarness;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionQueueInspection;

/// <summary>
/// A corrupt or incoherent outstanding admission, reference or project label makes the whole overview Unavailable (ADR-0064): no entry
/// and no count, not even of the healthy cycle beside it. Every case damages one stored fact of a ten-admission queued cycle (both
/// correction origins, two pinned recipes, three passes, a waiting verification as its tail) in a database that also holds one healthy
/// cycle, through an unpooled connection with checks off and the named immutability triggers dropped, exactly as damaged storage would hold
/// it: TEST-ONLY storage writes that no production path performs. An unknown, malformed or look-alike state must stay a candidate that
/// makes the answer Unavailable and never disappears as an absence. The undamaged world is asserted first so no case can pass by being
/// broken from the start. Damage to an excluded history row is not audited by this read.
/// </summary>
public sealed class LocalExecutionQueueInspectionDamageTests : IAsyncLifetime
{
    private const string Admissions = "local_execution_admissions";
    private const string Steps = "local_execution_sequence_steps";
    private const string Seq = "local_execution_sequences";
    private const string Recipes = "local_execution_sequence_recipes";
    private const string Tail = "SequenceId = $seq AND Ordinal = 9";
    private const string ForeignGuid =
        "(SELECT upper(hex(randomblob(4))) || '-' || hex(randomblob(2)) || '-4000-8000-' || hex(randomblob(6)))";

    private static readonly string[] Triggers =
    [
        LocalExecutionAdmissionHistoryHarness.AdmissionImmutable, SequenceImmutable, StepImmutable, RecipeImmutable,
        "trg_local_execution_guard_arming", "trg_local_execution_guard_tuple", "trg_local_execution_queue_bound",
    ];

    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    private static readonly Dictionary<string, string> Cases = new()
    {
        // ---- the candidate's own stored state: only a recognized Consumed or Retired value may leave membership ----
        ["state_unknown"] = $"UPDATE {Admissions} SET State = 'Bogus' WHERE {Tail}",
        ["state_lowercase_consumed"] = $"UPDATE {Admissions} SET State = 'consumed' WHERE {Tail}",
        ["state_consumed_with_hidden_nul"] = $"UPDATE {Admissions} SET State = 'Consumed' || char(0) || 'x' WHERE {Tail}",
        ["state_retired_with_trailing_space"] = $"UPDATE {Admissions} SET State = 'Retired ' WHERE {Tail}",
        ["state_integer"] = $"UPDATE {Admissions} SET State = 7 WHERE {Tail}",
        ["state_blob"] = $"UPDATE {Admissions} SET State = zeroblob(4) WHERE {Tail}",
        ["state_empty"] = $"UPDATE {Admissions} SET State = '' WHERE {Tail}",

        // ---- the candidate's order number ----
        ["number_text"] = $"UPDATE {Admissions} SET AdmissionNumber = 'x' WHERE {Tail}",
        ["number_real"] = $"UPDATE {Admissions} SET AdmissionNumber = 99999.5 WHERE {Tail}",
        ["number_zero"] = $"UPDATE {Admissions} SET AdmissionNumber = 0 WHERE {Tail}",
        ["number_negative"] = $"UPDATE {Admissions} SET AdmissionNumber = -4 WHERE {Tail}",
        ["number_duplicates_another_candidate"] = (
            $"DROP INDEX IF EXISTS ix_local_execution_admissions_number; UPDATE {Admissions} SET AdmissionNumber = (SELECT MAX(AdmissionNumber) FROM {Admissions}) WHERE {Tail}"),

        // ---- orphan, missing and foreign references ----
        ["orphan_sequence_missing"] = $"DELETE FROM {Seq} WHERE Id = $seq",
        ["orphan_run_missing"] = "DELETE FROM runs WHERE Id = $run",
        ["orphan_project_missing"] = "DELETE FROM projects WHERE Id = $project",
        ["run_project_differs_from_the_sequence_project"] = "UPDATE runs SET ProjectId = (SELECT Id FROM projects WHERE Id <> $project LIMIT 1), ExecutionNumber = 999 WHERE Id = $run",
        ["run_mode_text"] = "UPDATE runs SET ExecutionMode = 'two' WHERE Id = $run",
        ["workspace_of_another_project"] = $"UPDATE git_workspaces SET ProjectId = {ForeignGuid} WHERE Id = (SELECT WorkspaceId FROM {Seq} WHERE Id = $seq)",
        ["initial_fingerprint_differs"] = $"UPDATE {Seq} SET FingerprintSha256 = '{new string('d', 64)}' WHERE Id = $seq",

        // ---- the registered project label ----
        ["project_name_blank"] = "UPDATE projects SET Name = '   ' WHERE Id = $project",
        ["project_name_empty"] = "UPDATE projects SET Name = '' WHERE Id = $project",
        ["project_name_over_200_units"] = "UPDATE projects SET Name = hex(zeroblob(101)) WHERE Id = $project",
        ["project_name_over_the_byte_guard"] = "UPDATE projects SET Name = hex(zeroblob(401)) WHERE Id = $project",
        ["project_name_hidden_nul_over_the_byte_guard"] = "UPDATE projects SET Name = 'a' || char(0) || hex(zeroblob(400)) WHERE Id = $project",

        // ---- the sequence and its one-per-run shape ----
        ["second_sequence_of_the_run"] = (
            $"DROP INDEX IF EXISTS ix_local_execution_sequences_run; DROP INDEX IF EXISTS ix_local_execution_sequences_coordination; "
            + $"CREATE TEMP TABLE copy AS SELECT * FROM {Seq} WHERE Id = $seq; UPDATE copy SET Id = {ForeignGuid}, CoordinationNumber = CoordinationNumber + 1000; "
            + $"INSERT INTO {Seq} SELECT * FROM copy"),
        ["immediate_mode_with_an_outstanding_admission"] = $"UPDATE {Seq} SET AdmissionMode = 'Immediate' WHERE Id = $seq",
        ["earlier_row_also_outstanding_so_the_candidate_is_not_alone"] = (
            $"DROP INDEX IF EXISTS ix_local_execution_admissions_outstanding; UPDATE {Admissions} SET State = 'Waiting', StepId = NULL, ReasonCode = NULL WHERE SequenceId = $seq AND Ordinal = 3"),
        ["reserved_tail_without_a_step_on_a_waiting_sequence"] = $"UPDATE {Admissions} SET State = 'Reserved' WHERE {Tail}",
        ["waiting_tail_on_an_ended_sequence"] = (
            $"UPDATE {Seq} SET State = 'Stopped', ReasonCode = 'stopped_before_claim', EndedAtUtc = UpdatedAtUtc WHERE Id = $seq"),
        ["unresolved_tail_on_a_live_sequence"] = (
            $"UPDATE {Admissions} SET State = 'Unresolved', ReasonCode = '{LocalExecutionSequenceReason.PreparationUnresolved}' WHERE {Tail}"),
        ["sequence_revision_text"] = $"UPDATE {Seq} SET Revision = 'abc' WHERE Id = $seq",
        ["advancement_deadline_differs"] = $"UPDATE {Seq} SET AdvancementDeadlineUtc = '2099-01-01 00:00:00+00:00' WHERE Id = $seq",
        ["recipe_count_differs_from_the_pinned_set"] = $"UPDATE {Seq} SET RecipeCount = 3 WHERE Id = $seq",

        // ---- the unchanged whole-ledger proof reaches every candidate ----
        ["ledger_gap"] = $"DELETE FROM {Admissions} WHERE SequenceId = $seq AND Ordinal = 4",
        ["ledger_kind_unknown"] = $"UPDATE {Admissions} SET Kind = 'Bogus' WHERE SequenceId = $seq AND Ordinal = 3",
        ["ledger_step_missing"] = $"DELETE FROM {Steps} WHERE SequenceId = $seq AND Ordinal = 5",
        ["ledger_step_tuple_kind"] = $"UPDATE {Steps} SET Kind = 'CodeReview' WHERE SequenceId = $seq AND Ordinal = 2",
        ["ledger_recipe_missing"] = $"DELETE FROM {Recipes} WHERE SequenceId = $seq AND Ordinal = 1",
        ["ledger_source_checkpoint_of_another_workspace"] = (
            $"UPDATE git_checkpoints SET WorkspaceId = {ForeignGuid} WHERE Id = (SELECT SourceCheckpointId FROM {Admissions} WHERE SequenceId = $seq AND Ordinal = 5)"),
        ["ledger_report_is_not_an_execution_report"] = (
            $"UPDATE collaboration_messages SET Type = 'Acceptance' WHERE Id = (SELECT ReportMessageId FROM {Admissions} WHERE SequenceId = $seq AND Ordinal = 7)"),
    };

    public static TheoryData<string> CaseNames => new(Cases.Keys);

    private async Task<(LocalExecutionQueueInspectionHarness Harness, LocalExecutionAdmissionHistoryHarness Damaged)> WorldAsync()
    {
        var harness = new LocalExecutionQueueInspectionHarness(fixture);
        var (damaged, sequenceId) = await harness.AddCycleAsync(recipeCount: 2, projectName: "Damaged");
        await damaged.DriveStepsThenWaitAsync(sequenceId, 9, LocalExecutionAdmissionHistoryHarness.MixedOriginsScript());
        await harness.AddCycleAsync(projectName: "Healthy");
        Assert.Equal(10, (await damaged.Scene.ReadAdmissionsAsync()).Count);
        return (harness, damaged);
    }

    private static string Bind(string sql, LocalExecutionSequenceTestScene scene, Guid sequenceId) => sql
        .Replace("$seq", Literal(sequenceId), StringComparison.Ordinal)
        .Replace("$run", Literal(scene.RunId), StringComparison.Ordinal)
        .Replace("$project", Literal(scene.ProjectId), StringComparison.Ordinal);

    private static string Literal(Guid id) => $"'{id.ToString("D").ToUpperInvariant()}'";

    [Fact]
    public async Task The_undamaged_world_is_two_recorded_entries()
    {
        var (harness, damaged) = await WorldAsync();

        var result = await harness.InspectAsync();

        Assert.Equal(LocalExecutionQueueInspectionStatus.Recorded, result.Status);
        Assert.Equal(["Damaged", "Healthy"], result.Entries.Select(entry => entry.ProjectName));
        Assert.Equal(9, result.Entries[0].Admission.Ordinal);
        Assert.Equal(LocalExecutionStepKind.Verification, result.Entries[0].Admission.Kind);
        await AssertMatchesPerRunLedgerAsync(result.Entries[0], damaged);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public async Task A_damaged_fact_makes_the_whole_overview_unavailable_with_no_entry_and_no_count(string name)
    {
        var (harness, damaged) = await WorldAsync();
        var sequenceId = (await damaged.Scene.ReadSequenceAsync()).Id;

        await harness.CorruptAsync(Bind(Cases[name], damaged.Scene, sequenceId), null, Triggers);

        AssertShowsNothing(await harness.InspectAsync(), LocalExecutionQueueInspectionStatus.Unavailable);
    }

    [Fact]
    public async Task A_reload_of_damaged_storage_is_deterministic_and_stays_unavailable()
    {
        var (harness, damaged) = await WorldAsync();
        var sequenceId = (await damaged.Scene.ReadSequenceAsync()).Id;
        await harness.CorruptAsync(Bind(Cases["state_unknown"], damaged.Scene, sequenceId), null, Triggers);

        var first = await harness.InspectAsync();
        var second = await harness.InspectAsync();

        AssertShowsNothing(first, LocalExecutionQueueInspectionStatus.Unavailable);
        AssertShowsNothing(second, LocalExecutionQueueInspectionStatus.Unavailable);
    }

    [Fact]
    public async Task Damage_to_an_excluded_history_row_of_a_cycle_that_is_not_outstanding_is_not_audited_by_this_read()
    {
        var harness = new LocalExecutionQueueInspectionHarness(fixture);
        var (ended, _) = await harness.AddCycleAsync();
        await new LocalExecutionQueueHost((ended.Scene, new LocalExecutionStepScript())).RunUntilAllEndedAsync();
        var (live, _) = await harness.AddCycleAsync();
        Assert.Equal(LocalExecutionQueueInspectionStatus.Recorded, (await harness.InspectAsync()).Status);

        await harness.CorruptAsync(
            $"UPDATE {Admissions} SET Kind = 'Bogus' WHERE SequenceId = (SELECT Id FROM {Seq} WHERE RunId = {Literal(ended.Scene.RunId)}) AND Ordinal = 2",
            null,
            Triggers);

        var result = await harness.InspectAsync();

        Assert.Equal(LocalExecutionQueueInspectionStatus.Recorded, result.Status);
        Assert.Equal(live.Scene.RunId, Assert.Single(result.Entries).RunId);
    }
}
