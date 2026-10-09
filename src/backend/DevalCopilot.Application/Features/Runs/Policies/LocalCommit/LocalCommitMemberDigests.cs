using System.Security.Cryptography;
using System.Text;
using DevalCopilot.Domain.Features.Projects;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalCommit;

/// <summary>
/// The one serialization of the per-member digests recorded at admission (ADR-0029) and recomputed by the historical receipt
/// (ADR-0032). The format is pure data in, hex out: it grants no authority and reads nothing, so changing it would invalidate every
/// recorded operation.
/// </summary>
internal static class LocalCommitMemberDigests
{
    public static string Verification(
        Guid commandId,
        Guid executionId,
        int executionNumber,
        string commandName,
        string executablePath,
        int timeoutSeconds,
        IReadOnlyList<string> arguments,
        string? completionFingerprintSha256) => Sha256(
        $"{commandId:N}|{executionId:N}|{executionNumber}|{commandName}|{executablePath}"
        + $"|{timeoutSeconds}|{string.Join((char)0x1f, arguments)}|{completionFingerprintSha256}");

    public static string HumanDecision(CheckpointReview review, IEnumerable<CheckpointReviewEvidence> evidence) => Sha256(
        $"{review.Id:N}|{review.Decision}|{review.ActorKind}|{review.GitCheckpointId:N}|{review.CheckpointFingerprintSha256}|"
        + string.Join(
            ',',
            evidence.OrderBy(member => member.VerificationExecutionId)
                .Select(member => $"{member.VerificationCommandId:N}:{member.VerificationExecutionId:N}")));

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
