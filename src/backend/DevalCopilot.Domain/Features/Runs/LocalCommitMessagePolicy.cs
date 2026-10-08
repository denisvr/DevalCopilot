using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The human-written commit message contract of ADR-0029: trimmed, LF-only, at most 2 KiB of UTF-8, a nonempty subject, no
/// control character other than LF and well-formed UTF-16. The host appends one fixed operation-identity trailer; a caller line
/// that imitates the trailer name is refused so the trailer is never ambiguous.
/// </summary>
public static class LocalCommitMessagePolicy
{
    public const int MaximumUtf8Bytes = 2 * 1024;

    public const string TrailerName = "DevalCopilot-Operation";

    public static bool TryNormalize(string? message, out string normalized)
    {
        normalized = string.Empty;
        if (message is null)
        {
            return false;
        }

        var trimmed = message.Trim();
        var lines = trimmed.Split('\n');
        if (trimmed.Length == 0 || lines[0].Trim().Length == 0)
        {
            return false;
        }

        for (var index = 0; index < trimmed.Length; index++)
        {
            var character = trimmed[index];
            if (char.IsHighSurrogate(character))
            {
                if (index + 1 >= trimmed.Length || !char.IsLowSurrogate(trimmed[index + 1]))
                {
                    return false;
                }

                index++;
                continue;
            }

            if (char.IsLowSurrogate(character))
            {
                return false;
            }

            if (character != '\n' && (char.IsControl(character)
                || CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.Format
                || (int)character is 0x2028 or 0x2029))
            {
                return false;
            }
        }

        if (Encoding.UTF8.GetByteCount(trimmed) > MaximumUtf8Bytes
            || lines.Any(line => line.TrimStart().StartsWith(TrailerName, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        normalized = trimmed;
        return true;
    }

    /// <summary>The exact commit message the host writes: the normalized human text, a blank line and the trailer.</summary>
    public static string BuildCommitMessage(string normalized, Guid operationId) =>
        $"{normalized}\n\n{TrailerName}: {operationId:D}\n";

    /// <summary>The identity of one normalized request: replaying it returns the recorded operation, anything else conflicts.</summary>
    public static string ComputeRequestSha256(
        Guid checkpointId, Guid codeReviewAttemptId, Guid humanCheckpointReviewId, string normalizedMessage)
    {
        var canonical =
            $"local-commit-request-v1\n{checkpointId:N}\n{codeReviewAttemptId:N}\n{humanCheckpointReviewId:N}\n{normalizedMessage}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}
