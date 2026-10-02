using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DevalCopilot.Domain.Features.Projects;

namespace DevalCopilot.Application.Features.Runs.Policies.VerificationDiagnosis;

/// <summary>
/// The deterministic, versioned snapshot digest of one verification-diagnosis membership (ADR-0018). It is the SHA-256 of a
/// canonical text of the command facts, the complete execution facts and ownership, and — for a failed execution — both
/// sealed output rows (identity, storage path, length, hash, truncation, capture outcome). Relational membership stays the
/// authority for which executions belong to the diagnosis; this digest only proves that the facts the claim decided against
/// are still exactly the facts stored. The canonical text and the digest are host-internal: neither reaches a provider, a
/// manifest, or an HTTP response.
/// </summary>
/// <remarks>
/// Canonical form (version 1): the version line, then one <c>name=value</c> line per fact in a fixed order, each terminated by
/// a line feed. Text values are written as <c>{utf8-byte-length}:{value}</c>, absent values as <c>-</c>, booleans as
/// <c>1</c>/<c>0</c>, identifiers as lowercase hyphenated GUIDs, instants as UTC ticks, and enums by member name. Arguments are
/// written as their count followed by one line per argument. A passed execution carries no output lines; a failed one carries
/// the standard-output block and then the standard-error block.
/// </remarks>
internal static class VerificationDiagnosisSnapshot
{
    public const string Version = "verification-diagnosis-snapshot-v1";

    public static string Compute(VerificationDiagnosisEvidence.Entry entry)
    {
        var builder = new StringBuilder();
        builder.Append(Version).Append('\n');

        var command = entry.Command;
        Line(builder, "command.id", command.Id);
        Line(builder, "command.projectId", command.ProjectId);
        Line(builder, "command.number", command.CommandNumber);
        Line(builder, "command.name", command.Name);
        Line(builder, "command.executablePath", command.ExecutablePath);
        Arguments(builder, "command.arguments", command.Arguments);
        Line(builder, "command.timeoutSeconds", command.TimeoutSeconds);
        Line(builder, "command.enabled", command.IsEnabled);
        Line(builder, "command.configuredAt", command.ConfiguredAtUtc.UtcTicks);
        Line(builder, "command.updatedAt", command.UpdatedAtUtc.UtcTicks);

        var execution = entry.Execution;
        Line(builder, "execution.id", execution.Id);
        Line(builder, "execution.projectId", execution.ProjectId);
        Line(builder, "execution.number", execution.ExecutionNumber);
        Line(builder, "execution.workspaceId", execution.GitWorkspaceId);
        Line(builder, "execution.checkpointId", execution.GitCheckpointId);
        Line(builder, "execution.commandId", execution.VerificationCommandId);
        Line(builder, "execution.workspacePath", execution.WorkspacePath);
        Line(builder, "execution.checkpointFingerprint", execution.CheckpointFingerprintSha256);
        Line(builder, "execution.commandName", execution.CommandName);
        Line(builder, "execution.executablePath", execution.ExecutablePath);
        Arguments(builder, "execution.arguments", execution.Arguments);
        Line(builder, "execution.timeoutSeconds", execution.TimeoutSeconds);
        Line(builder, "execution.status", execution.Status.ToString());
        Line(builder, "execution.claimedAt", execution.ClaimedAtUtc.UtcTicks);
        Line(builder, "execution.dispatchedAt", execution.DispatchedAtUtc?.UtcTicks);
        Line(builder, "execution.completedAt", execution.CompletedAtUtc?.UtcTicks);
        Line(builder, "execution.outcome", execution.Outcome?.ToString());
        Line(builder, "execution.exitCode", execution.ExitCode);
        Line(builder, "execution.completionFingerprint", execution.CompletionFingerprintSha256);

        Line(builder, "failed", entry.Failed);
        if (entry.Failed)
        {
            Output(builder, "stdout", entry.StandardOutput);
            Output(builder, "stderr", entry.StandardError);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }

    private static void Output(StringBuilder builder, string prefix, VerificationDiagnosisEvidence.Output? output)
    {
        Line(builder, prefix + ".id", output?.ArtifactId);
        Line(builder, prefix + ".path", output?.RelativeStoragePath);
        Line(builder, prefix + ".length", output?.ByteLength);
        Line(builder, prefix + ".hash", output?.ContentHash);
        Line(builder, prefix + ".truncated", output?.Truncated);
        Line(builder, prefix + ".capture", output?.CaptureOutcome.ToString());
    }

    private static void Arguments(StringBuilder builder, string name, IReadOnlyList<string> arguments)
    {
        Line(builder, name + ".count", arguments.Count);
        for (var index = 0; index < arguments.Count; index++)
        {
            Line(builder, $"{name}[{index}]", arguments[index]);
        }
    }

    private static void Line(StringBuilder builder, string name, string? value) =>
        builder.Append(name).Append('=').Append(value is null ? "-" : Encoding.UTF8.GetByteCount(value).ToString(CultureInfo.InvariantCulture) + ":" + value).Append('\n');

    private static void Line(StringBuilder builder, string name, Guid? value) =>
        builder.Append(name).Append('=').Append(value is null ? "-" : value.Value.ToString("D", CultureInfo.InvariantCulture)).Append('\n');

    private static void Line(StringBuilder builder, string name, long? value) =>
        builder.Append(name).Append('=').Append(value is null ? "-" : value.Value.ToString(CultureInfo.InvariantCulture)).Append('\n');

    private static void Line(StringBuilder builder, string name, bool? value) =>
        builder.Append(name).Append('=').Append(value is null ? "-" : value.Value ? "1" : "0").Append('\n');
}
