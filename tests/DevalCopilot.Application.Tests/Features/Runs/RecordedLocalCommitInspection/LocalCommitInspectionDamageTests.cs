using DevalCopilot.Application.Features.Runs.Policies.RecordedLocalCommit;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalCommitInspection;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.RecordedLocalCommitInspection;

/// <summary>
/// A present but damaged, foreign or incomplete operation is Unavailable and never absent (ADR-0070), and absence is proved only by the
/// run-keyed association. Every case first proves its scene Recorded, then damages one stored fact through an unpooled connection with
/// foreign keys off, exactly as damaged storage would hold it (test-only writes that no production path performs; the reader is not
/// weakened and no production hook exists), and expects the one named answer. Unsupported or malformed identities, times, text, enums,
/// REAL, TEXT or hidden-NUL storage, ownership by a different project, run, workspace or checkpoint, a missing registered parent and the
/// complete status/time/reason/commit tuple are each covered.
/// </summary>
public sealed class LocalCommitInspectionDamageTests : LocalCommitInspectionTestBase
{
    private const string Operations = "local_commit_operations";
    private static readonly string Hex40 = new('b', 40);
    private static readonly string Hex64 = new('b', 64);

    public enum Expectation
    {
        Unavailable,
        NotRecorded,
        NotFound,
    }

