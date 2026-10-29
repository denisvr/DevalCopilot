using DevalCopilot.Application.Features.Runs.Policies.RecordedRunAbandonment;
using DevalCopilot.Application.Features.Runs.Queries.GetRunAbandonmentInspection;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.RunAbandonmentInspection;

/// <summary>
/// Present but damaged, foreign, stray, doubled or incomplete abandonment evidence is Unavailable and never absent (ADR-0071), and
/// absence is proved only by a recognized non-Abandoned run with both abandonment columns SQL NULL and no exactly associated event. Every
/// case first proves its scene (Recorded for a production-closed run, NotRecorded for an open one), then damages one stored fact through
/// a context of its own with foreign keys off, exactly as damaged storage would hold it (test-only writes no production path performs;
/// the reader is not weakened and no production hook exists), and expects the one named answer. Identities, times, text, enums, REAL,
/// BLOB, hidden-NUL and invalid-UTF-8 storage, the Human/run-scoped actor, the event association, the payload interpretation and the
/// shared coherence inputs are each covered; the coherence rule itself stays covered by its own Domain tests.
/// </summary>
public sealed class RunAbandonmentInspectionDamageTests : RunAbandonmentInspectionTestBase
{
    private const string Events = "events";
    private const string Written = "2026-10-08 12:00:00+00:00";

    public enum Expectation
    {
        Unavailable,
        NotRecorded,
        NotFound,
    }

    private static string BothReasons(string runReason, string payloadReason) =>
        $"UPDATE runs SET AbandonmentReason = {runReason}; "
        + $"UPDATE {Events} SET PayloadJson = '{{\"reason\":\"' || {payloadReason} || '\"}}' WHERE EventType = 'run.abandoned'";

    private static readonly string Oversized = "replace(hex(zeroblob(1025)), '0', 'a')";

