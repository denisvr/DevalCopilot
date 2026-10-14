using System.Buffers;
using System.Globalization;
using System.Text;

namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubCheckContextsObservation;

/// <summary>
/// The operation-owned admission rules for the contexts of one commit (ADR-0045): the closed kinds and literal enumerations GitHub's
/// GraphQL reference defines, an opaque context or check-suite identifier, the label text contract and the first page's internal
/// consistency. It is deliberately not the repository node-ID policy and is not shared with another operation. Nothing is normalized,
/// translated or defaulted. Pure.
/// </summary>
public static class GitHubCheckContextsPolicy
{
    public const int MaxContexts = 50;
    public const string CheckRunKind = "CheckRun";
    public const string StatusContextKind = "StatusContext";

    private const int MaxIdLength = 256;
    private const int MaxLabelUtf8Bytes = 256;
    private const int ReplacementCharacter = 0xFFFD;

    /// <summary>One to 256 ASCII letters, digits, <c>_</c>, <c>-</c>, <c>/</c>, <c>+</c> or <c>=</c>, compared ordinally and never decoded.</summary>
    public static bool IsContextId(string? value)
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

    /// <summary>Nonblank, well-formed Unicode of 1 to 256 UTF-8 bytes with no Control, Format, Surrogate, LineSeparator or
    /// ParagraphSeparator character and no U+FFFD. Exact admitted text is preserved; nothing is trimmed, normalized or escaped.</summary>
    public static bool IsLabel(string? value)
    {
        // Every UTF-16 code unit is at least one UTF-8 byte, so a longer string can never fit; this also bounds the work below.
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxLabelUtf8Bytes)
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
                case UnicodeCategory.Control:
                case UnicodeCategory.Format:
                case UnicodeCategory.Surrogate:
                case UnicodeCategory.LineSeparator:
                case UnicodeCategory.ParagraphSeparator:
                    return false;
            }

            remaining = remaining[consumed..];
        }

        return Encoding.UTF8.GetByteCount(value) <= MaxLabelUtf8Bytes;
    }

    public static bool IsCheckRunStatus(string? value) =>
        value is "COMPLETED" or "IN_PROGRESS" or "PENDING" or "QUEUED" or "REQUESTED" or "WAITING";

    public static bool IsCheckRunConclusion(string? value) =>
        value is "ACTION_REQUIRED" or "CANCELLED" or "FAILURE" or "NEUTRAL" or "SKIPPED" or "STALE" or "STARTUP_FAILURE" or "SUCCESS"
            or "TIMED_OUT";

    public static bool IsStatusContextState(string? value) =>
        value is "ERROR" or "EXPECTED" or "FAILURE" or "PENDING" or "SUCCESS";

    /// <summary>A check run needs every field (its conclusion may be null, never invented); a status context carries its state as the
    /// status and has no conclusion or check suite. The status and the conclusion are validated independently: no pairing is implied.</summary>
    public static bool IsContext(GitHubCheckContext? context) =>
        context is not null
        && IsContextId(context.Id)
        && IsLabel(context.Name)
        && (context.Kind switch
        {
            CheckRunKind => IsCheckRunStatus(context.Status)
                && (context.Conclusion is null || IsCheckRunConclusion(context.Conclusion))
                && IsContextId(context.CheckSuiteId),
            StatusContextKind => IsStatusContextState(context.Status) && context.Conclusion is null && context.CheckSuiteId is null,
            _ => false,
        });

    /// <summary>The returned page holds exactly the lesser of 50 and GitHub's non-negative total, every entry valid and no identifier
    /// repeated (ordinally). Repeated names and shared check suites are legitimate and preserved.</summary>
    public static bool IsPage(GitHubCheckContextsPage? page)
    {
        if (page is null || page.TotalCount < 0 || page.Contexts is null || page.Contexts.Count != Math.Min(page.TotalCount, MaxContexts))
        {
            return false;
        }

        return page.Contexts.All(IsContext)
            && page.Contexts.Select(context => context.Id).Distinct(StringComparer.Ordinal).Count() == page.Contexts.Count;
    }
}
