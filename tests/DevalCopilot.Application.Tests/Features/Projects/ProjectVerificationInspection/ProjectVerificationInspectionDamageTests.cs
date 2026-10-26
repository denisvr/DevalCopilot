using DevalCopilot.Application.Features.Projects.Queries.GetProjectVerificationInspection;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Projects.ProjectVerificationInspection;

/// <summary>
/// ADR-0069 malformed or contradictory storage of one member execution at every conversion and proof seam: a number, an identifier, a
/// fingerprint, a time or a status/outcome/exit-code/completion tuple the Domain never records makes the selected execution Unavailable
/// (a 200 answer with no fact, never absent and never a conversion fault), and a nonmember stays absent however damaged.
/// </summary>
public sealed class ProjectVerificationInspectionDamageTests : ProjectVerificationInspectionBaseDamage
{
    private static string Hex(char digit, int length = 64) => new(digit, length);

    private static Dictionary<string, (ExecutionShape Shape, Func<SeededExecution, Guid, FormattableString> Damage)> Defects() => new()
    {
        // Stored enums and numbers.
        ["status-unknown"] = (ExecutionShape.Passed, (e, _) => $"UPDATE verification_executions SET Status = 'Weird' WHERE Id = {e.Id}"),
        ["outcome-unknown"] = (ExecutionShape.Passed, (e, _) => $"UPDATE verification_executions SET Outcome = 'Weird' WHERE Id = {e.Id}"),
        ["exit-text"] = (ExecutionShape.FailedExited, (e, _) => $"UPDATE verification_executions SET ExitCode = 'three' WHERE Id = {e.Id}"),
        ["exit-numeric-prefix-text"] = (ExecutionShape.FailedExited, (e, _) => $"UPDATE verification_executions SET ExitCode = '3x' WHERE Id = {e.Id}"),
        ["exit-real"] = (ExecutionShape.FailedExited, (e, _) => $"UPDATE verification_executions SET ExitCode = 1.5 WHERE Id = {e.Id}"),
        ["exit-real-beside-maximum"] = (ExecutionShape.FailedExited, (e, _) => $"UPDATE verification_executions SET ExitCode = 2147483646.5 WHERE Id = {e.Id}"),
        ["exit-real-beside-negated-maximum"] = (ExecutionShape.FailedExited, (e, _) => $"UPDATE verification_executions SET ExitCode = -2147483646.5 WHERE Id = {e.Id}"),
        ["exit-real-below-negated-maximum"] = (ExecutionShape.FailedExited, (e, _) => $"UPDATE verification_executions SET ExitCode = -2147483647.5 WHERE Id = {e.Id}"),
        ["exit-beyond-int32"] = (ExecutionShape.FailedExited, (e, _) => $"UPDATE verification_executions SET ExitCode = 4294967297 WHERE Id = {e.Id}"),
        ["exit-above-int32-maximum"] = (ExecutionShape.FailedExited, (e, _) => $"UPDATE verification_executions SET ExitCode = 2147483648 WHERE Id = {e.Id}"),
        ["exit-below-int32-minimum"] = (ExecutionShape.FailedExited, (e, _) => $"UPDATE verification_executions SET ExitCode = -2147483649 WHERE Id = {e.Id}"),
        ["exit-blob"] = (ExecutionShape.FailedExited, (e, _) => $"UPDATE verification_executions SET ExitCode = X'0102' WHERE Id = {e.Id}"),
        ["number-zero"] = (ExecutionShape.Passed, (e, _) => $"UPDATE verification_executions SET ExecutionNumber = 0 WHERE Id = {e.Id}"),
        ["number-text"] = (ExecutionShape.Passed, (e, _) => $"UPDATE verification_executions SET ExecutionNumber = 'four' WHERE Id = {e.Id}"),
        ["number-real"] = (ExecutionShape.Passed, (e, _) => $"UPDATE verification_executions SET ExecutionNumber = 4.5 WHERE Id = {e.Id}"),
        ["number-beyond-int32"] = (ExecutionShape.Passed, (e, _) => $"UPDATE verification_executions SET ExecutionNumber = 4294967297 WHERE Id = {e.Id}"),
        ["checkpoint-number-zero"] = (ExecutionShape.Passed, (e, _) => $"UPDATE git_checkpoints SET CheckpointNumber = 0 WHERE Id = {e.Checkpoint.Id}"),
        ["checkpoint-number-text"] = (ExecutionShape.Passed, (e, _) => $"UPDATE git_checkpoints SET CheckpointNumber = 'one' WHERE Id = {e.Checkpoint.Id}"),
        ["checkpoint-number-real"] = (ExecutionShape.Passed, (e, _) => $"UPDATE git_checkpoints SET CheckpointNumber = 1.5 WHERE Id = {e.Checkpoint.Id}"),
        ["checkpoint-number-beyond-int32"] = (ExecutionShape.Passed, (e, _) => $"UPDATE git_checkpoints SET CheckpointNumber = 4294967297 WHERE Id = {e.Checkpoint.Id}"),

        // Identities and ownership.
        ["workspace-missing"] = (ExecutionShape.Passed, (e, _) => $"UPDATE verification_executions SET GitWorkspaceId = {Guid.NewGuid()} WHERE Id = {e.Id}"),
        ["workspace-garbage"] = (ExecutionShape.Passed, (e, _) => $"UPDATE verification_executions SET GitWorkspaceId = 'garbage' WHERE Id = {e.Id}"),
        ["checkpoint-missing"] = (ExecutionShape.Passed, (e, _) => $"UPDATE verification_executions SET GitCheckpointId = {Guid.NewGuid()} WHERE Id = {e.Id}"),
        ["checkpoint-garbage"] = (ExecutionShape.Passed, (e, _) => $"UPDATE verification_executions SET GitCheckpointId = 'garbage' WHERE Id = {e.Id}"),
        ["workspace-of-another-project"] = (ExecutionShape.Passed, (e, other) => $"UPDATE git_workspaces SET ProjectId = {other} WHERE Id = {e.Workspace.Id}"),
        ["checkpoint-of-another-workspace"] = (ExecutionShape.Passed, (e, _) => $"UPDATE git_checkpoints SET WorkspaceId = {Guid.NewGuid()} WHERE Id = {e.Checkpoint.Id}"),
        ["checkpoint-workspace-lowercase"] = (ExecutionShape.Passed, (e, _) => $"UPDATE git_checkpoints SET WorkspaceId = lower(WorkspaceId) WHERE Id = {e.Checkpoint.Id}"),
        ["command-garbage"] = (ExecutionShape.Passed, (e, _) => $"UPDATE verification_executions SET VerificationCommandId = 'garbage' WHERE Id = {e.Id}"),
        ["command-empty"] = (ExecutionShape.Passed, (e, _) => $"UPDATE verification_executions SET VerificationCommandId = {Guid.Empty} WHERE Id = {e.Id}"),
        ["command-lowercase"] = (ExecutionShape.Passed, (e, _) => $"UPDATE verification_executions SET VerificationCommandId = lower(VerificationCommandId) WHERE Id = {e.Id}"),

        // Fingerprints.
        ["claim-fingerprint-differs-from-checkpoint"] = (ExecutionShape.Passed, (e, _) => $"UPDATE verification_executions SET CheckpointFingerprintSha256 = {Hex('b')} WHERE Id = {e.Id}"),
        ["checkpoint-fingerprint-differs"] = (ExecutionShape.Passed, (e, _) => $"UPDATE git_checkpoints SET FingerprintSha256 = {Hex('b')} WHERE Id = {e.Checkpoint.Id}"),
        ["fingerprints-uppercase"] = (ExecutionShape.Passed, (e, _) => $"UPDATE verification_executions SET CheckpointFingerprintSha256 = {Hex('A')}, CompletionFingerprintSha256 = {Hex('A')} WHERE Id = {e.Id}; UPDATE git_checkpoints SET FingerprintSha256 = {Hex('A')} WHERE Id = {e.Checkpoint.Id}"),
        ["fingerprints-63-characters"] = (ExecutionShape.Passed, (e, _) => $"UPDATE verification_executions SET CheckpointFingerprintSha256 = {Hex('a', 63)}, CompletionFingerprintSha256 = {Hex('a', 63)} WHERE Id = {e.Id}; UPDATE git_checkpoints SET FingerprintSha256 = {Hex('a', 63)} WHERE Id = {e.Checkpoint.Id}"),
        ["fingerprints-oversized"] = (ExecutionShape.Passed, (e, _) => $"UPDATE verification_executions SET CheckpointFingerprintSha256 = {Hex('a', 65)}, CompletionFingerprintSha256 = {Hex('a', 65)} WHERE Id = {e.Id}; UPDATE git_checkpoints SET FingerprintSha256 = {Hex('a', 65)} WHERE Id = {e.Checkpoint.Id}"),
        ["claim-fingerprint-nul-hidden"] = (ExecutionShape.Passed, (e, _) => $"UPDATE verification_executions SET CheckpointFingerprintSha256 = {Hex('a')} || char(0) || 'x' WHERE Id = {e.Id}"),
        ["completion-fingerprint-uppercase"] = (ExecutionShape.Passed, (e, _) => $"UPDATE verification_executions SET CompletionFingerprintSha256 = {Hex('A')} WHERE Id = {e.Id}"),
        ["completion-fingerprint-oversized"] = (ExecutionShape.SourceChangedExited, (e, _) => $"UPDATE verification_executions SET CompletionFingerprintSha256 = {Hex('b', 65)} WHERE Id = {e.Id}"),
        ["completion-fingerprint-not-hex"] = (ExecutionShape.SourceChangedExited, (e, _) => $"UPDATE verification_executions SET CompletionFingerprintSha256 = {Hex('g')} WHERE Id = {e.Id}"),
        ["passed-completion-differs-from-claim"] = (ExecutionShape.Passed, (e, _) => $"UPDATE verification_executions SET CompletionFingerprintSha256 = {Hex('b')} WHERE Id = {e.Id}"),
        ["passed-completion-missing"] = (ExecutionShape.Passed, (e, _) => $"UPDATE verification_executions SET CompletionFingerprintSha256 = NULL WHERE Id = {e.Id}"),
        ["source-changed-completion-equals-claim"] = (ExecutionShape.SourceChangedExited, (e, _) => $"UPDATE verification_executions SET CompletionFingerprintSha256 = CheckpointFingerprintSha256 WHERE Id = {e.Id}"),
        ["source-changed-completion-missing"] = (ExecutionShape.SourceChangedExited, (e, _) => $"UPDATE verification_executions SET CompletionFingerprintSha256 = NULL WHERE Id = {e.Id}"),

        // Times.
        ["claimed-t-separator"] = (ExecutionShape.Passed, (e, _) => $"UPDATE verification_executions SET ClaimedAtUtc = replace(ClaimedAtUtc, ' ', 'T') WHERE Id = {e.Id}"),
        ["claimed-offset"] = (ExecutionShape.Passed, (e, _) => $"UPDATE verification_executions SET ClaimedAtUtc = substr(ClaimedAtUtc, 1, length(ClaimedAtUtc) - 6) || '+01:00' WHERE Id = {e.Id}"),
        ["claimed-default"] = (ExecutionShape.Passed, (e, _) => $"UPDATE verification_executions SET ClaimedAtUtc = '0001-01-01 00:00:00+00:00' WHERE Id = {e.Id}"),
        ["claimed-garbage"] = (ExecutionShape.Passed, (e, _) => $"UPDATE verification_executions SET ClaimedAtUtc = 'garbage' WHERE Id = {e.Id}"),
        ["dispatched-garbage"] = (ExecutionShape.Passed, (e, _) => $"UPDATE verification_executions SET DispatchedAtUtc = 'garbage' WHERE Id = {e.Id}"),
        ["dispatched-default"] = (ExecutionShape.Passed, (e, _) => $"UPDATE verification_executions SET DispatchedAtUtc = '0001-01-01 00:00:00+00:00' WHERE Id = {e.Id}"),
        ["dispatched-offset"] = (ExecutionShape.Passed, (e, _) => $"UPDATE verification_executions SET DispatchedAtUtc = substr(DispatchedAtUtc, 1, length(DispatchedAtUtc) - 6) || '-02:00' WHERE Id = {e.Id}"),
        ["completed-garbage"] = (ExecutionShape.Passed, (e, _) => $"UPDATE verification_executions SET CompletedAtUtc = 'garbage' WHERE Id = {e.Id}"),
        ["completed-t-separator"] = (ExecutionShape.Passed, (e, _) => $"UPDATE verification_executions SET CompletedAtUtc = replace(CompletedAtUtc, ' ', 'T') WHERE Id = {e.Id}"),
        ["dispatched-before-claim"] = (ExecutionShape.Passed, (e, _) => $"UPDATE verification_executions SET DispatchedAtUtc = '2000-01-01 00:00:00+00:00' WHERE Id = {e.Id}"),
        ["completed-before-dispatch"] = (ExecutionShape.Passed, (e, _) => $"UPDATE verification_executions SET CompletedAtUtc = DispatchedAtUtc, DispatchedAtUtc = '2099-01-01 00:00:00+00:00' WHERE Id = {e.Id}"),
        ["completed-before-claim-undispatched"] = (ExecutionShape.Interrupted, (e, _) => $"UPDATE verification_executions SET CompletedAtUtc = '2000-01-01 00:00:00+00:00' WHERE Id = {e.Id}"),
        ["dispatched-missing-for-passed"] = (ExecutionShape.Passed, (e, _) => $"UPDATE verification_executions SET DispatchedAtUtc = NULL WHERE Id = {e.Id}"),
        ["completed-missing-for-passed"] = (ExecutionShape.Passed, (e, _) => $"UPDATE verification_executions SET CompletedAtUtc = NULL WHERE Id = {e.Id}"),

        // Contradictory result tuples.
        ["passed-with-nonzero-exit"] = (ExecutionShape.Passed, (e, _) => $"UPDATE verification_executions SET ExitCode = 3 WHERE Id = {e.Id}"),
        ["passed-with-timed-out-outcome"] = (ExecutionShape.Passed, (e, _) => $"UPDATE verification_executions SET Outcome = 'TimedOut', ExitCode = NULL WHERE Id = {e.Id}"),
        ["passed-without-outcome"] = (ExecutionShape.Passed, (e, _) => $"UPDATE verification_executions SET Outcome = NULL, ExitCode = NULL WHERE Id = {e.Id}"),
        ["failed-exited-with-zero-exit"] = (ExecutionShape.FailedExited, (e, _) => $"UPDATE verification_executions SET ExitCode = 0 WHERE Id = {e.Id}"),
        ["failed-exited-without-exit"] = (ExecutionShape.FailedExited, (e, _) => $"UPDATE verification_executions SET ExitCode = NULL WHERE Id = {e.Id}"),
        ["failed-outcome-with-exit"] = (ExecutionShape.FailedOutcome, (e, _) => $"UPDATE verification_executions SET ExitCode = 5 WHERE Id = {e.Id}"),
        ["timed-out-with-exit"] = (ExecutionShape.TimedOut, (e, _) => $"UPDATE verification_executions SET ExitCode = 0 WHERE Id = {e.Id}"),
        ["timed-out-with-cancelled-outcome"] = (ExecutionShape.TimedOut, (e, _) => $"UPDATE verification_executions SET Outcome = 'Cancelled' WHERE Id = {e.Id}"),
        ["cancelled-undispatched"] = (ExecutionShape.Cancelled, (e, _) => $"UPDATE verification_executions SET DispatchedAtUtc = NULL WHERE Id = {e.Id}"),
        ["source-changed-undispatched-with-outcome"] = (ExecutionShape.SourceChangedBeforeDispatch, (e, _) => $"UPDATE verification_executions SET Outcome = 'TimedOut' WHERE Id = {e.Id}"),
        ["source-changed-undispatched-with-exit"] = (ExecutionShape.SourceChangedBeforeDispatch, (e, _) => $"UPDATE verification_executions SET Outcome = 'Exited', ExitCode = 0 WHERE Id = {e.Id}"),
        ["source-changed-dispatched-without-outcome"] = (ExecutionShape.SourceChangedExited, (e, _) => $"UPDATE verification_executions SET Outcome = NULL, ExitCode = NULL WHERE Id = {e.Id}"),
        ["source-changed-exited-without-exit"] = (ExecutionShape.SourceChangedExited, (e, _) => $"UPDATE verification_executions SET ExitCode = NULL WHERE Id = {e.Id}"),
        ["source-changed-timed-out-with-exit"] = (ExecutionShape.SourceChangedTimedOut, (e, _) => $"UPDATE verification_executions SET ExitCode = 1 WHERE Id = {e.Id}"),
        ["running-with-completion-time"] = (ExecutionShape.Running, (e, _) => $"UPDATE verification_executions SET CompletedAtUtc = ClaimedAtUtc WHERE Id = {e.Id}"),
        ["running-with-completion-fingerprint"] = (ExecutionShape.RunningDispatched, (e, _) => $"UPDATE verification_executions SET CompletionFingerprintSha256 = CheckpointFingerprintSha256 WHERE Id = {e.Id}"),
        ["running-with-outcome"] = (ExecutionShape.RunningDispatched, (e, _) => $"UPDATE verification_executions SET Outcome = 'Exited', ExitCode = 0 WHERE Id = {e.Id}"),
        ["interrupted-with-outcome"] = (ExecutionShape.InterruptedDispatched, (e, _) => $"UPDATE verification_executions SET Outcome = 'TimedOut' WHERE Id = {e.Id}"),
        ["interrupted-with-exit"] = (ExecutionShape.Interrupted, (e, _) => $"UPDATE verification_executions SET Outcome = 'Exited', ExitCode = 0 WHERE Id = {e.Id}"),
        ["interrupted-with-completion-fingerprint"] = (ExecutionShape.Interrupted, (e, _) => $"UPDATE verification_executions SET CompletionFingerprintSha256 = CheckpointFingerprintSha256 WHERE Id = {e.Id}"),
        ["interrupted-without-completion-time"] = (ExecutionShape.Interrupted, (e, _) => $"UPDATE verification_executions SET CompletedAtUtc = NULL WHERE Id = {e.Id}"),
        ["refusal-with-dispatch"] = (ExecutionShape.DispatchRefused, (e, _) => $"UPDATE verification_executions SET DispatchedAtUtc = CompletedAtUtc WHERE Id = {e.Id}"),
        ["refusal-with-outcome"] = (ExecutionShape.DispatchRefused, (e, _) => $"UPDATE verification_executions SET Outcome = 'Failed' WHERE Id = {e.Id}"),
        ["refusal-with-exit"] = (ExecutionShape.DispatchRefused, (e, _) => $"UPDATE verification_executions SET Outcome = 'Exited', ExitCode = 0 WHERE Id = {e.Id}"),
        ["refusal-with-completion-fingerprint"] = (ExecutionShape.DispatchRefused, (e, _) => $"UPDATE verification_executions SET CompletionFingerprintSha256 = CheckpointFingerprintSha256 WHERE Id = {e.Id}"),

        // Command name snapshot.
        ["name-blank"] = (ExecutionShape.Passed, (e, _) => $"UPDATE verification_executions SET CommandName = '   ' WHERE Id = {e.Id}"),
        ["name-201-units"] = (ExecutionShape.Passed, (e, _) => $"UPDATE verification_executions SET CommandName = {new string('n', 201)} WHERE Id = {e.Id}"),
        ["name-over-600-bytes"] = (ExecutionShape.Passed, (e, _) => $"UPDATE verification_executions SET CommandName = {new string('n', 601)} WHERE Id = {e.Id}"),
        ["name-nul-hidden"] = (ExecutionShape.Passed, (e, _) => $"UPDATE verification_executions SET CommandName = 'ok' || char(0) || {new string('n', 700)} WHERE Id = {e.Id}"),
    };