    private static readonly Dictionary<string, (string Sql, Expectation Expected)> ClosedCases = new()
    {
        // ---- the event and its exact association ----
        ["event_deleted"] = ($"DELETE FROM {Events} WHERE EventType = 'run.abandoned'", Expectation.Unavailable),
        ["event_type_upper_case"] = ($"UPDATE {Events} SET EventType = 'RUN.ABANDONED' WHERE EventType = 'run.abandoned'", Expectation.Unavailable),
        ["event_type_trailing_blank"] = ($"UPDATE {Events} SET EventType = 'run.abandoned ' WHERE EventType = 'run.abandoned'", Expectation.Unavailable),
        ["event_of_another_run"] = ($"UPDATE {Events} SET RunId = '{{foreign}}' WHERE EventType = 'run.abandoned'", Expectation.Unavailable),
        ["event_run_key_lower_case"] = ($"UPDATE {Events} SET RunId = lower(RunId) WHERE EventType = 'run.abandoned'", Expectation.Unavailable),
        ["second_event"] = (Duplicate("'{foreign}'", "PayloadJson"), Expectation.Unavailable),
        ["second_event_with_a_damaged_identity"] = (Duplicate("'not-an-identity'", "PayloadJson"), Expectation.Unavailable),
        ["second_event_with_another_payload"] = (Duplicate("'{foreign}'", "'{}'"), Expectation.Unavailable),
        ["event_id_lower_case"] = ($"UPDATE {Events} SET Id = lower(Id) WHERE EventType = 'run.abandoned'", Expectation.Unavailable),
        ["event_id_blank"] = ($"UPDATE {Events} SET Id = '' WHERE EventType = 'run.abandoned'", Expectation.Unavailable),
        ["event_id_oversized"] = ($"UPDATE {Events} SET Id = Id || 'EXTRA' WHERE EventType = 'run.abandoned'", Expectation.Unavailable),
        ["event_id_embedded_nul"] = ($"UPDATE {Events} SET Id = substr(Id, 1, 35) || char(0) WHERE EventType = 'run.abandoned'", Expectation.Unavailable),
        ["event_id_nil"] = ($"UPDATE {Events} SET Id = '00000000-0000-0000-0000-000000000000' WHERE EventType = 'run.abandoned'", Expectation.Unavailable),
        ["event_id_braced"] = ($"UPDATE {Events} SET Id = char(123) || Id || char(125) WHERE EventType = 'run.abandoned'", Expectation.Unavailable),
        ["event_actor_agent"] = ($"UPDATE {Events} SET ActorKind = 'Agent' WHERE EventType = 'run.abandoned'", Expectation.Unavailable),
        ["event_actor_orchestrator"] = ($"UPDATE {Events} SET ActorKind = 'Orchestrator' WHERE EventType = 'run.abandoned'", Expectation.Unavailable),
        ["event_actor_text"] = ($"UPDATE {Events} SET ActorKind = 'human' WHERE EventType = 'run.abandoned'", Expectation.Unavailable),
        ["event_human_with_role"] = ($"UPDATE {Events} SET ActorAgentRole = 'Planner' WHERE EventType = 'run.abandoned'", Expectation.Unavailable),
        ["event_human_with_provider"] = ($"UPDATE {Events} SET ActorAgentProvider = 'Codex' WHERE EventType = 'run.abandoned'", Expectation.Unavailable),
        ["event_sequence_zero"] = ($"UPDATE {Events} SET Sequence = 0 WHERE EventType = 'run.abandoned'", Expectation.Unavailable),
        ["event_sequence_negative"] = ($"UPDATE {Events} SET Sequence = -5 WHERE EventType = 'run.abandoned'", Expectation.Unavailable),
        ["event_attempt_scoped"] = ($"UPDATE {Events} SET AttemptId = '{{foreign}}' WHERE EventType = 'run.abandoned'", Expectation.Unavailable),

        // ---- the event payload interpretation ----
        ["payload_not_json"] = ($"UPDATE {Events} SET PayloadJson = '{{' WHERE EventType = 'run.abandoned'", Expectation.Unavailable),
        ["payload_empty_object"] = ($"UPDATE {Events} SET PayloadJson = '{{}}' WHERE EventType = 'run.abandoned'", Expectation.Unavailable),
        ["payload_array_root"] = ($"UPDATE {Events} SET PayloadJson = '[]' WHERE EventType = 'run.abandoned'", Expectation.Unavailable),
        ["payload_reason_not_a_string"] = ($"UPDATE {Events} SET PayloadJson = '{{\"reason\":7}}' WHERE EventType = 'run.abandoned'", Expectation.Unavailable),
        ["payload_reason_differs"] = ($"UPDATE {Events} SET PayloadJson = '{{\"reason\":\"Another reason.\"}}' WHERE EventType = 'run.abandoned'", Expectation.Unavailable),
        ["payload_reason_case_differs"] = (
            $"UPDATE {Events} SET PayloadJson = upper(PayloadJson) WHERE EventType = 'run.abandoned'", Expectation.Unavailable),
        ["payload_lone_surrogate_escape"] = (
            $"UPDATE {Events} SET PayloadJson = '{{\"reason\":\"\\ud800\"}}' WHERE EventType = 'run.abandoned'", Expectation.Unavailable),
        ["payload_embedded_nul"] = (
            $"UPDATE {Events} SET PayloadJson = PayloadJson || char(0) WHERE EventType = 'run.abandoned'", Expectation.Unavailable),
        ["payload_invalid_utf8"] = (
            $"UPDATE {Events} SET PayloadJson = CAST(X'7B22726561736F6E223A22FF227D' AS TEXT) WHERE EventType = 'run.abandoned'",
            Expectation.Unavailable),
        ["payload_beyond_the_storage_bound"] = (
            $"UPDATE {Events} SET PayloadJson = PayloadJson || replace(hex(zeroblob(8192)), '0', ' ') WHERE EventType = 'run.abandoned'",
            Expectation.Unavailable),

        // ---- the event time ----
        ["event_time_one_tick_later"] = ($"UPDATE {Events} SET OccurredAtUtc = '2026-10-08 12:00:00.0000001+00:00' WHERE EventType = 'run.abandoned'", Expectation.Unavailable),
        ["event_time_one_tick_earlier"] = ($"UPDATE {Events} SET OccurredAtUtc = '2026-10-08 11:59:59.9999999+00:00' WHERE EventType = 'run.abandoned'", Expectation.Unavailable),
        ["event_time_unparsable"] = ($"UPDATE {Events} SET OccurredAtUtc = 'noon' WHERE EventType = 'run.abandoned'", Expectation.Unavailable),
        ["event_time_not_the_writers_text"] = ($"UPDATE {Events} SET OccurredAtUtc = '2026-10-08T12:00:00+00:00' WHERE EventType = 'run.abandoned'", Expectation.Unavailable),
        ["event_time_oversized"] = ($"UPDATE {Events} SET OccurredAtUtc = '{Written}' || replace(hex(zeroblob(40)), '0', ' ') WHERE EventType = 'run.abandoned'", Expectation.Unavailable),
        ["event_time_integer"] = ($"UPDATE {Events} SET OccurredAtUtc = 7 WHERE EventType = 'run.abandoned'", Expectation.Unavailable),

        // ---- the run row: lifecycle, mode, participants and project ----
        ["lifecycle_unknown"] = ("UPDATE runs SET Lifecycle = 'Bogus'", Expectation.Unavailable),
        ["lifecycle_still_running_beside_every_fact"] = ("UPDATE runs SET Lifecycle = 'Running'", Expectation.Unavailable),
        ["lifecycle_completed_beside_every_fact"] = ("UPDATE runs SET Lifecycle = 'Completed'", Expectation.Unavailable),
        ["mode_legacy"] = ("UPDATE runs SET ExecutionMode = '0'", Expectation.Unavailable),
        ["mode_simulated"] = ("UPDATE runs SET ExecutionMode = '1'", Expectation.Unavailable),
        ["mode_text"] = ("UPDATE runs SET ExecutionMode = 'two'", Expectation.Unavailable),
        ["mode_out_of_range"] = ("UPDATE runs SET ExecutionMode = 7", Expectation.Unavailable),
        ["mode_fractional"] = ("UPDATE runs SET ExecutionMode = 2.5", Expectation.Unavailable),
        ["mode_oversized_text"] = ("UPDATE runs SET ExecutionMode = '2222222222'", Expectation.Unavailable),
        ["mode_blob"] = ("UPDATE runs SET ExecutionMode = CAST('2' AS BLOB)", Expectation.Unavailable),
        ["participant_kind_not_none"] = ("UPDATE runs SET ActiveParticipantKind = 'Agent'", Expectation.Unavailable),
        ["participant_role_present"] = ("UPDATE runs SET ActiveAgentRole = 'Planner'", Expectation.Unavailable),
        ["participant_provider_present"] = ("UPDATE runs SET ActiveAgentProvider = 'Codex'", Expectation.Unavailable),
        ["run_project_key_lower_case_on_both_sides"] = ("UPDATE runs SET ProjectId = lower(ProjectId); UPDATE projects SET Id = lower(Id)", Expectation.Unavailable),
        ["run_project_not_registered"] = ("UPDATE runs SET ProjectId = '{foreign}'", Expectation.NotFound),

        // ---- the recorded reason ----
        ["reason_null"] = ("UPDATE runs SET AbandonmentReason = NULL", Expectation.Unavailable),
        ["reason_empty"] = (BothReasons("''", "''"), Expectation.Unavailable),
        ["reason_leading_blank"] = (BothReasons("' x'", "' x'"), Expectation.Unavailable),
        ["reason_trailing_line_feed"] = (BothReasons("'x' || char(10)", "'x\\n'"), Expectation.Unavailable),
        ["reason_carriage_return_line_feed"] = (BothReasons("'a' || char(13) || char(10) || 'b'", "'a\\r\\nb'"), Expectation.Unavailable),
        ["reason_control_character"] = (BothReasons("'x' || char(7)", "'x\\u0007'"), Expectation.Unavailable),
        ["reason_line_separator"] = (BothReasons("'x' || char(8232) || 'y'", "'x\\u2028y'"), Expectation.Unavailable),
        ["reason_beyond_two_kibibytes"] = (BothReasons(Oversized, Oversized), Expectation.Unavailable),
        ["reason_embedded_nul"] = ("UPDATE runs SET AbandonmentReason = AbandonmentReason || char(0) || 'tail'", Expectation.Unavailable),
        ["reason_invalid_utf8"] = ("UPDATE runs SET AbandonmentReason = CAST(X'61FF62' AS TEXT)", Expectation.Unavailable),
        ["reason_differs_from_the_event"] = ("UPDATE runs SET AbandonmentReason = 'A different reason.'", Expectation.Unavailable),

        // ---- the recorded abandonment time ----
        ["abandoned_at_null"] = ("UPDATE runs SET AbandonedAtUtc = NULL", Expectation.Unavailable),
        ["abandoned_at_unparsable"] = ("UPDATE runs SET AbandonedAtUtc = 'noon'", Expectation.Unavailable),
        ["abandoned_at_blob_of_the_exact_text"] = ("UPDATE runs SET AbandonedAtUtc = CAST(AbandonedAtUtc AS BLOB)", Expectation.Unavailable),
        ["abandoned_at_integer"] = ("UPDATE runs SET AbandonedAtUtc = 7", Expectation.Unavailable),
        ["abandoned_at_real"] = ("UPDATE runs SET AbandonedAtUtc = 7.5", Expectation.Unavailable),
        ["abandoned_at_oversized"] = ($"UPDATE runs SET AbandonedAtUtc = '{Written}' || replace(hex(zeroblob(40)), '0', ' ')", Expectation.Unavailable),
        ["abandoned_at_one_tick_later"] = ("UPDATE runs SET AbandonedAtUtc = '2026-10-08 12:00:00.0000001+00:00'", Expectation.Unavailable),
        ["abandoned_at_one_tick_earlier"] = ("UPDATE runs SET AbandonedAtUtc = '2026-10-08 11:59:59.9999999+00:00'", Expectation.Unavailable),
        ["last_advance_one_tick_later"] = ("UPDATE runs SET LastAdvancedAtUtc = '2026-10-08 12:00:00.0000001+00:00'", Expectation.Unavailable),
        ["last_advance_unparsable"] = ("UPDATE runs SET LastAdvancedAtUtc = 'noon'", Expectation.Unavailable),
        ["last_advance_oversized"] = ($"UPDATE runs SET LastAdvancedAtUtc = '{Written}' || replace(hex(zeroblob(40)), '0', ' ')", Expectation.Unavailable),
    };

