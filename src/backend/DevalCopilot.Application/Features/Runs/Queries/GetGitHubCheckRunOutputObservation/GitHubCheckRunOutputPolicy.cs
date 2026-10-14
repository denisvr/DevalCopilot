using System.Buffers;
using System.Globalization;
using System.Text;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubCheckRunOutputObservation;

/// <summary>
/// The operation-owned admission rules of one check-run output inspection (ADR-0047): the opaque request identifier, the safe check name,
/// the closed literal status and conclusion enumerations GitHub's GraphQL reference defines, the output text contract and its byte
/// bounds, the complete check association and the node shape. It is deliberately not shared with another operation and is not the
/// repository node-ID policy. Nothing is normalized, translated, truncated or defaulted. Pure.
/// </summary>
public static class GitHubCheckRunOutputPolicy
{
    public const int MaxTitleBytes = 256;
    public const int MaxSummaryBytes = 8 * 1024;
    public const int MaxTextBytes = 16 * 1024;
    public const int MaxCombinedOutputBytes = 24 * 1024;
    public const string CheckRunTypeName = "CheckRun";

    private const int MaxIdLength = 256;
    private const int MaxNameBytes = 256;
    private const int MaxTypeNameLength = 64;
    private const int ReplacementCharacter = 0xFFFD;

    /// <summary>One to 256 ASCII letters, digits, <c>_</c>, <c>-</c>, <c>/</c>, <c>+</c> or <c>=</c>, compared ordinally, never trimmed,
    /// normalized or decoded.</summary>
    public static bool IsCheckRunId(string? value) => IsOpaqueId(value);

    /// <summary>Nonblank, well-formed Unicode of 1 to 256 UTF-8 bytes with no Control (tab and line breaks included), Format, Surrogate,
    /// LineSeparator or ParagraphSeparator character and no U+FFFD. Exact admitted text is preserved.</summary>
    public static bool IsName(string? value) =>
        !string.IsNullOrWhiteSpace(value) && IsWellFormedText(value, MaxNameBytes, allowLineControls: false);

    public static bool IsStatus(string? value) =>
        value is "COMPLETED" or "IN_PROGRESS" or "PENDING" or "QUEUED" or "REQUESTED" or "WAITING";

    public static bool IsConclusion(string? value) =>
        value is "ACTION_REQUIRED" or "CANCELLED" or "FAILURE" or "NEUTRAL" or "SKIPPED" or "STALE" or "STARTUP_FAILURE" or "SUCCESS"
            or "TIMED_OUT";

    /// <summary>The literal GraphQL type name shape: a letter or underscore followed by letters, digits or underscores, at most 64.</summary>
    public static bool IsTypeName(string? value)
    {
        if (value is null || value.Length is 0 or > MaxTypeNameLength || char.IsAsciiDigit(value[0]))
        {
            return false;
        }

        return value.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');
    }

    /// <summary>A null field is admitted. A string (empty and whitespace included) is well-formed Unicode of at most the given number of
    /// UTF-8 bytes with no Control character other than tab, line feed and carriage return, and no Format, Surrogate, LineSeparator or
    /// ParagraphSeparator character or U+FFFD. Nothing is trimmed, normalized, escaped or truncated.</summary>
    public static bool IsOutputField(string? value, int maxBytes) =>
        value is null || IsWellFormedText(value, maxBytes, allowLineControls: true);

    /// <summary>The three fields are each within their own bound and, together, within the combined bound.</summary>
    public static bool IsOutput(GitHubCheckRunOutputText? output) =>
        output is not null
        && IsOutputField(output.Title, MaxTitleBytes)
        && IsOutputField(output.Summary, MaxSummaryBytes)
        && IsOutputField(output.Text, MaxTextBytes)
        && Utf8Bytes(output.Title) + Utf8Bytes(output.Summary) + Utf8Bytes(output.Text) <= MaxCombinedOutputBytes;

    /// <summary>Every identifier and the commit are well formed, the check run echoes the requested identifier ordinally and its repository
    /// equals the repository of its check suite (a disagreement is internal incoherence, not a foreign association).</summary>
    public static bool IsAssociation(GitHubCheckRunAssociation? association, string requestedCheckRunId) =>
        association is not null
        && IsCheckRunId(requestedCheckRunId)
        && string.Equals(association.CheckRunId, requestedCheckRunId, StringComparison.Ordinal)
        && IsOpaqueId(association.CheckSuiteId)
        && GitHubRepositoryNodeIdPolicy.IsNodeId(association.CheckRunRepositoryId)
        && GitHubRepositoryNodeIdPolicy.IsNodeId(association.CheckSuiteRepositoryId)
        && string.Equals(association.CheckRunRepositoryId, association.CheckSuiteRepositoryId, StringComparison.Ordinal)
        && GitHubObservationTarget.IsCommitSha(association.CommitSha);

    /// <summary>The name, status, conclusion and output are validated independently: no pairing is implied and a null conclusion is never
    /// turned into a verdict.</summary>
    public static bool IsDetail(GitHubCheckRunDetail? detail) =>
        detail is not null
        && IsName(detail.Name)
        && IsStatus(detail.Status)
        && (detail.Conclusion is null || IsConclusion(detail.Conclusion))
        && IsOutput(detail.Output);

    /// <summary>A node is absent, a check run with its association (and, only in the output phase, its detail) or another type with
    /// neither. Whether it belongs to the bound repository and recorded commit is not decided here.</summary>
    public static bool IsNode(GitHubCheckRunNode? node, string requestedCheckRunId, bool requireDetail)
    {
        if (node is null)
        {
            return true;
        }

        if (!IsTypeName(node.TypeName))
        {
            return false;
        }

        if (!string.Equals(node.TypeName, CheckRunTypeName, StringComparison.Ordinal))
        {
            return node.Association is null && node.Detail is null;
        }

        return IsAssociation(node.Association, requestedCheckRunId)
            && (requireDetail ? IsDetail(node.Detail) : node.Detail is null);
    }

    private static bool IsOpaqueId(string? value)
    {
        if (value is null || value.Length is 0 or > MaxIdLength)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (character is not ((>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_' or '-' or '/' or '+' or '='))
            {
                return false;
            }
        }

        return true;
    }

    private static int Utf8Bytes(string? value) => value is null ? 0 : Encoding.UTF8.GetByteCount(value);

    private static bool IsWellFormedText(string value, int maxBytes, bool allowLineControls)
    {
        // Every UTF-16 code unit is at least one UTF-8 byte, so a longer string can never fit; this also bounds the work below.
        if (value.Length > maxBytes)
        {
            return false;
        }

        var remaining = value.AsSpan();
        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(remaining, out var rune, out var consumed) != OperationStatus.Done
                || rune.Value == ReplacementCharacter)
            {
                return false;
            }

            switch (Rune.GetUnicodeCategory(rune))
            {
                case UnicodeCategory.Control when !(allowLineControls && rune.Value is 0x09 or 0x0A or 0x0D):
                case UnicodeCategory.Format:
                case UnicodeCategory.Surrogate:
                case UnicodeCategory.LineSeparator:
                case UnicodeCategory.ParagraphSeparator:
                    return false;
            }

            remaining = remaining[consumed..];
        }

        return Encoding.UTF8.GetByteCount(value) <= maxBytes;
    }
}
