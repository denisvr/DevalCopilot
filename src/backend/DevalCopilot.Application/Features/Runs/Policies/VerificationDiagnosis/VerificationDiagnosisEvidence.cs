using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Projects;
using Devalente.Shared.Results;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.VerificationDiagnosis;

/// <summary>
/// Derives the complete, ordered verification selection a verification diagnosis (ADR-0018) is claimed against, and
/// refuses every class of evidence the diagnosis stage does not admit. Every enabled command must have a latest
/// execution bound to the exact result checkpoint and fingerprint, and every one must be terminal and coherent: either
/// Passed/Exited/0 or Failed/Exited/nonzero, with at least one failure. A timeout, cancellation, interruption, source
/// drift, process-start failure, contradictory status/outcome/exit data, or malformed ownership is never diagnosable (a
/// human decides what to do about those), and a failed execution must also have both of its sealed output rows. The same
/// derivation runs at claim, at dispatch, and before result recording, always read afresh, so the selection pinned at claim
/// time is compared against the selection that holds now.
/// </summary>
internal static class VerificationDiagnosisEvidence
{
    public const string NoCommandsEnabledCode = "verification_diagnosis.no_verification_commands_enabled";
    public const string EvidenceMissingCode = "verification_diagnosis.evidence_missing";
    public const string EvidenceRunningCode = "verification_diagnosis.evidence_running";
    public const string NotDiagnosableCode = "verification_diagnosis.evidence_not_diagnosable";
    public const string NoFailedVerificationCode = "verification_diagnosis.no_failed_verification";
    public const string OutputUnavailableCode = "verification_diagnosis.failed_output_unavailable";

    /// <summary>One sealed output row of a failed execution, as the claim pinned it. Never carries a path to a caller.</summary>
    internal sealed record Output(
        Guid ArtifactId,
        string RelativeStoragePath,
        long ByteLength,
        string ContentHash,
        bool? Truncated,
        VerificationOutputCaptureOutcome CaptureOutcome);

    internal sealed record Entry(
        VerificationCommand Command,
        VerificationExecution Execution,
        bool Failed,
        Output? StandardOutput,
        Output? StandardError);

    internal sealed record Selection(IReadOnlyList<Entry> Entries)
    {
        public IReadOnlyList<Guid> OrderedExecutionIds => Entries.Select(entry => entry.Execution.Id).ToArray();

        public IReadOnlyList<(Guid CommandId, Guid ExecutionId)> OrderedPairs =>
            Entries.Select(entry => (entry.Command.Id, entry.Execution.Id)).ToArray();

        /// <summary>The ordered snapshot digests of every membership (see <see cref="VerificationDiagnosisSnapshot"/>).</summary>
        public IReadOnlyList<string> OrderedSnapshots => Entries.Select(VerificationDiagnosisSnapshot.Compute).ToArray();

        /// <summary>Whether this selection is the very selection <paramref name="other"/> pinned: the same ordered
        /// (command, execution) pairs, the same failed set, the same sealed failed-output identity, and the same snapshot of every command, execution, and
        /// output fact.</summary>
        public bool SameAs(Selection other)
        {
            if (Entries.Count != other.Entries.Count)
            {
                return false;
            }

            for (var index = 0; index < Entries.Count; index++)
            {
                var left = Entries[index];
                var right = other.Entries[index];
                if (left.Command.Id != right.Command.Id
                    || left.Execution.Id != right.Execution.Id
                    || left.Failed != right.Failed
                    || !Equals(left.StandardOutput, right.StandardOutput)
                    || !Equals(left.StandardError, right.StandardError)
                    || VerificationDiagnosisSnapshot.Compute(left) != VerificationDiagnosisSnapshot.Compute(right))
                {
                    return false;
                }
            }

            return true;
        }
    }

    internal sealed record ReadResult(Selection? Value, Error? Error)
    {
        public static ReadResult Succeeded(Selection value) => new(value, null);

        public static ReadResult Failed(string code, string message) => new(null, Error.Conflict(code, message));
    }