    private static readonly Dictionary<string, (string Sql, Expectation Expected)> OpenCases = new()
    {
        ["control_nothing_recorded"] = ("UPDATE runs SET Stage = Stage", Expectation.NotRecorded),
        ["reason_beside_a_running_run"] = ("UPDATE runs SET AbandonmentReason = 'A stray reason.'", Expectation.Unavailable),
        ["time_beside_a_running_run"] = ($"UPDATE runs SET AbandonedAtUtc = '{Written}'", Expectation.Unavailable),
        ["blob_time_beside_a_running_run"] = ($"UPDATE runs SET AbandonedAtUtc = CAST('{Written}' AS BLOB)", Expectation.Unavailable),
        ["unparsable_time_beside_a_running_run"] = ("UPDATE runs SET AbandonedAtUtc = 'noon'", Expectation.Unavailable),
        ["empty_reason_beside_a_running_run"] = ("UPDATE runs SET AbandonmentReason = ''", Expectation.Unavailable),
        ["event_beside_a_running_run"] = (OpenEvent("'{foreign}'"), Expectation.Unavailable),
        ["damaged_event_beside_a_running_run"] = (
            OpenEvent("'not-an-identity'") + $"; UPDATE {Events} SET PayloadJson = '{{', OccurredAtUtc = 'noon' WHERE EventType = 'run.abandoned'",
            Expectation.Unavailable),
        ["event_of_another_type_is_no_association"] = (OpenEvent("'{foreign}'") + $"; UPDATE {Events} SET EventType = 'run.abandoned.x' WHERE EventType = 'run.abandoned'", Expectation.NotRecorded),
        ["lifecycle_unknown"] = ("UPDATE runs SET Lifecycle = 'Bogus'", Expectation.Unavailable),
        ["mode_unrecognized_text"] = ("UPDATE runs SET ExecutionMode = 'two'", Expectation.Unavailable),
        ["mode_unrecognized_number"] = ("UPDATE runs SET ExecutionMode = 7", Expectation.Unavailable),
        ["mode_fractional"] = ("UPDATE runs SET ExecutionMode = 1.5", Expectation.Unavailable),
        ["mode_oversized"] = ("UPDATE runs SET ExecutionMode = '1111111111'", Expectation.Unavailable),
        ["project_key_lower_case_on_both_sides"] = ("UPDATE runs SET ProjectId = lower(ProjectId); UPDATE projects SET Id = lower(Id)", Expectation.Unavailable),
        ["project_not_registered"] = ("UPDATE runs SET ProjectId = '{foreign}'", Expectation.NotFound),
    };