    private static readonly Dictionary<string, (LocalCommitInspectionShape Shape, string Sql, Expectation Expected)> Cases = new()
    {
        // ---- the operation's own identity and association ----
        ["op_id_lowercase"] = (Completed, $"UPDATE {Operations} SET Id = lower(Id)", Expectation.Unavailable),
        ["op_id_blank"] = (Completed, $"UPDATE {Operations} SET Id = ''", Expectation.Unavailable),
        ["op_id_oversized"] = (Completed, $"UPDATE {Operations} SET Id = Id || 'EXTRA'", Expectation.Unavailable),
        ["op_id_embedded_nul"] = (Completed, $"UPDATE {Operations} SET Id = substr(Id, 1, 35) || char(0)", Expectation.Unavailable),
        ["op_id_nil"] = (Completed, $"UPDATE {Operations} SET Id = '00000000-0000-0000-0000-000000000000'", Expectation.Unavailable),
        ["op_id_braced"] = (Completed, $"UPDATE {Operations} SET Id = char(123) || Id || char(125)", Expectation.Unavailable),
        ["op_run_key_lowercase_is_not_the_association"] = (Completed, $"UPDATE {Operations} SET RunId = lower(RunId)", Expectation.NotRecorded),
        ["op_run_key_of_another_run_is_not_the_association"] = (Completed, $"UPDATE {Operations} SET RunId = {{foreign}}", Expectation.NotRecorded),
        ["op_project_foreign"] = (Completed, $"UPDATE {Operations} SET ProjectId = {{foreign}}", Expectation.Unavailable),
        ["op_project_lowercase"] = (Completed, $"UPDATE {Operations} SET ProjectId = lower(ProjectId)", Expectation.Unavailable),
        ["op_project_oversized"] = (Completed, $"UPDATE {Operations} SET ProjectId = ProjectId || 'X'", Expectation.Unavailable),
        ["op_workspace_missing"] = (Completed, $"UPDATE {Operations} SET GitWorkspaceId = {{foreign}}", Expectation.Unavailable),
        ["op_workspace_lowercase"] = (Completed, $"UPDATE {Operations} SET GitWorkspaceId = lower(GitWorkspaceId)", Expectation.Unavailable),
        ["op_checkpoint_missing"] = (Completed, $"UPDATE {Operations} SET GitCheckpointId = {{foreign}}", Expectation.Unavailable),
        ["op_checkpoint_lowercase"] = (Completed, $"UPDATE {Operations} SET GitCheckpointId = lower(GitCheckpointId)", Expectation.Unavailable),
        ["second_operation_for_the_run"] = (
            Completed,
            $"DROP INDEX ix_local_commit_operations_run; CREATE TEMP TABLE copy AS SELECT * FROM {Operations}; UPDATE copy SET Id = {{foreign}}; "
            + $"INSERT INTO {Operations} SELECT * FROM copy;",
            Expectation.Unavailable),

        // ---- the registered run, project, workspace and checkpoint ----
        ["run_project_not_registered"] = (Completed, "UPDATE runs SET ProjectId = {foreign}", Expectation.NotFound),
        ["run_mode_legacy"] = (Completed, "UPDATE runs SET ExecutionMode = '0'", Expectation.Unavailable),
        ["run_mode_simulated"] = (Completed, "UPDATE runs SET ExecutionMode = '1'", Expectation.Unavailable),
        ["run_mode_text"] = (Completed, "UPDATE runs SET ExecutionMode = 'two'", Expectation.Unavailable),
        ["run_mode_out_of_range"] = (Completed, "UPDATE runs SET ExecutionMode = 7", Expectation.Unavailable),
        ["run_mode_fractional"] = (Completed, "UPDATE runs SET ExecutionMode = 2.5", Expectation.Unavailable),
        ["run_mode_oversized_text"] = (Completed, "UPDATE runs SET ExecutionMode = '2222222222'", Expectation.Unavailable),
        ["workspace_row_deleted"] = (Completed, "DELETE FROM git_workspaces WHERE Id = '{ws}'", Expectation.Unavailable),
        ["checkpoint_row_deleted"] = (Completed, "DELETE FROM git_checkpoints WHERE Id = '{cp}'", Expectation.Unavailable),
        ["workspace_of_another_project"] = (Completed, "UPDATE git_workspaces SET ProjectId = {foreign}", Expectation.Unavailable),
        ["workspace_project_lowercase"] = (Completed, "UPDATE git_workspaces SET ProjectId = lower(ProjectId)", Expectation.Unavailable),
        ["checkpoint_of_another_workspace"] = (Completed, "UPDATE git_checkpoints SET WorkspaceId = {foreign}", Expectation.Unavailable),
        ["workspace_branch_differs"] = (Completed, "UPDATE git_workspaces SET BranchName = 'devalcopilot/other'", Expectation.Unavailable),
        ["workspace_branch_case_differs"] = (Completed, "UPDATE git_workspaces SET BranchName = upper(BranchName)", Expectation.Unavailable),
        ["checkpoint_number_differs"] = (Completed, "UPDATE git_checkpoints SET CheckpointNumber = 2", Expectation.Unavailable),
        ["checkpoint_fingerprint_differs"] = (Completed, $"UPDATE git_checkpoints SET FingerprintSha256 = '{Hex64}'", Expectation.Unavailable),
        ["checkpoint_parent_head_differs"] = (Completed, $"UPDATE git_checkpoints SET HeadCommitSha = '{Hex40}'", Expectation.Unavailable),

        // ---- branch ----
        ["branch_blank"] = (Completed, $"UPDATE {Operations} SET BranchName = ''; UPDATE git_workspaces SET BranchName = '';", Expectation.Unavailable),
        ["branch_whitespace"] = (Completed, $"UPDATE {Operations} SET BranchName = '   '; UPDATE git_workspaces SET BranchName = '   ';", Expectation.Unavailable),
        ["branch_control_character"] = (
            Completed, $"UPDATE {Operations} SET BranchName = 'a' || char(10) || 'b'; UPDATE git_workspaces SET BranchName = 'a' || char(10) || 'b';",
            Expectation.Unavailable),
        ["branch_embedded_nul"] = (
            Completed, $"UPDATE {Operations} SET BranchName = 'a' || char(0) || 'b'; UPDATE git_workspaces SET BranchName = 'a' || char(0) || 'b';",
            Expectation.Unavailable),
        ["branch_513_characters"] = (
            Completed, $"UPDATE {Operations} SET BranchName = '{new string('x', 513)}'; UPDATE git_workspaces SET BranchName = '{new string('x', 513)}';",
            Expectation.Unavailable),
        ["branch_oversized_bytes"] = (
            Completed, $"UPDATE {Operations} SET BranchName = '{new string('x', 1600)}'; UPDATE git_workspaces SET BranchName = '{new string('x', 1600)}';",
            Expectation.Unavailable),

        // ---- object identities and fingerprint ----
        ["parent_uppercase"] = (
            Completed, $"UPDATE {Operations} SET ParentCommitSha = upper(ParentCommitSha); UPDATE git_checkpoints SET HeadCommitSha = upper(HeadCommitSha);",
            Expectation.Unavailable),
        ["parent_39_characters"] = (
            Completed, $"UPDATE {Operations} SET ParentCommitSha = substr(ParentCommitSha, 1, 39); UPDATE git_checkpoints SET HeadCommitSha = substr(HeadCommitSha, 1, 39);",
            Expectation.Unavailable),
        ["parent_41_characters"] = (
            Completed, $"UPDATE {Operations} SET ParentCommitSha = ParentCommitSha || 'a'; UPDATE git_checkpoints SET HeadCommitSha = HeadCommitSha || 'a';",
            Expectation.Unavailable),
        ["parent_not_hexadecimal"] = (
            Completed, $"UPDATE {Operations} SET ParentCommitSha = '{new string('g', 40)}'; UPDATE git_checkpoints SET HeadCommitSha = '{new string('g', 40)}';",
            Expectation.Unavailable),
        ["tree_uppercase"] = (Completed, $"UPDATE {Operations} SET TreeSha = upper(TreeSha)", Expectation.Unavailable),
        ["tree_39_characters"] = (Completed, $"UPDATE {Operations} SET TreeSha = substr(TreeSha, 1, 39)", Expectation.Unavailable),
        ["tree_41_characters"] = (Completed, $"UPDATE {Operations} SET TreeSha = TreeSha || 'a'", Expectation.Unavailable),
        ["tree_blank"] = (Completed, $"UPDATE {Operations} SET TreeSha = ''", Expectation.Unavailable),
        ["fingerprint_uppercase"] = (
            Completed,
            $"UPDATE {Operations} SET CheckpointFingerprintSha256 = upper(CheckpointFingerprintSha256); UPDATE git_checkpoints SET FingerprintSha256 = upper(FingerprintSha256);",
            Expectation.Unavailable),
        ["fingerprint_63_characters"] = (
            Completed,
            $"UPDATE {Operations} SET CheckpointFingerprintSha256 = substr(CheckpointFingerprintSha256, 1, 63); UPDATE git_checkpoints SET FingerprintSha256 = substr(FingerprintSha256, 1, 63);",
            Expectation.Unavailable),
        ["fingerprint_65_characters"] = (
            Completed,
            $"UPDATE {Operations} SET CheckpointFingerprintSha256 = CheckpointFingerprintSha256 || 'a'; UPDATE git_checkpoints SET FingerprintSha256 = FingerprintSha256 || 'a';",
            Expectation.Unavailable),
        ["completed_commit_uppercase"] = (Completed, $"UPDATE {Operations} SET CommitSha = upper(CommitSha)", Expectation.Unavailable),
        ["completed_commit_39_characters"] = (Completed, $"UPDATE {Operations} SET CommitSha = substr(CommitSha, 1, 39)", Expectation.Unavailable),
        ["completed_commit_41_characters"] = (Completed, $"UPDATE {Operations} SET CommitSha = CommitSha || 'a'", Expectation.Unavailable),
        ["completed_commit_blank"] = (Completed, $"UPDATE {Operations} SET CommitSha = ''", Expectation.Unavailable),

        // ---- times ----
        ["created_garbage"] = (Completed, $"UPDATE {Operations} SET CreatedAtUtc = 'garbage'", Expectation.Unavailable),
        ["created_without_offset"] = (Completed, $"UPDATE {Operations} SET CreatedAtUtc = '2026-10-06 12:00:00'", Expectation.Unavailable),
        ["created_with_a_nonzero_offset"] = (Completed, $"UPDATE {Operations} SET CreatedAtUtc = '2026-10-06 12:00:00+02:00'", Expectation.Unavailable),
        ["created_default"] = (Completed, $"UPDATE {Operations} SET CreatedAtUtc = '0001-01-01 00:00:00+00:00'", Expectation.Unavailable),
        ["created_iso_form"] = (Completed, $"UPDATE {Operations} SET CreatedAtUtc = '2026-10-06T12:00:00Z'", Expectation.Unavailable),
        ["created_eight_fraction_digits"] = (Completed, $"UPDATE {Operations} SET CreatedAtUtc = '2026-10-06 12:00:00.12345678+00:00'", Expectation.Unavailable),
        ["created_blob"] = (Completed, $"UPDATE {Operations} SET CreatedAtUtc = zeroblob(8)", Expectation.Unavailable),
        ["created_integer"] = (Completed, $"UPDATE {Operations} SET CreatedAtUtc = 5", Expectation.Unavailable),
        ["started_garbage"] = (Completed, $"UPDATE {Operations} SET ExecutionStartedAtUtc = 'garbage'", Expectation.Unavailable),
        ["completed_garbage"] = (Completed, $"UPDATE {Operations} SET CompletedAtUtc = 'garbage'", Expectation.Unavailable),
        ["completed_with_a_nonzero_offset"] = (Completed, $"UPDATE {Operations} SET CompletedAtUtc = '2026-10-06 12:00:09+01:00'", Expectation.Unavailable),
        ["executing_started_before_created"] = (Executing, $"UPDATE {Operations} SET ExecutionStartedAtUtc = '2026-10-06 11:59:59+00:00'", Expectation.Unavailable),
        ["completed_before_started"] = (Completed, $"UPDATE {Operations} SET CompletedAtUtc = '2026-10-06 12:00:04+00:00'", Expectation.Unavailable),
        ["completed_started_before_created"] = (Completed, $"UPDATE {Operations} SET ExecutionStartedAtUtc = '2026-10-06 11:59:59+00:00'", Expectation.Unavailable),
        ["failed_started_after_completion"] = (FailedStarted, $"UPDATE {Operations} SET ExecutionStartedAtUtc = '2026-10-06 12:00:10+00:00'", Expectation.Unavailable),
        ["failed_started_before_created"] = (FailedStarted, $"UPDATE {Operations} SET ExecutionStartedAtUtc = '2026-10-06 11:59:59+00:00'", Expectation.Unavailable),
        ["failed_completed_before_created"] = (FailedUnstarted, $"UPDATE {Operations} SET CompletedAtUtc = '2026-10-06 11:59:59+00:00'", Expectation.Unavailable),
        ["interrupted_started_after_completion"] = (InterruptedStarted, $"UPDATE {Operations} SET ExecutionStartedAtUtc = '2026-10-06 12:00:10+00:00'", Expectation.Unavailable),
        ["attention_started_before_created"] = (AttentionStarted, $"UPDATE {Operations} SET ExecutionStartedAtUtc = '2026-10-06 11:59:59+00:00'", Expectation.Unavailable),

        // ---- the complete status/time/reason tuple ----
        ["prepared_with_a_start"] = (Prepared, $"UPDATE {Operations} SET ExecutionStartedAtUtc = '2026-10-06 12:00:05+00:00'", Expectation.Unavailable),
        ["prepared_with_a_completion"] = (Prepared, $"UPDATE {Operations} SET CompletedAtUtc = '2026-10-06 12:00:09+00:00'", Expectation.Unavailable),
        ["prepared_with_a_reason"] = (Prepared, $"UPDATE {Operations} SET OutcomeReasonCode = 'local_commit.git_unprovable'", Expectation.Unavailable),
        ["executing_without_a_start"] = (Executing, $"UPDATE {Operations} SET ExecutionStartedAtUtc = NULL", Expectation.Unavailable),
        ["executing_with_a_completion"] = (Executing, $"UPDATE {Operations} SET CompletedAtUtc = '2026-10-06 12:00:09+00:00'", Expectation.Unavailable),
        ["executing_with_a_reason"] = (Executing, $"UPDATE {Operations} SET OutcomeReasonCode = 'local_commit.git_unprovable'", Expectation.Unavailable),
        ["completed_without_a_start"] = (Completed, $"UPDATE {Operations} SET ExecutionStartedAtUtc = NULL", Expectation.Unavailable),
        ["completed_without_a_completion"] = (Completed, $"UPDATE {Operations} SET CompletedAtUtc = NULL", Expectation.Unavailable),
        ["completed_with_a_reason"] = (Completed, $"UPDATE {Operations} SET OutcomeReasonCode = 'local_commit.git_unprovable'", Expectation.Unavailable),
        ["failed_without_a_completion"] = (FailedStarted, $"UPDATE {Operations} SET CompletedAtUtc = NULL", Expectation.Unavailable),
        ["failed_without_a_reason"] = (FailedStarted, $"UPDATE {Operations} SET OutcomeReasonCode = NULL", Expectation.Unavailable),
        ["failed_blank_reason"] = (FailedStarted, $"UPDATE {Operations} SET OutcomeReasonCode = ''", Expectation.Unavailable),
        ["failed_whitespace_reason"] = (FailedStarted, $"UPDATE {Operations} SET OutcomeReasonCode = '   '", Expectation.Unavailable),
        ["failed_reason_129_characters"] = (FailedStarted, $"UPDATE {Operations} SET OutcomeReasonCode = '{new string('r', 129)}'", Expectation.Unavailable),
        ["failed_reason_oversized_bytes"] = (FailedStarted, $"UPDATE {Operations} SET OutcomeReasonCode = '{new string('r', 400)}'", Expectation.Unavailable),
        ["interrupted_without_a_reason"] = (InterruptedUnstarted, $"UPDATE {Operations} SET OutcomeReasonCode = NULL", Expectation.Unavailable),
        ["interrupted_without_a_completion"] = (InterruptedUnstarted, $"UPDATE {Operations} SET CompletedAtUtc = NULL", Expectation.Unavailable),
        ["attention_with_a_completion"] = (AttentionStarted, $"UPDATE {Operations} SET CompletedAtUtc = '2026-10-06 12:00:09+00:00'", Expectation.Unavailable),
        ["attention_without_a_reason"] = (AttentionStarted, $"UPDATE {Operations} SET OutcomeReasonCode = NULL", Expectation.Unavailable),
        ["attention_blank_reason"] = (AttentionUnstarted, $"UPDATE {Operations} SET OutcomeReasonCode = ''", Expectation.Unavailable),
        ["status_unknown"] = (Completed, $"UPDATE {Operations} SET Status = 'Bogus'", Expectation.Unavailable),
        ["status_lowercase"] = (Completed, $"UPDATE {Operations} SET Status = 'completed'", Expectation.Unavailable),
        ["status_empty"] = (Completed, $"UPDATE {Operations} SET Status = ''", Expectation.Unavailable),
        ["status_numeric"] = (Completed, $"UPDATE {Operations} SET Status = 2", Expectation.Unavailable),
    };