    public static TheoryData<string> DefectNames() => new(Defects().Keys.ToArray());

    [Theory]
    [MemberData(nameof(DefectNames))]
    public async Task Damaged_or_contradictory_storage_of_a_member_execution_is_unavailable_never_absent_and_never_a_fault(string defect)
    {
        var (shape, damage) = Defects()[defect];
        var scene = await SeedAsync(shape);
        Assert.Equal(ProjectVerificationInspectionStatus.Recorded, (await InspectAsync(scene.ProjectId, scene.Execution.Id)).Status);
        var otherProject = await Seed.SeedProjectAsync();

        await ExecuteAsync(damage(scene.Execution, otherProject));

        var result = await InspectAsync(scene.ProjectId, scene.Execution.Id);
        Assert.Equal(ProjectVerificationInspectionStatus.Unavailable, result.Status);
        Assert.Null(result.Execution);
        Assert.Equal((scene.ProjectId, scene.Execution.Id), (result.ProjectId, result.ExecutionId));
    }

    [Fact]
    public async Task A_damaged_sibling_execution_never_damages_a_coherent_one()
    {
        var scene = await SeedAsync(ExecutionShape.Passed);
        var sibling = await Seed.SeedExecutionAsync(
            scene.ProjectId, scene.Execution.Workspace, scene.Execution.Checkpoint, scene.Execution.Command, 5, ExecutionShape.FailedExited);
        await ExecuteAsync($"UPDATE verification_executions SET Status = 'Weird', ExitCode = 'x', ClaimedAtUtc = 'garbage' WHERE Id = {sibling.Id}");

        Assert.Equal(ProjectVerificationInspectionStatus.Recorded, (await InspectAsync(scene.ProjectId, scene.Execution.Id)).Status);
        Assert.Equal(ProjectVerificationInspectionStatus.Unavailable, (await InspectAsync(scene.ProjectId, sibling.Id)).Status);
    }

    [Fact]
    public async Task Another_workspace_of_the_project_with_a_different_checkpoint_does_not_matter_but_the_named_checkpoint_does()
    {
        var scene = await SeedAsync(ExecutionShape.Passed);
        var newer = await Seed.AddCheckpointAsync(scene.Execution.Workspace, 2, new string('9', 64));

        Assert.Equal(ProjectVerificationInspectionStatus.Recorded, (await InspectAsync(scene.ProjectId, scene.Execution.Id)).Status);
        await ExecuteAsync($"UPDATE verification_executions SET GitCheckpointId = {newer.Id} WHERE Id = {scene.Execution.Id}");

        Assert.Equal(ProjectVerificationInspectionStatus.Unavailable, (await InspectAsync(scene.ProjectId, scene.Execution.Id)).Status);
    }
}