    public static TheoryData<string> ClosedNames => new(ClosedCases.Keys);

    public static TheoryData<string> OpenNames => new(OpenCases.Keys);

    private static string Duplicate(string newId, string payload) =>
        $"INSERT INTO {Events} (Id, RunId, AttemptId, EventType, ActorKind, ActorAgentRole, ActorAgentProvider, PayloadJson, OccurredAtUtc) "
        + $"SELECT {newId}, RunId, AttemptId, EventType, ActorKind, ActorAgentRole, ActorAgentProvider, {payload}, OccurredAtUtc "
        + $"FROM {Events} WHERE EventType = 'run.abandoned'";

    private static string OpenEvent(string newId) =>
        $"INSERT INTO {Events} (Id, RunId, AttemptId, EventType, ActorKind, ActorAgentRole, ActorAgentProvider, PayloadJson, OccurredAtUtc) "
        + $"SELECT {newId}, RunId, NULL, 'run.abandoned', 'Human', NULL, NULL, '{{\"reason\":\"A stray reason.\"}}', '{Written}' "
        + $"FROM {Events} WHERE EventType = 'run.started'";

    [Theory]
    [MemberData(nameof(ClosedNames))]
    public async Task Damaged_or_foreign_evidence_of_a_closed_run_is_never_recorded_and_never_absent(string name)
    {
        var (sql, expected) = ClosedCases[name];
        var closed = await AbandonAsync(running: true);
        Assert.Equal(RunAbandonmentInspectionStatus.Recorded, (await InspectAsync(closed.RunId)).Status);

        Assert.True(await ExecuteAsync(Bind(sql, closed.RunId)) > 0, "The damage statement must change at least one row.");

        await AssertAnswerAsync(closed.RunId, expected);
    }