    public static async Task<ReadResult> ReadAsync(
        IDevalCopilotDbContext dbContext,
        Guid projectId,
        Guid workspaceId,
        GitCheckpoint checkpoint,
        bool asNoTracking,
        CancellationToken cancellationToken)
    {
        var commandSet = asNoTracking ? dbContext.VerificationCommands.AsNoTracking() : dbContext.VerificationCommands;
        var executionSet = asNoTracking ? dbContext.VerificationExecutions.AsNoTracking() : dbContext.VerificationExecutions;
        var outputSet = asNoTracking ? dbContext.VerificationOutputArtifacts.AsNoTracking() : dbContext.VerificationOutputArtifacts;

        var enabledCommands = await commandSet
            .Where(command => command.ProjectId == projectId && command.IsEnabled)
            .OrderBy(command => command.CommandNumber)
            .ToListAsync(cancellationToken);
        if (enabledCommands.Count == 0)
        {
            return ReadResult.Failed(NoCommandsEnabledCode, "At least one enabled verification command is required to request a diagnosis.");
        }

        var commandIds = enabledCommands.Select(command => command.Id).ToArray();
        var boundExecutions = await executionSet
            .Where(execution =>
                commandIds.Contains(execution.VerificationCommandId)
                && execution.GitCheckpointId == checkpoint.Id
                && execution.CheckpointFingerprintSha256 == checkpoint.FingerprintSha256)
            .ToListAsync(cancellationToken);
        var latestByCommand = boundExecutions
            .GroupBy(execution => execution.VerificationCommandId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(execution => execution.ExecutionNumber).First());

        var evaluated = new List<(VerificationCommand Command, VerificationExecution Execution, bool Failed)>(enabledCommands.Count);
        foreach (var command in enabledCommands)
        {
            if (!latestByCommand.TryGetValue(command.Id, out var execution))
            {
                return ReadResult.Failed(
                    EvidenceMissingCode,
                    $"Enabled verification command '{command.Name}' has no execution bound to the current checkpoint.");
            }

            if (execution.Status == VerificationExecutionStatus.Running)
            {
                return ReadResult.Failed(
                    EvidenceRunningCode,
                    $"Enabled verification command '{command.Name}' has no terminal execution bound to the current checkpoint yet.");
            }

            var failed = Classify(execution, projectId, workspaceId, checkpoint);
            if (failed is null)
            {
                return ReadResult.Failed(
                    NotDiagnosableCode,
                    $"The latest execution of verification command '{command.Name}' is not a diagnosable pass or failure.");
            }

            evaluated.Add((command, execution, failed.Value));
        }

        if (!evaluated.Any(item => item.Failed))
        {
            return ReadResult.Failed(
                NoFailedVerificationCode,
                "Every enabled verification command Passed for the current checkpoint; there is no failure to diagnose.");
        }

        var failedExecutionIds = evaluated.Where(item => item.Failed).Select(item => item.Execution.Id).ToArray();
        var outputRows = await outputSet
            .Where(output => failedExecutionIds.Contains(output.VerificationExecutionId))
            .ToListAsync(cancellationToken);

        var entries = new List<Entry>(evaluated.Count);
        foreach (var (command, execution, failed) in evaluated)
        {
            if (!failed)
            {
                entries.Add(new Entry(command, execution, false, null, null));
                continue;
            }

            var standardOutput = SingleOutput(outputRows, execution.Id, VerificationOutputPurpose.StandardOutput);
            var standardError = SingleOutput(outputRows, execution.Id, VerificationOutputPurpose.StandardError);
            if (standardOutput is null || standardError is null)
            {
                return ReadResult.Failed(
                    OutputUnavailableCode,
                    $"The sealed output of the failed verification command '{command.Name}' is not available.");
            }

            entries.Add(new Entry(command, execution, true, standardOutput, standardError));
        }

        return ReadResult.Succeeded(new Selection(entries));
    }

    /// <summary>True: a coherent Failed/Exited/nonzero. False: a coherent Passed/Exited/0. Null: anything else.</summary>
    private static bool? Classify(VerificationExecution execution, Guid projectId, Guid workspaceId, GitCheckpoint checkpoint)
    {
        if (execution.ProjectId != projectId
            || execution.GitWorkspaceId != workspaceId
            || execution.GitCheckpointId != checkpoint.Id
            || !string.Equals(execution.CheckpointFingerprintSha256, checkpoint.FingerprintSha256, StringComparison.Ordinal)
            || !string.Equals(execution.CompletionFingerprintSha256, checkpoint.FingerprintSha256, StringComparison.Ordinal)
            || !execution.DispatchedAtUtc.HasValue
            || !execution.CompletedAtUtc.HasValue
            || execution.Outcome != VerificationExecutionOutcome.Exited
            || execution.ExitCode is not { } exitCode)
        {
            return null;
        }

        return (execution.Status, exitCode) switch
        {
            (VerificationExecutionStatus.Passed, 0) => false,
            (VerificationExecutionStatus.Failed, not 0) => true,
            _ => null,
        };
    }

    private static Output? SingleOutput(
        IReadOnlyList<VerificationOutputArtifact> rows, Guid executionId, VerificationOutputPurpose purpose)
    {
        var matching = rows.Where(row => row.VerificationExecutionId == executionId && row.Purpose == purpose).ToArray();
        return matching.Length == 1
            ? new Output(
                matching[0].Id,
                matching[0].RelativeStoragePath,
                matching[0].ByteLength,
                matching[0].ContentHash,
                matching[0].Truncated,
                matching[0].CaptureOutcome)
            : null;
    }
}