    public static TheoryData<string> CaseNames => new(Cases.Keys);

    private static LocalCommitInspectionShape Completed => LocalCommitInspectionShape.Completed;

    private static LocalCommitInspectionShape Executing => LocalCommitInspectionShape.Executing;

    private static LocalCommitInspectionShape Prepared => LocalCommitInspectionShape.Prepared;

    private static LocalCommitInspectionShape FailedStarted => LocalCommitInspectionShape.FailedStarted;

    private static LocalCommitInspectionShape FailedUnstarted => LocalCommitInspectionShape.FailedUnstarted;

    private static LocalCommitInspectionShape InterruptedStarted => LocalCommitInspectionShape.InterruptedStarted;

    private static LocalCommitInspectionShape InterruptedUnstarted => LocalCommitInspectionShape.InterruptedUnstarted;

    private static LocalCommitInspectionShape AttentionStarted => LocalCommitInspectionShape.AttentionStarted;

    private static LocalCommitInspectionShape AttentionUnstarted => LocalCommitInspectionShape.AttentionUnstarted;

    [Theory]
    [MemberData(nameof(CaseNames))]
    public async Task One_damaged_stored_fact_gives_the_one_named_answer(string name)
    {
        var (shape, sql, expected) = Cases[name];
        var rows = await SeedAsync(shape);
        Assert.Equal(LocalCommitInspectionStatus.Recorded, (await InspectAsync(rows.Run.Id)).Status);

        var changed = await ExecuteAsync(Bind(sql, rows));
        if (!sql.Contains(';', StringComparison.Ordinal))
        {
            Assert.True(changed > 0, "The damage statement changed no row.");
        }

        var result = await TryInspectAsync(rows.Run.Id);

        switch (expected)
        {
            case Expectation.NotFound:
                Assert.True(result.IsFailure);
                Assert.Equal(LocalCommitInspectionErrors.NotFoundCode, result.Errors[0].Code);
                break;
            case Expectation.NotRecorded:
                Assert.Equal(LocalCommitInspectionStatus.NotRecorded, result.Value.Status);
                Assert.Null(result.Value.Operation);
                break;
            default:
                Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null);
                Assert.Equal(LocalCommitInspectionStatus.Unavailable, result.Value.Status);
                Assert.Null(result.Value.Operation);
                break;
        }
    }

    [Fact]
    public async Task An_operation_owned_by_another_registered_project_is_unavailable()
    {
        var rows = await SeedAsync(Completed);
        var other = await SeedAsync(LocalCommitInspectionShape.Prepared);
        Assert.Equal(1, await ExecuteAsync($"UPDATE {Operations} SET ProjectId = '{Id(other.Project.Id)}' WHERE Id = '{Id(rows.Operation.Id)}'"));

        var result = await InspectAsync(rows.Run.Id);

        Assert.Equal(LocalCommitInspectionStatus.Unavailable, result.Status);
        Assert.Equal(LocalCommitInspectionStatus.Recorded, (await InspectAsync(other.Run.Id)).Status);
    }

    [Fact]
    public async Task A_workspace_and_a_checkpoint_of_another_registered_project_are_unavailable()
    {
        var rows = await SeedAsync(Completed);
        var other = await SeedAsync(LocalCommitInspectionShape.Prepared);
        Assert.Equal(1, await ExecuteAsync(
            $"UPDATE git_workspaces SET ProjectId = '{Id(other.Project.Id)}', WorkspaceNumber = 99 WHERE Id = '{Id(rows.Workspace.Id)}'"));
        Assert.Equal(LocalCommitInspectionStatus.Unavailable, (await InspectAsync(rows.Run.Id)).Status);

        Assert.Equal(1, await ExecuteAsync(
            $"UPDATE git_workspaces SET ProjectId = '{Id(rows.Project.Id)}', WorkspaceNumber = 1 WHERE Id = '{Id(rows.Workspace.Id)}'"));
        Assert.Equal(1, await ExecuteAsync($"UPDATE {Operations} SET GitCheckpointId = '{Id(other.Checkpoint.Id)}' WHERE Id = '{Id(rows.Operation.Id)}'"));
        Assert.Equal(LocalCommitInspectionStatus.Unavailable, (await InspectAsync(rows.Run.Id)).Status);
    }

    [Fact]
    public async Task A_damaged_operation_of_another_run_does_not_change_this_runs_answer()
    {
        var rows = await SeedAsync(Completed);
        var other = await SeedAsync(LocalCommitInspectionShape.Executing);
        Assert.Equal(1, await ExecuteAsync($"UPDATE {Operations} SET Status = 'Bogus' WHERE Id = '{Id(other.Operation.Id)}'"));

        Assert.Equal(LocalCommitInspectionStatus.Recorded, (await InspectAsync(rows.Run.Id)).Status);
        Assert.Equal(LocalCommitInspectionStatus.Unavailable, (await InspectAsync(other.Run.Id)).Status);
    }

    private static string Bind(string sql, LocalCommitRows rows) => sql
        .Replace("{op}", Id(rows.Operation.Id), StringComparison.Ordinal)
        .Replace("{ws}", Id(rows.Workspace.Id), StringComparison.Ordinal)
        .Replace("{cp}", Id(rows.Checkpoint.Id), StringComparison.Ordinal)
        .Replace("{foreign}", $"'{Id(Guid.NewGuid())}'", StringComparison.Ordinal);
}