    [Theory]
    [MemberData(nameof(OpenNames))]
    public async Task Stray_or_unsupported_facts_of_an_open_run_are_never_absent(string name)
    {
        var (sql, expected) = OpenCases[name];
        var scene = await AbandonmentScene.CreateAsync(Fixture, running: true);
        Assert.Equal(RunAbandonmentInspectionStatus.NotRecorded, (await InspectAsync(scene.RunId)).Status);

        Assert.True(await ExecuteAsync(Bind(sql, scene.RunId)) > 0, "The damage statement must change at least one row.");

        await AssertAnswerAsync(scene.RunId, expected);
    }

    [Fact]
    public async Task Damaged_evidence_of_another_run_never_disturbs_a_healthy_run()
    {
        var healthy = await AbandonAsync(running: true);
        var damaged = await AbandonAsync(running: false);
        await ExecuteAsync(
            $"UPDATE runs SET AbandonedAtUtc = CAST(AbandonedAtUtc AS BLOB) WHERE Id = '{Id(damaged.RunId)}'; "
            + $"UPDATE {Events} SET PayloadJson = '{{' WHERE RunId = '{Id(damaged.RunId)}' AND EventType = 'run.abandoned'");

        Assert.Equal(RunAbandonmentInspectionStatus.Recorded, (await InspectAsync(healthy.RunId)).Status);
        Assert.Equal(RunAbandonmentInspectionStatus.Unavailable, (await InspectAsync(damaged.RunId)).Status);
    }

    [Fact]
    public async Task The_event_of_another_run_is_neither_this_runs_event_nor_a_duplicate_of_it()
    {
        var first = await AbandonAsync(running: true);
        var second = await AbandonAsync(running: false);

        var recorded = Recorded(await InspectAsync(first.RunId));

        Assert.Equal(first.EventId, recorded.EventId);
        Assert.NotEqual(second.EventId, recorded.EventId);
    }

    private string Bind(string sql, Guid runId) => sql
        .Replace("{foreign}", Id(Guid.NewGuid()), StringComparison.Ordinal)
        .Replace("{run}", Id(runId), StringComparison.Ordinal);

    private async Task AssertAnswerAsync(Guid runId, Expectation expected)
    {
        var result = await TryInspectAsync(runId);
        switch (expected)
        {
            case Expectation.NotFound:
                Assert.True(result.IsFailure);
                Assert.Equal(RunAbandonmentInspectionErrors.NotFoundCode, Assert.Single(result.Errors).Code);
                break;
            case Expectation.NotRecorded:
                Assert.Equal(RunAbandonmentInspectionStatus.NotRecorded, Assert.IsType<GetRunAbandonmentInspectionQueryResult>(result.Value).Status);
                Assert.Null(result.Value.Abandonment);
                break;
            default:
                Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null);
                Assert.Equal(RunAbandonmentInspectionStatus.Unavailable, result.Value.Status);
                Assert.Null(result.Value.Abandonment);
                break;
        }
    }
}
