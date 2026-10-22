using System.Data.Common;
using System.Text;
using System.Text.Json;
using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Runs.Policies.AgentAttemptVerification;
using DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptVerificationEvidence;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// ADR-0052 at the query boundary, over real file-backed SQLite with the production migrations: the recorded verification membership of
/// one CodeReviewer attempt is proved whole (identity, ownership, positions, terminal shapes) from bounded scalar reads before any member
/// is shown, malformed storage is refused instead of converted, history is read by recorded identity despite newer records, streams are
/// described without paths, hashes or bytes, and nothing is written or substituted.
/// </summary>
public sealed class GetAgentAttemptVerificationEvidenceQueryHandlerTests : IAsyncLifetime
{
    private const string Secret = "SECRET-SENTINEL-52a7";

    private readonly SqliteDatabaseFixture _fixture = new();
    private readonly RecordedVerificationSeed _seed;

    public GetAgentAttemptVerificationEvidenceQueryHandlerTests() => _seed = new RecordedVerificationSeed(_fixture);

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private sealed record RecordedCommand(string Text, IReadOnlyList<(string Name, object? Value)> Parameters);

    private sealed class RecordingInterceptor : DbCommandInterceptor
    {
        public List<RecordedCommand> Commands { get; } = [];

        private void Record(DbCommand command) => Commands.Add(new RecordedCommand(
            command.CommandText, command.Parameters.Cast<SqliteParameter>().Select(parameter => (parameter.ParameterName, parameter.Value)).ToArray()));

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Record(command);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Record(command);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
        {
            Record(command);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class FaultingInterceptor(string table) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains($"FROM \"{table}\"", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("injected database fault");
            }

            return ValueTask.FromResult(result);
        }
    }

    private async Task ExecuteAsync(FormattableString sql)
    {
        await using var dbContext = _fixture.CreateContext();
        await dbContext.Database.OpenConnectionAsync();
        await dbContext.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF");
        await dbContext.Database.ExecuteSqlInterpolatedAsync(sql);
    }

    private async Task<Result<GetAgentAttemptVerificationEvidenceQueryResult>> SendAsync(
        Guid runId, Guid attemptId, CancellationToken cancellationToken = default, params IInterceptor[] interceptors)
    {
        await using var dbContext = _fixture.CreateContext(interceptors);
        return await new GetAgentAttemptVerificationEvidenceQueryHandler(dbContext).HandleAsync(
            new GetAgentAttemptVerificationEvidenceQuery(runId, attemptId), cancellationToken);
    }

    private async Task<GetAgentAttemptVerificationEvidenceQueryResult> ReadAsync(SeededVerificationScene scene)
    {
        var result = await SendAsync(scene.RunId, scene.Attempt.Id);
        Assert.True(result.IsSuccess, string.Join(", ", result.IsSuccess ? [] : result.Errors.Select(error => error.Code)));
        return result.Value;
    }

    private static void AssertUnavailable(GetAgentAttemptVerificationEvidenceQueryResult result, SeededVerificationScene scene)
    {
        Assert.Equal(scene.RunId, result.RunId);
        Assert.Equal(scene.Attempt.Id, result.AttemptId);
        Assert.Equal(AgentAttemptVerificationEvidenceStatus.Unavailable, result.Status);
        Assert.Null(result.Attempt);
        Assert.Null(result.Checkpoint);
        Assert.Null(result.MemberTotalCount);
        Assert.Empty(result.Members);
        Assert.DoesNotContain(Secret, JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    private static void AssertMember(SeededVerificationMember expected, AgentAttemptVerificationMember member, int position, bool failed)
    {
        Assert.Equal(position, member.Sequence);
        Assert.Equal(expected.Command.Id, member.CommandId);
        Assert.Equal(expected.Execution.Id, member.ExecutionId);
        Assert.Equal(expected.Execution.ExecutionNumber, member.ExecutionNumber);
        Assert.Equal(expected.Execution.CommandName, member.CommandName);
        Assert.Equal(failed ? AgentAttemptVerificationMemberStatus.Failed : AgentAttemptVerificationMemberStatus.Passed, member.Status);
        Assert.Equal(failed ? 1 : 0, member.ExitCode);
        Assert.Equal(expected.Execution.ClaimedAtUtc, member.ClaimedAtUtc);
        Assert.Equal(expected.Execution.DispatchedAtUtc, member.DispatchedAtUtc);
        Assert.Equal(expected.Execution.CompletedAtUtc, member.CompletedAtUtc);
    }

    [Fact]
    public async Task An_ordinary_review_with_one_passed_member_is_recorded_with_the_immutable_header_and_checkpoint()
    {
        var scene = await _seed.SeedAsync(false, false);

        var result = await ReadAsync(scene);

        Assert.Equal(AgentAttemptVerificationEvidenceStatus.Recorded, result.Status);
        Assert.Equal(
            new AgentAttemptVerificationEvidenceHeader(
                scene.Attempt.Id, 1, AgentRole.CodeReviewer, AgentProvider.Codex, AgentResponseContract.ImplementationReview),
            result.Attempt);
        Assert.Equal(new AgentAttemptVerificationEvidenceCheckpoint(scene.Checkpoint.Id, 1), result.Checkpoint);
        Assert.Equal(1, result.MemberTotalCount);
        AssertMember(scene.Members[0], Assert.Single(result.Members), 0, failed: false);
        Assert.Equal(RecordedVerificationOutputDescriptor.NotRecorded, result.Members[0].StandardOutput);
        Assert.Equal(RecordedVerificationOutputDescriptor.NotRecorded, result.Members[0].StandardError);
    }

    [Fact]
    public async Task A_diagnosis_with_failed_and_passed_members_keeps_recorded_order_and_each_terminal_shape()
    {
        var scene = await _seed.SeedAsync(true, false, true, false, true);

        var result = await ReadAsync(scene);

        Assert.Equal(AgentAttemptVerificationEvidenceStatus.Recorded, result.Status);
        Assert.Equal(AgentResponseContract.VerificationDiagnosis, result.Attempt!.ResponseContract);
        Assert.Equal(4, result.MemberTotalCount);
        var failed = new[] { false, true, false, true };
        for (var index = 0; index < 4; index++)
        {
            AssertMember(scene.Members[index], result.Members[index], index, failed[index]);
        }
    }

    [Fact]
    public async Task A_diagnosis_without_a_failed_member_and_a_review_with_one_are_unavailable()
    {
        var allPassedDiagnosis = await _seed.SeedAsync(true, false, false);
        var failedReview = await _seed.SeedAsync(false, false, true);

        AssertUnavailable(await ReadAsync(allPassedDiagnosis), allPassedDiagnosis);
        AssertUnavailable(await ReadAsync(failedReview), failedReview);
    }

    [Fact]
    public async Task Thirty_two_members_are_recorded_and_a_thirty_third_makes_the_whole_set_unavailable_without_a_count()
    {
        var scene = await _seed.SeedAsync(false, new bool[32]);

        var recorded = await ReadAsync(scene);
        Assert.Equal(AgentAttemptVerificationEvidenceStatus.Recorded, recorded.Status);
        Assert.Equal(32, recorded.MemberTotalCount);
        Assert.Equal(Enumerable.Range(0, 32), recorded.Members.Select(member => member.Sequence));

        await using (var dbContext = _fixture.CreateContext())
        {
            var extra = await _seed.SeedMemberAsync(dbContext, scene.ProjectId, scene.Workspace, scene.Checkpoint, 33, failed: false);
            dbContext.AttemptVerificationEvidence.Add(RecordedVerificationSeed.RecordMembership(scene.Attempt.Id, extra, 32, diagnosis: false));
            await dbContext.SaveChangesAsync();
        }

        AssertUnavailable(await ReadAsync(scene), scene);
    }

    [Fact]
    public async Task Zero_memberships_are_unavailable_never_empty()
    {
        var scene = await _seed.SeedAsync(false, false);
        await ExecuteAsync($"DELETE FROM attempt_verification_evidence WHERE AttemptId = {scene.Attempt.Id}");

        AssertUnavailable(await ReadAsync(scene), scene);
    }

    private async Task<Guid> SeedOtherProjectAsync() => (await _seed.SeedRunAsync()).ProjectId;

    private Dictionary<string, Func<SeededVerificationScene, Task>> LastMemberDefects() => new()
    {
        ["status-unknown"] = scene => ExecuteAsync($"UPDATE verification_executions SET Status = {"Weird" + Secret} WHERE Id = {Last(scene).Execution.Id}"),
        ["status-source-changed"] = scene => ExecuteAsync($"UPDATE verification_executions SET Status = 'SourceChanged' WHERE Id = {Last(scene).Execution.Id}"),
        ["outcome-timed-out"] = scene => ExecuteAsync($"UPDATE verification_executions SET Outcome = 'TimedOut' WHERE Id = {Last(scene).Execution.Id}"),
        ["outcome-unknown"] = scene => ExecuteAsync($"UPDATE verification_executions SET Outcome = {"Weird" + Secret} WHERE Id = {Last(scene).Execution.Id}"),
        ["outcome-null"] = scene => ExecuteAsync($"UPDATE verification_executions SET Outcome = NULL WHERE Id = {Last(scene).Execution.Id}"),
        ["passed-with-nonzero-exit"] = scene => ExecuteAsync($"UPDATE verification_executions SET ExitCode = 3 WHERE Id = {Last(scene).Execution.Id}"),
        ["exit-text"] = scene => ExecuteAsync($"UPDATE verification_executions SET ExitCode = {Secret} WHERE Id = {Last(scene).Execution.Id}"),
        ["exit-real"] = scene => ExecuteAsync($"UPDATE verification_executions SET Status = 'Failed', ExitCode = 1.5 WHERE Id = {Last(scene).Execution.Id}"),
        ["exit-beyond-int32"] = scene => ExecuteAsync($"UPDATE verification_executions SET Status = 'Failed', ExitCode = 4294967297 WHERE Id = {Last(scene).Execution.Id}"),
        ["exit-above-int32-maximum"] = scene => ExecuteAsync($"UPDATE verification_executions SET Status = 'Failed', ExitCode = 2147483648 WHERE Id = {Last(scene).Execution.Id}"),
        ["exit-below-int32-minimum"] = scene => ExecuteAsync($"UPDATE verification_executions SET Status = 'Failed', ExitCode = -2147483649 WHERE Id = {Last(scene).Execution.Id}"),
        ["exit-real-beside-negated-maximum"] = scene => ExecuteAsync($"UPDATE verification_executions SET Status = 'Failed', ExitCode = -2147483646.5 WHERE Id = {Last(scene).Execution.Id}"),
        ["exit-real-below-negated-maximum"] = scene => ExecuteAsync($"UPDATE verification_executions SET Status = 'Failed', ExitCode = -2147483647.5 WHERE Id = {Last(scene).Execution.Id}"),
        ["exit-real-below-maximum"] = scene => ExecuteAsync($"UPDATE verification_executions SET Status = 'Failed', ExitCode = 2147483646.5 WHERE Id = {Last(scene).Execution.Id}"),
        ["exit-blob"] = scene => ExecuteAsync($"UPDATE verification_executions SET Status = 'Failed', ExitCode = X'0102' WHERE Id = {Last(scene).Execution.Id}"),
        ["exit-null"] = scene => ExecuteAsync($"UPDATE verification_executions SET ExitCode = NULL WHERE Id = {Last(scene).Execution.Id}"),
        ["dispatched-missing"] = scene => ExecuteAsync($"UPDATE verification_executions SET DispatchedAtUtc = NULL WHERE Id = {Last(scene).Execution.Id}"),
        ["completed-missing"] = scene => ExecuteAsync($"UPDATE verification_executions SET CompletedAtUtc = NULL WHERE Id = {Last(scene).Execution.Id}"),
        ["command-differs"] = scene => ExecuteAsync($"UPDATE verification_executions SET VerificationCommandId = {Guid.NewGuid()} WHERE Id = {Last(scene).Execution.Id}"),
        ["project-foreign"] = async scene =>
        {
            var other = await SeedOtherProjectAsync();
            await ExecuteAsync($"UPDATE verification_executions SET ProjectId = {other} WHERE Id = {Last(scene).Execution.Id}");
        },
        ["workspace-foreign"] = scene => ExecuteAsync($"UPDATE verification_executions SET GitWorkspaceId = {Guid.NewGuid()} WHERE Id = {Last(scene).Execution.Id}"),
        ["checkpoint-foreign"] = scene => ExecuteAsync($"UPDATE verification_executions SET GitCheckpointId = {Guid.NewGuid()} WHERE Id = {Last(scene).Execution.Id}"),
        ["claim-fingerprint-differs"] = scene => ExecuteAsync($"UPDATE verification_executions SET CheckpointFingerprintSha256 = {new string('b', 64)} WHERE Id = {Last(scene).Execution.Id}"),
        ["claim-fingerprint-oversized"] = scene => ExecuteAsync($"UPDATE verification_executions SET CheckpointFingerprintSha256 = {new string('a', 65)} WHERE Id = {Last(scene).Execution.Id}"),
        ["claim-fingerprint-nul-hidden"] = scene => ExecuteAsync($"UPDATE verification_executions SET CheckpointFingerprintSha256 = {new string('a', 64)} || char(0) || {Secret} WHERE Id = {Last(scene).Execution.Id}"),
        ["completion-fingerprint-differs"] = scene => ExecuteAsync($"UPDATE verification_executions SET CompletionFingerprintSha256 = {new string('b', 64)} WHERE Id = {Last(scene).Execution.Id}"),
        ["completion-fingerprint-null"] = scene => ExecuteAsync($"UPDATE verification_executions SET CompletionFingerprintSha256 = NULL WHERE Id = {Last(scene).Execution.Id}"),
        ["number-zero"] = scene => ExecuteAsync($"UPDATE verification_executions SET ExecutionNumber = 0 WHERE Id = {Last(scene).Execution.Id}"),
        ["number-text"] = scene => ExecuteAsync($"UPDATE verification_executions SET ExecutionNumber = {Secret} WHERE Id = {Last(scene).Execution.Id}"),
        ["number-real"] = scene => ExecuteAsync($"UPDATE verification_executions SET ExecutionNumber = 7.5 WHERE Id = {Last(scene).Execution.Id}"),
        ["number-beyond-int32"] = scene => ExecuteAsync($"UPDATE verification_executions SET ExecutionNumber = 4294967297 WHERE Id = {Last(scene).Execution.Id}"),
        ["name-blank"] = scene => ExecuteAsync($"UPDATE verification_executions SET CommandName = '   ' WHERE Id = {Last(scene).Execution.Id}"),
        ["name-201-units"] = scene => ExecuteAsync($"UPDATE verification_executions SET CommandName = {new string('n', 201)} WHERE Id = {Last(scene).Execution.Id}"),
        ["name-over-600-bytes"] = scene => ExecuteAsync($"UPDATE verification_executions SET CommandName = {new string('n', 601)} WHERE Id = {Last(scene).Execution.Id}"),
        ["name-nul-hidden"] = scene => ExecuteAsync($"UPDATE verification_executions SET CommandName = 'ok' || char(0) || {new string('n', 700)} WHERE Id = {Last(scene).Execution.Id}"),
        ["execution-missing"] = scene => ExecuteAsync($"DELETE FROM verification_executions WHERE Id = {Last(scene).Execution.Id}"),
        ["position-beyond-bound"] = scene => ExecuteAsync($"UPDATE attempt_verification_evidence SET Sequence = 100 WHERE VerificationExecutionId = {Last(scene).Execution.Id}"),
        ["position-negative"] = scene => ExecuteAsync($"UPDATE attempt_verification_evidence SET Sequence = -1 WHERE VerificationExecutionId = {Last(scene).Execution.Id}"),
        ["position-text"] = scene => ExecuteAsync($"UPDATE attempt_verification_evidence SET Sequence = {Secret} WHERE VerificationExecutionId = {Last(scene).Execution.Id}"),
        ["position-real"] = scene => ExecuteAsync($"UPDATE attempt_verification_evidence SET Sequence = 2.5 WHERE VerificationExecutionId = {Last(scene).Execution.Id}"),
        ["position-blob"] = scene => ExecuteAsync($"UPDATE attempt_verification_evidence SET Sequence = X'0102' WHERE VerificationExecutionId = {Last(scene).Execution.Id}"),
        ["membership-command-empty"] = scene => ExecuteAsync($"UPDATE attempt_verification_evidence SET VerificationCommandId = {Guid.Empty} WHERE VerificationExecutionId = {Last(scene).Execution.Id}"),
    };

    private static SeededVerificationMember Last(SeededVerificationScene scene) => scene.Members[^1];

    public static TheoryData<string> LastMemberDefectNames() => new(
        "status-unknown", "status-source-changed", "outcome-timed-out", "outcome-unknown", "outcome-null", "passed-with-nonzero-exit",
        "exit-text", "exit-real", "exit-beyond-int32", "exit-above-int32-maximum", "exit-below-int32-minimum",
        "exit-real-beside-negated-maximum", "exit-real-below-negated-maximum", "exit-real-below-maximum", "exit-blob", "exit-null", "dispatched-missing", "completed-missing", "command-differs",
        "project-foreign", "workspace-foreign", "checkpoint-foreign", "claim-fingerprint-differs", "claim-fingerprint-oversized",
        "claim-fingerprint-nul-hidden", "completion-fingerprint-differs", "completion-fingerprint-null", "number-zero", "number-text",
        "number-real", "number-beyond-int32", "name-blank", "name-201-units", "name-over-600-bytes", "name-nul-hidden",
        "execution-missing", "position-beyond-bound", "position-negative", "position-text", "position-real", "position-blob",
        "membership-command-empty");

    [Theory]
    [MemberData(nameof(LastMemberDefectNames))]
    public async Task A_defect_in_only_the_last_of_thirty_two_members_makes_the_whole_set_unavailable(string defect)
    {
        // Diagnosis shape so both a Failed and a Passed member are admissible; the damaged member is the last, after 31 coherent ones.
        var failed = new bool[32];
        failed[0] = true;
        var scene = await _seed.SeedAsync(true, failed);
        Assert.Equal(AgentAttemptVerificationEvidenceStatus.Recorded, (await ReadAsync(scene)).Status);

        await LastMemberDefects()[defect](scene);

        AssertUnavailable(await ReadAsync(scene), scene);
    }

    /// <summary>Every Int32 boundary of the exact-integer guard and its neighbors, including the three values that are not their own
    /// remainder modulo int.MaxValue (int.MinValue, -int.MaxValue and int.MaxValue).</summary>
    public static TheoryData<int> FailedExitCodes() => new(
        int.MinValue, -int.MaxValue, -int.MaxValue + 1, -2, -1, 1, 2, int.MaxValue - 1, int.MaxValue);

    [Theory]
    [MemberData(nameof(FailedExitCodes))]
    public async Task Every_nonzero_int32_exit_code_of_a_failed_diagnosis_member_is_recorded_exactly_with_its_stream(int exitCode)
    {
        var scene = await _seed.SeedAsync(true, true, false);
        var failed = scene.Members[0].Execution.Id;
        await ExecuteAsync($"UPDATE verification_executions SET ExitCode = {exitCode} WHERE Id = {failed}");
        var stdout = await _seed.AddOutputAsync(failed, VerificationOutputPurpose.StandardOutput, "a/out.sealed", RecordedVerificationSeed.ContentHash(), 12);

        var result = await ReadAsync(scene);

        Assert.Equal(AgentAttemptVerificationEvidenceStatus.Recorded, result.Status);
        Assert.Equal(2, result.MemberTotalCount);
        Assert.Equal(AgentAttemptVerificationMemberStatus.Failed, result.Members[0].Status);
        Assert.Equal(exitCode, result.Members[0].ExitCode);
        Assert.Equal(
            new RecordedVerificationOutputDescriptor(
                RecordedVerificationOutputDescriptorStatus.Recorded, stdout.Id, 12, VerificationOutputCaptureOutcome.CapturedWithKnownTruncation, false),
            result.Members[0].StandardOutput);
        AssertMember(scene.Members[1], result.Members[1], 1, failed: false);
    }

    public static TheoryData<string> CheckpointAndAttemptDefectNames() => new(
        "attempt-workspace-null", "attempt-checkpoint-null", "attempt-fingerprint-null", "attempt-fingerprint-differs",
        "attempt-fingerprint-oversized", "attempt-workspace-empty", "checkpoint-missing", "checkpoint-workspace-differs",
        "checkpoint-workspace-missing", "workspace-project-foreign", "checkpoint-number-zero", "checkpoint-number-text",
        "checkpoint-number-real", "checkpoint-number-beyond-int32", "checkpoint-fingerprint-differs", "checkpoint-fingerprint-oversized");

    [Theory]
    [MemberData(nameof(CheckpointAndAttemptDefectNames))]
    public async Task Damaged_attempt_workspace_or_checkpoint_ownership_makes_the_whole_set_unavailable(string defect)
    {
        var scene = await _seed.SeedAsync(false, false, false);
        var attemptId = scene.Attempt.Id;
        var checkpointId = scene.Checkpoint.Id;
        var workspaceId = scene.Workspace.Id;
        var otherProject = await SeedOtherProjectAsync();

        FormattableString update = defect switch
        {
            "attempt-workspace-null" => $"UPDATE attempts SET AgentGitWorkspaceId = NULL WHERE Id = {attemptId}",
            "attempt-checkpoint-null" => $"UPDATE attempts SET AgentGitCheckpointId = NULL WHERE Id = {attemptId}",
            "attempt-fingerprint-null" => $"UPDATE attempts SET AgentCheckpointFingerprintSha256 = NULL WHERE Id = {attemptId}",
            "attempt-fingerprint-differs" => $"UPDATE attempts SET AgentCheckpointFingerprintSha256 = {new string('b', 64)} WHERE Id = {attemptId}",
            "attempt-fingerprint-oversized" => $"UPDATE attempts SET AgentCheckpointFingerprintSha256 = {new string('a', 65)} WHERE Id = {attemptId}",
            "attempt-workspace-empty" => $"UPDATE attempts SET AgentGitWorkspaceId = {Guid.Empty} WHERE Id = {attemptId}",
            "checkpoint-missing" => $"UPDATE attempts SET AgentGitCheckpointId = {Guid.NewGuid()} WHERE Id = {attemptId}",
            "checkpoint-workspace-differs" => $"UPDATE attempts SET AgentGitWorkspaceId = {Guid.NewGuid()} WHERE Id = {attemptId}",
            "checkpoint-workspace-missing" => $"DELETE FROM git_workspaces WHERE Id = {workspaceId}",
            "workspace-project-foreign" => $"UPDATE git_workspaces SET ProjectId = {otherProject} WHERE Id = {workspaceId}",
            "checkpoint-number-zero" => $"UPDATE git_checkpoints SET CheckpointNumber = 0 WHERE Id = {checkpointId}",
            "checkpoint-number-text" => $"UPDATE git_checkpoints SET CheckpointNumber = {Secret} WHERE Id = {checkpointId}",
            "checkpoint-number-real" => $"UPDATE git_checkpoints SET CheckpointNumber = 1.5 WHERE Id = {checkpointId}",
            "checkpoint-number-beyond-int32" => $"UPDATE git_checkpoints SET CheckpointNumber = 4294967297 WHERE Id = {checkpointId}",
            "checkpoint-fingerprint-differs" => $"UPDATE git_checkpoints SET FingerprintSha256 = {new string('b', 64)} WHERE Id = {checkpointId}",
            "checkpoint-fingerprint-oversized" => $"UPDATE git_checkpoints SET FingerprintSha256 = {new string('a', 65)} WHERE Id = {checkpointId}",
            _ => throw new InvalidOperationException(defect),
        };
        await ExecuteAsync(update);

        AssertUnavailable(await ReadAsync(scene), scene);
    }

    [Fact]
    public async Task A_gap_inside_the_position_range_makes_the_whole_set_unavailable()
    {
        var scene = await _seed.SeedAsync(false, false, false, false);
        await ExecuteAsync($"UPDATE attempt_verification_evidence SET Sequence = 5 WHERE VerificationExecutionId = {Last(scene).Execution.Id}");

        AssertUnavailable(await ReadAsync(scene), scene);
    }

    [Fact]
    public async Task Only_a_code_reviewer_with_a_supported_contract_and_a_provable_identity_is_inspected()
    {
        var planner = await _seed.SeedRunAsync();
        var plannerScene = await _seed.SeedAsync(planner, false, 1, false);
        await ExecuteAsync($"UPDATE attempts SET AgentRole = 'Planner' WHERE Id = {plannerScene.Attempt.Id}");

        var corrupt = await _seed.SeedAsync(false, false);
        await ExecuteAsync($"UPDATE attempts SET AgentRole = {"Overlord" + Secret} WHERE Id = {corrupt.Attempt.Id}");

        var resolverContract = await _seed.SeedAsync(false, false);
        await ExecuteAsync($"UPDATE attempts SET AgentResponseContract = 'ChallengeResolution' WHERE Id = {resolverContract.Attempt.Id}");

        AssertUnavailable(await ReadAsync(plannerScene), plannerScene);
        AssertUnavailable(await ReadAsync(corrupt), corrupt);
        AssertUnavailable(await ReadAsync(resolverContract), resolverContract);
    }

    [Fact]
    public async Task An_unknown_run_and_a_missing_foreign_or_non_agent_attempt_are_not_found()
    {
        var scene = await _seed.SeedAsync(false, false);
        var otherRun = await _seed.SeedRunAsync();
        var simulated = await _seed.SeedRunAsync();
        var processAttempt = Attempt.Claim(Guid.NewGuid(), simulated.RunId, 1, RecordedVerificationSeed.Now);
        await using (var dbContext = _fixture.CreateContext())
        {
            dbContext.Attempts.Add(processAttempt);
            await dbContext.SaveChangesAsync();
        }

        foreach (var (runId, attemptId) in new[]
        {
            (Guid.NewGuid(), scene.Attempt.Id), (scene.RunId, Guid.NewGuid()), (otherRun.RunId, scene.Attempt.Id), (simulated.RunId, processAttempt.Id),
        })
        {
            var result = await SendAsync(runId, attemptId);
            Assert.True(result.IsFailure);
            Assert.Equal(AgentAttemptVerificationErrors.NotFoundCode, Assert.Single(result.Errors).Code);
        }
    }

    [Fact]
    public async Task Ownership_is_the_recorded_history_despite_newer_checkpoints_runs_executions_and_edited_recipes()
    {
        var scene = await _seed.SeedAsync(false, false, false);
        var expected = await ReadAsync(scene);

        await using (var dbContext = _fixture.CreateContext())
        {
            // A newer checkpoint of the same workspace, newer executions of the same recipes on it, a newer run of the project and an
            // edited, disabled recipe: none may replace the recorded membership, its names, numbers or checkpoint.
            var newer = GitCheckpoint.Capture(Guid.NewGuid(), scene.Workspace.Id, 2, RecordedVerificationSeed.Now.AddHours(1),
                RecordedVerificationSeed.Sha, new string('d', 64), []);
            dbContext.GitCheckpoints.Add(newer);
            await dbContext.SaveChangesAsync();
            for (var index = 0; index < scene.Members.Count; index++)
            {
                var command = scene.Members[index].Command;
                var execution = VerificationExecution.Claim(
                    Guid.NewGuid(), scene.ProjectId, 100 + index, scene.Workspace, newer, command, RecordedVerificationSeed.Now.AddHours(2));
                execution.MarkDispatched(RecordedVerificationSeed.Now.AddHours(2).AddSeconds(1));
                execution.Complete(VerificationExecutionOutcome.Exited, 1, newer.FingerprintSha256, RecordedVerificationSeed.Now.AddHours(2).AddSeconds(2));
                dbContext.VerificationExecutions.Add(execution);
                command.Update("Renamed after the fact", @"C:\tools\other.exe", ["--changed"], 30, false, RecordedVerificationSeed.Now.AddHours(3));
            }

            await dbContext.SaveChangesAsync();
        }

        await _seed.SeedAsync((scene.ProjectId, (await _seed.SeedRunAsync()).RunId), false, 1, false);

        var actual = await ReadAsync(scene);

        Assert.Equal(expected.Checkpoint, actual.Checkpoint);
        Assert.Equal(expected.Members.Select(member => (member.ExecutionId, member.ExecutionNumber, member.CommandName)),
            actual.Members.Select(member => (member.ExecutionId, member.ExecutionNumber, member.CommandName)));
        Assert.All(actual.Members, member => Assert.StartsWith("Command ", member.CommandName, StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_workspace_need_not_be_active_the_checkpoint_latest_or_any_lease_current()
    {
        var scene = await _seed.SeedAsync(false, false);
        await ExecuteAsync($"UPDATE git_workspaces SET Status = 'MissingExternally' WHERE Id = {scene.Workspace.Id}");

        Assert.Equal(AgentAttemptVerificationEvidenceStatus.Recorded, (await ReadAsync(scene)).Status);
    }

    [Fact]
    public async Task Streams_are_described_independently_and_a_bad_one_hides_neither_its_sibling_nor_the_members()
    {
        var scene = await _seed.SeedAsync(true, true, false);
        var failed = scene.Members[0].Execution.Id;
        var passed = scene.Members[1].Execution.Id;
        var hash = RecordedVerificationSeed.ContentHash();
        var stdout = await _seed.AddOutputAsync(failed, VerificationOutputPurpose.StandardOutput, "a/out.sealed", hash, 12, truncated: true);
        var stderr = await _seed.AddOutputAsync(
            failed, VerificationOutputPurpose.StandardError, "a/err.sealed", hash, 0, truncated: null,
            VerificationOutputCaptureOutcome.RecoveredAfterHostInterruption);
        await _seed.AddOutputAsync(passed, VerificationOutputPurpose.StandardOutput, "b/out.sealed", hash, 5, truncated: false);
        var brokenError = await _seed.AddOutputAsync(passed, VerificationOutputPurpose.StandardError, "b/err.sealed", hash, 5, truncated: false);
        await ExecuteAsync($"UPDATE verification_output_artifacts SET CaptureOutcome = {"Weird" + Secret} WHERE Id = {brokenError.Id}");

        var result = await ReadAsync(scene);

        Assert.Equal(AgentAttemptVerificationEvidenceStatus.Recorded, result.Status);
        Assert.Equal(
            new RecordedVerificationOutputDescriptor(
                RecordedVerificationOutputDescriptorStatus.Recorded, stdout.Id, 12, VerificationOutputCaptureOutcome.CapturedWithKnownTruncation, true),
            result.Members[0].StandardOutput);
        Assert.Equal(
            new RecordedVerificationOutputDescriptor(
                RecordedVerificationOutputDescriptorStatus.Recorded, stderr.Id, 0,
                VerificationOutputCaptureOutcome.RecoveredAfterHostInterruption, null),
            result.Members[0].StandardError);
        Assert.Equal(RecordedVerificationOutputDescriptorStatus.Recorded, result.Members[1].StandardOutput.Status);
        Assert.False(result.Members[1].StandardOutput.Truncated);
        Assert.Equal(RecordedVerificationOutputDescriptor.Unavailable, result.Members[1].StandardError);
    }

    [Theory]
    [InlineData("capture-unknown")]
    [InlineData("length-negative")]
    [InlineData("length-beyond-safe-integer")]
    [InlineData("length-text")]
    [InlineData("length-real")]
    [InlineData("known-without-truncation")]
    [InlineData("recovered-with-truncation")]
    [InlineData("truncated-text")]
    [InlineData("truncated-two")]
    [InlineData("truncated-negative-one")]
    [InlineData("truncated-seven")]
    [InlineData("truncated-real")]
    [InlineData("truncated-blob")]
    [InlineData("truncated-oversized")]
    [InlineData("artifact-empty")]
    public async Task A_stream_whose_metadata_cannot_be_admitted_is_unavailable_with_no_other_field(string defect)
    {
        var scene = await _seed.SeedAsync(false, false);
        var execution = scene.Members[0].Execution.Id;
        var output = await _seed.AddOutputAsync(execution, VerificationOutputPurpose.StandardOutput, "a/out.sealed",
            RecordedVerificationSeed.ContentHash(), 5);
        var id = output.Id;
        FormattableString update = defect switch
        {
            "capture-unknown" => $"UPDATE verification_output_artifacts SET CaptureOutcome = 'Weird' WHERE Id = {id}",
            "length-negative" => $"UPDATE verification_output_artifacts SET ByteLength = -1 WHERE Id = {id}",
            "length-beyond-safe-integer" => $"UPDATE verification_output_artifacts SET ByteLength = 9007199254740992 WHERE Id = {id}",
            "length-text" => $"UPDATE verification_output_artifacts SET ByteLength = {Secret} WHERE Id = {id}",
            "length-real" => $"UPDATE verification_output_artifacts SET ByteLength = 5.5 WHERE Id = {id}",
            "known-without-truncation" => $"UPDATE verification_output_artifacts SET Truncated = NULL WHERE Id = {id}",
            "recovered-with-truncation" => $"UPDATE verification_output_artifacts SET CaptureOutcome = 'RecoveredAfterHostInterruption' WHERE Id = {id}",
            "truncated-text" => $"UPDATE verification_output_artifacts SET Truncated = {Secret} WHERE Id = {id}",
            "truncated-two" => $"UPDATE verification_output_artifacts SET Truncated = 2 WHERE Id = {id}",
            "truncated-negative-one" => $"UPDATE verification_output_artifacts SET Truncated = -1 WHERE Id = {id}",
            "truncated-seven" => $"UPDATE verification_output_artifacts SET Truncated = 7 WHERE Id = {id}",
            "truncated-real" => $"UPDATE verification_output_artifacts SET Truncated = 1.5 WHERE Id = {id}",
            "truncated-blob" => $"UPDATE verification_output_artifacts SET Truncated = X'0102' WHERE Id = {id}",
            "truncated-oversized" => $"UPDATE verification_output_artifacts SET Truncated = {new string('1', 300)} WHERE Id = {id}",
            "artifact-empty" => $"UPDATE verification_output_artifacts SET Id = {Guid.Empty} WHERE Id = {id}",
            _ => throw new InvalidOperationException(defect),
        };
        await ExecuteAsync(update);

        var result = await ReadAsync(scene);

        Assert.Equal(AgentAttemptVerificationEvidenceStatus.Recorded, result.Status);
        Assert.Equal(RecordedVerificationOutputDescriptor.Unavailable, Assert.Single(result.Members).StandardOutput);
        Assert.Equal(RecordedVerificationOutputDescriptor.NotRecorded, result.Members[0].StandardError);
        Assert.DoesNotContain(Secret, JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public async Task The_canonical_stored_truncation_values_are_recorded_exactly(int stored, bool expected)
    {
        var scene = await _seed.SeedAsync(false, false);
        var output = await _seed.AddOutputAsync(
            scene.Members[0].Execution.Id, VerificationOutputPurpose.StandardOutput, "a/out.sealed", RecordedVerificationSeed.ContentHash(), 5);
        await ExecuteAsync($"UPDATE verification_output_artifacts SET Truncated = {stored} WHERE Id = {output.Id}");

        var descriptor = (await ReadAsync(scene)).Members[0].StandardOutput;

        Assert.Equal(RecordedVerificationOutputDescriptorStatus.Recorded, descriptor.Status);
        Assert.Equal(expected, descriptor.Truncated);
    }

    [Fact]
    public async Task An_unknown_stored_purpose_is_never_read_as_a_supported_stream_and_no_path_or_hash_is_disclosed()
    {
        var scene = await _seed.SeedAsync(false, false);
        var execution = scene.Members[0].Execution.Id;
        var output = await _seed.AddOutputAsync(execution, VerificationOutputPurpose.StandardOutput, $"{Secret}/out.sealed",
            RecordedVerificationSeed.ContentHash('9'), 5);
        await ExecuteAsync($"UPDATE verification_output_artifacts SET Purpose = 'Weird' WHERE Id = {output.Id}");
        await _seed.AddOutputAsync(execution, VerificationOutputPurpose.StandardError, $"{Secret}/err.sealed",
            RecordedVerificationSeed.ContentHash('8'), 7);

        var result = await ReadAsync(scene);

        Assert.Equal(RecordedVerificationOutputDescriptor.NotRecorded, result.Members[0].StandardOutput);
        Assert.Equal(RecordedVerificationOutputDescriptorStatus.Recorded, result.Members[0].StandardError.Status);
        var text = JsonSerializer.Serialize(result);
        foreach (var hidden in new[] { Secret, new string('9', 64), new string('8', 64), "sha256", "--secret-argument", "verify.exe", "C:\\workspaces" })
        {
            Assert.DoesNotContain(hidden, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Every_read_is_resolved_by_an_index_or_primary_key_without_scanning_or_sorting_a_table()
    {
        var scene = await _seed.SeedAsync(true, true, false, false);
        var interceptor = new RecordingInterceptor();
        Assert.True((await SendAsync(scene.RunId, scene.Attempt.Id, default, interceptor)).IsSuccess);

        await using var connection = new SqliteConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        var plans = new List<string>();
        foreach (var command in interceptor.Commands)
        {
            await using var explain = connection.CreateCommand();
#pragma warning disable CA2100 // The text is the handler's own EF-generated SELECT, captured from the disposable test database.
            explain.CommandText = "EXPLAIN QUERY PLAN " + command.Text;
#pragma warning restore CA2100
            foreach (var (name, value) in command.Parameters)
            {
                explain.Parameters.AddWithValue(name, value ?? DBNull.Value);
            }

            await using var reader = await explain.ExecuteReaderAsync();
            var plan = new StringBuilder();
            while (await reader.ReadAsync())
            {
                plan.AppendLine(reader.GetString(3));
            }

            plans.Add(plan.ToString());
        }

        var all = string.Join('\n', plans);
        // Every plan line is a SEARCH by index or primary key (an alias names the table), never a table SCAN or a temporary sort.
        Assert.DoesNotContain("SCAN", all, StringComparison.Ordinal);
        Assert.DoesNotContain("TEMP B-TREE", all, StringComparison.Ordinal);
        Assert.Contains("USING INDEX IX_attempt_verification_evidence_AttemptId_Sequence", all, StringComparison.Ordinal);
        Assert.All(
            all.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Trim()),
            line => Assert.True(line.StartsWith("SEARCH", StringComparison.Ordinal) || line.Contains("SUBQUERY", StringComparison.Ordinal), line));
    }

    [Fact]
    public async Task The_read_issues_a_fixed_number_of_bounded_untracked_scalar_queries_and_never_writes()
    {
        var small = await _seed.SeedAsync(false, false);
        var large = await _seed.SeedAsync(false, new bool[32]);
        var smallInterceptor = new RecordingInterceptor();
        var largeInterceptor = new RecordingInterceptor();

        Assert.True((await SendAsync(small.RunId, small.Attempt.Id, default, smallInterceptor)).IsSuccess);
        Assert.True((await SendAsync(large.RunId, large.Attempt.Id, default, largeInterceptor)).IsSuccess);

        Assert.Equal(6, smallInterceptor.Commands.Count);
        Assert.Equal(smallInterceptor.Commands.Count, largeInterceptor.Commands.Count);
        foreach (var command in smallInterceptor.Commands.Concat(largeInterceptor.Commands))
        {
            Assert.StartsWith("SELECT", command.Text.TrimStart(), StringComparison.Ordinal);
            foreach (var forbidden in new[]
            {
                "INSERT", "UPDATE ", "DELETE", "SnapshotSha256", "\"Arguments\"", "WorkspacePath", "ExecutablePath", "TimeoutSeconds",
                "RelativeStoragePath", "ContentHash", "HeadCommitSha", "BranchName", "SourceCommitSha", "AgentProviderSessionId",
                "AgentContextManifestArtifactId", "VerificationCommands", "verification_commands",
            })
            {
                Assert.DoesNotContain(forbidden, command.Text, StringComparison.Ordinal);
            }
        }

        var memberships = Assert.Single(largeInterceptor.Commands, command => command.Text.Contains("FROM \"attempt_verification_evidence\"", StringComparison.Ordinal));
        Assert.Contains("ORDER BY", memberships.Text, StringComparison.Ordinal);
        Assert.Contains("LIMIT", memberships.Text, StringComparison.Ordinal);
        Assert.Contains(memberships.Parameters, parameter => Equals(parameter.Value, 33) || Equals(parameter.Value, 33L));
        var outputs = Assert.Single(largeInterceptor.Commands, command => command.Text.Contains("FROM \"verification_output_artifacts\"", StringComparison.Ordinal));
        Assert.Contains(outputs.Parameters, parameter => Equals(parameter.Value, 65) || Equals(parameter.Value, 65L));
    }

    [Theory]
    [InlineData("attempts")]
    [InlineData("git_checkpoints")]
    [InlineData("attempt_verification_evidence")]
    [InlineData("verification_executions")]
    [InlineData("verification_output_artifacts")]
    public async Task A_database_fault_in_any_read_propagates_and_is_never_turned_into_an_availability_state(string table)
    {
        var scene = await _seed.SeedAsync(false, false);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => SendAsync(scene.RunId, scene.Attempt.Id, default, new FaultingInterceptor(table)));
    }

    [Fact]
    public async Task Cancellation_propagates()
    {
        var scene = await _seed.SeedAsync(false, false);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SendAsync(scene.RunId, scene.Attempt.Id, cancelled.Token));
    }

    [Fact]
    public async Task Reading_the_evidence_changes_no_stored_row()
    {
        var scene = await _seed.SeedAsync(true, true, false);
        async Task<string> SnapshotAsync()
        {
            await using var dbContext = _fixture.CreateContext();
            var attempts = await dbContext.Attempts.AsNoTracking().Select(row => row.Id + "|" + row.Status).ToListAsync();
            var executions = await dbContext.VerificationExecutions.AsNoTracking().OrderBy(row => row.Id)
                .Select(row => row.Id + "|" + row.Status + "|" + row.ExitCode + "|" + row.CommandName).ToListAsync();
            var members = await dbContext.AttemptVerificationEvidence.AsNoTracking().OrderBy(row => row.Id)
                .Select(row => row.Id + "|" + row.Sequence + "|" + row.SnapshotSha256).ToListAsync();
            return string.Join(";", attempts) + "#" + string.Join(";", executions) + "#" + string.Join(";", members) + "#"
                + await dbContext.Events.CountAsync() + "#" + await dbContext.Artifacts.CountAsync() + "#" + await dbContext.VerificationOutputArtifacts.CountAsync();
        }

        var before = await SnapshotAsync();
        await ReadAsync(scene);
        await SendAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(before, await SnapshotAsync());
    }
}
